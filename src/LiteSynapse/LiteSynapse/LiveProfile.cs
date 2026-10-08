using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LiteSynapse;

// Runs a mouse profile from the mouse's own memory ("live" profiles).
//
// Slots 1 and 2 are LiteSynapse's live slots; 3..5 belong to the user. data/onboard.json records what each
// slot holds with a content hash, so a profile that is already on the mouse is only switched to (or nothing is
// sent when the active slot already holds it), and a slot is only written when its content really differs.
// A write never goes to the active slot, so a half-written slot is never in use.
//
// Not thread-safe for device work: the Service calls it from its device worker only (the CLI tests use it
// from one thread). Names() may be called from any thread.
internal class LiveProfile
{
	private readonly string tablePath;

	private readonly object tableLock = new object();

	private OnboardSlots table;

	private DateTime tableStamp;

	private int returnSlot;

	// The live slot (1 or 2) the mouse was switched to by Apply; -1 when the mouse is on the user's own slot.
	private int liveSlot = -1;

	// When each slot was last written or switched to (bigger = more recent), to pick which live slot to overwrite.
	private readonly long[] lastUse = new long[OnboardSlots.SlotCount + 1];

	private long useClock;

	public string Status = "";

	public int ReturnSlot => returnSlot;

	public int LiveSlot => liveSlot;

	// The last Apply/Restore changed the active slot (the slot's own lighting and DPI may now be in effect).
	public bool Switched { get; private set; }

	// The last Apply wrote the mouse's memory.
	public bool Wrote { get; private set; }

	private static string SlotFile => Path.Combine(Cfg.Dir, "return-slot.txt");

	public static string TableFile => Path.Combine(Cfg.Dir, "onboard.json");

	public LiveProfile()
		: this(TableFile)
	{
	}

	public LiveProfile(string tablePath)
	{
		this.tablePath = tablePath;
		returnSlot = LoadReturnSlot();
	}

	public static int LoadReturnSlot()
	{
		try
		{
			if (File.Exists(SlotFile) && int.TryParse(File.ReadAllText(SlotFile).Trim(), out var s) && s > 2 && s <= 5)
			{
				return s;
			}
		}
		catch
		{
		}
		try
		{
			int own = OnboardSlots.Load(TableFile).Slots.FirstOrDefault((int i) => i > 2 && i <= 5);
			if (own > 0)
			{
				return own;
			}
		}
		catch
		{
		}
		return -1;
	}

	private void Remember(int slot)
	{
		if (returnSlot == slot)
		{
			return;
		}
		returnSlot = slot;
		try
		{
			File.WriteAllText(SlotFile, slot.ToString());
		}
		catch
		{
		}
	}

	// ---- Slot table -------------------------------------------------------------------------------------

	// Reloads data/onboard.json when someone else (the classic window, another copy) rewrote it.
	private OnboardSlots Table()
	{
		lock (tableLock)
		{
			DateTime stamp = Stamp();
			if (table == null || stamp != tableStamp)
			{
				table = OnboardSlots.Load(tablePath);
				tableStamp = stamp;
			}
			return table;
		}
	}

	private void SaveTable()
	{
		lock (tableLock)
		{
			try
			{
				table.Save(tablePath);
			}
			catch (Exception ex)
			{
				Cfg.Log("onboard.json: " + ex.Message);
			}
			tableStamp = Stamp();
		}
	}

	private DateTime Stamp()
	{
		try
		{
			return File.Exists(tablePath) ? File.GetLastWriteTimeUtc(tablePath) : DateTime.MinValue;
		}
		catch
		{
			return DateTime.MinValue;
		}
	}

	// Slot number -> profile name last written there, for the UI.
	public Dictionary<string, string> Names()
	{
		lock (tableLock)
		{
			return Table().Names();
		}
	}

	private void Touch(int slot)
	{
		if (slot >= 1 && slot <= OnboardSlots.SlotCount)
		{
			lastUse[slot] = ++useClock;
		}
	}

	// Writes p into slot and records it. The slot is marked unknown first, so a write that fails half way is
	// never mistaken for a good copy.
	public string WriteSlot(RazerDevice dev, int slot, MouseProfile p, Action<string> progress = null)
	{
		string hash = OnboardSlots.ContentHash(p);
		lock (tableLock)
		{
			Table().Forget(slot);
			SaveTable();
		}
		string err = OnboardMemory.Write(dev, slot, p, progress);
		if (err != null)
		{
			return err;
		}
		lock (tableLock)
		{
			Table().Set(slot, p.Name, hash);
			SaveTable();
		}
		Touch(slot);
		return null;
	}

	// ---- Live profile -----------------------------------------------------------------------------------

	// Makes the mouse run p from a slot: nothing is sent when the active slot already holds it, a live slot that
	// holds it is switched to, otherwise the least recently used live slot that is not active is written first.
	public string Apply(RazerDevice dev, MouseProfile p)
	{
		Switched = false;
		Wrote = false;
		int active = dev.GetActiveSlot() ?? (-1);
		if (active < 1)
		{
			return "could not read the mouse's active slot";
		}
		if (active > 2)
		{
			Remember(active);
		}
		string hash = OnboardSlots.ContentHash(p);
		SlotPlan plan = Table().PlanApply(active, hash, lastUse);
		int slot = plan.Slot;
		if (plan.Action == SlotAction.Write)
		{
			string err = WriteSlot(dev, slot, p);
			if (err != null)
			{
				return err;
			}
			Wrote = true;
		}
		if (plan.Action != SlotAction.None)
		{
			if (!dev.SetActiveSlot(slot))
			{
				return "could not activate the slot";
			}
			Switched = true;
		}
		Touch(slot);
		liveSlot = OnboardSlots.IsLiveSlot(slot) ? slot : (-1);
		dev.SetDpi(p.Dpi);
		dev.SetPolling(p.PollingRate);
		Status = $"\"{p.Name}\" is running on the mouse (slot {slot}, {OnboardMemory.SlotNames[slot]})";
		string how = plan.Action switch
		{
			SlotAction.None => "already active",
			SlotAction.Switch => "switched, no write",
			_ => "written and switched",
		};
		Cfg.Log($"live slot {slot} <- {p.Name}: {how}");
		return null;
	}

	// Puts p into an idle live slot ahead of time (no switch), so the next game start only switches slots.
	// Returns null when done or when nothing was needed.
	public string Prewrite(RazerDevice dev, MouseProfile p)
	{
		int active = dev.GetActiveSlot() ?? (-1);
		if (active < 1)
		{
			return "could not read the mouse's active slot";
		}
		if (active > 2)
		{
			Remember(active);
		}
		SlotPlan plan = Table().PlanPrewrite(active, OnboardSlots.ContentHash(p), lastUse);
		if (plan.Action != SlotAction.Write)
		{
			return null;
		}
		string err = WriteSlot(dev, plan.Slot, p);
		Cfg.Log($"pre-wrote \"{p.Name}\" to slot {plan.Slot}: {err ?? "ok"}");
		return err;
	}

	// Forgets which live slot is active (after a reconnect); what the slots hold stays known.
	public void Forget()
	{
		liveSlot = -1;
	}

	// Puts the mouse back on the user's own slot (the last slot 3..5 it was on).
	public string Restore(RazerDevice dev)
	{
		Switched = false;
		Wrote = false;
		int active = dev.GetActiveSlot() ?? (-1);
		if (active < 1)
		{
			return "could not read the mouse's active slot";
		}
		liveSlot = -1;
		if (active > 2)
		{
			Remember(active);
			Status = "";
			return null;
		}
		if (returnSlot > 2)
		{
			if (!dev.SetActiveSlot(returnSlot))
			{
				return "could not switch back";
			}
			Switched = true;
			Touch(returnSlot);
			Status = $"back on slot {returnSlot} ({OnboardMemory.SlotNames[returnSlot]})";
		}
		else
		{
			Status = "";
		}
		return null;
	}
}
