using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteSynapse;

// Windows services the Performance page offers to turn off, and turn back on.
//
// The list is FR33THY's (github.com/FR33THYFR33THY/Ultimate, "8 Advanced/17 Services.ps1") narrowed to the
// services that actually run on a normal gaming PC, with a written-out cost for each and the start type to
// put back. Every row can be turned off AND back on: Default is what Windows ships, so nothing here is a
// one-way door.
//
// Tiers are about what you give up, not about how much they help:
//   Safe      - nothing you use on a gaming PC stops working.
//   Caution   - a Windows feature stops working. The row says which.
//   Risky     - security, networking or device handling is affected. Read it before ticking it.
//
// NeverOffer below is the one hard limit, and it is not about risk tolerance: those services either stop the
// PC booting or stop the mouse and keyboard working, and the second one cannot be undone from inside this app
// because you would have no mouse to undo it with.
internal static class ServiceCatalog
{
	// start types: 2 = automatic, 3 = manual, 4 = disabled.
	internal const int Auto = 2;

	internal const int Manual = 3;

	internal const int Disabled = 4;

	internal sealed class Row
	{
		public string Service;

		public string Title;

		// What it costs while it runs.
		public string Costs;

		// What you give up by turning it off.
		public string Lose;

		public string Risk;

		// The start type Windows ships, restored when the row is turned back on.
		public int Default;

		// A per-user service template. What actually runs is an instance with a logon-id suffix
		// (OneSyncSvc_49cd3), so "sc stop <template>" cannot reach it: the change is a start-type change that
		// takes effect at the next sign-in. The script skips the pointless stop, and the row says so.
		public bool PerUser;

		public Row(string service, string title, string costs, string lose, string risk, int def, bool perUser = false)
		{
			Service = service;
			Title = title;
			Costs = costs;
			Lose = lose;
			Risk = risk;
			Default = def;
			PerUser = perUser;
		}
	}

	// Never offered, whatever the user asks for:
	//  * the kernel-mode input stack - with these off there is no mouse and no keyboard, so there is no way
	//    back. mshidkmdf in particular must be left alone.
	//  * the services Windows cannot boot or log in without.
	internal static readonly string[] NeverOffer = new string[]
	{
		// Input. Without these the PC has no pointer and no typing.
		"mouclass", "mouhid", "kbdclass", "kbdhid", "mshidkmdf", "HidUsb", "hidi2c", "HidBth", "hidinterrupt",
		"USBHUB3", "usbhub", "USBXHCI", "usbccgp", "usbehci", "usbohci", "usbuhci",
		// Elevation. With Appinfo off nothing on the PC can ask for administrator rights - including
		// LiteSynapse, so the change could not be undone from the page that made it. FR33THY's list leaves
		// it alone too.
		"Appinfo",
		// Boot and sign-in.
		"DcomLaunch", "RpcSs", "RpcEptMapper", "PlugPlay", "Power", "LSM", "UserManager", "ProfSvc",
		"BrokerInfrastructure", "CoreMessagingRegistrar", "SystemEventsBroker", "DeviceInstall", "Winmgmt",
		"gpsvc", "CryptSvc", "TrustedInstaller", "SamSs", "Themes", "DispBrokerDesktopSvc",
		// Storage and the file system the OS is on.
		"StorSvc", "volmgr", "volsnap", "partmgr", "disk", "FltMgr", "Ntfs", "vmbus",
		// Sound. The user plays with sound on.
		"Audiosrv", "AudioEndpointBuilder",
		// Network stack the PC needs to reach anything at all.
		"nsi", "Dhcp", "Dnscache", "NlaSvc", "Wcmsvc", "WlanSvc", "netprofm", "Netman"
	};

	internal static readonly Row[] All = new Row[]
	{
		// ---- Safe: telemetry, indexing, prefetch, things a gaming PC does not use -------------------
		new Row("DiagTrack", "Connected User Experiences and Telemetry",
			"Collects usage data and uploads it to Microsoft in the background, using your connection.",
			"Nothing you use. Some feedback and diagnostics stop reporting.", Tweaks.Safe, Auto),
		new Row("dmwappushservice", "Device Management WAP Push",
			"Part of the same telemetry path.",
			"Nothing on a home PC.", Tweaks.Safe, Manual),
		new Row("diagnosticshub.standardcollector.service", "Diagnostics Hub Collector",
			"Collects ETW traces for Visual Studio's diagnostics tools.",
			"Nothing unless you profile code with Visual Studio.", Tweaks.Safe, Manual),
		new Row("diagsvc", "Diagnostic Execution Service",
			"Runs diagnostic scenarios in the background.",
			"Nothing you use.", Tweaks.Safe, Manual),
		new Row("DPS", "Diagnostic Policy Service",
			"Runs Windows' problem detection in the background, which is constant small CPU and disk work.",
			"The Windows troubleshooters stop working.", Tweaks.Safe, Auto),
		new Row("WdiServiceHost", "Diagnostic Service Host",
			"Hosts the same diagnostics.",
			"The Windows troubleshooters stop working.", Tweaks.Safe, Manual),
		new Row("WdiSystemHost", "Diagnostic System Host",
			"Hosts the same diagnostics at system level.",
			"The Windows troubleshooters stop working.", Tweaks.Safe, Manual),
		new Row("SysMain", "SysMain (Superfetch)",
			"Reads programs into memory ahead of time. On an SSD it is mostly background disk work.",
			"Nothing noticeable on an SSD. On a hard disk, programs may open a little slower.", Tweaks.Safe, Auto),
		new Row("WSearch", "Windows Search",
			"Indexes your files in the background: real disk and CPU while you play.",
			"Start-menu and File Explorer search get slower and less complete.", Tweaks.Safe, Auto),
		new Row("WerSvc", "Windows Error Reporting",
			"Collects and uploads crash dumps.",
			"Crashes are no longer reported to Microsoft. You can still read them in Event Viewer.", Tweaks.Safe, Manual),
		new Row("wercplsupport", "Problem Reports Control Panel",
			"Supports the problem-reports list.",
			"The problem-reports page in Control Panel.", Tweaks.Safe, Manual),
		new Row("PcaSvc", "Program Compatibility Assistant",
			"Watches every program you run to offer compatibility fixes.",
			"Windows stops offering to apply compatibility settings for old programs.", Tweaks.Safe, Auto),
		new Row("RetailDemo", "Retail Demo Service",
			"For shop display machines.",
			"Nothing.", Tweaks.Safe, Manual),
		new Row("MapsBroker", "Downloaded Maps Manager",
			"Keeps offline maps updated in the background.",
			"The Maps app stops using offline maps.", Tweaks.Safe, Auto),
		new Row("lfsvc", "Geolocation Service",
			"Keeps your location available to apps.",
			"Apps that ask for your location stop getting it, including the weather and time-zone features.", Tweaks.Safe, Manual),
		new Row("TrkWks", "Distributed Link Tracking Client",
			"Tracks files moved between NTFS volumes so shortcuts keep working.",
			"A shortcut to a file you later move may stop resolving.", Tweaks.Safe, Auto),
		new Row("WpnService", "Windows Push Notifications",
			"Keeps a connection open to deliver toast notifications.",
			"Store-app and Windows notifications stop arriving.", Tweaks.Safe, Auto),
		new Row("CDPSvc", "Connected Devices Platform",
			"Keeps a connection open so your PC can talk to your other Microsoft devices.",
			"Phone Link, shared clipboard and Nearby Sharing stop working.", Tweaks.Safe, Auto),
		new Row("CDPUserSvc", "Connected Devices Platform (user)",
			"The per-account half of the same thing.",
			"Phone Link, shared clipboard and Nearby Sharing stop working. Takes effect at the next sign-in: this one runs per account, so there is no instance to stop now.", Tweaks.Safe, Auto, perUser: true),
		new Row("DusmSvc", "Data Usage",
			"Measures how much data each app uses.",
			"The data-usage page in Settings stops filling in.", Tweaks.Safe, Auto),
		new Row("OneSyncSvc", "Sync Host",
			"Syncs mail, contacts and calendar for the built-in apps.",
			"The built-in Mail, Calendar and People apps stop syncing. Takes effect at the next sign-in: this one runs per account, so there is no instance to stop now.", Tweaks.Safe, Auto, perUser: true),
		new Row("PimIndexMaintenanceSvc", "Contact Data",
			"Indexes contacts for the same apps.",
			"Contact search in the built-in apps. Takes effect at the next sign-in: this one runs per account, so there is no instance to stop now.", Tweaks.Safe, Auto, perUser: true),
		new Row("MessagingService", "Messaging Service",
			"Text-messaging support for the built-in apps.",
			"Nothing unless you use Phone Link messaging. Takes effect at the next sign-in: this one runs per account, so there is no instance to stop now.", Tweaks.Safe, Manual, perUser: true),
		new Row("PhoneSvc", "Phone Service",
			"Phone-call support for the built-in apps.",
			"Nothing unless you use Phone Link calling.", Tweaks.Safe, Manual),
		new Row("wisvc", "Windows Insider Service",
			"Handles Insider preview builds.",
			"You cannot join the Windows Insider programme while it is off.", Tweaks.Safe, Manual),
		new Row("WpcMonSvc", "Parental Controls",
			"Enforces family-safety limits.",
			"Microsoft Family Safety limits stop being enforced on this PC.", Tweaks.Safe, Manual),
		new Row("Fax", "Fax",
			"Fax support.",
			"Nothing.", Tweaks.Safe, Manual),
		new Row("WMPNetworkSvc", "Windows Media Player Network Sharing",
			"Shares media libraries over the network.",
			"Media sharing from Windows Media Player.", Tweaks.Safe, Manual),
		new Row("WalletService", "WalletService",
			"Payment-card storage for Store apps.",
			"Nothing unless you pay inside Store apps.", Tweaks.Safe, Manual),
		new Row("SEMgrSvc", "Payments and NFC",
			"NFC payment support.",
			"Tap-to-pay on a PC with NFC.", Tweaks.Safe, Manual),
		new Row("autotimesvc", "Cellular Time",
			"Sets the clock from a mobile network.",
			"Nothing on a desktop.", Tweaks.Safe, Manual),
		new Row("tzautoupdate", "Auto Time Zone Updater",
			"Changes the time zone as you travel.",
			"The time zone no longer follows your location.", Tweaks.Safe, Disabled),
		new Row("GraphicsPerfSvc", "Graphics Performance Monitor",
			"Records per-app graphics performance for the Settings page.",
			"The graphics-performance list in Settings.", Tweaks.Safe, Manual),
		new Row("SensorService", "Sensor Service",
			"Manages ambient-light and orientation sensors.",
			"Auto-brightness and screen rotation, neither of which a desktop has.", Tweaks.Safe, Manual),
		new Row("SensrSvc", "Sensor Monitoring",
			"Watches those sensors.",
			"Auto-brightness.", Tweaks.Safe, Manual),
		new Row("SensorDataService", "Sensor Data Service",
			"Delivers sensor data to apps.",
			"Nothing on a desktop.", Tweaks.Safe, Manual),
		new Row("WbioSrvc", "Windows Biometric Service",
			"Fingerprint and face sign-in.",
			"Windows Hello fingerprint and face sign-in stop working.", Tweaks.Safe, Manual),
		new Row("ScDeviceEnum", "Smart Card Device Enumeration",
			"Finds smart-card readers.",
			"Smart-card sign-in.", Tweaks.Safe, Manual),
		new Row("SCardSvr", "Smart Card",
			"Smart-card support.",
			"Smart-card sign-in.", Tweaks.Safe, Manual),
		new Row("stisvc", "Windows Image Acquisition",
			"Talks to scanners and cameras.",
			"Scanning from this PC.", Tweaks.Safe, Auto),
		new Row("WpdBusEnum", "Portable Device Enumerator",
			"Applies policy to portable media players.",
			"Media-transfer sync with an MP3 player or phone over MTP.", Tweaks.Safe, Manual),
		new Row("PrintNotify", "Printer Extensions and Notifications",
			"Printer pop-ups and vendor print dialogs.",
			"Printer notifications. Printing itself still works.", Tweaks.Safe, Manual),
		new Row("Spooler", "Print Spooler",
			"Runs all the time even with no printer, and is a long-standing target for attacks.",
			"Printing, from everything. Only turn it off if you do not print from this PC.", Tweaks.Safe, Auto),
		new Row("PeerDistSvc", "BranchCache",
			"Caches network content for other PCs.",
			"Nothing on a home PC.", Tweaks.Safe, Manual),
		new Row("RemoteRegistry", "Remote Registry",
			"Lets other PCs read this one's registry. Already disabled by default.",
			"Nothing. Leave it off.", Tweaks.Safe, Disabled),
		new Row("RemoteAccess", "Routing and Remote Access",
			"Routes traffic for other machines.",
			"Nothing on a home PC.", Tweaks.Safe, Disabled),
		new Row("SharedAccess", "Internet Connection Sharing",
			"Shares this PC's connection with others, and is wanted by some vendor software.",
			"Mobile hotspot and connection sharing.", Tweaks.Safe, Manual),
		new Row("icssvc", "Windows Mobile Hotspot",
			"The hotspot feature.",
			"Mobile hotspot.", Tweaks.Safe, Manual),
		new Row("SSDPSRV", "SSDP Discovery",
			"Broadcasts and listens for UPnP devices on your network, constantly.",
			"Finding UPnP devices: some media servers, smart TVs and the Xbox app's console discovery.", Tweaks.Safe, Manual),
		new Row("upnphost", "UPnP Device Host",
			"Hosts UPnP devices on this PC.",
			"Media streaming from this PC to a TV.", Tweaks.Safe, Manual),
		new Row("fdPHost", "Function Discovery Provider Host",
			"Finds devices on the network.",
			"Network device discovery in File Explorer.", Tweaks.Safe, Manual),
		new Row("FDResPub", "Function Discovery Resource Publication",
			"Announces this PC on the network.",
			"Other PCs stop seeing this one in their network list.", Tweaks.Safe, Manual),
		new Row("lltdsvc", "Link-Layer Topology Discovery",
			"Draws the network map.",
			"The network map.", Tweaks.Safe, Manual),
		new Row("AJRouter", "AllJoyn Router",
			"Internet-of-things messaging.",
			"Nothing on a normal PC.", Tweaks.Safe, Manual),
		new Row("ALG", "Application Layer Gateway",
			"Plug-ins for connection sharing.",
			"Nothing unless you use Internet Connection Sharing.", Tweaks.Safe, Manual),
		new Row("CscService", "Offline Files",
			"Caches network shares for offline use.",
			"Offline Files, which a home PC does not use.", Tweaks.Safe, Manual),
		new Row("defragsvc", "Optimize drives",
			"Scheduled defragmentation and SSD trim.",
			"Scheduled drive optimization. On an SSD, trim is worth keeping - prefer leaving this on.", Tweaks.Caution, Manual),

		// ---- Caution: a feature you might actually use ----------------------------------------------
		new Row("XblAuthManager", "Xbox Live Auth Manager",
			"Runs for the Xbox app and Game Pass.",
			"Xbox app, Game Pass and Xbox sign-in stop working. Leave it on if you use them.", Tweaks.Caution, Manual),
		new Row("XblGameSave", "Xbox Live Game Save",
			"Syncs Xbox game saves to the cloud.",
			"Xbox cloud saves. Leave it on if you use Game Pass.", Tweaks.Caution, Manual),
		new Row("XboxNetApiSvc", "Xbox Live Networking",
			"Networking for the Xbox app.",
			"Xbox app networking and multiplayer for Game Pass titles.", Tweaks.Caution, Manual),
		new Row("XboxGipSvc", "Xbox Accessory Management",
			"Manages Xbox controllers and their firmware.",
			"Xbox controller support, including firmware updates. Leave it on if you use a controller.", Tweaks.Caution, Manual),
		new Row("GameInputSvc", "GameInput Service",
			"Newer input stack some games use for controllers.",
			"Controller input in games that use GameInput. Mouse and keyboard are unaffected.", Tweaks.Caution, Manual),
		new Row("BcastDVRUserService", "GameDVR and Broadcast",
			"The Game Bar's recording and streaming.",
			"Game Bar recording and broadcasting. Game Mode is separate and stays on. Takes effect at the next sign-in: this one runs per account, so there is no instance to stop now.", Tweaks.Caution, Manual, perUser: true),
		new Row("bthserv", "Bluetooth Support",
			"Manages Bluetooth devices.",
			"Every Bluetooth device stops working. Only turn it off if nothing you use is Bluetooth.", Tweaks.Caution, Manual),
		new Row("BTAGService", "Bluetooth Audio Gateway",
			"Bluetooth hands-free audio.",
			"Bluetooth headsets.", Tweaks.Caution, Manual),
		new Row("BthAvctpSvc", "Bluetooth AVCTP",
			"Bluetooth audio and remote control.",
			"Bluetooth headphones and speakers.", Tweaks.Caution, Manual),
		new Row("TabletInputService", "Touch Keyboard and Handwriting",
			"The on-screen keyboard and handwriting panel.",
			"The touch keyboard, emoji panel and handwriting. Some games use it for text entry.", Tweaks.Caution, Manual),
		new Row("TextInputManagementService", "Text Input Management",
			"Text input for Windows 11's own surfaces.",
			"On Windows 11 this can break the Start menu search box and the emoji panel. Leave it on there.", Tweaks.Risky, Auto),
		new Row("WebClient", "WebClient (WebDAV)",
			"Mounts web folders as drives.",
			"WebDAV drives.", Tweaks.Caution, Manual),
		new Row("LanmanWorkstation", "Workstation (SMB client)",
			"Connects to Windows file shares.",
			"Every network share and mapped drive stops working.", Tweaks.Caution, Auto),
		new Row("LanmanServer", "Server (SMB server)",
			"Shares this PC's files and printers.",
			"Other PCs cannot reach shares on this one.", Tweaks.Caution, Auto),
		new Row("SessionEnv", "Remote Desktop Configuration",
			"Configures Remote Desktop.",
			"Remote Desktop into this PC.", Tweaks.Caution, Manual),
		new Row("TermService", "Remote Desktop Services",
			"The Remote Desktop host.",
			"Remote Desktop into this PC, and Quick Assist.", Tweaks.Caution, Manual),
		new Row("seclogon", "Secondary Logon",
			"Runs programs as another user. Turns itself back on when something needs it.",
			"\"Run as different user\".", Tweaks.Caution, Manual),
		new Row("SDRSVC", "Windows Backup",
			"The old backup feature.",
			"Windows Backup and system image.", Tweaks.Caution, Manual),
		new Row("wbengine", "Block Level Backup Engine",
			"Backs up at block level.",
			"Windows Backup.", Tweaks.Caution, Manual),
		new Row("VSS", "Volume Shadow Copy",
			"Makes the snapshots behind restore points and backups.",
			"System Restore points and any backup software that uses VSS. Keep it if you want restore points.", Tweaks.Risky, Manual),
		new Row("swprv", "Software Shadow Copy Provider",
			"The provider VSS uses.",
			"System Restore and VSS backups.", Tweaks.Risky, Manual),
		new Row("iphlpsvc", "IP Helper",
			"IPv6 transition tunnelling, and some VPNs need it.",
			"IPv6 tunnels, and some VPN clients stop connecting.", Tweaks.Caution, Auto),
		new Row("NcbService", "Network Connection Broker",
			"Lets Store apps receive data in the background.",
			"Background notifications for Store apps.", Tweaks.Caution, Manual),
		new Row("ShellHWDetection", "Shell Hardware Detection",
			"AutoPlay when you plug something in.",
			"AutoPlay. Drives still appear in File Explorer.", Tweaks.Caution, Auto),
		new Row("FontCache", "Windows Font Cache",
			"Caches glyphs so text draws faster.",
			"Text rendering gets slower, not faster. Turning this off is a common mistake - leave it on.", Tweaks.Risky, Auto),
		new Row("EventLog", "Windows Event Log",
			"Writes the system, application and security logs.",
			"Event Viewer stops recording, so there is no record when something goes wrong - including the ring-buffer warnings that say a mouse buffer is too small. Keep it on.", Tweaks.Risky, Auto),
		new Row("Schedule", "Task Scheduler",
			"Runs scheduled tasks, including Windows' own maintenance.",
			"A great deal of Windows stops working, including parts of servicing and many apps' updaters. Keep it on.", Tweaks.Risky, Auto),

		// ---- Risky: updates, security, networking ---------------------------------------------------
		new Row("DoSvc", "Delivery Optimization",
			"Downloads updates from, and uploads them to, other PCs - using your upload while you play.",
			"Updates come from Microsoft only, which can be slower. Turning off the uploading alone is the better option on the Windows settings list.", Tweaks.Caution, Auto),
		new Row("wuauserv", "Windows Update",
			"Scans for and downloads updates in the background, whenever it likes.",
			"The PC stops getting security updates. It also turns itself back on. Prefer setting active hours, or pausing updates in Settings.", Tweaks.Risky, Manual),
		new Row("UsoSvc", "Update Orchestrator",
			"Schedules update scans and installs.",
			"Windows Update stops working properly.", Tweaks.Risky, Auto),
		new Row("WaaSMedicSvc", "Update Medic Service",
			"Repairs Windows Update when it breaks, and undoes attempts to disable it.",
			"Windows Update cannot repair itself.", Tweaks.Risky, Manual),
		new Row("BITS", "Background Intelligent Transfer Service",
			"Downloads in the background for Windows Update and many other programs.",
			"Windows Update and anything that uses BITS to download, which includes some game launchers.", Tweaks.Risky, Manual),
		new Row("InstallService", "Microsoft Store Install Service",
			"Installs and updates Store apps.",
			"The Microsoft Store cannot install or update anything.", Tweaks.Caution, Manual),
		new Row("ClipSVC", "Client License Service",
			"Licenses Store apps.",
			"Store apps stop launching.", Tweaks.Risky, Manual),
		new Row("LicenseManager", "Windows License Manager",
			"Store licensing.",
			"Store apps and some Windows features stop launching.", Tweaks.Risky, Manual),
		new Row("sppsvc", "Software Protection",
			"Windows and Office activation.",
			"Windows may report itself as not activated.", Tweaks.Risky, Auto),
		new Row("mpssvc", "Windows Defender Firewall",
			"Filters inbound and outbound traffic.",
			"The firewall stops filtering anything. On a PC that plays online this is a real exposure, and some guides turn it off anyway. Your choice, but know what it is.", Tweaks.Risky, Auto),
		new Row("BFE", "Base Filtering Engine",
			"The engine the firewall, IPsec and many VPNs are built on.",
			"The firewall and most VPN clients stop working entirely.", Tweaks.Risky, Auto),
		new Row("WinDefend", "Microsoft Defender Antivirus",
			"Scans files as they are opened, which is measurable overhead while a game loads assets.",
			"The PC has no antivirus. Tamper Protection in Windows Security will usually refuse this change anyway, and turn it back on. Add an exclusion for your game folder instead - same gain, no exposure.", Tweaks.Risky, Auto),
		new Row("wscsvc", "Security Center",
			"Reports security status and nags when something is off.",
			"Windows stops telling you when your protection is off.", Tweaks.Risky, Auto),
		new Row("SecurityHealthService", "Windows Security Service",
			"The Windows Security app and its notifications.",
			"The Windows Security app stops working.", Tweaks.Risky, Manual),
		new Row("Sense", "Microsoft Defender for Endpoint",
			"Enterprise endpoint telemetry.",
			"Nothing on a home PC; it is normally not running there anyway.", Tweaks.Caution, Manual),
		new Row("SgrmBroker", "System Guard Runtime Monitor",
			"Attests to system integrity.",
			"System Guard attestation.", Tweaks.Caution, Auto),
		new Row("vds", "Virtual Disk",
			"Disk and volume management.",
			"Disk Management and diskpart stop working until it is back on.", Tweaks.Caution, Manual),
		new Row("msiserver", "Windows Installer",
			"Installs .msi packages. Starts on demand.",
			"Nothing can install from an .msi while it is off, including many games' prerequisites.", Tweaks.Risky, Manual),
		new Row("EventSystem", "COM+ Event System",
			"Delivers COM+ events; a lot of Windows depends on it.",
			"Various parts of Windows break in ways that are hard to connect back to this. Keep it on.", Tweaks.Risky, Auto),
		new Row("HvHost", "HV Host Service",
			"Hyper-V host support.",
			"Hyper-V, WSL 2 and Windows Sandbox. Also part of the virtualisation-based security stack.", Tweaks.Caution, Manual),
		new Row("hidserv", "Human Interface Device Service",
			"Delivers the extra buttons on a keyboard - volume keys, media keys and similar hotkeys.",
			"Media and volume keys on the keyboard stop working. The mouse, normal typing and LiteSynapse's own remaps are unaffected: those do not go through this service.", Tweaks.Caution, Manual)
	};

	// A service the page may offer, or null when it is not on the list or is one that must be left alone.
	internal static Row Find(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return null;
		}
		string want = name.Trim();
		if (IsProtected(want))
		{
			return null;
		}
		foreach (Row row in All)
		{
			if (string.Equals(row.Service, want, StringComparison.OrdinalIgnoreCase))
			{
				return row;
			}
		}
		return null;
	}

	// Never offered, whatever else says otherwise.
	internal static bool IsProtected(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return true;
		}
		string want = name.Trim();
		return NeverOffer.Any((string n) => string.Equals(n, want, StringComparison.OrdinalIgnoreCase));
	}

	// The PowerShell for a set of service changes. on = put it back to the start type Windows ships and start
	// it; off = disable it and then stop it, in that order, so nothing restarts it in between.
	//
	// Service names come from this catalog, never from anything read off the PC, and are quoted anyway.
	internal static string Script(IEnumerable<(string Service, bool On)> choices)
	{
		List<(Row Row, bool On)> list = new List<(Row, bool)>();
		if (choices != null)
		{
			foreach ((string name, bool on) in choices)
			{
				Row row = Find(name);
				if (row != null)
				{
					list.Add((row, on));
				}
			}
		}
		System.Text.StringBuilder sb = new System.Text.StringBuilder();
		sb.Append("$failed = 0\n");
		foreach ((Row row, bool on) in list)
		{
			string n = RazerLeftovers.Ps(row.Service);
			if (on)
			{
				string start = row.Default == Auto ? "auto" : (row.Default == Manual ? "demand" : "disabled");
				sb.Append($"sc.exe config {n} start= {start} | Out-Null\n");
				sb.Append("if ($LASTEXITCODE -ne 0) { $failed++ }\n");
				if (row.Default == Auto && !row.PerUser)
				{
					// A service that will not start is not counted as a failure: several of these start on
					// demand even when they are set to automatic.
					sb.Append($"sc.exe start {n} | Out-Null\n");
				}
			}
			else
			{
				sb.Append($"sc.exe config {n} start= disabled | Out-Null\n");
				sb.Append("if ($LASTEXITCODE -ne 0) { $failed++ }\n");
				// A per-user template has no running instance under its own name, so stopping it by name
				// fails and tells the user nothing useful. The start-type change is what takes effect.
				if (!row.PerUser)
				{
					sb.Append($"sc.exe stop {n} | Out-Null\n");
				}
			}
		}
		sb.Append("exit $failed\n");
		return sb.ToString();
	}
}
