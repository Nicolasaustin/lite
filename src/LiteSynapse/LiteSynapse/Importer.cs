using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace LiteSynapse;

internal static class Importer
{
	private static readonly string[] SideIds = new string[12]
	{
		"KEY_1", "KEY_2", "KEY_3", "KEY_4", "KEY_5", "KEY_6", "KEY_7", "KEY_8", "KEY_9", "KEY_0",
		"KEY_HYPEN", "KEY_EQUAL"
	};

	public static string ButtonId(string inputId)
	{
		int i = Array.IndexOf(SideIds, inputId);
		if (i >= 0)
		{
			return $"Side{i + 1}";
		}
		return inputId switch
		{
			"ScrollButton" => "Scroll", 
			"DKM_M_01" => "M1", 
			"DKM_M_02" => "M2", 
			"RepeatScrollLeft" => "TiltR", 
			"RepeatScrollRight" => "TiltL", 
			_ => null, 
		};
	}

	private static JsonNode Payload(JsonNode item)
	{
		return JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(item["payload"].GetValue<string>())));
	}

	public static string Run(string[] files)
	{
		ProfileStore store = Cfg.Load<ProfileStore>(Cfg.Profiles);
		StringBuilder sb = new StringBuilder();
		StringBuilder stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler;
		foreach (string f in files)
		{
			if (f.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
			{
				Macro m = ImportMacroXml(f);
				store.Macros.RemoveAll((Macro x) => x.Name == m.Name);
				store.Macros.Add(m);
				stringBuilder = sb;
				StringBuilder stringBuilder2 = stringBuilder;
				handler = new StringBuilder.AppendInterpolatedStringHandler(14, 2, stringBuilder);
				handler.AppendLiteral("macro ");
				handler.AppendFormatted(m.Name);
				handler.AppendLiteral(": ");
				handler.AppendFormatted(m.Steps.Count);
				handler.AppendLiteral(" steps");
				stringBuilder2.AppendLine(ref handler);
				continue;
			}
			JsonNode root = JsonNode.Parse(File.ReadAllText(f));
			string cat = root["category"]?.GetValue<string>();
			foreach (JsonNode item in root["profiles"]?.AsArray() ?? new JsonArray())
			{
				JsonNode j = Payload(item);
				if (cat == "MOUSE")
				{
					MouseProfile mp = MouseFrom(j);
					store.Mouse.RemoveAll((MouseProfile x) => x.Name == mp.Name);
					store.Mouse.Add(mp);
				}
				else if (cat == "KEYBOARD")
				{
					KbProfile kp = KbFrom(j);
					store.Keyboard.RemoveAll((KbProfile x) => x.Name == kp.Name);
					store.Keyboard.Add(kp);
				}
			}
			foreach (JsonNode m2 in root["macros"]?.AsArray() ?? new JsonArray())
			{
				Macro mm = MacroFromJson(Payload(m2), m2["name"]?.GetValue<string>());
				if (!store.Macros.Any((Macro x) => x.Name == mm.Name))
				{
					store.Macros.Add(mm);
				}
			}
			stringBuilder = sb;
			StringBuilder stringBuilder3 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(2, 2, stringBuilder);
			handler.AppendFormatted(Path.GetFileName(f));
			handler.AppendLiteral(": ");
			handler.AppendFormatted(cat);
			stringBuilder3.AppendLine(ref handler);
		}
		Cfg.Save(Cfg.Profiles, store);
		stringBuilder = sb;
		StringBuilder stringBuilder4 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(51, 3, stringBuilder);
		handler.AppendLiteral("Total: ");
		handler.AppendFormatted(store.Mouse.Count);
		handler.AppendLiteral(" mouse profiles, ");
		handler.AppendFormatted(store.Keyboard.Count);
		handler.AppendLiteral(" keyboard profiles, ");
		handler.AppendFormatted(store.Macros.Count);
		handler.AppendLiteral(" macros");
		stringBuilder4.AppendLine(ref handler);
		return sb.ToString();
	}

	private static MouseProfile MouseFrom(JsonNode j)
	{
		MouseProfile p = new MouseProfile
		{
			Name = j["name"].GetValue<string>(),
			PollingRate = (j["pollingRate"]?.GetValue<int>() ?? 1000)
		};
		JsonNode? jsonNode = j["dpiStages"];
		int active = jsonNode?["active"]?.GetValue<int>() ?? 1;
		JsonArray stages = jsonNode?["stages"]?.AsArray();
		if (stages != null && active >= 1 && active <= stages.Count)
		{
			p.Dpi = stages[active - 1]["x"].GetValue<int>();
		}
		ReadStages(j, p);
		List<JsonNode> all = new List<JsonNode>();
		foreach (JsonNode m in j["mappings"]?.AsArray() ?? new JsonArray())
		{
			all.Add(m);
		}
		foreach (JsonNode m2 in j["sidePanelMappings"]?["12ButtonSide"]?.AsArray() ?? new JsonArray())
		{
			all.Add(m2);
		}
		foreach (JsonNode m3 in all)
		{
			if (m3["outputType"]?.GetValue<string>() != "keyboardGroup")
			{
				continue;
			}
			string id = ButtonId(m3["inputID"].GetValue<string>());
			if (id == null)
			{
				continue;
			}
			JsonNode kg = m3["keyboardGroup"];
			if (kg?["key"] == null)
			{
				continue;
			}
			KeyTarget t = new KeyTarget
			{
				Key = kg["key"].GetValue<string>()
			};
			foreach (JsonNode mod in kg["modifiers"]?.AsArray() ?? new JsonArray())
			{
				t.Mods.Add(mod.GetValue<string>());
			}
			p.Buttons[id] = t;
		}
		return p;
	}

	private static void ReadStages(JsonNode j, MouseProfile p)
	{
		JsonNode st = j["dpiStages"];
		JsonArray arr = st?["stages"]?.AsArray();
		p.DpiStages = new List<int>();
		p.DpiActive = 1;
		if (arr == null)
		{
			return;
		}
		int active = st["active"]?.GetValue<int>() ?? 1;
		foreach (JsonNode s in from jsonNode in arr
			where jsonNode["visible"]?.GetValue<bool>() ?? false
			orderby jsonNode["index"]?.GetValue<int>() ?? 0
			select jsonNode)
		{
			p.DpiStages.Add(s["x"].GetValue<int>());
		}
		if (p.DpiStages.Count > 0 && active >= 1 && active <= p.DpiStages.Count)
		{
			p.DpiActive = active;
			p.Dpi = p.DpiStages[active - 1];
		}
		else
		{
			p.DpiStages.Clear();
		}
	}

	public static string UpdateStages(string[] files)
	{
		ProfileStore store = Cfg.Load<ProfileStore>(Cfg.Profiles);
		int n = 0;
		for (int i = 0; i < files.Length; i++)
		{
			JsonNode root = JsonNode.Parse(File.ReadAllText(files[i]));
			if (root["category"]?.GetValue<string>() != "MOUSE")
			{
				continue;
			}
			foreach (JsonNode item in root["profiles"].AsArray())
			{
				JsonNode j = Payload(item);
				MouseProfile tmp = new MouseProfile
				{
					Name = j["name"].GetValue<string>()
				};
				MouseProfile existing = store.Mouse.FirstOrDefault((MouseProfile x) => x.Name == tmp.Name);
				if (existing != null)
				{
					ReadStages(j, tmp);
					if (tmp.DpiStages.Count > 0)
					{
						existing.DpiStages = tmp.DpiStages;
						existing.DpiActive = tmp.DpiActive;
						existing.Dpi = tmp.Dpi;
						n++;
					}
				}
			}
		}
		Cfg.Save(Cfg.Profiles, store);
		return $"{n} profiles now keep their DPI stages";
	}

	private static KbProfile KbFrom(JsonNode j)
	{
		KbProfile p = new KbProfile
		{
			Name = j["name"].GetValue<string>(),
			Brightness = (j["brightness"]?["value"]?.GetValue<int>() ?? 100),
			SnapTap = (j["snapTap"]?["isEnabled"]?.GetValue<bool>() == true)
		};
		foreach (JsonNode m in j["mappings"]?.AsArray() ?? new JsonArray())
		{
			if (m["outputType"]?.GetValue<string>() == "macroGroup")
			{
				p.MacroKeys[m["inputID"].GetValue<string>()] = m["macroGroup"]["name"].GetValue<string>();
			}
		}
		return p;
	}

	private static Macro MacroFromJson(JsonNode j, string name)
	{
		Macro m = new Macro
		{
			Name = (name ?? "?")
		};
		foreach (JsonNode e in j["macroEvents"].AsArray())
		{
			JsonNode k = e["KeyEvent"];
			AddEvent(m, e["Type"].ToString(), k?["Makecode"].GetValue<int>() ?? 0, k?["IsExtended"]?.ToString() == "true", k?["State"].GetValue<int>() ?? 0, (e["Number"] != null) ? e["Number"].GetValue<double>() : 0.0);
		}
		return m;
	}

	private static Macro ImportMacroXml(string f)
	{
		XElement x = XDocument.Load(f).Root;
		Macro m = new Macro
		{
			Name = x.Element("Name").Value
		};
		foreach (XElement e in x.Element("MacroEvents").Elements("MacroEvent"))
		{
			XElement k = e.Element("KeyEvent");
			AddEvent(m, e.Element("Type").Value, (k != null) ? int.Parse(k.Element("Makecode").Value) : 0, k?.Element("IsExtended")?.Value == "true", (k != null) ? int.Parse(k.Element("State").Value) : 0, (e.Element("Number") != null) ? double.Parse(e.Element("Number").Value, CultureInfo.InvariantCulture) : 0.0);
		}
		return m;
	}

	private static void AddEvent(Macro m, string type, int make, bool ext, int state, double seconds)
	{
		if (type == "1")
		{
			List<MacroStep> steps = m.Steps;
			MacroStep macroStep = new MacroStep();
			macroStep.Vk = make;
			macroStep.Ext = ext;
			MacroStep macroStep2 = macroStep;
			bool down = ((state == 0 || state == 2) ? true : false);
			macroStep2.Down = down;
			steps.Add(macroStep);
		}
		else if (type == "0")
		{
			m.Steps.Add(new MacroStep
			{
				Vk = 0,
				DelayMs = (int)Math.Round(seconds * 1000.0)
			});
		}
	}
}
