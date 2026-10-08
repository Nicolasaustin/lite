using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;

namespace LiteSynapse;

public static class Cfg
{
	public static readonly string Dir = Init();

	private static readonly JsonSerializerOptions Opt = new JsonSerializerOptions
	{
		WriteIndented = true,
		PropertyNameCaseInsensitive = true
	};

	public static bool Quiet;

	// Above this size log.txt is moved to log.old.txt and a new file is started.
	internal const long LogLimit = 1024 * 1024;

	public static string Profiles => Path.Combine(Dir, "profiles.json");

	public static string Games => Path.Combine(Dir, "games.json");

	public static string Calib => Path.Combine(Dir, "calibration.json");

	public static string LogFile => Path.Combine(Dir, "log.txt");

	public static string OldLogFile => Path.Combine(Dir, "log.old.txt");

	private static string Init()
	{
		string dir = Path.Combine(AppContext.BaseDirectory, "data");
		try
		{
			Directory.CreateDirectory(dir);
			if (!File.Exists(Path.Combine(dir, "profiles.json")))
			{
				string old = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LiteSynapse");
				if (Directory.Exists(old))
				{
					string[] files = Directory.GetFiles(old);
					foreach (string f in files)
					{
						string target = Path.Combine(dir, Path.GetFileName(f));
						if (!File.Exists(target) && !f.EndsWith("log.txt", StringComparison.OrdinalIgnoreCase))
						{
							File.Copy(f, target);
						}
					}
				}
			}
		}
		catch
		{
		}
		return dir;
	}

	public static T Load<T>(string path) where T : new()
	{
		try
		{
			if (File.Exists(path))
			{
				T val = JsonSerializer.Deserialize<T>(File.ReadAllText(path), Opt);
				return (val != null) ? val : new T();
			}
		}
		catch (Exception ex)
		{
			Log("load " + path + ": " + ex.Message);
		}
		return new T();
	}

	public static void Save<T>(string path, T v)
	{
		Directory.CreateDirectory(Dir);
		File.WriteAllText(path, JsonSerializer.Serialize(v, Opt));
	}

	// ---- Logging --------------------------------------------------------------------------------
	// Log() only stamps the line and puts it in a queue, so the keyboard hook, the tray thread and the
	// device worker never touch the disk. One below-normal background thread writes the queue to a
	// file it keeps open.

	private static readonly Channel<string> logQueue = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
	{
		SingleReader = true,
		SingleWriter = false,
		AllowSynchronousContinuations = false
	});

	private static int logStarted;

	private static int logPending;

	private static readonly ManualResetEventSlim logIdle = new ManualResetEventSlim(initialState: true);

	public static void Log(string s)
	{
		if (Quiet)
		{
			return;
		}
		if (logStarted == 0 && Interlocked.Exchange(ref logStarted, 1) == 0)
		{
			StartLogWriter();
		}
		Interlocked.Increment(ref logPending);
		logIdle.Reset();
		if (!logQueue.Writer.TryWrite($"{DateTime.Now:HH:mm:ss} {s}\n"))
		{
			MarkWritten(1);
		}
	}

	// Waits (up to timeoutMs) until every queued line is on disk. Used before the process exits.
	public static bool FlushLog(int timeoutMs = 1000)
	{
		long end = Environment.TickCount64 + timeoutMs;
		while (Volatile.Read(ref logPending) > 0)
		{
			if (Environment.TickCount64 >= end)
			{
				return false;
			}
			logIdle.Wait(20);
		}
		return true;
	}

	// Reads the last lines of the log while the writer keeps the file open.
	public static string ReadLogTail(int lines)
	{
		try
		{
			FlushLog(300);
			if (!File.Exists(LogFile))
			{
				return "";
			}
			using FileStream fs = new FileStream(LogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
			using StreamReader r = new StreamReader(fs, Encoding.UTF8);
			Queue<string> tail = new Queue<string>();
			string line;
			while ((line = r.ReadLine()) != null)
			{
				tail.Enqueue(line);
				if (tail.Count > Math.Max(1, lines))
				{
					tail.Dequeue();
				}
			}
			return string.Join("\n", tail);
		}
		catch (Exception ex)
		{
			return "log unavailable: " + ex.Message;
		}
	}

	private static void MarkWritten(int n)
	{
		if (Interlocked.Add(ref logPending, -n) <= 0)
		{
			logIdle.Set();
		}
	}

	private static void StartLogWriter()
	{
		Thread t = new Thread(LogWriterLoop)
		{
			IsBackground = true,
			Name = "LiteSynapse log",
			Priority = ThreadPriority.BelowNormal
		};
		t.Start();
	}

	private static void LogWriterLoop()
	{
		ChannelReader<string> reader = logQueue.Reader;
		LogFileWriter file = new LogFileWriter(LogFile, OldLogFile, LogLimit);
		while (true)
		{
			int n = 0;
			try
			{
				if (!reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
				{
					return;
				}
				while (reader.TryRead(out string line))
				{
					n++;
					file.Append(line);
				}
				file.Flush();
			}
			catch
			{
				// A full or locked disk must never take the app down: drop this batch and try again later.
				file.Close();
				Thread.Sleep(1000);
			}
			finally
			{
				if (n > 0)
				{
					MarkWritten(n);
				}
			}
		}
	}
}

// Appends to one log file kept open, and starts over (old file -> oldPath) once it passes the limit.
internal sealed class LogFileWriter
{
	private readonly string path;

	private readonly string oldPath;

	private readonly long limit;

	private FileStream stream;

	public LogFileWriter(string path, string oldPath, long limit)
	{
		this.path = path;
		this.oldPath = oldPath;
		this.limit = limit;
	}

	public void Append(string line)
	{
		if (stream == null)
		{
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, 4096);
		}
		byte[] bytes = Encoding.UTF8.GetBytes(line);
		stream.Write(bytes, 0, bytes.Length);
		if (stream.Length >= limit)
		{
			Rotate();
		}
	}

	public void Flush()
	{
		stream?.Flush();
	}

	public void Close()
	{
		try
		{
			stream?.Dispose();
		}
		catch
		{
		}
		stream = null;
	}

	private void Rotate()
	{
		Close();
		try
		{
			File.Move(path, oldPath, overwrite: true);
		}
		catch
		{
			// Someone holds log.old.txt without sharing: keep appending to log.txt rather than lose lines.
		}
	}
}
