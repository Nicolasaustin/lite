using System;
using System.IO;
using System.Text.Json;

namespace LiteSynapse;

public static class Cfg
{
	public static readonly string Dir = Init();

	private static readonly JsonSerializerOptions Opt = new JsonSerializerOptions
	{
		WriteIndented = true,
		PropertyNameCaseInsensitive = true
	};

	private static readonly object L = new object();

	public static bool Quiet;

	public static string Profiles => Path.Combine(Dir, "profiles.json");

	public static string Games => Path.Combine(Dir, "games.json");

	public static string Calib => Path.Combine(Dir, "calibration.json");

	public static string LogFile => Path.Combine(Dir, "log.txt");

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

	public static void Log(string s)
	{
		if (Quiet)
		{
			return;
		}
		try
		{
			Directory.CreateDirectory(Dir);
			lock (L)
			{
				File.AppendAllText(LogFile, $"{DateTime.Now:HH:mm:ss} {s}\n");
			}
		}
		catch
		{
		}
	}
}
