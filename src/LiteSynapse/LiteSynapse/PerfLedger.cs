using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace LiteSynapse;

// What LiteSynapse itself last set on the Performance tab, and when.
//
// Most settings read back from the registry, so the tab can show what the PC is really set to. Some cannot:
// a bcdedit value (dynamic tick, DEP) lives in the boot store, which only an administrator can read, and a
// netsh setting has no registry value at all. Before this ledger those rows read "can't tell" forever, even
// right after LiteSynapse had applied them - the "it doesn't recognise whether it went through" problem.
//
// The elevated script now reports, per setting, whether every one of its steps succeeded; those are written
// here. A row whose state Windows will not tell us shows what LiteSynapse last set instead, and says so. A
// setting that needs a restart and was changed since this boot shows as waiting for the restart.
internal sealed class PerfLedger
{
	internal sealed class Entry
	{
		public bool On { get; set; }

		// UTC.
		public DateTime At { get; set; }
	}

	private readonly Dictionary<string, Entry> entries;

	private readonly string file;

	private PerfLedger(string file, Dictionary<string, Entry> entries)
	{
		this.file = file;
		this.entries = entries;
	}

	public static string DefaultFile => Path.Combine(Cfg.Dir, "perf-applied.json");

	// A missing or unreadable file is an empty ledger, never an error: it only adds information.
	public static PerfLedger Load(string file = null)
	{
		string path = file ?? DefaultFile;
		Dictionary<string, Entry> map = null;
		try
		{
			if (File.Exists(path))
			{
				map = JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(path));
			}
		}
		catch (Exception ex)
		{
			Cfg.Log("performance ledger: " + ex.Message);
		}
		return new PerfLedger(path, new Dictionary<string, Entry>(map ?? new Dictionary<string, Entry>(), StringComparer.OrdinalIgnoreCase));
	}

	public Entry Get(string id)
	{
		return (id != null && entries.TryGetValue(id, out Entry e)) ? e : null;
	}

	public void Set(string id, bool on, DateTime atUtc)
	{
		if (string.IsNullOrEmpty(id))
		{
			return;
		}
		entries[id] = new Entry
		{
			On = on,
			At = atUtc
		};
	}

	public void Save()
	{
		try
		{
			string dir = Path.GetDirectoryName(file);
			if (!string.IsNullOrEmpty(dir))
			{
				Directory.CreateDirectory(dir);
			}
			string tmp = file + ".tmp";
			File.WriteAllText(tmp, JsonSerializer.Serialize(entries));
			File.Move(tmp, file, overwrite: true);
		}
		catch (Exception ex)
		{
			Cfg.Log("performance ledger: " + ex.Message);
		}
	}

	// When this boot started, in UTC: a change made after it that needs a restart has not taken effect yet.
	public static DateTime BootUtc => DateTime.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);

	// The state a row shows, given what Windows said (probe: on | off | part | other | unknown) and what
	// LiteSynapse last set. Pure, so the rules are tested.
	//
	// probeIsRunning: the probe reports the running boot (DEP policy, the performance counter's clock) rather
	// than the stored configuration (a registry value). After a change that needs a restart the two differ
	// until the restart, and only the configured one says whether the change went through.
	//
	//   - Nothing of ours on record: what Windows said.
	//   - Ours needs a restart and was made since this boot:
	//       running probe    -> show ours, waiting for the restart (unless it is already running that way);
	//       configured probe -> it agrees: show it, waiting for the restart; it cannot say: show ours, waiting;
	//                           it says something else: something changed it after us, so show what it says.
	//   - Otherwise: what Windows said, or ours (flagged as remembered) when Windows cannot say.
	internal static (string State, bool Remembered, bool Restart) Resolve(string probe, Entry last, bool needsReboot,
		bool probeIsRunning, DateTime bootUtc)
	{
		string read = string.IsNullOrEmpty(probe) ? "unknown" : probe;
		if (last == null)
		{
			return (read, false, false);
		}
		string ours = last.On ? "on" : "off";
		if (needsReboot && last.At > bootUtc)
		{
			if (probeIsRunning)
			{
				return (read == ours) ? (ours, false, false) : (ours, true, true);
			}
			if (read == ours)
			{
				return (ours, false, true);
			}
			if (read == "unknown")
			{
				return (ours, true, true);
			}
			return (read, false, false);
		}
		if (read == "unknown")
		{
			return (ours, true, false);
		}
		return (read, false, false);
	}
}
