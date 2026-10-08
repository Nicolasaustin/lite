using System;
using System.IO;
using System.Text;
using Xunit;

namespace LiteSynapse.Tests;

public class SettingsAndLogTests
{
	private static string TempDir()
	{
		string dir = Path.Combine(Path.GetTempPath(), "litesynapse-settings-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
		return dir;
	}

	[Fact]
	public void AnOldSettingsFileKeepsTheOldBehaviour()
	{
		string dir = TempDir();
		try
		{
			string path = Path.Combine(dir, "settings.json");
			File.WriteAllText(path, "{\n  \"DefaultMouse\": \"CS2\",\n  \"DefaultKeyboard\": \"gamer - S3\"\n}");
			AppSettings s = Cfg.Load<AppSettings>(path);
			Assert.True(s.LiveWrite);
			Assert.False(s.CompetitiveSafe);
			Assert.Equal("", s.GameKeyboardDefault);
			Assert.Equal("CS2", s.DefaultMouse);
			Assert.Equal("gamer - S3", s.DefaultKeyboard);
		}
		finally
		{
			Directory.Delete(dir, recursive: true);
		}
	}

	[Fact]
	public void SettingsRoundTripKeepsUnknownFields()
	{
		string dir = TempDir();
		try
		{
			string path = Path.Combine(dir, "settings.json");
			File.WriteAllText(path, "{ \"LiveWrite\": false, \"FutureOption\": { \"x\": 1 }, \"WindowWidth\": 1280 }");
			AppSettings s = Cfg.Load<AppSettings>(path);
			Assert.False(s.LiveWrite);
			s.CompetitiveSafe = true;
			s.GameKeyboardDefault = "gamer - S3";
			Cfg.Save(path, s);
			string json = File.ReadAllText(path);
			Assert.Contains("FutureOption", json);
			Assert.Contains("WindowWidth", json);
			AppSettings back = Cfg.Load<AppSettings>(path);
			Assert.False(back.LiveWrite);
			Assert.True(back.CompetitiveSafe);
			Assert.Equal("gamer - S3", back.GameKeyboardDefault);
		}
		finally
		{
			Directory.Delete(dir, recursive: true);
		}
	}

	[Fact]
	public void LogWriterAppendsAndRotatesAtTheLimit()
	{
		string dir = TempDir();
		try
		{
			string log = Path.Combine(dir, "log.txt");
			string old = Path.Combine(dir, "log.old.txt");
			LogFileWriter w = new LogFileWriter(log, old, 100);
			w.Append("first line\n");
			w.Flush();
			Assert.Equal("first line\n", ReadShared(log));
			w.Append(new string('x', 120) + "\n");
			w.Flush();
			// The big line pushed the file past the limit: it became log.old.txt.
			Assert.True(File.Exists(old));
			Assert.StartsWith("first line\n", File.ReadAllText(old));
			w.Append("after rotate\n");
			w.Flush();
			Assert.Equal("after rotate\n", ReadShared(log));
			w.Close();
		}
		finally
		{
			Directory.Delete(dir, recursive: true);
		}
	}

	[Fact]
	public void LogWriterAppendsToAnExistingFile()
	{
		string dir = TempDir();
		try
		{
			string log = Path.Combine(dir, "log.txt");
			File.WriteAllText(log, "old\n");
			LogFileWriter w = new LogFileWriter(log, Path.Combine(dir, "log.old.txt"), 1024 * 1024);
			w.Append("new\n");
			w.Flush();
			Assert.Equal("old\nnew\n", ReadShared(log));
			w.Close();
		}
		finally
		{
			Directory.Delete(dir, recursive: true);
		}
	}

	[Fact]
	public void LogIsWrittenInTheBackground()
	{
		// Log() only queues; FlushLog waits for the writer thread.
		string marker = "test marker " + Guid.NewGuid().ToString("N");
		Cfg.Log(marker);
		Assert.True(Cfg.FlushLog(5000));
		Assert.Contains(marker, Cfg.ReadLogTail(5000));
	}

	[Fact]
	public void BackupCopiesTheFilesIntoANewFolder()
	{
		string dir = TempDir();
		string backup = null;
		try
		{
			string a = Path.Combine(dir, "profiles.json");
			File.WriteAllText(a, "{\"Mouse\":[]}");
			backup = Cfg.BackupFiles(a, Path.Combine(dir, "missing.json"));
			Assert.NotNull(backup);
			Assert.StartsWith(Path.Combine(Cfg.Dir, "backup-"), backup);
			Assert.Equal("{\"Mouse\":[]}", File.ReadAllText(Path.Combine(backup, "profiles.json")));
			Assert.False(File.Exists(Path.Combine(backup, "missing.json")));
			string second = Cfg.BackupFiles(a);
			Assert.NotEqual(backup, second);
			Directory.Delete(second, recursive: true);
		}
		finally
		{
			Directory.Delete(dir, recursive: true);
			if (backup != null && Directory.Exists(backup))
			{
				Directory.Delete(backup, recursive: true);
			}
		}
	}

	private static string ReadShared(string path)
	{
		using FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
		using StreamReader r = new StreamReader(fs, Encoding.UTF8);
		return r.ReadToEnd();
	}
}
