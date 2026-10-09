using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace LiteSynapse.Tests;

public class DeviceWorkerTests
{
	[Fact]
	public void OnlyTheLatestRequestOfAKindRuns()
	{
		LatestQueue<string> q = new LatestQueue<string>();
		q.Set("mouse", "CS2", 0, out _);
		q.Set("mouse", "Valorant", 0, out string replaced);
		Assert.Equal("CS2", replaced);
		Assert.True(q.TryTakeDue(0, out string kind, out string item));
		Assert.Equal("mouse", kind);
		Assert.Equal("Valorant", item);
		Assert.False(q.TryTakeDue(0, out _, out _));
	}

	[Fact]
	public void DifferentKindsRunInTheOrderTheyWerePosted()
	{
		LatestQueue<string> q = new LatestQueue<string>();
		q.Set("kb", "brightness", 0, out _);
		q.Set("mouse", "slot", 0, out _);
		q.Set("kb", "brightness 2", 0, out _);
		Assert.True(q.TryTakeDue(0, out string k1, out _));
		Assert.True(q.TryTakeDue(0, out string k2, out string i2));
		Assert.Equal("mouse", k1);
		Assert.Equal("kb", k2);
		Assert.Equal("brightness 2", i2);
	}

	// Alt-tab out of a game and back within 3 s: the delayed desktop restore is replaced by the game apply and
	// never runs; the mouse is not switched twice.
	[Fact]
	public void AGameComingBackCancelsThePendingRestore()
	{
		LatestQueue<string> q = new LatestQueue<string>();
		q.Set("mouse", "restore desktop", 1000 + ServicePlan.RestoreDelayMs, out _);
		Assert.False(q.TryTakeDue(2000, out _, out _));
		Assert.Equal(2000, q.WaitTime(2000));
		q.Set("mouse", "apply game", 2500, out string replaced);
		Assert.Equal("restore desktop", replaced);
		Assert.True(q.TryTakeDue(2500, out _, out string item));
		Assert.Equal("apply game", item);
		Assert.Equal(-1, q.WaitTime(5000));
	}

	[Fact]
	public void TheRestoreRunsOnceTheGameStayedAwayLongEnough()
	{
		LatestQueue<string> q = new LatestQueue<string>();
		q.Set("mouse", "restore desktop", 1000 + ServicePlan.RestoreDelayMs, out _);
		Assert.False(q.TryTakeDue(3999, out _, out _));
		Assert.True(q.TryTakeDue(4000, out _, out string item));
		Assert.Equal("restore desktop", item);
	}

	[Fact]
	public void ADueRequestIsNotHeldUpByADelayedOne()
	{
		LatestQueue<string> q = new LatestQueue<string>();
		q.Set("mouse", "restore", 3000, out _);
		q.Set("kb", "brightness", 0, out _);
		Assert.Equal(0, q.WaitTime(0));
		Assert.True(q.TryTakeDue(0, out string kind, out _));
		Assert.Equal("kb", kind);
		Assert.Equal(3000, q.WaitTime(0));
	}

	[Fact]
	public void WorkerRunsJobsOneAtATimeOnItsOwnThread()
	{
		using DeviceWorker w = new DeviceWorker();
		int caller = Thread.CurrentThread.ManagedThreadId;
		ConcurrentBag<int> threads = new ConcurrentBag<int>();
		int running = 0;
		int overlap = 0;
		List<Task<bool>> tasks = new List<Task<bool>>();
		for (int i = 0; i < 5; i++)
		{
			tasks.Add(w.Post("job" + i, () =>
			{
				if (Interlocked.Increment(ref running) > 1)
				{
					Interlocked.Increment(ref overlap);
				}
				threads.Add(Thread.CurrentThread.ManagedThreadId);
				Thread.Sleep(5);
				Interlocked.Decrement(ref running);
			}));
		}
		Assert.True(Task.WaitAll(tasks.ToArray(), 5000));
		Assert.All(tasks, (Task<bool> t) => Assert.True(t.Result));
		Assert.Equal(0, overlap);
		Assert.DoesNotContain(caller, threads);
	}

	[Fact]
	public void PostingTheSameKindAgainSupersedesTheWaitingJob()
	{
		using DeviceWorker w = new DeviceWorker();
		List<string> ran = new List<string>();
		Task<bool> restore = w.Post("mouse", () => ran.Add("restore"), 300);
		Task<bool> game = w.Post("mouse", () => ran.Add("game"));
		Assert.True(game.Wait(5000));
		Assert.True(game.Result);
		Assert.True(restore.Wait(5000));
		Assert.False(restore.Result);
		Thread.Sleep(400);
		Assert.Equal(new List<string> { "game" }, ran);
	}

	[Fact]
	public void PausedWorkWaitsUntilResumeAndThenOnlyTheLatestRuns()
	{
		using DeviceWorker w = new DeviceWorker();
		using ManualResetEventSlim release = new ManualResetEventSlim();
		ConcurrentQueue<string> ran = new ConcurrentQueue<string>();
		// A job that is running when Pause is called is waited for.
		Task<bool> busy = w.Post("mouse", () => { release.Wait(2000); ran.Enqueue("first"); });
		SpinWait.SpinUntil(() => w.Busy, 2000);
		Task pause = Task.Run(() => w.Pause(5000));
		Assert.False(pause.Wait(100));
		release.Set();
		Assert.True(pause.Wait(3000));
		Assert.True(busy.Result);
		// While paused nothing runs; the latest job of a kind is kept.
		Task<bool> old = w.Post("mouse", () => ran.Enqueue("stale"));
		Task<bool> latest = w.Post("mouse", () => ran.Enqueue("latest"));
		Task<bool> kb = w.Post("kb", () => ran.Enqueue("kb"));
		Assert.False(old.Result);
		Assert.False(latest.Wait(300));
		Assert.Equal(new[] { "first" }, ran.ToArray());
		w.Resume();
		Assert.True(latest.Wait(3000) && latest.Result);
		Assert.True(kb.Wait(3000) && kb.Result);
		Assert.Equal(new[] { "first", "latest", "kb" }, ran.ToArray());
	}

	[Fact]
	public void DisposeWhilePausedDropsTheWaitingWork()
	{
		DeviceWorker w = new DeviceWorker();
		w.Pause(100);
		Task<bool> t = w.Post("mouse", () => { });
		w.Dispose();
		Assert.True(t.Wait(3000));
		Assert.False(t.Result);
	}

	[Fact]
	public void CancelDropsAWaitingJob()
	{
		using DeviceWorker w = new DeviceWorker();
		bool ran = false;
		Task<bool> t = w.Post("prewrite", () => ran = true, 200);
		Assert.True(w.IsPending("prewrite"));
		Assert.True(w.Cancel("prewrite"));
		Assert.False(t.Result);
		Thread.Sleep(300);
		Assert.False(ran);
	}

	[Fact]
	public void AFailingJobDoesNotStopTheWorker()
	{
		using DeviceWorker w = new DeviceWorker();
		Task<bool> bad = w.Post("bad", () => throw new System.InvalidOperationException("device gone"));
		Task<bool> good = w.Post("good", () => { });
		Assert.True(bad.Wait(5000));
		Assert.True(good.Wait(5000));
		Assert.True(good.Result);
	}

	[Fact]
	public void DisposeRunsDueWorkAndDropsDelayedWork()
	{
		DeviceWorker w = new DeviceWorker();
		Task<bool> blocker = w.Post("a", () => Thread.Sleep(100));
		Task<bool> due = w.Post("b", () => { });
		Task<bool> later = w.Post("c", () => { }, 10000);
		w.Dispose();
		Assert.True(blocker.Result);
		Assert.True(due.Result);
		Assert.False(later.Result);
		Assert.False(w.Post("d", () => { }).Result);
	}
}
