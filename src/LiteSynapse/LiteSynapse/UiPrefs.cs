using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LiteSynapse;

// Preferences of the HTML window (data/ui.json). Kept out of settings.json, which the service owns.
public sealed class UiPrefs
{
	public const int DefaultWidth = 1180;

	public const int DefaultHeight = 780;

	public const int MinWidth = 1000;

	public const int MinHeight = 700;

	// UI accent colour: one of WebOps.Accents.
	public string Accent { get; set; } = WebOps.DefaultAccent;

	// Close the window when a linked game comes to the front, so nothing renders while playing.
	public bool CloseOnGame { get; set; } = true;

	// Last window size (normal state) and whether it was maximized.
	public int Width { get; set; } = DefaultWidth;

	public int Height { get; set; } = DefaultHeight;

	public bool Maximized { get; set; }

	[JsonExtensionData]
	public Dictionary<string, JsonElement> ExtraData { get; set; }

	public static string FilePath => Path.Combine(Cfg.Dir, "ui.json");

	public static UiPrefs Load()
	{
		UiPrefs p = Cfg.Load<UiPrefs>(FilePath);
		p.Normalize();
		return p;
	}

	public void Save()
	{
		try
		{
			Cfg.Save(FilePath, this);
		}
		catch (Exception ex)
		{
			Cfg.Log("ui.json: " + ex.Message);
		}
	}

	// Repairs values a hand-edited file may hold.
	public void Normalize()
	{
		if (!WebOps.IsAccent(Accent))
		{
			Accent = WebOps.DefaultAccent;
		}
		if (Width < MinWidth || Width > 16000)
		{
			Width = DefaultWidth;
		}
		if (Height < MinHeight || Height > 16000)
		{
			Height = DefaultHeight;
		}
	}
}
