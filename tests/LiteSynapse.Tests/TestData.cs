using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LiteSynapse;

namespace LiteSynapse.Tests;

// Copies of the user's real data files (LiteSynapse/data in the repo), so tests never touch the originals.
internal static class TestData
{
	public static string RepoDataDir()
	{
		DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null)
		{
			string candidate = Path.Combine(dir.FullName, "LiteSynapse", "data");
			if (File.Exists(Path.Combine(candidate, "profiles.json")) && File.Exists(Path.Combine(candidate, "games.json")))
			{
				return candidate;
			}
			dir = dir.Parent;
		}
		throw new InvalidOperationException("LiteSynapse/data/profiles.json not found above " + AppContext.BaseDirectory);
	}

	// A fresh temp folder holding profiles.json and games.json copied from the repo.
	public static string CopyRealData()
	{
		string src = RepoDataDir();
		string dst = Path.Combine(Path.GetTempPath(), "litesynapse-tests-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dst);
		File.Copy(Path.Combine(src, "profiles.json"), Path.Combine(dst, "profiles.json"));
		File.Copy(Path.Combine(src, "games.json"), Path.Combine(dst, "games.json"));
		return dst;
	}

	public static (ProfileStore Store, List<GameRule> Games, string Dir) LoadRealCopy()
	{
		string dir = CopyRealData();
		ProfileStore store = Cfg.Load<ProfileStore>(Path.Combine(dir, "profiles.json"));
		List<GameRule> games = Cfg.Load<List<GameRule>>(Path.Combine(dir, "games.json"));
		return (store, games, dir);
	}
}

// The onboard writer as it was before this change (git f9b59cc..ebdc9f6 OnboardMemory.Write), recording instead of
// sending. Used to prove the new builder keeps the exact command order and bytes for existing profiles.
internal static class LegacyOnboard
{
	public static int Usage(string key)
	{
		if (string.IsNullOrEmpty(key))
		{
			return 0;
		}
		string n = (key.StartsWith("KEY_") ? key.Substring(4) : key);
		if (n.Length == 1 && n[0] >= 'A' && n[0] <= 'Z')
		{
			return 4 + (n[0] - 65);
		}
		if (n.Length == 1 && n[0] >= '1' && n[0] <= '9')
		{
			return 30 + (n[0] - 49);
		}
		if (n == "0")
		{
			return 39;
		}
		if (n.Length >= 2 && n[0] == 'F' && int.TryParse(n.Substring(1), out var f) && f >= 1 && f <= 12)
		{
			return 58 + (f - 1);
		}
		return n switch
		{
			"ENTER" => 40, "ESCAPE" => 41, "BACKSPACE" => 42, "TAB" => 43, "SPACEBAR" => 44,
			"HYPEN" or "HYPHEN" => 45, "EQUAL" => 46, "OPEN_SQUARE_BRACKET" => 47, "CLOSE_SQUARE_BRACKET" => 48,
			"BACKSLASH" => 49, "SEMICOLON" => 51, "APOSTROPHE" => 52, "GRAVE" => 53, "COMMA" => 54, "PERIOD" => 55,
			"SLASH" => 56, "CAPS_LOCK" => 57, "RIGHT_ARROW" => 79, "LEFT_ARROW" => 80, "DOWN_ARROW" => 81,
			"UP_ARROW" => 82, "LEFT_CTRL" => 224, "LEFT_SHIFT" => 225, "LEFT_ALT" => 226, "RIGHT_CTRL" => 228,
			"RIGHT_SHIFT" => 229, "RIGHT_ALT" => 230, _ => 0,
		};
	}

	public static int ModMask(IEnumerable<string> mods)
	{
		return mods.Aggregate(0, (int m, string s) => m | (s switch
		{
			"KEY_LEFT_CTRL" => 1, "KEY_LEFT_SHIFT" => 2, "KEY_LEFT_ALT" => 4,
			"KEY_RIGHT_CTRL" => 16, "KEY_RIGHT_SHIFT" => 32, "KEY_RIGHT_ALT" => 64, _ => 0,
		}));
	}

	// (class, id, size, args) in send order. guid fills the first 16 bytes of the profile blob.
	public static List<(byte Class, byte Id, byte Size, byte[] Args)> Commands(int slot, MouseProfile p, byte[] guid)
	{
		List<(byte, byte, byte, byte[])> sent = new List<(byte, byte, byte, byte[])>();
		void Send(byte cls, byte b, byte size, byte[] args) => sent.Add((cls, b, size, args));
		byte S = (byte)slot;
		Send(5, 2, 1, new byte[1] { S });
		Send(0, 142, 2, new byte[2] { S, 0 });
		byte[] blob = new byte[250];
		guid.CopyTo(blob, 0);
		byte[] name = Encoding.UTF8.GetBytes((p.Name.Length > 40) ? p.Name.Substring(0, 40) : p.Name);
		Array.Copy(name, 0, blob, 16, name.Length);
		for (int off = 0; off < 250; off += 64)
		{
			int len = Math.Min(64, 250 - off);
			byte[] a = new byte[5 + len];
			a[0] = S;
			a[2] = (byte)off;
			a[4] = 250;
			Array.Copy(blob, off, a, 5, len);
			Send(5, 8, (byte)(5 + len), a);
		}
		List<int> stages = ((p.DpiStages.Count > 0) ? p.DpiStages : new List<int> { p.Dpi }).Select((int d) => Math.Clamp(d, 100, 20000)).Take(5).ToList();
		int active = Math.Clamp((p.DpiStages.Count <= 0) ? 1 : p.DpiActive, 1, stages.Count);
		int dpi = stages[active - 1];
		Send(4, 5, 7, new byte[7] { S, (byte)(dpi >> 8), (byte)dpi, (byte)(dpi >> 8), (byte)dpi, 0, 0 });
		List<byte> st = new List<byte> { S, (byte)active, (byte)stages.Count };
		for (int i = 0; i < stages.Count; i++)
		{
			st.AddRange(new byte[7] { (byte)(i + 1), (byte)(stages[i] >> 8), (byte)stages[i], (byte)(stages[i] >> 8), (byte)stages[i], 0, 0 });
		}
		Send(4, 6, (byte)st.Count, st.ToArray());
		Send(15, 4, 3, new byte[3] { S, 0, 255 });
		Send(2, 12, 10, new byte[6] { S, 1, 0, 1, 1, 2 });
		Send(2, 12, 10, new byte[6] { S, 2, 0, 1, 1, 1 });
		foreach (var (id, t) in p.Buttons)
		{
			int code = OnboardMemory.ButtonCode(id);
			int use = Usage(t.Key);
			if (code != 0 && use != 0)
			{
				Send(2, 12, 10, new byte[7] { S, (byte)code, 0, 2, 2, (byte)ModMask(t.Mods), (byte)use });
			}
		}
		Send(6, 142, 14, new byte[14]);
		return sent;
	}
}
