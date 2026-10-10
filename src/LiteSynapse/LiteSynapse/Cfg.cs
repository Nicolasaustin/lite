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

	// Profiles, settings and the log live in this Windows account's own folder (%LOCALAPPDATA%\LiteSynapse\data), not
	// next to LiteSynapse.exe: the program is in Program Files, which only an administrator can change - it runs as
	// one, so its own files must not be writable by programs that do not - and each account keeps its own profiles.
	// Setup copies them over from where an earlier version kept them; this does the same when Setup did not.
	private static string Init()
	{
		string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		string dir = Path.Combine(local, "LiteSynapse", "data");
		try
		{
			Directory.CreateDirectory(dir);
			if (!File.Exists(Path.Combine(dir, "profiles.json")))
			{
				// Newest first: the per-user install's data folder (1.2.6 and earlier), a data folder next to this
				// exe (a copy run from its own folder), and %APPDATA%\LiteSynapse (the first versions).
				foreach (string old in new[]
				{
					Path.Combine(local, "Programs", "LiteSynapse", "data"),
					Path.Combine(AppContext.BaseDirectory, "data"),
					Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LiteSynapse")
				})
				{
					if (!string.Equals(Path.GetFullPath(old).TrimEnd('\\', '/'), Path.GetFullPath(dir).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)
						&& File.Exists(Path.Combine(old, "profiles.json")))
					{
						CopyMissing(old, dir);
						break;
					}
				}
			}
		}
		catch
		{
		}
		return dir;
	}

	// Copies what the target does not have yet, folders included; never overwrites, and leaves the logs behind.
	internal static void CopyMissing(string from, string to)
	{
		Directory.CreateDirectory(to);
		foreach (string f in Directory.GetFiles(from))
		{
			string name = Path.GetFileName(f);
			string target = Path.Combine(to, name);
			if (!File.Exists(target) && !name.StartsWith("log", StringComparison.OrdinalIgnoreCase))
			{
				File.Copy(f, target);
			}
		}
		foreach (string d in Directory.GetDirectories(from))
		{
			CopyMissing(d, Path.Combine(to, Path.GetFileName(d)));
		}
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
			// A file that is there but cannot be read is kept aside, so nothing that saves the empty default
			// afterwards can destroy the only copy - and the copy Save keeps from the time before is tried.
			try
			{
				File.Copy(path, path + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), overwrite: true);
			}
			catch
			{
			}
			try
			{
				string previous = path + ".bak";
				if (File.Exists(previous))
				{
					T old = JsonSerializer.Deserialize<T>(File.ReadAllText(previous), Opt);
					if (old != null)
					{
						Log("load " + path + ": using the previous copy (" + Path.GetFileName(previous) + ")");
						return old;
					}
				}
			}
			catch (Exception ex2)
			{
				Log("load " + path + ".bak: " + ex2.Message);
			}
		}
		return new T();
	}

	// Written to a temporary file, flushed to the disk itself, and then swapped in with the old file kept as
	// .bak: a crash, power cut or full disk mid-save must not leave profiles.json or settings.json empty,
	// which would load as defaults and lose the user's settings. (A rename alone is journalled, but the data
	// behind it is written lazily - without the flush a power cut could still leave a file of zeros.)
	public static void Save<T>(string path, T v)
	{
		Directory.CreateDirectory(Dir);
		string tmp = path + ".tmp";
		using (FileStream fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
		{
			JsonSerializer.Serialize(fs, v, Opt);
			fs.Flush(flushToDisk: true);
		}
		if (File.Exists(path))
		{
			File.Replace(tmp, path, path + ".bak", ignoreMetadataErrors: true);
		}
		else
		{
			File.Move(tmp, path);
		}
	}

	// Copies the files that exist into a new folder data/backup-<yyyyMMdd-HHmmss>[-n] and returns that folder,
	// or null when nothing could be backed up (the caller should then not overwrite the originals).
	public static string BackupFiles(params string[] paths)
	{
		try
		{
			string stamp = "backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
			string dir = Path.Combine(Dir, stamp);
			for (int n = 2; Directory.Exists(dir); n++)
			{
				dir = Path.Combine(Dir, stamp + "-" + n);
			}
			Directory.CreateDirectory(dir);
			foreach (string p in paths)
			{
				if (p != null && File.Exists(p))
				{
					File.Copy(p, Path.Combine(dir, Path.GetFileName(p)));
				}
			}
			return dir;
		}
		catch (Exception ex)
		{
			Log("backup: " + ex.Message);
			return null;
		}
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
			// A short wait for the writer, not a long one: this runs on the window's thread.
			FlushLog(20);
			if (!File.Exists(LogFile))
			{
				return "";
			}
			using FileStream fs = new FileStream(LogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
			// Only the end of the file: the last few hundred lines fit in 64 KB, and reading a megabyte from the
			// start to keep 200 lines was a hitch every time the Settings page refreshed.
			long start = Math.Max(0, fs.Length - 64 * 1024);
			fs.Seek(start, SeekOrigin.Begin);
			using StreamReader r = new StreamReader(fs, Encoding.UTF8);
			if (start > 0)
			{
				r.ReadLine();
			}
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
		if (DateTime.UtcNow < retryRotateAt)
		{
			return;
		}
		Close();
		try
		{
			File.Move(path, oldPath, overwrite: true);
		}
		catch
		{
			// Someone holds log.txt or log.old.txt without sharing: keep appending to log.txt rather than lose
			// lines, and do not try again (a reopen, a failed move and an exception) on every line - wait ten
			// minutes. If it is still held by then and the log has grown well past its limit, start it over.
			retryRotateAt = DateTime.UtcNow.AddMinutes(10);
			try
			{
				if (new FileInfo(path).Length > limit * 4)
				{
					using FileStream truncate = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
					truncate.SetLength(0);
				}
			}
			catch
			{
			}
		}
	}

	private DateTime retryRotateAt = DateTime.MinValue;
}
