using System;
using System.Collections.Generic;
using System.Globalization;

namespace LiteSynapse;

// Naga Left-Handed Edition lighting over the extended matrix commands (class 0F).
// Zones and argument layouts follow OpenRazer (razerchromacommon.c, Naga Left-Handed 2020 cases in razermouse_driver.c).
internal static class MouseLighting
{
	public const byte VarStore = 0x01;

	public static readonly string[] Effects = new string[4] { "off", "static", "breathing", "spectrum" };

	// Zones written by Apply and by onboard writes (the Naga Left-Handed answers for these three). Id is what profiles
	// and the window use for per-zone lighting (MouseLightingSettings.Zones).
	public static readonly (string Id, byte Led, string Name)[] Zones = new (string, byte, string)[3]
	{
		("scroll", 0x01, "scroll wheel"),
		("logo", 0x04, "logo"),
		("side", 0x10, "right side")
	};

	public static bool IsZone(string id)
	{
		return Array.Exists(Zones, ((string Id, byte Led, string Name) z) => z.Id == id);
	}

	// LEDs asked for by Probe; 0x00 addresses every zone at once.
	private static readonly (byte Led, string Name)[] ProbeLeds = new (byte, string)[5]
	{
		(0x00, "All zones"),
		(0x01, "Scroll wheel"),
		(0x04, "Logo"),
		(0x10, "Right side"),
		(0x11, "Left side")
	};

	internal readonly record struct Cmd(byte Led, string Zone, byte Class, byte Id, byte Size, byte[] Args);

	// Short text for the log and for "already showing": one setting, or each zone's when they differ.
	public static string Describe(MouseLightingSettings s)
	{
		if (s == null)
		{
			return "none";
		}
		static string One(MouseLightingSettings z)
		{
			return $"{z.Effect} {z.Color} {z.Brightness}%";
		}
		List<string> parts = new List<string>();
		bool same = true;
		foreach (var (id, _, _) in Zones)
		{
			string d = One(s.For(id));
			parts.Add(id + " " + d);
			same &= d == One(s);
		}
		return same ? One(s) : string.Join(", ", parts);
	}

	// Read-only: asks each LED for its brightness (0F/84) and lists the ones the mouse answers for. An LED the mouse
	// does not have answers "failed", which is expected here, so nothing is logged for it.
	public static List<(int Id, string Name)> Probe(RazerDevice mouse)
	{
		List<(int, string)> zones = new List<(int, string)>();
		if (mouse == null)
		{
			return zones;
		}
		foreach (var (led, name) in ProbeLeds)
		{
			if (mouse.Send(0x0F, 0x84, 3, new byte[2] { VarStore, led }, new byte[80]) == RazerDevice.StatusOk)
			{
				zones.Add((led, name));
			}
		}
		return zones;
	}

	// Applies s to every zone. storage is 0x01 (current settings) or an onboard slot number. null = ok.
	public static string Apply(RazerDevice mouse, MouseLightingSettings s, byte storage = VarStore)
	{
		if (s == null || mouse == null)
		{
			return null;
		}
		string invalid = Validate(s);
		if (invalid != null)
		{
			return invalid;
		}
		List<string> rejected = new List<string>();
		foreach (Cmd c in Commands(s, storage))
		{
			if (mouse.Command(c.Class, c.Id, c.Size, c.Args) == null && !rejected.Contains(c.Zone))
			{
				rejected.Add(c.Zone);
			}
		}
		if (rejected.Count == 0)
		{
			return null;
		}
		return "mouse rejected lighting for " + string.Join(", ", rejected);
	}

	// null when s (and each of its zones) can be written, otherwise why not.
	public static string Validate(MouseLightingSettings s)
	{
		if (s == null)
		{
			return null;
		}
		foreach (var (id, _, name) in Zones)
		{
			MouseLightingSettings z = s.For(id);
			if (z != s)
			{
				string bad = ValidateOne(z);
				if (bad != null)
				{
					return name + ": " + bad;
				}
			}
		}
		return ValidateOne(s);
	}

	private static string ValidateOne(MouseLightingSettings s)
	{
		if (EffectId(s.Effect) < 0)
		{
			return $"unknown lighting effect \"{s.Effect}\"";
		}
		if (NeedsColor(s.Effect) && !TryParseColor(s.Color, out _, out _, out _))
		{
			return $"invalid lighting colour \"{s.Color}\"";
		}
		return null;
	}

	// For each zone: the effect (0F/02), then its brightness (0F/04), with the zone's own settings if it has
	// them. s must pass Validate.
	internal static List<Cmd> Commands(MouseLightingSettings s, byte storage)
	{
		List<Cmd> list = new List<Cmd>();
		foreach (var (id, led, zone) in Zones)
		{
			MouseLightingSettings z = s.For(id);
			var (size, args) = EffectArgs(z, storage, led);
			list.Add(new Cmd(led, zone, 0x0F, 0x02, size, args));
			list.Add(new Cmd(led, zone, 0x0F, 0x04, 3, BrightnessArgs(storage, led, z.Brightness)));
		}
		return list;
	}

	internal static (byte Size, byte[] Args) EffectArgs(MouseLightingSettings s, byte storage, byte led)
	{
		TryParseColor(s.Color, out var r, out var g, out var b);
		return EffectId(s.Effect) switch
		{
			0 => (6, new byte[3] { storage, led, 0x00 }),
			1 => (9, new byte[9] { storage, led, 0x01, 0, 0, 0x01, r, g, b }),
			2 => (9, new byte[9] { storage, led, 0x02, 0x01, 0, 0x01, r, g, b }),
			3 => (6, new byte[3] { storage, led, 0x03 }),
			_ => throw new ArgumentException($"unknown lighting effect \"{s.Effect}\""),
		};
	}

	internal static byte[] BrightnessArgs(byte storage, byte led, int percent)
	{
		return new byte[3] { storage, led, ToByte(percent) };
	}

	// 0..100 % -> 0..255 (100 % = 255, as the existing onboard brightness write uses).
	internal static byte ToByte(int percent)
	{
		return (byte)Math.Round(Math.Clamp(percent, 0, 100) * 255 / 100.0);
	}

	// Index into Effects, or -1.
	internal static int EffectId(string effect)
	{
		for (int i = 0; i < Effects.Length; i++)
		{
			if (string.Equals(Effects[i], effect, StringComparison.OrdinalIgnoreCase))
			{
				return i;
			}
		}
		return -1;
	}

	private static bool NeedsColor(string effect)
	{
		int id = EffectId(effect);
		return id == 1 || id == 2;
	}

	// "#RRGGBB" (the '#' is optional).
	internal static bool TryParseColor(string color, out byte r, out byte g, out byte b)
	{
		r = g = b = 0;
		if (string.IsNullOrEmpty(color))
		{
			return false;
		}
		string hex = color.StartsWith('#') ? color.Substring(1) : color;
		if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var v))
		{
			return false;
		}
		r = (byte)(v >> 16);
		g = (byte)(v >> 8);
		b = (byte)v;
		return true;
	}
}
