using System;

namespace LiteSynapse;

// BlackWidow Lite backlight effects (extended matrix effect 0F/02, layouts from OpenRazer razerchromacommon.c).
// The board has single-colour white LEDs, so every coloured effect is sent with white.
internal static class KbLighting
{
	public const byte VarStore = 0x01;

	public const byte BacklightLed = 0x05;

	public static readonly string[] Effects = new string[5] { "off", "static", "breathing", "reactive", "starlight" };

	private const byte R = 0xFF;

	private const byte G = 0xFF;

	private const byte B = 0xFF;

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

	// Size and arguments of the 0F/02 command; null for an unknown effect. speed is 1 (slow) .. 3 (fast).
	internal static (byte Size, byte[] Args)? EffectCommand(string effect, int speed)
	{
		speed = Math.Clamp(speed, 1, 3);
		switch (effect?.ToLowerInvariant())
		{
		case "off":
			return (6, new byte[3] { VarStore, BacklightLed, 0x00 });
		case "static":
			return (9, new byte[9] { VarStore, BacklightLed, 0x01, 0, 0, 0x01, R, G, B });
		case "breathing":
			return (9, new byte[9] { VarStore, BacklightLed, 0x02, 0x01, 0, 0x01, R, G, B });
		case "reactive":
			return (9, new byte[9] { VarStore, BacklightLed, 0x05, 0, ReactiveSpeed(speed), 0x01, R, G, B });
		case "starlight":
			return (9, new byte[9] { VarStore, BacklightLed, 0x07, 0, (byte)speed, 0x01, R, G, B });
		default:
			return null;
		}
	}

	// Reactive takes 1..4 (fade time); the UI's 1..3 maps to 1, 2 and 4.
	internal static byte ReactiveSpeed(int speed)
	{
		return Math.Clamp(speed, 1, 3) switch
		{
			1 => 1,
			2 => 2,
			_ => 4,
		};
	}
}
