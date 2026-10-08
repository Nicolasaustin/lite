using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LiteSynapse;

internal class LiveProfile
{
	private int returnSlot;

	private int liveSlot = -1;

	private string hash;

	public string Status = "";

	public int ReturnSlot => returnSlot;

	private static string SlotFile => Path.Combine(Cfg.Dir, "return-slot.txt");

	public LiveProfile()
	{
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
			List<int> slots = (from k in Cfg.Load<Dictionary<string, string>>(Path.Combine(Cfg.Dir, "onboard.json")).Keys
				select int.TryParse(k, out var result) ? result : 0 into i
				where i > 2 && i <= 5
				orderby i
				select i).ToList();
			if (slots.Count > 0)
			{
				return slots[0];
			}
		}
		catch
		{
		}
		return -1;
	}

	private void Remember(int slot)
	{
		returnSlot = slot;
		try
		{
			if (!File.Exists(SlotFile) || File.ReadAllText(SlotFile).Trim() != slot.ToString())
			{
				File.WriteAllText(SlotFile, slot.ToString());
			}
		}
		catch
		{
		}
	}

	private static string Hash(MouseProfile p)
	{
		return $"{p.Name}|{p.Dpi}|{string.Join(",", p.DpiStages)}|{p.DpiActive}|{p.PollingRate}|" + string.Join(";", from k in p.Buttons
			orderby k.Key
			select k.Key + "=" + string.Join("+", k.Value.Mods) + "+" + k.Value.Key);
	}

	public string Apply(RazerDevice dev, MouseProfile p)
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
		string h = Hash(p);
		if (h == hash && liveSlot > 0)
		{
			if (!dev.SetActiveSlot(liveSlot))
			{
				return "could not activate the slot";
			}
		}
		else
		{
			int target = ((active != 1) ? 1 : 2);
			string err = OnboardMemory.Write(dev, target, p);
			if (err != null)
			{
				return err;
			}
			if (!dev.SetActiveSlot(target))
			{
				return "could not activate the slot";
			}
			hash = h;
			liveSlot = target;
		}
		dev.SetDpi(p.Dpi);
		dev.SetPolling(p.PollingRate);
		Status = $"\"{p.Name}\" is running on the mouse (slot {liveSlot}, {OnboardMemory.SlotNames[liveSlot]})";
		return null;
	}

	public void Forget()
	{
		hash = null;
		liveSlot = -1;
	}

	public string Restore(RazerDevice dev)
	{
		int active = dev.GetActiveSlot() ?? (-1);
		if (active < 1)
		{
			return "could not read the mouse's active slot";
		}
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
			Status = $"back on slot {returnSlot} ({OnboardMemory.SlotNames[returnSlot]})";
		}
		else
		{
			Status = "";
		}
		return null;
	}
}
