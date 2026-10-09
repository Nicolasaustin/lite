using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace LiteSynapse;

// Pending work by kind ("mouse", "kb", "prewrite", ...). Posting a kind again replaces what was waiting
// for that kind, so only the latest request of each kind ever runs. A request can be due later (for
// example the desktop restore 3 s after a game left the foreground); posting the same kind again
// before then cancels it. Times are plain numbers (milliseconds) so this can be tested without a clock.
internal sealed class LatestQueue<T>
{
	private sealed class Entry
	{
		public T Item;

		public long Due;

		public long Seq;
	}

	private readonly Dictionary<string, Entry> pending = new Dictionary<string, Entry>();

	private long seq;

	public int Count => pending.Count;

	// Returns the item it replaced (default if none) so the caller can tell its waiter it was superseded.
	public bool Set(string kind, T item, long due, out T replaced)
	{
		bool had = pending.TryGetValue(kind, out Entry old);
		replaced = had ? old.Item : default(T);
		pending[kind] = new Entry
		{
			Item = item,
			Due = due,
			Seq = ++seq
		};
		return had;
	}

	public bool Remove(string kind, out T removed)
	{
		if (pending.Remove(kind, out Entry e))
		{
			removed = e.Item;
			return true;
		}
		removed = default(T);
		return false;
	}

	public bool Contains(string kind)
	{
		return pending.ContainsKey(kind);
	}

	// The due request that was posted first, if any is due at 'now'.
	public bool TryTakeDue(long now, out string kind, out T item)
	{
		string best = null;
		Entry bestEntry = null;
		foreach (KeyValuePair<string, Entry> kv in pending)
		{
			if (kv.Value.Due <= now && (bestEntry == null || kv.Value.Seq < bestEntry.Seq))
			{
				best = kv.Key;
				bestEntry = kv.Value;
			}
		}
		if (bestEntry == null)
		{
			kind = null;
			item = default(T);
			return false;
		}
		pending.Remove(best);
		kind = best;
		item = bestEntry.Item;
		return true;
	}

	// Milliseconds until the next request is due: 0 = something is due now, -1 = nothing pending.
	public long WaitTime(long now)
	{
		long best = -1L;
		foreach (Entry e in pending.Values)
		{
			long w = Math.Max(0L, e.Due - now);
			if (best < 0 || w < best)
			{
				best = w;
			}
		}
		return best;
	}

	public List<T> Clear()
	{
		List<T> all = new List<T>();
		foreach (Entry e in pending.Values)
		{
			all.Add(e.Item);
		}
		pending.Clear();
		return all;
	}
}

// The one background thread that talks to the devices. Profile apply/restore, lighting, brightness and
// onboard writes all run here, one at a time, so the tray, the UI and the keyboard hook never wait for
// a USB transfer or a lock held by one.
internal sealed class DeviceWorker : IDisposable
{
	private sealed class Job
	{
		public Action Work;

		public TaskCompletionSource<bool> Done;
	}

	private readonly LatestQueue<Job> queue = new LatestQueue<Job>();

	private readonly object gate = new object();

	private readonly AutoResetEvent wake = new AutoResetEvent(initialState: false);

	private readonly Thread thread;

	private readonly Stopwatch clock = Stopwatch.StartNew();

	private volatile bool stopping;

	private volatile string running;

	// Work waits (queued, latest per kind) while another process drives the devices (the self-test).
	private bool paused;

	public DeviceWorker()
	{
		thread = new Thread(Loop)
		{
			IsBackground = true,
			Name = "LiteSynapse devices"
		};
		thread.Start();
	}

	// True while a job runs on the worker (for status texts).
	public bool Busy => running != null;

	// Runs 'work' on the worker after delayMs, unless another Post of the same kind comes first.
	// The task completes with true once the work ran (also when it threw; that is logged), or false
	// if it was replaced, cancelled or the worker stopped.
	public Task<bool> Post(string kind, Action work, int delayMs = 0)
	{
		Job job = new Job
		{
			Work = work,
			Done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
		};
		Job old;
		lock (gate)
		{
			if (stopping)
			{
				job.Done.TrySetResult(false);
				return job.Done.Task;
			}
			queue.Set(kind, job, clock.ElapsedMilliseconds + Math.Max(0, delayMs), out old);
		}
		old?.Done.TrySetResult(false);
		wake.Set();
		return job.Done.Task;
	}

	public bool Cancel(string kind)
	{
		Job old;
		bool had;
		lock (gate)
		{
			had = queue.Remove(kind, out old);
		}
		old?.Done.TrySetResult(false);
		return had;
	}

	public bool IsPending(string kind)
	{
		lock (gate)
		{
			return queue.Contains(kind);
		}
	}

	// Holds all work until Resume; what is posted meanwhile stays queued (latest per kind). Waits up to waitMs
	// for the job that is running to finish, so nothing of ours is on the wire when this returns.
	public void Pause(int waitMs)
	{
		lock (gate)
		{
			paused = true;
		}
		Stopwatch sw = Stopwatch.StartNew();
		while (running != null && sw.ElapsedMilliseconds < waitMs)
		{
			Thread.Sleep(10);
		}
	}

	public void Resume()
	{
		lock (gate)
		{
			paused = false;
		}
		wake.Set();
	}

	private void Loop()
	{
		while (true)
		{
			string kind = null;
			Job job = null;
			int sleep;
			lock (gate)
			{
				if (stopping && queue.Count == 0)
				{
					return;
				}
				long now = clock.ElapsedMilliseconds;
				if (!paused && queue.TryTakeDue(now, out kind, out job))
				{
					sleep = 0;
					// Set under the lock, so Pause never misses a job that was just taken.
					running = kind;
				}
				else
				{
					long w = paused ? -1 : queue.WaitTime(now);
					sleep = (w < 0) ? Timeout.Infinite : (int)Math.Min(w, int.MaxValue);
					if (stopping)
					{
						// Shutting down: delayed work (like a pending restore) is dropped, not waited for.
						foreach (Job j in queue.Clear())
						{
							j.Done.TrySetResult(false);
						}
						return;
					}
				}
			}
			if (job == null)
			{
				wake.WaitOne(sleep);
				continue;
			}
			try
			{
				job.Work();
			}
			catch (Exception ex)
			{
				Cfg.Log("device " + kind + ": " + ex.Message);
			}
			finally
			{
				running = null;
				job.Done.TrySetResult(true);
			}
		}
	}

	// Lets work that is already due finish (waits up to 3 s), drops delayed work, and stops the thread.
	public void Dispose()
	{
		lock (gate)
		{
			stopping = true;
		}
		wake.Set();
		thread.Join(3000);
	}
}
