using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace LiteSynapse.Tests;

public class KbLightingTests
{
	[Theory]
	[InlineData("off", 2, 6, "010500")]
	[InlineData("static", 2, 9, "010501000001FFFFFF")]
	[InlineData("breathing", 2, 9, "010502010001FFFFFF")]
	[InlineData("breathing", 3, 9, "010502010001FFFFFF")]
	[InlineData("STATIC", 2, 9, "010501000001FFFFFF")]
	public void Effect_arguments(string effect, int speed, int size, string args)
	{
		var cmd = KbLighting.EffectCommand(effect, speed);
		Assert.NotNull(cmd);
		Assert.Equal(size, cmd.Value.Size);
		Assert.Equal(args, Convert.ToHexString(cmd.Value.Args));
	}

	[Fact]
	public void Every_listed_effect_has_a_command_and_unknown_ones_do_not()
	{
		// OpenRazer gives the BlackWidow Lite only none, static and breathing.
		Assert.Equal(new[] { "off", "static", "breathing" }, KbLighting.Effects);
		Assert.All(KbLighting.Effects, (string e) => Assert.NotNull(KbLighting.EffectCommand(e, 2)));
		Assert.Null(KbLighting.EffectCommand("reactive", 2));
		Assert.Null(KbLighting.EffectCommand("starlight", 2));
		Assert.Null(KbLighting.EffectCommand("spectrum", 2));
		Assert.Null(KbLighting.EffectCommand(null, 2));
	}

	[Theory]
	[InlineData("off", "off")]
	[InlineData("Breathing", "breathing")]
	[InlineData("reactive", "static")]
	[InlineData("starlight", "static")]
	[InlineData("", "static")]
	[InlineData(null, "static")]
	public void Effects_the_keyboard_lacks_become_static(string saved, string expected)
	{
		Assert.Equal(expected, KbLighting.Normalize(saved));
	}

	[Fact]
	public void Apply_sends_the_effect_then_the_brightness()
	{
		RazerDevice kb = new RazerDevice();
		Assert.True(KbLighting.Apply(kb, "breathing", 3, 40));
		var sent = Assert.Single(kb.Sent);
		Assert.Equal(0x0F, sent.Class);
		Assert.Equal(0x02, sent.Id);
		Assert.Equal(9, sent.Size);
		Assert.Equal("010502010001FFFFFF", Convert.ToHexString(sent.Args));
		Assert.Equal(new[] { 40 }, kb.BrightnessWrites);
		KbLighting.Apply(kb, "off", 2, 150);
		Assert.Equal(100, kb.BrightnessWrites.Last());
	}

	[Fact]
	public void Apply_is_true_only_when_the_keyboard_accepts_both()
	{
		RazerDevice kb = new RazerDevice { CommandReply = (byte cls, byte id, byte[] args) => null };
		Assert.False(KbLighting.Apply(kb, "static", 2, 50));
		kb = new RazerDevice { SetBrightnessResult = false };
		Assert.False(KbLighting.Apply(kb, "static", 2, 50));
		kb = new RazerDevice();
		Assert.False(KbLighting.Apply(kb, "rainbow", 2, 50));
		Assert.Empty(kb.Sent);
		Assert.Empty(kb.BrightnessWrites);
		Assert.False(KbLighting.Apply(null, "static", 2, 50));
	}
}

public class MouseLightingTests
{
	private static MouseLightingSettings S(string effect, string color = "#00FF00", int brightness = 100)
	{
		return new MouseLightingSettings { Effect = effect, Color = color, Brightness = brightness };
	}

	[Theory]
	[InlineData("off", 6, "010400")]
	[InlineData("static", 9, "010401000001102030")]
	[InlineData("breathing", 9, "010402010001102030")]
	[InlineData("spectrum", 6, "010403")]
	[InlineData("Spectrum", 6, "010403")]
	public void Effect_arguments(string effect, int size, string args)
	{
		var (s, a) = MouseLighting.EffectArgs(S(effect, "#102030"), 0x01, 0x04);
		Assert.Equal(size, s);
		Assert.Equal(args, Convert.ToHexString(a));
	}

	[Fact]
	public void Commands_cover_scroll_logo_and_right_side_with_brightness()
	{
		List<MouseLighting.Cmd> cmds = MouseLighting.Commands(S("static", "#FF8000", 40), 3);
		Assert.Equal(new[]
		{
			"0F/02 9 030101000001FF8000", "0F/04 3 030166",
			"0F/02 9 030401000001FF8000", "0F/04 3 030466",
			"0F/02 9 031001000001FF8000", "0F/04 3 031066"
		}, cmds.Select((MouseLighting.Cmd c) => $"{c.Class:X2}/{c.Id:X2} {c.Size} {Convert.ToHexString(c.Args)}"));
		Assert.Equal(new[] { "scroll wheel", "scroll wheel", "logo", "logo", "right side", "right side" }, cmds.Select((MouseLighting.Cmd c) => c.Zone));
	}

	[Theory]
	[InlineData(0, 0)]
	[InlineData(7, 18)]
	[InlineData(50, 128)]
	[InlineData(100, 255)]
	[InlineData(-5, 0)]
	[InlineData(120, 255)]
	public void Brightness_percent_to_byte(int percent, int value)
	{
		Assert.Equal(value, MouseLighting.ToByte(percent));
	}

	[Theory]
	[InlineData("#00FF00", true, 0, 255, 0)]
	[InlineData("c06000", true, 0xC0, 0x60, 0)]
	[InlineData("#ffc0c0", true, 0xFF, 0xC0, 0xC0)]
	[InlineData("#FFF", false, 0, 0, 0)]
	[InlineData("#GG0000", false, 0, 0, 0)]
	[InlineData("", false, 0, 0, 0)]
	[InlineData(null, false, 0, 0, 0)]
	public void Colour_parsing(string color, bool ok, int r, int g, int b)
	{
		Assert.Equal(ok, MouseLighting.TryParseColor(color, out byte rr, out byte gg, out byte bb));
		Assert.Equal((r, g, b), (rr, gg, bb));
	}

	[Fact]
	public void Validate_needs_a_known_effect_and_a_colour_only_where_used()
	{
		Assert.Null(MouseLighting.Validate(null));
		Assert.Null(MouseLighting.Validate(S("static")));
		Assert.Null(MouseLighting.Validate(S("spectrum", null)));
		Assert.Null(MouseLighting.Validate(S("off", "nonsense")));
		Assert.NotNull(MouseLighting.Validate(S("static", "red")));
		Assert.NotNull(MouseLighting.Validate(S("wave")));
		Assert.Equal(new[] { "off", "static", "breathing", "spectrum" }, MouseLighting.Effects);
	}

	[Fact]
	public void Apply_writes_every_zone_and_names_the_rejected_ones()
	{
		RazerDevice mouse = new RazerDevice
		{
			CommandReply = (byte cls, byte id, byte[] args) => (args[1] == 0x10) ? null : new byte[80]
		};
		string err = MouseLighting.Apply(mouse, S("breathing", "#0000FF", 100));
		Assert.Equal("mouse rejected lighting for right side", err);
		Assert.Equal(6, mouse.Sent.Count);
		Assert.All(mouse.Sent, s => Assert.Equal(MouseLighting.VarStore, s.Args[0]));
		mouse = new RazerDevice();
		Assert.Null(MouseLighting.Apply(mouse, S("spectrum"), storage: 5));
		Assert.All(mouse.Sent, s => Assert.Equal(5, s.Args[0]));
		Assert.Null(MouseLighting.Apply(mouse, null));
		Assert.Null(MouseLighting.Apply(null, S("static")));
	}

	[Fact]
	public void Apply_sends_nothing_for_invalid_settings()
	{
		RazerDevice mouse = new RazerDevice();
		Assert.NotNull(MouseLighting.Apply(mouse, S("static", "#12")));
		Assert.Empty(mouse.Sent);
	}

	[Fact]
	public void Probe_only_reads_and_lists_the_zones_that_answer()
	{
		RazerDevice mouse = new RazerDevice
		{
			CommandReply = (byte cls, byte id, byte[] args) => (args[1] == 0x11) ? null : new byte[80]
		};
		List<(int Id, string Name)> zones = MouseLighting.Probe(mouse);
		Assert.Equal(new[] { 0x00, 0x01, 0x04, 0x10 }, zones.Select(z => z.Id));
		Assert.Equal("All zones", zones[0].Name);
		Assert.Equal(5, mouse.Sent.Count);
		Assert.All(mouse.Sent, s =>
		{
			Assert.Equal(0x0F, s.Class);
			Assert.Equal(0x84, s.Id);
			Assert.Equal(3, s.Size);
			Assert.Equal(0x01, s.Args[0]);
		});
		Assert.Equal(new byte[] { 0x00, 0x01, 0x04, 0x10, 0x11 }, mouse.Sent.Select(s => s.Args[1]));
		Assert.Empty(MouseLighting.Probe(null));
	}
}
