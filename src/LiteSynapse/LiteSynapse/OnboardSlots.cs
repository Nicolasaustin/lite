using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LiteSynapse;

// What one onboard slot of the mouse holds, as far as LiteSynapse knows.
public sealed class OnboardSlot
{
	public string Name { get; set; }

	// Content hash of what was written (OnboardSlots.ContentHash); null = content unknown, never reused.
	public string Hash { get; set; }
}

internal enum SlotAction
{
	// The active slot already holds the profile: send nothing.
	None,
	// Another slot already holds it: switch to that slot.
	Switch,
	// Write the profile into Slot (never the active one), then switch to it unless pre-writing.
	Write
}

internal readonly record struct SlotPlan(SlotAction Action, int Slot);

// The mouse's five onboard slots as recorded in data/onboard.json:
//   {"1":{"name":"CS2","hash":"9F2C..."},"3":{"name":"CS2"}}
// The old format {"3":"CS2"} (written by earlier versions and the classic window) is read too; such a slot has
// a name but no hash, so its content is treated as unknown.
// Slots 1 and 2 are LiteSynapse's live slots; 3..5 belong to the user and are never written or switched to
// for a live profile (only reused when the mouse is already on one that holds the profile).
internal sealed class OnboardSlots
{
	public const int SlotCount = 5;

	public static readonly int[] LiveSlots = new int[2] { 1, 2 };

	private readonly Dictionary<int, OnboardSlot> slots = new Dictionary<int, OnboardSlot>();

	public static bool IsLiveSlot(int slot)
	{
		return slot == 1 || slot == 2;
	}

	public OnboardSlot this[int slot]
	{
		get
		{
			if (!slots.TryGetValue(slot, out var s))
			{
				return null;
			}
			return s;
		}
	}

	public string HashOf(int slot)
	{
		return this[slot]?.Hash;
	}

	public void Set(int slot, string name, string hash)
	{
		Check(slot);
		slots[slot] = new OnboardSlot
		{
			Name = name,
			Hash = hash
		};
	}

	// The slot's content is no longer known (a write is in progress or failed).
	public void Forget(int slot)
	{
		slots.Remove(slot);
	}

	// Slot number (as a string) -> profile name, for the UI.
	public Dictionary<string, string> Names()
	{
		return slots.Where((KeyValuePair<int, OnboardSlot> kv) => !string.IsNullOrEmpty(kv.Value.Name)).OrderBy((KeyValuePair<int, OnboardSlot> kv) => kv.Key).ToDictionary((KeyValuePair<int, OnboardSlot> kv) => kv.Key.ToString(), (KeyValuePair<int, OnboardSlot> kv) => kv.Value.Name);
	}

	public IEnumerable<int> Slots => slots.Keys.OrderBy((int k) => k);

	private static void Check(int slot)
	{
		if (slot < 1 || slot > SlotCount)
		{
			throw new ArgumentOutOfRangeException(nameof(slot), "Slot must be 1-5");
		}
	}

	// ---- Planning ------------------------------------------------------------------------------------

	// What to do so the mouse runs the profile with this hash. lastUse[slot] orders the live slots by when they
	// were last written or switched to (bigger = more recent); the least recently used one is overwritten.
	// force: write whatever the records say. The records only know what LiteSynapse wrote; Razer Synapse (or a
	// driver reset) may have changed the mouse's memory while LiteSynapse was closed, so its first write after a
	// start, and the first after the mouse comes back, must not trust them.
	public SlotPlan PlanApply(int active, string hash, IReadOnlyList<long> lastUse, bool force = false)
	{
		// Nothing to send when the active live slot already holds the profile. A user slot (3..5) never counts,
		// even with the same content: while LiteSynapse runs the mouse runs from a live slot, so changes made in
		// LiteSynapse never touch the user's own slots.
		if (!force && hash != null && IsLiveSlot(active) && HashOf(active) == hash)
		{
			return new SlotPlan(SlotAction.None, active);
		}
		if (!force && hash != null)
		{
			foreach (int s in LiveSlots)
			{
				if (s != active && HashOf(s) == hash)
				{
					return new SlotPlan(SlotAction.Switch, s);
				}
			}
		}
		return new SlotPlan(SlotAction.Write, WriteTarget(active, lastUse));
	}

	// What to do to have the profile ready in a live slot without switching to it (while no game is in front).
	public SlotPlan PlanPrewrite(int active, string hash, IReadOnlyList<long> lastUse)
	{
		if (hash != null && LiveSlots.Any((int s) => HashOf(s) == hash))
		{
			return new SlotPlan(SlotAction.None, active);
		}
		return new SlotPlan(SlotAction.Write, WriteTarget(active, lastUse));
	}

	// The live slot that is not active and was used least recently (slot 1 on a tie, as before).
	private static int WriteTarget(int active, IReadOnlyList<long> lastUse)
	{
		int best = -1;
		foreach (int s in LiveSlots)
		{
			if (s == active)
			{
				continue;
			}
			if (best < 0 || Use(lastUse, s) < Use(lastUse, best))
			{
				best = s;
			}
		}
		return best;
	}

	private static long Use(IReadOnlyList<long> lastUse, int slot)
	{
		if (lastUse == null || slot >= lastUse.Count)
		{
			return 0L;
		}
		return lastUse[slot];
	}

	// ---- Content hash ---------------------------------------------------------------------------------

	// Hash of every command OnboardMemory.Write sends for this profile (built for slot 1, order-independent),
	// so any change to the profile or to how it is encoded gives a different hash.
	public static string ContentHash(MouseProfile p)
	{
		if (p == null)
		{
			return null;
		}
		List<string> parts = (from c in OnboardMemory.BuildCommands(1, p)
			select $"{c.Class:X2}{c.Id:X2}{c.Size:X2}:{Convert.ToHexString(c.Args ?? Array.Empty<byte>())}").ToList();
		parts.Sort(StringComparer.Ordinal);
		byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", parts)));
		return Convert.ToHexString(hash, 0, 12);
	}

	// ---- File format ----------------------------------------------------------------------------------

	public static OnboardSlots Parse(string json)
	{
		OnboardSlots t = new OnboardSlots();
		if (string.IsNullOrWhiteSpace(json))
		{
			return t;
		}
		using JsonDocument doc = JsonDocument.Parse(json);
		if (doc.RootElement.ValueKind != JsonValueKind.Object)
		{
			return t;
		}
		foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
		{
			if (!int.TryParse(prop.Name, out var slot) || slot < 1 || slot > SlotCount)
			{
				continue;
			}
			switch (prop.Value.ValueKind)
			{
			case JsonValueKind.String:
				t.slots[slot] = new OnboardSlot
				{
					Name = prop.Value.GetString()
				};
				break;
			case JsonValueKind.Object:
				t.slots[slot] = new OnboardSlot
				{
					Name = GetString(prop.Value, "name"),
					Hash = GetString(prop.Value, "hash")
				};
				break;
			}
		}
		return t;
	}

	private static string GetString(JsonElement obj, string name)
	{
		foreach (JsonProperty p in obj.EnumerateObject())
		{
			if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String)
			{
				return p.Value.GetString();
			}
		}
		return null;
	}

	public string ToJson()
	{
		using MemoryStream ms = new MemoryStream();
		using (Utf8JsonWriter w = new Utf8JsonWriter(ms, new JsonWriterOptions
		{
			Indented = true
		}))
		{
			w.WriteStartObject();
			foreach (int slot in Slots)
			{
				OnboardSlot s = slots[slot];
				w.WriteStartObject(slot.ToString());
				w.WriteString("name", s.Name ?? "");
				if (s.Hash != null)
				{
					w.WriteString("hash", s.Hash);
				}
				w.WriteEndObject();
			}
			w.WriteEndObject();
		}
		return Encoding.UTF8.GetString(ms.ToArray());
	}

	// Missing or unreadable file = nothing known.
	public static OnboardSlots Load(string path)
	{
		try
		{
			if (File.Exists(path))
			{
				return Parse(File.ReadAllText(path));
			}
		}
		catch (Exception ex)
		{
			Cfg.Log("load " + path + ": " + ex.Message);
		}
		return new OnboardSlots();
	}

	// Written to a temporary file and moved over the old one, so a crash or power cut mid-write leaves the
	// previous table rather than a truncated one (which loads as empty and forces every slot to be rewritten).
	public void Save(string path)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path));
		string tmp = path + ".tmp";
		File.WriteAllText(tmp, ToJson());
		File.Move(tmp, path, overwrite: true);
	}
}
