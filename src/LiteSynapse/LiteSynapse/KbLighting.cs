using System;

namespace LiteSynapse;

// BlackWidow Lite backlight effects (extended matrix effect 0F/02, layouts from OpenRazer razerchromacommon.c).
// The board has single-colour white LEDs, so every effect is sent with white.
internal static class KbLighting
{
	public const byte VarStore = 0x01;

	public const byte BacklightLed = 0x05;

	// The only effects the BlackWidow Lite has (OpenRazer creates matrix_effect_none, _static and _breath for it).
	public static readonly string[] Effects = new string[3] { "off", "static", "breathing" };

	private const byte R = 0xFF;

	private const byte G = 0xFF;

	private const byte B = 0xFF;

	// The effect if the keyboard has it, otherwise "static" (e.g. reactive or starlight saved by test build 1).
	public static string Normalize(string effect)
	{
		return Array.Find(Effects, (string e) => string.Equals(e, effect, StringComparison.OrdinalIgnoreCase)) ?? "static";
	}

	// Sets the effect, then the brightness (0..100). True only when the keyboard accepted both.
	public static bool Apply(RazerDevice kb, string effect, int speed, int brightness)
	{
		if (kb == null)
		{
			return false;
		}
		var cmd = EffectCommand(effect, speed);
		if (cmd == null)
		{
			Cfg.Log($"keyboard: unknown lighting effect \"{effect}\"");
			return false;
		}
		bool effectOk = kb.Command(0x0F, 0x02, cmd.Value.Size, cmd.Value.Args) != null;
		bool brightnessOk = kb.SetBrightness(Math.Clamp(brightness, 0, 100));
		return effectOk && brightnessOk;
	}

	// Size and arguments of the 0F/02 command; null for an effect the keyboard does not have. None of the
	// BlackWidow Lite's effects takes a speed; the argument is kept for the bridge contract.
	internal static (byte Size, byte[] Args)? EffectCommand(string effect, int speed)
	{
		switch (effect?.ToLowerInvariant())
		{
		case "off":
			return (6, new byte[3] { VarStore, BacklightLed, 0x00 });
		case "static":
			return (9, new byte[9] { VarStore, BacklightLed, 0x01, 0, 0, 0x01, R, G, B });
		case "breathing":
			return (9, new byte[9] { VarStore, BacklightLed, 0x02, 0x01, 0, 0x01, R, G, B });
		default:
			return null;
		}
	}
}
