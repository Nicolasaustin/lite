using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace LiteSynapse;

// The State object and the command results of docs/bridge-api.md (v1.2). Property names become camelCase;
// dictionary keys ("Side8", "KEY_UP_ARROW", "3") are written as they are.

internal sealed class StateDto
{
	public string Version { get; set; } = "";

	public DeviceDto Mouse { get; set; } = new DeviceDto();

	public DeviceDto Keyboard { get; set; } = new DeviceDto();

	public bool Auto { get; set; }

	public bool InGame { get; set; }

	public bool LiveWrite { get; set; }

	public bool Startup { get; set; }

	public string LiveStatus { get; set; } = "";

	public string ActiveMouse { get; set; } = "";

	public string ActiveKeyboard { get; set; } = "";

	public string DefaultMouse { get; set; } = "";

	public string DefaultKeyboard { get; set; } = "";

	public List<MouseProfileDto> MouseProfiles { get; set; } = new List<MouseProfileDto>();

	public List<KbProfileDto> KeyboardProfiles { get; set; } = new List<KbProfileDto>();

	public List<MacroDto> Macros { get; set; } = new List<MacroDto>();

	public List<GameDto> Games { get; set; } = new List<GameDto>();

	public Dictionary<string, string> Calibration { get; set; } = new Dictionary<string, string>();

	public string[] ButtonIds { get; set; } = new string[0];

	public Dictionary<string, string> Onboard { get; set; } = new Dictionary<string, string>();

	public int[] LiveSlots { get; set; } = new int[0];

	public string Accent { get; set; } = WebOps.DefaultAccent;

	public bool CompetitiveSafe { get; set; }

	public string GameKeyboardDefault { get; set; } = "";

	public string LastGameExe { get; set; } = "";

	public bool ElevatedGame { get; set; }

	public RazerDto Razer { get; set; } = new RazerDto();

	// Not in the v1.2 contract yet: the "close this window when a game starts" setting (setCloseOnGame).
	public bool CloseOnGame { get; set; } = true;
}

internal sealed class DeviceDto
{
	public bool Connected { get; set; }

	public string Name { get; set; } = "";
}

internal sealed class MouseProfileDto
{
	public string Name { get; set; }

	public int PollingRate { get; set; }

	public int Dpi { get; set; }

	public List<int> DpiStages { get; set; }

	public int DpiActive { get; set; }

	public Dictionary<string, KeyTargetDto> Buttons { get; set; }

	// null = LiteSynapse leaves the mouse lighting as it is.
	public LightingDto Lighting { get; set; }

	public bool ClickSwap { get; set; }

	public bool AntiCheat { get; set; }
}

internal sealed class KeyTargetDto
{
	public string Key { get; set; }

	public List<string> Mods { get; set; }
}

internal sealed class LightingDto
{
	public string Effect { get; set; }

	public string Color { get; set; }

	public int Brightness { get; set; }
}

internal sealed class KbProfileDto
{
	public string Name { get; set; }

	public int Brightness { get; set; }

	public bool SnapTap { get; set; }

	public Dictionary<string, string> MacroKeys { get; set; }

	public string Effect { get; set; }

	public int EffectSpeed { get; set; }
}

internal sealed class MacroDto
{
	public string Name { get; set; }

	public List<MacroStep> Steps { get; set; }
}

internal sealed class GameDto
{
	public string Profile { get; set; }

	public List<string> Exes { get; set; }

	public string KeyboardProfile { get; set; }
}

internal sealed class RazerDto
{
	public bool Checked { get; set; }

	public bool SynapseInstalled { get; set; }

	public bool SynapseRunning { get; set; }

	// absent | running | stopped | disabled
	public string GmsService { get; set; } = "absent";

	public List<string> FilterDrivers { get; set; } = new List<string>();

	public bool RzCommon { get; set; }
}

// getDeviceInfo
internal sealed class DeviceInfoDto
{
	public MouseInfoDto Mouse { get; set; }

	public KeyboardInfoDto Keyboard { get; set; }
}

internal sealed class MouseInfoDto
{
	public int? Dpi { get; set; }

	public int? Polling { get; set; }

	public int? Slot { get; set; }

	public string SlotName { get; set; }
}

internal sealed class KeyboardInfoDto
{
	public int? Brightness { get; set; }
}

// Small command results.
internal sealed record NameResult(string Name);

internal sealed record PathResult(string Path);

internal sealed record MessageResult(string Message);

internal sealed record OnboardResult(bool Ok, string Message);

internal sealed record ExeResult(string Exe);

internal sealed record ZoneDto(int Id, string Name);

internal sealed record ZonesResult(List<ZoneDto> Zones);

// Builds the DTOs from the stored model. Pure: no device or Windows calls.
internal static class WebState
{
	public static MouseProfileDto Mouse(MouseProfile p)
	{
		return new MouseProfileDto
		{
			Name = p.Name ?? "",
			PollingRate = p.PollingRate,
			Dpi = p.Dpi,
			DpiStages = (p.DpiStages ?? new List<int>()).ToList(),
			DpiActive = p.DpiActive,
			Buttons = (p.Buttons ?? new Dictionary<string, KeyTarget>())
				.Where(kv => kv.Value != null && !string.IsNullOrEmpty(kv.Value.Key))
				.ToDictionary(kv => kv.Key, kv => new KeyTargetDto
				{
					Key = kv.Value.Key,
					Mods = (kv.Value.Mods ?? new List<string>()).ToList()
				}),
			Lighting = (p.Lighting == null) ? null : new LightingDto
			{
				Effect = p.Lighting.Effect,
				Color = p.Lighting.Color,
				Brightness = p.Lighting.Brightness
			},
			ClickSwap = p.ClickSwap,
			AntiCheat = AntiCheat.IsProtected(p.Name)
		};
	}

	public static KbProfileDto Keyboard(KbProfile p)
	{
		return new KbProfileDto
		{
			Name = p.Name ?? "",
			Brightness = p.Brightness,
			SnapTap = p.SnapTap,
			MacroKeys = new Dictionary<string, string>(p.MacroKeys ?? new Dictionary<string, string>()),
			Effect = p.Effect ?? "static",
			EffectSpeed = p.EffectSpeed
		};
	}

	public static MacroDto Macro(Macro m)
	{
		return new MacroDto
		{
			Name = m.Name ?? "",
			Steps = (m.Steps ?? new List<MacroStep>()).Select(s => new MacroStep
			{
				Vk = s.Vk,
				Ext = s.Ext,
				Down = s.Down,
				DelayMs = s.DelayMs
			}).ToList()
		};
	}

	public static GameDto Game(GameRule g)
	{
		return new GameDto
		{
			Profile = g.Profile ?? "",
			Exes = (g.Exes ?? new List<string>()).ToList(),
			KeyboardProfile = g.KeyboardProfile ?? ""
		};
	}

	// The profile, macro and game lists of the State object.
	public static void FillStore(StateDto s, ProfileStore store, IEnumerable<GameRule> games)
	{
		s.MouseProfiles = (store?.Mouse ?? new List<MouseProfile>()).Where(p => p != null).Select(Mouse).ToList();
		s.KeyboardProfiles = (store?.Keyboard ?? new List<KbProfile>()).Where(p => p != null).Select(Keyboard).ToList();
		s.Macros = (store?.Macros ?? new List<Macro>()).Where(m => m != null).Select(Macro).ToList();
		s.Games = (games ?? Enumerable.Empty<GameRule>()).Where(g => g != null).Select(Game).ToList();
	}
}

// Source-generated serializers for everything the bridge sends: no reflection when the window opens.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(StateDto))]
[JsonSerializable(typeof(DeviceInfoDto))]
[JsonSerializable(typeof(NameResult))]
[JsonSerializable(typeof(PathResult))]
[JsonSerializable(typeof(MessageResult))]
[JsonSerializable(typeof(OnboardResult))]
[JsonSerializable(typeof(ExeResult))]
[JsonSerializable(typeof(ZonesResult))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(string[]))]
internal sealed partial class WebJson : JsonSerializerContext
{
}
