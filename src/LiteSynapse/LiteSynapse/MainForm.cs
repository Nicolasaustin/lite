using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.Layout;

namespace LiteSynapse;

internal class MainForm : Form
{
	[Serializable]
	[CompilerGenerated]
	private sealed class _003C_003Ec
	{
		public static readonly _003C_003Ec _003C_003E9 = new _003C_003Ec();

		public static Func<MacroStep, string> _003C_003E9__73_3;

		public static PaintEventHandler _003C_003E9__74_0;

		public static Func<KbProfile, string> _003C_003E9__76_1;

		public static Func<GameRule, string> _003C_003E9__77_0;

		public static EventHandler _003C_003E9__78_4;

		public static PaintEventHandler _003C_003E9__79_5;

		public static PaintEventHandler _003C_003E9__79_6;

		public static LinkLabelLinkClickedEventHandler _003C_003E9__79_7;

		public static PaintEventHandler _003C_003E9__80_0;

		public static Func<MouseProfile, string> _003C_003E9__86_0;

		public static Func<MouseProfile, string> _003C_003E9__109_0;

		public static Func<KbProfile, string> _003C_003E9__109_1;

		public static Func<Macro, string> _003C_003E9__109_2;

		public static Func<Process, bool> _003C_003E9__112_0;

		public static Func<Process, string> _003C_003E9__112_1;

		public static Func<string, string> _003C_003E9__112_2;

		internal string _003CBuildMacro_003Eb__73_3(MacroStep s)
		{
			if (s.Vk != 0)
			{
				return (s.Down ? "press  " : "release") + "  " + VkName(s.Vk, s.Ext);
			}
			return $"wait {s.DelayMs} ms";
		}

		internal void _003CBuildGames_003Eb__74_0(object? _, PaintEventArgs pe)
		{
			//IL_001c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0022: Expected Obj, but got Unknown
			pe.Graphics.SmoothingMode = (SmoothingMode)4;
			Pen p = new Pen(Color.FromArgb(80, 80, 80), 1.6f);
			try
			{
				pe.Graphics.DrawEllipse(p, 11, 10, 11, 11);
				pe.Graphics.DrawLine(p, 20, 20, 26, 26);
			}
			finally
			{
				((IDisposable)p)?.Dispose();
			}
		}

		internal string _003CGameRow_003Eb__76_1(KbProfile p)
		{
			return p.Name;
		}

		internal string _003CAddGame_003Eb__77_0(GameRule x)
		{
			return x.Profile;
		}

		internal void _003CBuildSettings_003Eb__78_4(object? _1, EventArgs _2)
		{
			Process.Start("explorer.exe", Cfg.Dir);
		}

		internal void _003CBuildNaga_003Eb__79_5(object? _, PaintEventArgs pe)
		{
			//IL_0011: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Expected Obj, but got Unknown
			pe.Graphics.SmoothingMode = (SmoothingMode)4;
			SolidBrush b = new SolidBrush(Theme.Green);
			try
			{
				pe.Graphics.FillEllipse((Brush)(object)b, 0, 0, 29, 29);
				Tx.Center(pe.Graphics, "1", Theme.F(10f, (FontStyle)1), Color.Black, new RectangleF(0f, 0f, 30f, 30f));
			}
			finally
			{
				((IDisposable)b)?.Dispose();
			}
		}

		internal void _003CBuildNaga_003Eb__79_6(object? _, PaintEventArgs pe)
		{
			//IL_0014: Unknown result type (might be due to invalid IL or missing references)
			//IL_001a: Expected Obj, but got Unknown
			SolidBrush b = new SolidBrush(Color.FromArgb(190, 190, 190));
			try
			{
				pe.Graphics.FillRectangle((Brush)(object)b, 0, 0, 17, 17);
				pe.Graphics.FillRectangle((Brush)(object)b, 19, 0, 17, 17);
				pe.Graphics.FillRectangle((Brush)(object)b, 0, 19, 17, 17);
				pe.Graphics.FillRectangle((Brush)(object)b, 19, 19, 17, 17);
			}
			finally
			{
				((IDisposable)b)?.Dispose();
			}
		}

		internal void _003CBuildNaga_003Eb__79_7(object? _1, LinkLabelLinkClickedEventArgs _2)
		{
			try
			{
				Process.Start(new ProcessStartInfo("control.exe", "main.cpl")
				{
					UseShellExecute = true
				});
			}
			catch
			{
			}
		}

		internal void _003CBuildLighting_003Eb__80_0(object? _, PaintEventArgs pe)
		{
			//IL_000b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0011: Expected Obj, but got Unknown
			Pen p = new Pen(Color.FromArgb(90, 90, 90));
			try
			{
				pe.Graphics.DrawRectangle(p, 0, 0, 16, 16);
			}
			finally
			{
				((IDisposable)p)?.Dispose();
			}
		}

		internal string _003CToggleMemory_003Eb__86_0(MouseProfile p)
		{
			return p.Name;
		}

		internal string _003CLoadAll_003Eb__109_0(MouseProfile p)
		{
			return p.Name;
		}

		internal string _003CLoadAll_003Eb__109_1(KbProfile p)
		{
			return p.Name;
		}

		internal string _003CLoadAll_003Eb__109_2(Macro m)
		{
			return m.Name;
		}

		internal bool _003CPickRunning_003Eb__112_0(Process p)
		{
			try
			{
				return p.MainWindowHandle != IntPtr.Zero && !string.IsNullOrEmpty(p.MainWindowTitle);
			}
			catch
			{
				return false;
			}
		}

		internal string _003CPickRunning_003Eb__112_1(Process p)
		{
			return p.ProcessName + ".exe";
		}

		internal string _003CPickRunning_003Eb__112_2(string x)
		{
			return x;
		}
	}

	private readonly Service app;

	private readonly TopTabs top;

	private readonly Label lblTitle;

	private readonly Panel titleRow;

	private readonly Panel bar;

	private readonly DarkCombo cbProfile;

	private readonly Label btnMore;

	private readonly PillTabs pills;

	private readonly Panel host;

	private readonly AssignPanel assign;

	private readonly Panel memPanel;

	private readonly IconBtn memBtn;

	private readonly DarkCombo[] memCombos;

	private readonly Label memStatus;

	private Dictionary<string, string> memSlots;

	private bool memBusy;

	private bool loading;

	private string lastMouse;

	private string lastKb;

	private readonly Panel pHome;

	private readonly Panel pMacro;

	private readonly Panel pGames;

	private readonly Panel pSettings;

	private readonly Panel pNagaCustom;

	private readonly Panel pNagaPerf;

	private readonly Panel pNagaLight;

	private readonly Panel pNagaCalib;

	private readonly Panel pKbCustom;

	private readonly Panel pKbPerf;

	private readonly Panel pKbLight;

	private readonly MouseTopView topView;

	private readonly MouseSideView sideView;

	private readonly ThumbButton thTop;

	private readonly ThumbButton thSide;

	private readonly ModeSwitch modeSw;

	private readonly Label lblCalibWarn;

	private readonly Label lblDpi;

	private readonly GreenSlider dpi;

	private readonly ChoiceButton[] polls;

	private readonly DataGridView gridCalib;

	private readonly Toggle nagaBrightOn;

	private readonly Toggle kbBrightOn;

	private readonly GreenSlider nagaBright;

	private readonly GreenSlider kbBright;

	private readonly Dictionary<string, DarkCombo> arrowCombos;

	private readonly Toggle snap;

	private readonly FlowLayoutPanel gamesList;

	private readonly TextBox txtSearch;

	private readonly ListBox lstMacros;

	private readonly TextBox txtMacro;

	private readonly TextBox txtLog;

	private readonly Toggle tgAuto;

	private readonly Toggle tgStartup;

	private readonly Toggle tgLive;

	private readonly Label lblHome1;

	private readonly Label lblHome2;

	private static readonly Color[] SlotColors = new Color[6]
	{
		Color.Black,
		Color.White,
		Color.FromArgb(235, 60, 60),
		Color.FromArgb(68, 214, 44),
		Color.FromArgb(70, 120, 255),
		Color.FromArgb(40, 220, 230)
	};

	private bool IsMouse => top.Selected == 3;

	private bool IsDevice
	{
		get
		{
			int selected = top.Selected;
			if ((uint)(selected - 3) <= 1u)
			{
				return true;
			}
			return false;
		}
	}

	private MouseProfile MP => app.Store.Mouse.FirstOrDefault((MouseProfile p) => p.Name == app.ActiveMouse);

	private KbProfile KP => app.Store.Keyboard.FirstOrDefault((KbProfile p) => p.Name == app.ActiveKeyboard);

	private string StartingProfile
	{
		get
		{
			if (!IsMouse)
			{
				return app.DefaultKeyboard;
			}
			return app.DefaultMouse;
		}
	}

	// The Performance tab's index in the top bar (the tray's "Performance" opens it).
	public const int PagePerf = 6;

	// Built on demand: its five pages read the PC, so nothing happens until the tab is opened.
	private PerfPage perf;

	private static Panel Page()
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0018: Expected Obj, but got Unknown
		return new Panel
		{
			Dock = (DockStyle)5,
			BackColor = Theme.Bg
		};
	}

	public MainForm(Service app)
	{
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0049: Unknown result type (might be due to invalid IL or missing references)
		//IL_0054: Unknown result type (might be due to invalid IL or missing references)
		//IL_006a: Expected Obj, but got Unknown
		//IL_006b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0070: Unknown result type (might be due to invalid IL or missing references)
		//IL_0077: Unknown result type (might be due to invalid IL or missing references)
		//IL_007f: Unknown result type (might be due to invalid IL or missing references)
		//IL_008f: Expected Obj, but got Unknown
		//IL_0090: Unknown result type (might be due to invalid IL or missing references)
		//IL_0095: Unknown result type (might be due to invalid IL or missing references)
		//IL_009c: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b4: Expected Obj, but got Unknown
		//IL_00da: Unknown result type (might be due to invalid IL or missing references)
		//IL_00df: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ea: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fa: Unknown result type (might be due to invalid IL or missing references)
		//IL_010c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0114: Unknown result type (might be due to invalid IL or missing references)
		//IL_011f: Unknown result type (might be due to invalid IL or missing references)
		//IL_012a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0135: Unknown result type (might be due to invalid IL or missing references)
		//IL_014b: Expected Obj, but got Unknown
		//IL_0168: Unknown result type (might be due to invalid IL or missing references)
		//IL_016d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0174: Unknown result type (might be due to invalid IL or missing references)
		//IL_0184: Expected Obj, but got Unknown
		//IL_0190: Unknown result type (might be due to invalid IL or missing references)
		//IL_0195: Unknown result type (might be due to invalid IL or missing references)
		//IL_01aa: Unknown result type (might be due to invalid IL or missing references)
		//IL_01bb: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c7: Expected Obj, but got Unknown
		//IL_021d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0222: Unknown result type (might be due to invalid IL or missing references)
		//IL_022d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0238: Unknown result type (might be due to invalid IL or missing references)
		//IL_0249: Unknown result type (might be due to invalid IL or missing references)
		//IL_025b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0272: Expected Obj, but got Unknown
		//IL_02aa: Unknown result type (might be due to invalid IL or missing references)
		//IL_02af: Unknown result type (might be due to invalid IL or missing references)
		//IL_02b6: Unknown result type (might be due to invalid IL or missing references)
		//IL_02c6: Expected Obj, but got Unknown
		//IL_0363: Unknown result type (might be due to invalid IL or missing references)
		//IL_0368: Unknown result type (might be due to invalid IL or missing references)
		//IL_036f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0386: Unknown result type (might be due to invalid IL or missing references)
		//IL_0391: Unknown result type (might be due to invalid IL or missing references)
		//IL_03a2: Unknown result type (might be due to invalid IL or missing references)
		//IL_03b2: Expected Obj, but got Unknown
		//IL_03b3: Unknown result type (might be due to invalid IL or missing references)
		//IL_03b8: Unknown result type (might be due to invalid IL or missing references)
		//IL_03bf: Unknown result type (might be due to invalid IL or missing references)
		//IL_03ca: Unknown result type (might be due to invalid IL or missing references)
		//IL_03d5: Unknown result type (might be due to invalid IL or missing references)
		//IL_03eb: Expected Obj, but got Unknown
		//IL_048c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0491: Unknown result type (might be due to invalid IL or missing references)
		//IL_049d: Expected Obj, but got Unknown
		//IL_0559: Unknown result type (might be due to invalid IL or missing references)
		//IL_055e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0565: Unknown result type (might be due to invalid IL or missing references)
		//IL_056c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0573: Unknown result type (might be due to invalid IL or missing references)
		//IL_057a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0585: Unknown result type (might be due to invalid IL or missing references)
		//IL_058a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0599: Expected Obj, but got Unknown
		//IL_059a: Unknown result type (might be due to invalid IL or missing references)
		//IL_059f: Unknown result type (might be due to invalid IL or missing references)
		//IL_05a6: Unknown result type (might be due to invalid IL or missing references)
		//IL_05b7: Unknown result type (might be due to invalid IL or missing references)
		//IL_05c2: Unknown result type (might be due to invalid IL or missing references)
		//IL_05d8: Expected Obj, but got Unknown
		//IL_05d9: Unknown result type (might be due to invalid IL or missing references)
		//IL_05de: Unknown result type (might be due to invalid IL or missing references)
		//IL_05e5: Unknown result type (might be due to invalid IL or missing references)
		//IL_05f0: Unknown result type (might be due to invalid IL or missing references)
		//IL_05fb: Unknown result type (might be due to invalid IL or missing references)
		//IL_0606: Unknown result type (might be due to invalid IL or missing references)
		//IL_060d: Unknown result type (might be due to invalid IL or missing references)
		//IL_061e: Unknown result type (might be due to invalid IL or missing references)
		//IL_062a: Expected Obj, but got Unknown
		//IL_062b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0630: Unknown result type (might be due to invalid IL or missing references)
		//IL_0637: Unknown result type (might be due to invalid IL or missing references)
		//IL_063e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0645: Unknown result type (might be due to invalid IL or missing references)
		//IL_0650: Unknown result type (might be due to invalid IL or missing references)
		//IL_065b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0662: Unknown result type (might be due to invalid IL or missing references)
		//IL_066d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0677: Expected Obj, but got Unknown
		//IL_067c: Expected Obj, but got Unknown
		//IL_067d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0682: Unknown result type (might be due to invalid IL or missing references)
		//IL_0689: Unknown result type (might be due to invalid IL or missing references)
		//IL_0690: Unknown result type (might be due to invalid IL or missing references)
		//IL_0697: Unknown result type (might be due to invalid IL or missing references)
		//IL_06a2: Unknown result type (might be due to invalid IL or missing references)
		//IL_06bc: Unknown result type (might be due to invalid IL or missing references)
		//IL_06c3: Unknown result type (might be due to invalid IL or missing references)
		//IL_06ce: Unknown result type (might be due to invalid IL or missing references)
		//IL_06d8: Expected Obj, but got Unknown
		//IL_06d8: Unknown result type (might be due to invalid IL or missing references)
		//IL_06e4: Expected Obj, but got Unknown
		//IL_0727: Unknown result type (might be due to invalid IL or missing references)
		//IL_072c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0733: Unknown result type (might be due to invalid IL or missing references)
		//IL_073e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0749: Unknown result type (might be due to invalid IL or missing references)
		//IL_075f: Expected Obj, but got Unknown
		//IL_0760: Unknown result type (might be due to invalid IL or missing references)
		//IL_0765: Unknown result type (might be due to invalid IL or missing references)
		//IL_076c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0777: Unknown result type (might be due to invalid IL or missing references)
		//IL_0782: Unknown result type (might be due to invalid IL or missing references)
		//IL_0798: Expected Obj, but got Unknown
		//IL_0935: Unknown result type (might be due to invalid IL or missing references)
		//IL_093a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0945: Unknown result type (might be due to invalid IL or missing references)
		//IL_0950: Unknown result type (might be due to invalid IL or missing references)
		//IL_095b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0966: Unknown result type (might be due to invalid IL or missing references)
		//IL_0970: Expected Obj, but got Unknown
		//IL_0970: Unknown result type (might be due to invalid IL or missing references)
		//IL_097f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0987: Expected Obj, but got Unknown
		//IL_0a14: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a19: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a20: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a27: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a3d: Expected Obj, but got Unknown
		//IL_0b8e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b98: Expected Obj, but got Unknown
		TopTabs topTabs = new TopTabs();
		((Control)topTabs).Dock = (DockStyle)1;
		((Control)topTabs).Height = 40;
		top = topTabs;
		lblTitle = new Label
		{
			Dock = (DockStyle)5,
			TextAlign = (ContentAlignment)32,
			ForeColor = Color.FromArgb(160, 160, 160),
			BackColor = Theme.Bg,
			Font = Theme.F(10f, (FontStyle)0)
		};
		titleRow = new Panel
		{
			Dock = (DockStyle)1,
			Height = 44,
			BackColor = Theme.Bg
		};
		bar = new Panel
		{
			Dock = (DockStyle)1,
			Height = 50,
			BackColor = Theme.Bg
		};
		DarkCombo darkCombo = new DarkCombo();
		((Control)darkCombo).Width = 230;
		((Control)darkCombo).Location = new Point(40, 10);
		cbProfile = darkCombo;
		btnMore = new Label
		{
			Text = "•••",
			Width = 34,
			Height = 30,
			Location = new Point(280, 10),
			TextAlign = (ContentAlignment)32,
			ForeColor = Color.White,
			BackColor = Theme.Bg,
			Cursor = Cursors.Hand,
			Font = Theme.F(9f, (FontStyle)1)
		};
		PillTabs pillTabs = new PillTabs();
		((Control)pillTabs).Location = new Point(400, 8);
		pills = pillTabs;
		host = new Panel
		{
			Dock = (DockStyle)5,
			BackColor = Theme.Bg
		};
		assign = new AssignPanel();
		memPanel = new Panel
		{
			Size = new Size(430, 424),
			BackColor = Color.FromArgb(24, 24, 24),
			Visible = false
		};
		IconBtn iconBtn = new IconBtn
		{
			Sd = true,
			SdDot = Theme.Green,
			Tip = "On-Board Memory"
		};
		((Control)iconBtn).Location = new Point(322, 8);
		((Control)iconBtn).BackColor = Theme.Bg;
		memBtn = iconBtn;
		memCombos = new DarkCombo[6];
		memStatus = new Label
		{
			ForeColor = Theme.Dim,
			BackColor = Color.Transparent,
			Font = Theme.F(9.5f, (FontStyle)0),
			Location = new Point(20, 342),
			Size = new Size(390, 70)
		};
		memSlots = new Dictionary<string, string>();
		pHome = Page();
		pMacro = Page();
		pGames = Page();
		pSettings = Page();
		pNagaCustom = new Panel
		{
			Dock = (DockStyle)5,
			BackColor = Theme.Bg
		};
		pNagaPerf = Page();
		pNagaLight = Page();
		pNagaCalib = Page();
		pKbCustom = Page();
		pKbPerf = Page();
		pKbLight = Page();
		MouseTopView mouseTopView = new MouseTopView();
		((Control)mouseTopView).Dock = (DockStyle)5;
		topView = mouseTopView;
		MouseSideView mouseSideView = new MouseSideView();
		((Control)mouseSideView).Dock = (DockStyle)5;
		((Control)mouseSideView).Visible = false;
		sideView = mouseSideView;
		thTop = new ThumbButton
		{
			Selected = true
		};
		thSide = new ThumbButton
		{
			Side = true
		};
		modeSw = new ModeSwitch();
		lblCalibWarn = new Label
		{
			AutoSize = true,
			ForeColor = Color.FromArgb(230, 170, 60),
			BackColor = Theme.Bg,
			Font = Theme.F(9.5f, (FontStyle)0),
			Cursor = Cursors.Hand
		};
		lblDpi = new Label
		{
			AutoSize = true,
			ForeColor = Theme.Text,
			BackColor = Theme.Card,
			Font = Theme.F(10.5f, (FontStyle)0)
		};
		GreenSlider greenSlider = new GreenSlider
		{
			Min = 100,
			Max = 20000,
			Step = 100
		};
		((Control)greenSlider).Height = 28;
		dpi = greenSlider;
		ChoiceButton[] array = new ChoiceButton[3];
		ChoiceButton choiceButton = new ChoiceButton();
		((Control)choiceButton).Text = "125";
		((Control)choiceButton).Tag = 125;
		array[0] = choiceButton;
		ChoiceButton choiceButton2 = new ChoiceButton();
		((Control)choiceButton2).Text = "500";
		((Control)choiceButton2).Tag = 500;
		array[1] = choiceButton2;
		ChoiceButton choiceButton3 = new ChoiceButton();
		((Control)choiceButton3).Text = "1000";
		((Control)choiceButton3).Tag = 1000;
		array[2] = choiceButton3;
		polls = array;
		gridCalib = new DataGridView
		{
			Dock = (DockStyle)5
		};
		Toggle toggle = new Toggle();
		((Control)toggle).BackColor = Theme.Card;
		((Control)toggle).Enabled = false;
		nagaBrightOn = toggle;
		Toggle toggle2 = new Toggle();
		((Control)toggle2).BackColor = Theme.Card;
		kbBrightOn = toggle2;
		GreenSlider greenSlider2 = new GreenSlider
		{
			Min = 0,
			Max = 100,
			Bubble = true,
			Labels = true
		};
		((Control)greenSlider2).Height = 54;
		((Control)greenSlider2).Enabled = false;
		nagaBright = greenSlider2;
		GreenSlider greenSlider3 = new GreenSlider
		{
			Min = 0,
			Max = 100,
			Bubble = true,
			Labels = true
		};
		((Control)greenSlider3).Height = 54;
		kbBright = greenSlider3;
		arrowCombos = new Dictionary<string, DarkCombo>();
		Toggle toggle3 = new Toggle();
		((Control)toggle3).BackColor = Theme.Card;
		snap = toggle3;
		gamesList = new FlowLayoutPanel
		{
			Dock = (DockStyle)5,
			FlowDirection = (FlowDirection)1,
			WrapContents = false,
			AutoScroll = true,
			BackColor = Theme.Bg,
			Padding = new Padding(0, 4, 0, 4)
		};
		txtSearch = new TextBox
		{
			BorderStyle = (BorderStyle)0,
			BackColor = Color.FromArgb(45, 45, 45),
			ForeColor = Theme.Text,
			Font = Theme.F(10.5f, (FontStyle)0)
		};
		lstMacros = new ListBox
		{
			Dock = (DockStyle)3,
			Width = 240,
			BackColor = Theme.Card,
			ForeColor = Theme.Text,
			BorderStyle = (BorderStyle)0,
			Font = Theme.F(11f, (FontStyle)0),
			IntegralHeight = false
		};
		txtMacro = new TextBox
		{
			Dock = (DockStyle)5,
			Multiline = true,
			ReadOnly = true,
			BackColor = Theme.Card,
			ForeColor = Theme.Text,
			BorderStyle = (BorderStyle)0,
			Font = new Font("Consolas", 11f)
		};
		txtLog = new TextBox
		{
			Dock = (DockStyle)5,
			Multiline = true,
			ReadOnly = true,
			BackColor = Theme.Card,
			ForeColor = Color.FromArgb(200, 200, 200),
			BorderStyle = (BorderStyle)0,
			Font = new Font("Consolas", 9.5f),
			ScrollBars = (ScrollBars)2
		};
		Toggle toggle4 = new Toggle();
		((Control)toggle4).BackColor = Theme.Card;
		tgAuto = toggle4;
		Toggle toggle5 = new Toggle();
		((Control)toggle5).BackColor = Theme.Card;
		tgStartup = toggle5;
		Toggle toggle6 = new Toggle();
		((Control)toggle6).BackColor = Theme.Card;
		tgLive = toggle6;
		lblHome1 = new Label
		{
			AutoSize = true,
			ForeColor = Theme.Text,
			BackColor = Theme.Card,
			Font = Theme.F(11f, (FontStyle)0)
		};
		lblHome2 = new Label
		{
			AutoSize = true,
			ForeColor = Theme.Text,
			BackColor = Theme.Card,
			Font = Theme.F(11f, (FontStyle)0)
		};
		MainForm mainForm = this;
		this.app = app;
		base.Text = "LiteSynapse";
		base.Icon = AppIcon.Icon();
		base.Width = 1180;
		base.Height = 780;
		base.MinimumSize = new Size(1000, 700);
		base.StartPosition = (FormStartPosition)1;
		base.BackColor = Theme.Bg;
		base.ForeColor = Theme.Text;
		base.Font = Theme.F(10f, (FontStyle)0);
		base.DoubleBuffered = true;
		top.Items = new (string, string)[7]
		{
			("Synapse", "S"),
			("Macro", "M"),
			("Linked Games", "G"),
			("Naga Left-Handed Edition", "N"),
			("BlackWidow Lite", "B"),
			("Settings", "⚙"),
			("Performance", "P")
		};
		string tabsFile = Path.Combine(Cfg.Dir, "tabs.json");
		top.Order = Cfg.Load<List<int>>(tabsFile);
		top.OrderChanged += () =>
		{
			Cfg.Save(tabsFile, mainForm.top.Order);
		};
		((Control)titleRow).Controls.Add((Control)(object)lblTitle);
		Label layers = new Label
		{
			Text = "☰",
			ForeColor = Color.White,
			BackColor = Theme.Bg,
			Font = new Font("Segoe UI Symbol", 12f),
			Location = new Point(10, 12),
			AutoSize = true
		};
		((Control)bar).Controls.AddRange(new Control[5]
		{
			(Control)layers,
			cbProfile,
			(Control)btnMore,
			memBtn,
			pills
		});
		((Control)memBtn).Click += (object? _, EventArgs _) =>
		{
			mainForm.ToggleMemory();
		};
		((Control)bar).Resize += (object? _, EventArgs _) =>
		{
			((Control)mainForm.pills).SetBounds((((Control)mainForm.bar).Width - mainForm.pills.TotalWidth()) / 2, 8, mainForm.pills.TotalWidth() + 2, 34);
		};
		base.Controls.Add((Control)(object)host);
		base.Controls.Add((Control)new Panel
		{
			Dock = (DockStyle)1,
			Height = 2,
			BackColor = Color.FromArgb(10, 10, 10)
		});
		base.Controls.Add((Control)(object)bar);
		base.Controls.Add((Control)(object)titleRow);
		base.Controls.Add((Control)(object)top);
		BuildHome();
		BuildMacro();
		BuildGames();
		BuildSettings();
		BuildNaga();
		BuildKeyboard();
		BuildMemory();
		top.Changed += () =>
		{
			mainForm.SetPills();
			mainForm.ShowPage();
		};
		pills.Changed += ShowPage;
		cbProfile.SelectedIndexChanged += () =>
		{
			if (!mainForm.loading && mainForm.cbProfile.SelectedItem != null)
			{
				mainForm.SelectProfile((string)mainForm.cbProfile.SelectedItem);
			}
		};
		((Control)btnMore).Click += (object? _, EventArgs _) =>
		{
			mainForm.ShowMoreMenu();
		};
		((Control)btnMore).MouseEnter += (object? _, EventArgs _) =>
		{
			((Control)mainForm.btnMore).ForeColor = Theme.Green;
		};
		((Control)btnMore).MouseLeave += (object? _, EventArgs _) =>
		{
			((Control)mainForm.btnMore).ForeColor = Color.White;
		};
		assign.OnSave = (string id, string v) =>
		{
			mainForm.SaveButton(id, v);
		};
		Action onChanged = () =>
		{
			if (((Control)mainForm).IsHandleCreated && !((Control)mainForm).IsDisposed)
			{
				((Control)mainForm).BeginInvoke((Action)(() =>
				{
					mainForm.Sync();
					// Not while minimized: a profile switch in a game must not cost a log read.
					if (mainForm.top.Selected == 5 && ((Form)mainForm).WindowState != FormWindowState.Minimized && ((Control)mainForm).Visible)
					{
						mainForm.LoadLog();
					}
				}));
			}
		};
		app.Changed += onChanged;
		base.FormClosed += (object? _1, FormClosedEventArgs _2) =>
		{
			app.Changed -= onChanged;
		};
		SetPills();
		ShowPage();
	}

	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);
		Ui.DarkTitleBar((Form)(object)this);
	}

	public void OpenMemory()
	{
		ToggleMemory();
	}

	public void Goto(int page, int tab)
	{
		top.Selected = page;
		((Control)top).Invalidate();
		SetPills();
		pills.Selected = Math.Min(tab, Math.Max(0, pills.Items.Length - 1));
		((Control)pills).Invalidate();
		ShowPage();
	}

	private void SetPills()
	{
		PillTabs pillTabs = pills;
		pillTabs.Items = top.Selected switch
		{
			3 => new string[4] { "CUSTOMIZE", "PERFORMANCE", "LIGHTING", "CALIBRATION" }, 
			4 => new string[3] { "CUSTOMIZE", "PERFORMANCE", "LIGHTING" }, 
			PagePerf => PerfPage.Pills, 
			_ => Array.Empty<string>(), 
		};
		pills.Selected = 0;
		((Control)pills).Invalidate();
		((Control)bar).Visible = IsDevice || top.Selected == PagePerf;
		// The profile picker and the on-board memory button belong to a device page only.
		((Control)cbProfile).Visible = IsDevice;
		((Control)btnMore).Visible = IsDevice;
		((Control)bar).Width = ((Control)bar).Width;
		((Control)pills).SetBounds((((Control)bar).Width - pills.TotalWidth()) / 2, 8, pills.TotalWidth() + 2, 34);
		Label val = lblTitle;
		((Control)val).Text = top.Selected switch
		{
			0 => "SYNAPSE", 
			1 => "MACRO", 
			2 => "LINKED GAMES", 
			3 => "RAZER NAGA LEFT-HANDED EDITION", 
			4 => "RAZER BLACKWIDOW LITE", 
			PagePerf => "PERFORMANCE", 
			_ => "SETTINGS", 
		};
	}

	private void ShowPage()
	{
		if (top.Selected == PagePerf)
		{
			// First time in: build the five pages. Nothing is read from the PC until one of them is shown.
			perf ??= new PerfPage(app, (Form)(object)this);
		}
		Panel page = top.Selected switch
		{
			0 => pHome, 
			1 => pMacro, 
			2 => pGames, 
			5 => pSettings, 
			PagePerf => perf.Page(pills.Selected), 
			3 => pills.Selected switch
			{
				0 => pNagaCustom, 
				1 => pNagaPerf, 
				2 => pNagaLight, 
				_ => pNagaCalib, 
			}, 
			_ => pills.Selected switch
			{
				0 => pKbCustom, 
				1 => pKbPerf, 
				_ => pKbLight, 
			}, 
		};
		((Control)host).Controls.Clear();
		((Control)host).Controls.Add((Control)(object)page);
		((Control)host).Controls.Add((Control)(object)assign);
		((Control)host).Controls.Add((Control)(object)memPanel);
		((Control)assign).Hide();
		((Control)memPanel).Hide();
		((Control)assign).Location = new Point(24, 12);
		((Control)memBtn).Visible = IsMouse;
		if (top.Selected == PagePerf)
		{
			perf.Show(pills.Selected);
			return;
		}
		LoadAll();
	}

	private static Label Lbl(string t, Color? c = null, float size = 10f, bool bold = false)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_003d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0051: Expected Obj, but got Unknown
		return new Label
		{
			Text = t,
			AutoSize = true,
			ForeColor = (c ?? Theme.Text),
			BackColor = Color.Transparent,
			Font = Theme.F(size, (FontStyle)(bold ? 1 : 0))
		};
	}

	private void BuildHome()
	{
		//IL_0015: Unknown result type (might be due to invalid IL or missing references)
		((Control)pHome).Padding = new Padding(24);
		Card card = new Card
		{
			Title = "Naga Left-Handed Edition"
		};
		((Control)card).Location = new Point(24, 24);
		((Control)card).Size = new Size(520, 190);
		((Control)card).Cursor = Cursors.Hand;
		Card c1 = card;
		((Control)lblHome1).Location = new Point(40, 74);
		((Control)c1).Controls.Add((Control)(object)lblHome1);
		Action go1 = () =>
		{
			top.Selected = 3;
			((Control)top).Invalidate();
			SetPills();
			ShowPage();
		};
		((Control)c1).Click += (object? _, EventArgs _) =>
		{
			go1();
		};
		((Control)lblHome1).Click += (object? _, EventArgs _) =>
		{
			go1();
		};
		Card card2 = new Card
		{
			Title = "BlackWidow Lite"
		};
		((Control)card2).Location = new Point(560, 24);
		((Control)card2).Size = new Size(520, 190);
		((Control)card2).Cursor = Cursors.Hand;
		Card c2 = card2;
		((Control)lblHome2).Location = new Point(40, 74);
		((Control)c2).Controls.Add((Control)(object)lblHome2);
		Action go2 = () =>
		{
			top.Selected = 4;
			((Control)top).Invalidate();
			SetPills();
			ShowPage();
		};
		((Control)c2).Click += (object? _, EventArgs _) =>
		{
			go2();
		};
		((Control)lblHome2).Click += (object? _, EventArgs _) =>
		{
			go2();
		};
		((Control)pHome).Controls.AddRange(new Control[2]
		{
			(Control)c1,
			(Control)c2
		});
	}

	private void BuildMacro()
	{
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_0063: Unknown result type (might be due to invalid IL or missing references)
		//IL_006d: Expected Obj, but got Unknown
		//IL_009b: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00af: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bf: Expected Obj, but got Unknown
		((Control)pMacro).Padding = new Padding(24);
		Card card = new Card
		{
			Title = "Macros"
		};
		((Control)card).Dock = (DockStyle)5;
		((Control)card).Padding = new Padding(30, 70, 30, 30);
		Card card2 = card;
		lstMacros.DrawMode = (DrawMode)1;
		lstMacros.ItemHeight = 30;
		lstMacros.DrawItem += (object? s, DrawItemEventArgs d) =>
		{
			//IL_000b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0011: Unknown result type (might be due to invalid IL or missing references)
			//IL_0013: Invalid comparison between Unknown and I4
			//IL_002b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0031: Expected Obj, but got Unknown
			//IL_004b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0051: Expected Obj, but got Unknown
			if (d.Index < 0)
			{
				return;
			}
			bool flag = (d.State & DrawItemState.Selected) != 0;
			SolidBrush val = new SolidBrush(flag ? Color.FromArgb(40, 40, 40) : Theme.Card);
			try
			{
				d.Graphics.FillRectangle((Brush)(object)val, d.Bounds);
				if (flag)
				{
					SolidBrush val2 = new SolidBrush(Theme.Green);
					try
					{
						d.Graphics.FillRectangle((Brush)(object)val2, d.Bounds.X, d.Bounds.Y, 3, d.Bounds.Height);
					}
					finally
					{
						((IDisposable)val2)?.Dispose();
					}
				}
				Tx.Left(d.Graphics, lstMacros.Items[d.Index].ToString(), Theme.F(11f, (FontStyle)0), flag ? Theme.Green : Theme.Text, new RectangleF(d.Bounds.X + 12, d.Bounds.Y, d.Bounds.Width - 14, d.Bounds.Height));
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
		};
		lstMacros.SelectedIndexChanged += (object? _, EventArgs _) =>
		{
			Macro macro = app.Store.Macros.FirstOrDefault((Macro x) => x.Name == (string)lstMacros.SelectedItem);
			((Control)txtMacro).Text = ((macro == null) ? "" : ("  " + string.Join(Environment.NewLine + "  ", macro.Steps.Select((MacroStep s) => (s.Vk == 0) ? $"wait {s.DelayMs} ms" : ((s.Down ? "press  " : "release") + "  " + VkName(s.Vk, s.Ext))))));
		};
		((Control)card2).Controls.Add((Control)(object)txtMacro);
		((Control)card2).Controls.Add((Control)new Panel
		{
			Dock = (DockStyle)3,
			Width = 16,
			BackColor = Theme.Card
		});
		((Control)card2).Controls.Add((Control)(object)lstMacros);
		((Control)pMacro).Controls.Add((Control)(object)card2);
	}

	private void BuildGames()
	{
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0031: Unknown result type (might be due to invalid IL or missing references)
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		//IL_0045: Expected Obj, but got Unknown
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		//IL_004a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0058: Unknown result type (might be due to invalid IL or missing references)
		//IL_006a: Unknown result type (might be due to invalid IL or missing references)
		//IL_007c: Expected Obj, but got Unknown
		//IL_0091: Unknown result type (might be due to invalid IL or missing references)
		//IL_0096: Unknown result type (might be due to invalid IL or missing references)
		//IL_009c: Expected Obj, but got Unknown
		((Control)pGames).Padding = new Padding(28, 12, 20, 12);
		Panel bar2 = new Panel
		{
			Dock = (DockStyle)1,
			Height = 64,
			BackColor = Theme.Bg
		};
		Panel searchBox = new Panel
		{
			Location = new Point(0, 12),
			Size = new Size(320, 36),
			BackColor = Color.FromArgb(45, 45, 45)
		};
		PaintEventHandler val = _003C_003Ec._003C_003E9__74_0;
		if (val == null)
		{
			PaintEventHandler val2 = (object? _, PaintEventArgs pe) =>
			{
				//IL_001c: Unknown result type (might be due to invalid IL or missing references)
				//IL_0022: Expected Obj, but got Unknown
				pe.Graphics.SmoothingMode = (SmoothingMode)4;
				Pen val3 = new Pen(Color.FromArgb(80, 80, 80), 1.6f);
				try
				{
					pe.Graphics.DrawEllipse(val3, 11, 10, 11, 11);
					pe.Graphics.DrawLine(val3, 20, 20, 26, 26);
				}
				finally
				{
					((IDisposable)val3)?.Dispose();
				}
			};
			_003C_003Ec._003C_003E9__74_0 = val2;
			val = val2;
		}
		((Control)searchBox).Paint += val;
		((Control)txtSearch).Location = new Point(40, 8);
		((Control)txtSearch).Width = 270;
		txtSearch.PlaceholderText = "Search games";
		((Control)txtSearch).TextChanged += (object? _, EventArgs _) =>
		{
			FillGames();
		};
		((Control)searchBox).Controls.Add((Control)(object)txtSearch);
		PillButton pillButton = new PillButton();
		((Control)pillButton).Text = "+  ADD GAME";
		((Control)pillButton).Width = 150;
		pillButton.Primary = true;
		((Control)pillButton).Location = new Point(336, 13);
		PillButton addGame = pillButton;
		((Control)addGame).Click += (object? _, EventArgs _) =>
		{
			AddGame((Control)(object)addGame);
		};
		Label hint = Lbl("Games without a program are never switched to automatically.", Theme.Dim, 9.5f);
		((Control)hint).Location = new Point(504, 22);
		((Control)bar2).Controls.AddRange(new Control[3]
		{
			(Control)searchBox,
			addGame,
			(Control)hint
		});
		((Control)pGames).Controls.Add((Control)(object)gamesList);
		((Control)pGames).Controls.Add((Control)(object)bar2);
		DarkScroll.Apply((Control)(object)gamesList);
		DarkScroll.Apply((Control)(object)txtLog);
		DarkScroll.Apply((Control)(object)txtMacro);
		DarkScroll.Apply((Control)(object)lstMacros);
		((Control)pGames).Resize += (object? _, EventArgs _) =>
		{
			//IL_001e: Unknown result type (might be due to invalid IL or missing references)
			foreach (Control item in (ArrangedElementCollection)((Control)gamesList).Controls)
			{
				item.Width = RowWidth();
			}
		};
	}

	private int RowWidth()
	{
		return Math.Max(500, ((Control)gamesList).ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 6);
	}

	private Control GameRow(GameRule g)
	{
		//IL_0015: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		//IL_003f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0044: Unknown result type (might be due to invalid IL or missing references)
		//IL_0053: Expected Obj, but got Unknown
		//IL_00af: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b9: Expected Obj, but got Unknown
		Panel row = new Panel
		{
			Height = 74,
			Width = RowWidth(),
			BackColor = Color.FromArgb(24, 24, 24),
			Margin = new Padding(0, 0, 0, 8)
		};
		string name = g.Profile ?? "?";
		string exes = ((g.Exes.Count > 0) ? string.Join("   •   ", g.Exes) : "No program set - click the pencil to add one");
		((Control)row).Paint += (object? _, PaintEventArgs pe) =>
		{
			//IL_004e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0054: Expected Obj, but got Unknown
			//IL_0067: Unknown result type (might be due to invalid IL or missing references)
			//IL_006d: Expected Obj, but got Unknown
			//IL_007f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0086: Expected Obj, but got Unknown
			Graphics graphics = pe.Graphics;
			Theme.Quality(graphics);
			GraphicsPath val = Theme.Round(new RectangleF(0.5f, 0.5f, ((Control)row).Width - 2, ((Control)row).Height - 2), 6f);
			try
			{
				SolidBrush val2 = new SolidBrush(Color.FromArgb(24, 24, 24));
				try
				{
					graphics.FillPath((Brush)(object)val2, val);
					Pen val3 = new Pen(Color.FromArgb(46, 46, 46));
					try
					{
						graphics.DrawPath(val3, val);
						Pen val4 = new Pen(Theme.Green, 1.6f);
						try
						{
							graphics.DrawEllipse(val4, 18, 17, 40, 40);
							Font val5 = Theme.F(14f, (FontStyle)1);
							try
							{
								Tx.Center(graphics, (name.Length > 0) ? name.Substring(0, 1).ToUpperInvariant() : "?", val5, Theme.Green, new RectangleF(18f, 17f, 40f, 40f));
								Font val6 = Theme.F(11.5f, (FontStyle)1);
								try
								{
									Tx.Left(graphics, name, val6, Theme.Text, new RectangleF(76f, 12f, ((Control)row).Width - 560, 26f));
									Font val7 = Theme.F(9.5f, (FontStyle)0);
									try
									{
										Tx.Left(graphics, exes, val7, (g.Exes.Count > 0) ? Theme.Dim : Color.FromArgb(230, 170, 60), new RectangleF(76f, 40f, ((Control)row).Width - 560, 22f));
										Font val8 = Theme.F(8f, (FontStyle)1);
										try
										{
											Tx.Left(graphics, "KEYBOARD PROFILE", val8, Theme.Dim, new RectangleF(((Control)row).Width - 462, 8f, 200f, 14f));
										}
										finally
										{
											((IDisposable)val8)?.Dispose();
										}
									}
									finally
									{
										((IDisposable)val7)?.Dispose();
									}
								}
								finally
								{
									((IDisposable)val6)?.Dispose();
								}
							}
							finally
							{
								((IDisposable)val5)?.Dispose();
							}
						}
						finally
						{
							((IDisposable)val4)?.Dispose();
						}
					}
					finally
					{
						((IDisposable)val3)?.Dispose();
					}
				}
				finally
				{
					((IDisposable)val2)?.Dispose();
				}
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
		};
		DarkCombo darkCombo = new DarkCombo();
		((Control)darkCombo).Width = 200;
		((Control)darkCombo).Height = 30;
		((Control)darkCombo).BackColor = Color.FromArgb(24, 24, 24);
		((Control)darkCombo).Location = new Point(((Control)row).Width - 462, 28);
		((Control)darkCombo).Anchor = (AnchorStyles)9;
		DarkCombo kb = darkCombo;
		kb.Items.Add("(none)");
		kb.Items.AddRange(app.Store.Keyboard.Select((KbProfile p) => p.Name).Cast<object>().ToList());
		kb.SelectedItem = (string.IsNullOrEmpty(g.KeyboardProfile) ? "(none)" : g.KeyboardProfile);
		kb.SelectedIndexChanged += () =>
		{
			if (kb.SelectedItem != null)
			{
				g.KeyboardProfile = (((string)kb.SelectedItem == "(none)") ? "" : ((string)kb.SelectedItem));
				app.SaveGames();
				app.ForceRecheck();
			}
		};
		IconBtn iconBtn = new IconBtn
		{
			Glyph = "▶",
			Tip = "Pick from running apps"
		};
		((Control)iconBtn).Anchor = (AnchorStyles)9;
		((Control)iconBtn).Location = new Point(((Control)row).Width - 244, 21);
		IconBtn pick = iconBtn;
		IconBtn iconBtn2 = new IconBtn
		{
			Glyph = "✎",
			Tip = "Edit programs"
		};
		((Control)iconBtn2).Anchor = (AnchorStyles)9;
		((Control)iconBtn2).Location = new Point(((Control)row).Width - 204, 21);
		IconBtn edit = iconBtn2;
		IconBtn iconBtn3 = new IconBtn
		{
			Glyph = "✕",
			Tip = "Remove"
		};
		((Control)iconBtn3).Anchor = (AnchorStyles)9;
		((Control)iconBtn3).Location = new Point(((Control)row).Width - 164, 21);
		IconBtn del = iconBtn3;
		((Control)pick).Click += (object? _, EventArgs _) =>
		{
			string text = PickRunning();
			if (text != null && !g.Exes.Contains(text, StringComparer.OrdinalIgnoreCase))
			{
				g.Exes.Add(text);
				app.SaveGames();
				app.ForceRecheck();
				FillGames();
			}
		};
		((Control)edit).Click += (object? _, EventArgs _) =>
		{
			string text = Ui.Prompt((IWin32Window)(object)this, name, "Program files (.exe), separated by commas", string.Join(", ", g.Exes));
			if (text != null)
			{
				g.Exes = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
				app.SaveGames();
				app.ForceRecheck();
				FillGames();
			}
		};
		((Control)del).Click += (object? _, EventArgs _) =>
		{
			//IL_0021: Unknown result type (might be due to invalid IL or missing references)
			//IL_0027: Invalid comparison between Unknown and I4
			if ((int)MessageBox.Show((IWin32Window)(object)this, "Stop auto-switching to \"" + name + "\"?", "LiteSynapse", (MessageBoxButtons)1) == 1)
			{
				app.Games.Remove(g);
				app.SaveGames();
				FillGames();
			}
		};
		((Control)row).Resize += (object? _, EventArgs _) =>
		{
			((Control)kb).Left = ((Control)row).Width - 462;
			((Control)pick).Left = ((Control)row).Width - 244;
			((Control)edit).Left = ((Control)row).Width - 204;
			((Control)del).Left = ((Control)row).Width - 164;
			((Control)row).Invalidate();
		};
		((Control)row).Controls.AddRange(new Control[4] { kb, pick, edit, del });
		return (Control)(object)row;
	}

	private void AddGame(Control anchor)
	{
		//IL_00fb: Unknown result type (might be due to invalid IL or missing references)
		//IL_0100: Unknown result type (might be due to invalid IL or missing references)
		//IL_010c: Expected Obj, but got Unknown
		HashSet<string> used = app.Games.Select((GameRule x) => x.Profile).ToHashSet();
		ContextMenuStrip menu = Ui.Menu();
		foreach (MouseProfile p in app.Store.Mouse.Where((MouseProfile mouseProfile) => !used.Contains(mouseProfile.Name)))
		{
			string name = p.Name;
			((ToolStrip)menu).Items.Add(name, (Image)null, (EventHandler)((object? _, EventArgs _) =>
			{
				GameRule item = new GameRule
				{
					Profile = name
				};
				app.Games.Add(item);
				app.SaveGames();
				FillGames();
			}));
		}
		if (((ArrangedElementCollection)((ToolStrip)menu).Items).Count == 0)
		{
			((ToolStrip)menu).Items.Add((ToolStripItem)new ToolStripMenuItem("Every mouse profile is already linked")
			{
				Enabled = false
			});
		}
		((ToolStripDropDown)menu).Show(anchor, new Point(0, anchor.Height));
	}

	private void BuildSettings()
	{
		//IL_0015: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ce: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00da: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ee: Expected Obj, but got Unknown
		//IL_01b4: Unknown result type (might be due to invalid IL or missing references)
		((Control)pSettings).Padding = new Padding(24);
		Card card = new Card
		{
			Title = "General"
		};
		((Control)card).Dock = (DockStyle)1;
		((Control)card).Height = 300;
		Card card2 = card;
		int y = 84;
		Row("Switch profiles automatically when a game starts", (Control)(object)tgAuto);
		Row("Start LiteSynapse with Windows", (Control)(object)tgStartup);
		Row("Run selected profiles on the mouse itself", (Control)(object)tgLive, "Writes the profile into the mouse's memory (slots 1-2) and switches to it, so it keeps working if LiteSynapse is closed.");
		tgAuto.CheckedChanged += () =>
		{
			if (!loading)
			{
				app.Auto = tgAuto.Checked;
				if (tgAuto.Checked)
				{
					app.ForceRecheck();
				}
			}
		};
		tgStartup.CheckedChanged += () =>
		{
			if (!loading)
			{
				Service.SetStartup(tgStartup.Checked);
			}
		};
		tgLive.CheckedChanged += () =>
		{
			if (!loading)
			{
				app.SetLiveWrite(tgLive.Checked);
				LoadAll();
			}
		};
		Panel btns = new Panel
		{
			Dock = (DockStyle)1,
			Height = 54,
			BackColor = Theme.Bg
		};
		PillButton pillButton = new PillButton();
		((Control)pillButton).Text = "OPEN CONFIG FOLDER";
		((Control)pillButton).Width = 200;
		((Control)pillButton).Location = new Point(0, 8);
		PillButton cfg = pillButton;
		PillButton pillButton2 = new PillButton();
		((Control)pillButton2).Text = "RELOAD CONFIG";
		((Control)pillButton2).Width = 160;
		((Control)pillButton2).Location = new Point(212, 8);
		PillButton rel = pillButton2;
		((Control)cfg).Click += (object? _, EventArgs _) =>
		{
			Process.Start("explorer.exe", Cfg.Dir);
		};
		((Control)rel).Click += (object? _, EventArgs _) =>
		{
			app.Reload();
			app.ForceRecheck();
			LoadAll();
		};
		((Control)btns).Controls.AddRange(new Control[2] { cfg, rel });
		Card card3 = new Card
		{
			Title = "Log"
		};
		((Control)card3).Dock = (DockStyle)5;
		((Control)card3).Padding = new Padding(30, 70, 30, 24);
		Card logCard = card3;
		((Control)logCard).Controls.Add((Control)(object)txtLog);
		((Control)pSettings).Controls.Add((Control)(object)logCard);
		((Control)pSettings).Controls.Add((Control)(object)btns);
		((Control)pSettings).Controls.Add((Control)(object)card2);
		void Row(string label, Control c, string note = null)
		{
			Label l = Lbl(label, Theme.Text, 10.5f);
			((Control)l).Location = new Point(40, y + 3);
			c.Location = new Point(420, y);
			((Control)card2).Controls.Add((Control)(object)l);
			((Control)card2).Controls.Add(c);
			if (note != null)
			{
				Label n = Lbl(note, Theme.Dim, 9f);
				((Control)n).Location = new Point(40, y + 26);
				((Control)card2).Controls.Add((Control)(object)n);
			}
			y += 62;
		}
	}

	private void BuildNaga()
	{
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		//IL_0035: Expected Obj, but got Unknown
		//IL_0143: Unknown result type (might be due to invalid IL or missing references)
		//IL_0294: Unknown result type (might be due to invalid IL or missing references)
		//IL_0299: Unknown result type (might be due to invalid IL or missing references)
		//IL_02a4: Unknown result type (might be due to invalid IL or missing references)
		//IL_02b3: Unknown result type (might be due to invalid IL or missing references)
		//IL_02c5: Unknown result type (might be due to invalid IL or missing references)
		//IL_02cd: Unknown result type (might be due to invalid IL or missing references)
		//IL_02d8: Unknown result type (might be due to invalid IL or missing references)
		//IL_02e3: Unknown result type (might be due to invalid IL or missing references)
		//IL_02f6: Expected Obj, but got Unknown
		//IL_030c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0311: Unknown result type (might be due to invalid IL or missing references)
		//IL_0317: Expected Obj, but got Unknown
		//IL_04de: Unknown result type (might be due to invalid IL or missing references)
		//IL_04e3: Unknown result type (might be due to invalid IL or missing references)
		//IL_04f2: Unknown result type (might be due to invalid IL or missing references)
		//IL_0501: Unknown result type (might be due to invalid IL or missing references)
		//IL_050e: Expected Obj, but got Unknown
		//IL_0534: Unknown result type (might be due to invalid IL or missing references)
		//IL_0539: Unknown result type (might be due to invalid IL or missing references)
		//IL_0544: Unknown result type (might be due to invalid IL or missing references)
		//IL_054b: Unknown result type (might be due to invalid IL or missing references)
		//IL_055a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0565: Unknown result type (might be due to invalid IL or missing references)
		//IL_0570: Unknown result type (might be due to invalid IL or missing references)
		//IL_057b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0586: Unknown result type (might be due to invalid IL or missing references)
		//IL_0599: Expected Obj, but got Unknown
		//IL_0524: Unknown result type (might be due to invalid IL or missing references)
		//IL_0529: Unknown result type (might be due to invalid IL or missing references)
		//IL_052f: Expected Obj, but got Unknown
		//IL_0652: Unknown result type (might be due to invalid IL or missing references)
		//IL_065c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0661: Unknown result type (might be due to invalid IL or missing references)
		//IL_0668: Unknown result type (might be due to invalid IL or missing references)
		//IL_0670: Unknown result type (might be due to invalid IL or missing references)
		//IL_067d: Expected Obj, but got Unknown
		//IL_05af: Unknown result type (might be due to invalid IL or missing references)
		//IL_05b4: Unknown result type (might be due to invalid IL or missing references)
		//IL_05ba: Expected Obj, but got Unknown
		Panel bottom = new Panel
		{
			Dock = (DockStyle)2,
			Height = 150,
			BackColor = Theme.Bg
		};
		((Control)bottom).Controls.AddRange(new Control[4]
		{
			thTop,
			thSide,
			modeSw,
			(Control)lblCalibWarn
		});
		((Control)bottom).Resize += (object? _, EventArgs _) =>
		{
			((Control)thTop).Location = new Point(((Control)bottom).Width / 2 - 56, 6);
			((Control)thSide).Location = new Point(((Control)bottom).Width / 2 + 6, 6);
			((Control)modeSw).Location = new Point(((Control)bottom).Width / 2 - 140, 72);
			((Control)lblCalibWarn).Location = new Point(24, 112);
		};
		((Control)pNagaCustom).Controls.Add((Control)(object)topView);
		((Control)pNagaCustom).Controls.Add((Control)(object)sideView);
		((Control)pNagaCustom).Controls.Add((Control)(object)bottom);
		((Control)thTop).Click += (object? _, EventArgs _) =>
		{
			View(t: true);
		};
		((Control)thSide).Click += (object? _, EventArgs _) =>
		{
			View(t: false);
		};
		topView.EditRequested = OpenAssign;
		sideView.EditRequested = OpenAssign;
		((Control)lblCalibWarn).Click += (object? _, EventArgs _) =>
		{
			top.Selected = 3;
			pills.Selected = 3;
			((Control)pills).Invalidate();
			ShowPage();
		};
		((Control)pNagaPerf).Padding = new Padding(20);
		Card card = new Card
		{
			Title = "Sensitivity"
		};
		((Control)card).Location = new Point(20, 20);
		((Control)card).Size = new Size(580, 270);
		Card sens = card;
		Label d1 = Lbl("The number of dots-per-inch (DPI) of mouse movement.", Theme.Text, 10.5f);
		((Control)d1).Location = new Point(40, 78);
		Label st = Lbl("Sensitivity Stages", Theme.Text, 10.5f);
		((Control)st).Location = new Point(40, 118);
		Toggle toggle = new Toggle();
		((Control)toggle).BackColor = Theme.Card;
		((Control)toggle).Enabled = false;
		((Control)toggle).Location = new Point(176, 116);
		Toggle stT = toggle;
		Label hd = Lbl("DPI", Theme.Text);
		((Control)hd).Location = new Point(70, 160);
		Label hmin = Lbl("100", Theme.Text);
		((Control)hmin).Location = new Point(150, 160);
		Label hmax = Lbl("20000", Theme.Text);
		((Control)hmax).Location = new Point(400, 160);
		Label badge = new Label
		{
			Text = "1",
			Size = new Size(30, 30),
			Location = new Point(40, 190),
			TextAlign = (ContentAlignment)32,
			ForeColor = Color.Black,
			BackColor = Theme.Card,
			Font = Theme.F(10f, (FontStyle)1)
		};
		PaintEventHandler val = _003C_003Ec._003C_003E9__79_5;
		if (val == null)
		{
			PaintEventHandler val2 = (object? _, PaintEventArgs pe) =>
			{
				//IL_0011: Unknown result type (might be due to invalid IL or missing references)
				//IL_0017: Expected Obj, but got Unknown
				pe.Graphics.SmoothingMode = (SmoothingMode)4;
				SolidBrush val7 = new SolidBrush(Theme.Green);
				try
				{
					pe.Graphics.FillEllipse((Brush)(object)val7, 0, 0, 29, 29);
					Tx.Center(pe.Graphics, "1", Theme.F(10f, (FontStyle)1), Color.Black, new RectangleF(0f, 0f, 30f, 30f));
				}
				finally
				{
					((IDisposable)val7)?.Dispose();
				}
			};
			_003C_003Ec._003C_003E9__79_5 = val2;
			val = val2;
		}
		((Control)badge).Paint += val;
		((Control)badge).Text = "";
		((Control)lblDpi).Location = new Point(80, 196);
		((Control)dpi).Location = new Point(150, 190);
		((Control)dpi).Width = 300;
		((Control)dpi).BackColor = Theme.Card;
		((Control)sens).Controls.AddRange(new Control[9]
		{
			(Control)d1,
			(Control)st,
			stT,
			(Control)hd,
			(Control)hmin,
			(Control)hmax,
			(Control)badge,
			(Control)lblDpi,
			dpi
		});
		Card card2 = new Card
		{
			Title = "Wired polling rate"
		};
		((Control)card2).Location = new Point(620, 20);
		((Control)card2).Size = new Size(520, 150);
		Card poll = card2;
		Label d2 = Lbl("The frequency (Hz) of data updates in a second.", Theme.Text, 10.5f);
		((Control)d2).Location = new Point(40, 78);
		((Control)poll).Controls.Add((Control)(object)d2);
		for (int i = 0; i < 3; i++)
		{
			((Control)polls[i]).Location = new Point(40 + i * 70, 108);
			((Control)poll).Controls.Add((Control)(object)polls[i]);
			((Control)polls[i]).Click += (object? s, EventArgs _) =>
			{
				SetPolling((int)((Control)(ChoiceButton)s).Tag);
			};
		}
		Card card3 = new Card
		{
			Title = "Mouse properties"
		};
		((Control)card3).Location = new Point(620, 186);
		((Control)card3).Size = new Size(520, 130);
		Card props = card3;
		Panel win = new Panel
		{
			Location = new Point(40, 74),
			Size = new Size(36, 36),
			BackColor = Theme.Card
		};
		PaintEventHandler val3 = _003C_003Ec._003C_003E9__79_6;
		if (val3 == null)
		{
			PaintEventHandler val4 = (object? _, PaintEventArgs pe) =>
			{
				//IL_0014: Unknown result type (might be due to invalid IL or missing references)
				//IL_001a: Expected Obj, but got Unknown
				SolidBrush val7 = new SolidBrush(Color.FromArgb(190, 190, 190));
				try
				{
					pe.Graphics.FillRectangle((Brush)(object)val7, 0, 0, 17, 17);
					pe.Graphics.FillRectangle((Brush)(object)val7, 19, 0, 17, 17);
					pe.Graphics.FillRectangle((Brush)(object)val7, 0, 19, 17, 17);
					pe.Graphics.FillRectangle((Brush)(object)val7, 19, 19, 17, 17);
				}
				finally
				{
					((IDisposable)val7)?.Dispose();
				}
			};
			_003C_003Ec._003C_003E9__79_6 = val4;
			val3 = val4;
		}
		((Control)win).Paint += val3;
		LinkLabel link = new LinkLabel
		{
			Text = "Open Windows Mouse Properties",
			AutoSize = true,
			Location = new Point(92, 82),
			LinkColor = Color.White,
			ActiveLinkColor = Theme.Green,
			VisitedLinkColor = Color.White,
			BackColor = Theme.Card,
			Font = Theme.F(10.5f, (FontStyle)0)
		};
		LinkLabelLinkClickedEventHandler val5 = _003C_003Ec._003C_003E9__79_7;
		if (val5 == null)
		{
			LinkLabelLinkClickedEventHandler val6 = (object? _1, LinkLabelLinkClickedEventArgs _2) =>
			{
				try
				{
					Process.Start(new ProcessStartInfo("control.exe", "main.cpl")
					{
						UseShellExecute = true
					});
				}
				catch
				{
				}
			};
			_003C_003Ec._003C_003E9__79_7 = val6;
			val5 = val6;
		}
		link.LinkClicked += val5;
		((Control)props).Controls.AddRange(new Control[2]
		{
			(Control)win,
			(Control)link
		});
		((Control)pNagaPerf).Controls.AddRange(new Control[3]
		{
			(Control)sens,
			(Control)poll,
			(Control)props
		});
		dpi.Changing += () =>
		{
			((Control)lblDpi).Text = dpi.Value.ToString();
		};
		dpi.Committed += () =>
		{
			MouseProfile p = MP;
			if (p != null)
			{
				p.Dpi = dpi.Value;
				if (p.DpiStages.Count > 0 && p.DpiActive >= 1 && p.DpiActive <= p.DpiStages.Count)
				{
					p.DpiStages[p.DpiActive - 1] = dpi.Value;
				}
				// The service's path: saved under its lock, the live slot rewritten, and the DPI sent only to a slot
				// LiteSynapse may change (never to the user's own slot while live profiles are on).
				app.SaveProfiles(p.Name);
				if (p.Name == app.ActiveMouse)
				{
					app.ApplyMousePerformance();
				}
			}
		};
		BuildLighting(pNagaLight, nagaBrightOn, nagaBright, "Mouse lighting isn't supported by LiteSynapse yet. The Naga keeps the lighting Synapse last set.");
		((Control)pNagaCalib).Padding = new Padding(24);
		Panel cbar = new Panel
		{
			Dock = (DockStyle)1,
			Height = 62,
			BackColor = Theme.Bg
		};
		PillButton pillButton = new PillButton();
		((Control)pillButton).Text = "CALIBRATE BUTTONS";
		((Control)pillButton).Width = 210;
		pillButton.Primary = true;
		((Control)pillButton).Location = new Point(0, 10);
		PillButton cbtn = pillButton;
		((Control)cbtn).Click += (object? _, EventArgs _) =>
		{
			UiHost.Calibrate(app);
		};
		Label cnote = Lbl("Teach LiteSynapse which key each Naga button sends right now, so it can translate them per game.", Theme.Dim, 9.5f);
		((Control)cnote).Location = new Point(230, 19);
		((Control)cbar).Controls.AddRange(new Control[2]
		{
			cbtn,
			(Control)cnote
		});
		Theme.StyleGrid(gridCalib);
		gridCalib.AllowUserToAddRows = false;
		gridCalib.ReadOnly = true;
		gridCalib.AutoSizeColumnsMode = (DataGridViewAutoSizeColumnsMode)16;
		gridCalib.Columns.Add("b", "BUTTON");
		gridCalib.Columns.Add("k", "KEY THE MOUSE SENDS");
		((Control)pNagaCalib).Controls.Add((Control)(object)gridCalib);
		((Control)pNagaCalib).Controls.Add((Control)(object)cbar);
		DarkScroll.Apply((Control)(object)gridCalib);
		void View(bool t)
		{
			((Control)topView).Visible = t;
			((Control)sideView).Visible = !t;
			thTop.Selected = t;
			thSide.Selected = !t;
			((Control)thTop).Invalidate();
			((Control)thSide).Invalidate();
			((Control)assign).Hide();
		}
	}

	private void BuildLighting(Panel page, Toggle on, GreenSlider slider, string note)
	{
		//IL_0003: Unknown result type (might be due to invalid IL or missing references)
		//IL_014e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0153: Unknown result type (might be due to invalid IL or missing references)
		//IL_0162: Unknown result type (might be due to invalid IL or missing references)
		//IL_0171: Unknown result type (might be due to invalid IL or missing references)
		//IL_017e: Expected Obj, but got Unknown
		//IL_0194: Unknown result type (might be due to invalid IL or missing references)
		//IL_0199: Unknown result type (might be due to invalid IL or missing references)
		//IL_019f: Expected Obj, but got Unknown
		((Control)page).Padding = new Padding(20);
		Card card = new Card
		{
			Title = "Brightness"
		};
		((Control)card).Location = new Point(20, 20);
		((Control)card).Size = new Size(570, 190);
		Card bright = card;
		((Control)on).Location = new Point(176, 32);
		((Control)slider).Location = new Point(36, 100);
		((Control)slider).Width = 500;
		((Control)slider).BackColor = Theme.Card;
		((Control)bright).Controls.Add((Control)(object)on);
		((Control)bright).Controls.Add((Control)(object)slider);
		Card card2 = new Card
		{
			Title = "Switch off lighting"
		};
		((Control)card2).Location = new Point(20, 226);
		((Control)card2).Size = new Size(570, 220);
		card2.Muted = true;
		Card sw = card2;
		Label c1 = Lbl("When display is turned Off", Color.FromArgb(120, 120, 120), 10.5f);
		((Control)c1).Location = new Point(70, 84);
		Label c2 = Lbl("When idle for (minutes)", Color.FromArgb(120, 120, 120), 10.5f);
		((Control)c2).Location = new Point(70, 120);
		int[] array = new int[2] { 86, 122 };
		foreach (int cy in array)
		{
			Panel b = new Panel
			{
				Location = new Point(40, cy),
				Size = new Size(18, 18),
				BackColor = Theme.Card
			};
			PaintEventHandler val = _003C_003Ec._003C_003E9__80_0;
			if (val == null)
			{
				PaintEventHandler val2 = (object? _, PaintEventArgs pe) =>
				{
					//IL_000b: Unknown result type (might be due to invalid IL or missing references)
					//IL_0011: Expected Obj, but got Unknown
					Pen val3 = new Pen(Color.FromArgb(90, 90, 90));
					try
					{
						pe.Graphics.DrawRectangle(val3, 0, 0, 16, 16);
					}
					finally
					{
						((IDisposable)val3)?.Dispose();
					}
				};
				_003C_003Ec._003C_003E9__80_0 = val2;
				val = val2;
			}
			((Control)b).Paint += val;
			((Control)sw).Controls.Add((Control)(object)b);
		}
		((Control)sw).Controls.AddRange(new Control[2]
		{
			(Control)c1,
			(Control)c2
		});
		Card card3 = new Card
		{
			Title = "Effects"
		};
		((Control)card3).Location = new Point(610, 20);
		((Control)card3).Size = new Size(530, 280);
		card3.Muted = true;
		Card fx = card3;
		Label q = Lbl("Quick effects are presets saved to a device's profile.", Color.FromArgb(120, 120, 120));
		((Control)q).Location = new Point(40, 78);
		((Control)fx).Controls.Add((Control)(object)q);
		Label n = Lbl(note, Color.FromArgb(230, 170, 60));
		((Control)n).Location = new Point(40, 130);
		((Control)n).MaximumSize = new Size(450, 0);
		((Control)fx).Controls.Add((Control)(object)n);
		((Control)page).Controls.AddRange(new Control[3]
		{
			(Control)bright,
			(Control)sw,
			(Control)fx
		});
	}

	private void BuildKeyboard()
	{
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ec: Unknown result type (might be due to invalid IL or missing references)
		((Control)pKbCustom).Padding = new Padding(20);
		Card card = new Card
		{
			Title = "Macro keys"
		};
		((Control)card).Location = new Point(20, 20);
		((Control)card).Size = new Size(640, 360);
		Card card2 = card;
		Label d = Lbl("Runs a macro when the key is pressed (needs a game profile).", Theme.Text);
		((Control)d).Location = new Point(40, 78);
		((Control)card2).Controls.Add((Control)(object)d);
		int y = 124;
		(string, string)[] array = new (string, string)[4]
		{
			("KEY_UP_ARROW", "UP ARROW"),
			("KEY_DOWN_ARROW", "DOWN ARROW"),
			("KEY_LEFT_ARROW", "LEFT ARROW"),
			("KEY_RIGHT_ARROW", "RIGHT ARROW")
		};
		for (int i = 0; i < array.Length; i++)
		{
			(string, string) tuple = array[i];
			string key = tuple.Item1;
			string item = tuple.Item2;
			Label l = Lbl(item, Theme.Text, 10.5f, bold: true);
			((Control)l).Location = new Point(40, y + 5);
			DarkCombo darkCombo = new DarkCombo();
			((Control)darkCombo).Location = new Point(220, y);
			((Control)darkCombo).Width = 300;
			((Control)darkCombo).BackColor = Theme.Card;
			DarkCombo c = darkCombo;
			arrowCombos[key] = c;
			string kk = key;
			c.SelectedIndexChanged += () =>
			{
				if (!loading)
				{
					KbProfile kP = KP;
					if (kP != null)
					{
						if (c.SelectedIndex <= 0)
						{
							kP.MacroKeys.Remove(kk);
						}
						else
						{
							kP.MacroKeys[kk] = (string)c.SelectedItem;
						}
						SaveProfiles();
						app.ApplyNames(app.ActiveMouse, app.ActiveKeyboard, app.InGame);
					}
				}
			};
			((Control)card2).Controls.Add((Control)(object)l);
			((Control)card2).Controls.Add((Control)(object)c);
			y += 52;
		}
		((Control)pKbCustom).Controls.Add((Control)(object)card2);
		((Control)pKbPerf).Padding = new Padding(20);
		Card card3 = new Card
		{
			Title = "Snap Tap"
		};
		((Control)card3).Location = new Point(20, 20);
		((Control)card3).Size = new Size(640, 180);
		Card sc = card3;
		Label sd = Lbl("When both A and D are held, the most recent one wins. Only active in a game profile.", Theme.Text);
		((Control)sd).Location = new Point(40, 78);
		((Control)sd).MaximumSize = new Size(560, 0);
		((Control)snap).Location = new Point(40, 120);
		((Control)sc).Controls.Add((Control)(object)sd);
		((Control)sc).Controls.Add((Control)(object)snap);
		((Control)pKbPerf).Controls.Add((Control)(object)sc);
		snap.CheckedChanged += () =>
		{
			if (!loading)
			{
				KbProfile kP = KP;
				if (kP != null)
				{
					kP.SnapTap = snap.Checked;
					SaveProfiles();
					app.ApplyNames(app.ActiveMouse, app.ActiveKeyboard, app.InGame);
				}
			}
		};
		BuildLighting(pKbLight, kbBrightOn, kbBright, "The BlackWidow Lite has a single-color backlight: brightness is the only lighting control.");
		kbBright.Committed += () =>
		{
			KbProfile p = KP;
			if (p != null)
			{
				p.Brightness = kbBright.Value;
				kbBrightOn.Checked = p.Brightness > 0;
				SaveProfiles();
				Task.Run(() => app.Kb?.SetBrightness(p.Brightness));
			}
		};
		kbBrightOn.CheckedChanged += () =>
		{
			if (!loading)
			{
				KbProfile p = KP;
				if (p != null)
				{
					if (!kbBrightOn.Checked)
					{
						p.Brightness = 0;
						kbBright.Value = 0;
					}
					else if (p.Brightness == 0)
					{
						p.Brightness = 50;
						kbBright.Value = 50;
					}
					SaveProfiles();
					Task.Run(() => app.Kb?.SetBrightness(p.Brightness));
				}
			}
		};
	}

	private void BuildMemory()
	{
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Expected Obj, but got Unknown
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_003e: Expected Obj, but got Unknown
		memSlots = SafeOnboardNames();
		((Control)memPanel).Paint +=(object? _, PaintEventArgs pe) =>
		{
			//IL_0018: Unknown result type (might be due to invalid IL or missing references)
			//IL_001e: Expected Obj, but got Unknown
			//IL_0058: Unknown result type (might be due to invalid IL or missing references)
			//IL_005e: Expected Obj, but got Unknown
			//IL_00d5: Unknown result type (might be due to invalid IL or missing references)
			//IL_00dc: Expected Obj, but got Unknown
			Graphics graphics = pe.Graphics;
			Theme.Quality(graphics);
			Pen val = new Pen(Color.FromArgb(90, 90, 90));
			try
			{
				graphics.DrawRectangle(val, 0, 0, ((Control)memPanel).Width - 1, ((Control)memPanel).Height - 1);
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
			SolidBrush val2 = new SolidBrush(Color.FromArgb(34, 34, 34));
			try
			{
				graphics.FillRectangle((Brush)(object)val2, 1, 1, ((Control)memPanel).Width - 2, 42);
			}
			finally
			{
				((IDisposable)val2)?.Dispose();
			}
			Font val3 = Theme.F(10f, (FontStyle)0);
			try
			{
				Tx.Center(graphics, "On-Board Memory", val3, Color.Silver, new RectangleF(0f, 0f, ((Control)memPanel).Width, 44f));
			}
			finally
			{
				((IDisposable)val3)?.Dispose();
			}
			Pen val4 = new Pen(Color.Silver, 1.6f);
			try
			{
				graphics.DrawLine(val4, ((Control)memPanel).Width - 30, 15, ((Control)memPanel).Width - 18, 27);
				graphics.DrawLine(val4, ((Control)memPanel).Width - 18, 15, ((Control)memPanel).Width - 30, 27);
			}
			finally
			{
				((IDisposable)val4)?.Dispose();
			}
			for (int i = 1; i <= 5; i++)
			{
				float num = 60 + (i - 1) * 56 + 14;
				Icons.SdCard(graphics, 22f, num - 9f, 1f, (i <= 2) ? Color.FromArgb(120, 120, 120) : Color.FromArgb(215, 215, 215), SlotColors[i]);
			}
		};
		((Control)memPanel).MouseClick += (object? _, MouseEventArgs me) =>
		{
			if (me.Y < 44 && me.X > ((Control)memPanel).Width - 44)
			{
				((Control)memPanel).Hide();
			}
		};
		for (int s = 1; s <= 5; s++)
		{
			DarkCombo darkCombo = new DarkCombo();
			((Control)darkCombo).Location = new Point(64, 60 + (s - 1) * 56);
			((Control)darkCombo).Width = 340;
			((Control)darkCombo).BackColor = Color.FromArgb(24, 24, 24);
			((Control)darkCombo).Enabled = s > 2;
			DarkCombo c = darkCombo;
			memCombos[s] = c;
			((Control)memPanel).Controls.Add((Control)(object)c);
			int slot = s;
			c.SelectedIndexChanged += () =>
			{
				if (!loading)
				{
					MemoryPick(slot);
				}
			};
		}
		((Control)memPanel).Controls.Add((Control)(object)memStatus);
		((Control)memPanel).Height = 424;
	}

	private void ToggleMemory()
	{
		if (((Control)memPanel).Visible)
		{
			((Control)memPanel).Hide();
			return;
		}
		loading = true;
		memSlots = SafeOnboardNames();
		try
		{
			for (int s = 1; s <= 5; s++)
			{
				DarkCombo c = memCombos[s];
				c.Items.Clear();
				if (s <= 2)
				{
					c.Items.Add("LiteSynapse live slot");
					c.SelectedIndex = 0;
					continue;
				}
				c.Items.Add("Select a profile");
				c.Items.AddRange(app.Store.Mouse.Select((MouseProfile p) => p.Name).Cast<object>().ToList());
				c.SelectedItem = ((memSlots.TryGetValue(s.ToString(), out var n) && c.Items.Contains(n)) ? n : "Select a profile");
			}
		}
		finally
		{
			loading = false;
		}
		((Control)memStatus).Text = "Pick a profile to write it into that slot. The mouse's Cycle Up Profile button switches between slots. This replaces whatever is stored in the slot.";
		((Control)memPanel).Location = new Point(Math.Max(10, ((Control)host).Width - ((Control)memPanel).Width - 24), 12);
		((Control)memPanel).BringToFront();
		((Control)memPanel).Show();
	}

	private void MemoryPick(int slot)
	{
		//IL_011b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0121: Invalid comparison between Unknown and I4
		DarkCombo c = memCombos[slot];
		if (memBusy)
		{
			return;
		}
		object selectedItem = c.SelectedItem;
		string name = selectedItem as string;
		if (name == null || name == "Select a profile")
		{
			return;
		}
		MouseProfile p = app.Store.Mouse.FirstOrDefault((MouseProfile x) => x.Name == name);
		if (p == null)
		{
			return;
		}
		if (app.Mouse == null)
		{
			((Control)memStatus).Text = "Naga not found.";
		}
		else if (slot <= 2 || app.LiveSlots.Contains(slot))
		{
			((Control)memStatus).ForeColor = Color.FromArgb(230, 120, 80);
			((Control)memStatus).Text = $"Slot {slot} is one of LiteSynapse's live slots (1 and 2). Pick slot 3, 4 or 5.";
		}
		else
		{
			if ((int)MessageBox.Show((IWin32Window)(object)this, $"Write \"{name}\" into slot {slot} ({OnboardMemory.SlotNames[slot]})?\n\nThis replaces what is stored in that slot on the mouse.", "On-Board Memory", (MessageBoxButtons)1) != 1)
			{
				return;
			}
			memBusy = true;
			((Control)memStatus).ForeColor = Theme.Dim;
			((Control)memStatus).Text = "Writing...";
			// The service writes on its device worker (never alongside a profile switch) and records the slot's
			// content in data/onboard.json; it also logs the result.
			app.WriteOnboardSlot(slot, name, (string s) =>
			{
				SafeBeginInvoke(() =>
				{
					((Control)memStatus).Text = s + "...";
				});
			}).ContinueWith((Task<string> t) =>
			{
				string err = t.IsFaulted ? t.Exception.GetBaseException().Message : t.Result;
				SafeBeginInvoke(() =>
				{
					memBusy = false;
					if (err == null)
					{
						memSlots = SafeOnboardNames();
						((Control)memStatus).ForeColor = Theme.Green;
						((Control)memStatus).Text = $"Wrote \"{name}\" to slot {slot} ({OnboardMemory.SlotNames[slot]}). Press Cycle Up Profile on the mouse to use it.";
					}
					else
					{
						((Control)memStatus).ForeColor = Color.FromArgb(230, 120, 80);
						((Control)memStatus).Text = "Failed: " + err;
					}
				});
			}, TaskScheduler.Default);
		}
	}

	// What data/onboard.json records for each slot (slot number -> profile name).
	private Dictionary<string, string> SafeOnboardNames()
	{
		try
		{
			return app.OnboardNames ?? new Dictionary<string, string>();
		}
		catch (Exception ex)
		{
			Cfg.Log("onboard slots: " + ex.Message);
			return new Dictionary<string, string>();
		}
	}

	// For callbacks from the device worker: the window may have closed in the meantime.
	private void SafeBeginInvoke(Action action)
	{
		try
		{
			if (base.IsHandleCreated && !base.IsDisposed)
			{
				base.BeginInvoke(action);
			}
		}
		catch (InvalidOperationException)
		{
		}
	}

	private static string VkName(int vk, bool ext)
	{
		//IL_004e: Unknown result type (might be due to invalid IL or missing references)
		switch (vk)
		{
		case 13:
			if (ext)
			{
				return "Numpad Enter";
			}
			return "Enter";
		case 106:
			return "Numpad *";
		case 107:
			return "Numpad +";
		case 109:
			return "Numpad -";
		default:
			return ((object)(Keys)vk/*cast due to constrained. prefix*/).ToString();
		}
	}

	private void SaveProfiles()
	{
		Cfg.Save(Cfg.Profiles, app.Store);
	}

	private void SelectProfile(string name)
	{
		if (IsMouse)
		{
			app.SelectMouse(name);
		}
		else
		{
			app.SelectKeyboard(name);
		}
		LoadAll();
	}

	private void SetPolling(int hz)
	{
		MouseProfile p = MP;
		if (p != null)
		{
			p.PollingRate = hz;
			app.SaveProfiles(p.Name);
			LoadAll();
			if (p.Name == app.ActiveMouse)
			{
				app.ApplyMousePerformance();
			}
		}
	}

	private void OpenAssign(string id, string def)
	{
		MouseProfile p = MP;
		if (p != null)
		{
			Callout c = MouseTopView.Items.FirstOrDefault((Callout x) => x.Id == id);
			string title = ((c != null) ? c.Default : ("Side button " + id.Substring(4)));
			bool editable = c == null || c.Editable;
			((Control)assign).Height = Math.Min(330, ((Control)host).Height - 24);
			assign.Open(id, title, p.Buttons.TryGetValue(id, out var t) ? t.Key : null, def, editable);
		}
	}

	private void SaveButton(string id, string value)
	{
		MouseProfile p = MP;
		if (p != null)
		{
			if (value == "")
			{
				p.Buttons.Remove(id);
			}
			else
			{
				p.Buttons[id] = new KeyTarget
				{
					Key = value
				};
			}
			SaveProfiles();
			app.ApplyNames(app.ActiveMouse, app.ActiveKeyboard, app.InGame);
			LoadAll();
		}
	}

	private void ShowMoreMenu()
	{
		//IL_0074: Unknown result type (might be due to invalid IL or missing references)
		//IL_007e: Expected Obj, but got Unknown
		//IL_0084: Unknown result type (might be due to invalid IL or missing references)
		//IL_0089: Unknown result type (might be due to invalid IL or missing references)
		//IL_0090: Unknown result type (might be due to invalid IL or missing references)
		//IL_009c: Expected Obj, but got Unknown
		//IL_0187: Unknown result type (might be due to invalid IL or missing references)
		//IL_0191: Expected Obj, but got Unknown
		//IL_016f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0174: Unknown result type (might be due to invalid IL or missing references)
		//IL_0180: Expected Obj, but got Unknown
		//IL_012d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0132: Unknown result type (might be due to invalid IL or missing references)
		//IL_0142: Expected Obj, but got Unknown
		//IL_01cd: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d2: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c1: Unknown result type (might be due to invalid IL or missing references)
		//IL_01de: Expected Obj, but got Unknown
		//IL_01e5: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ef: Expected Obj, but got Unknown
		//IL_0250: Unknown result type (might be due to invalid IL or missing references)
		//IL_025a: Expected Obj, but got Unknown
		ContextMenuStrip menu = Ui.Menu();
		string cur = (string)cbProfile.SelectedItem;
		if (cur == null)
		{
			return;
		}
		((ToolStrip)menu).Items.Add("Add", (Image)null, (EventHandler)((object? _, EventArgs _) =>
		{
			AddProfile();
		}));
		((ToolStrip)menu).Items.Add("Import", (Image)null, (EventHandler)((object? _, EventArgs _) =>
		{
			ImportProfiles();
		}));
		((ToolStrip)menu).Items.Add((ToolStripItem)new ToolStripSeparator());
		ToolStripMenuItem lg = new ToolStripMenuItem("Linked Games")
		{
			Enabled = false,
			ForeColor = Color.White
		};
		((ToolStrip)menu).Items.Add((ToolStripItem)(object)lg);
		List<GameRule> rules = app.Games.Where((GameRule g) => (!IsMouse) ? (g.KeyboardProfile == cur) : (g.Profile == cur)).ToList();
		foreach (GameRule r in rules)
		{
			((ToolStrip)menu).Items.Add((ToolStripItem)new ToolStripMenuItem((r.Exes.Count > 0) ? string.Join(", ", r.Exes) : "(no program set)", (Image)null, (EventHandler)((object? _, EventArgs _) =>
			{
				top.Selected = 2;
				((Control)top).Invalidate();
				SetPills();
				ShowPage();
			}))
			{
				ForeColor = Theme.Green
			});
		}
		if (rules.Count == 0)
		{
			((ToolStrip)menu).Items.Add((ToolStripItem)new ToolStripMenuItem("(none)")
			{
				Enabled = false
			});
		}
		((ToolStrip)menu).Items.Add((ToolStripItem)new ToolStripSeparator());
		bool isStart = cur == StartingProfile;
		((ToolStrip)menu).Items.Add((ToolStripItem)(isStart ? new ToolStripMenuItem("✓  Starting profile")
		{
			Enabled = false
		} : new ToolStripMenuItem("Set as starting profile", (Image)null, (EventHandler)((object? _, EventArgs _) =>
		{
			SetStartingProfile(cur);
		}))));
		((ToolStrip)menu).Items.Add((ToolStripItem)new ToolStripSeparator());
		((ToolStrip)menu).Items.Add("Rename", (Image)null, (EventHandler)((object? _, EventArgs _) =>
		{
			RenameProfile(cur);
		}));
		((ToolStrip)menu).Items.Add("Duplicate", (Image)null, (EventHandler)((object? _, EventArgs _) =>
		{
			DuplicateProfile(cur);
		}));
		((ToolStrip)menu).Items.Add("Export", (Image)null, (EventHandler)((object? _, EventArgs _) =>
		{
			ExportProfile(cur);
		}));
		((ToolStrip)menu).Items.Add((ToolStripItem)new ToolStripSeparator());
		((ToolStrip)menu).Items.Add("Reset", (Image)null, (EventHandler)((object? _, EventArgs _) =>
		{
			ResetProfile(cur);
		}));
		((ToolStrip)menu).Items.Add("Delete", (Image)null, (EventHandler)((object? _, EventArgs _) =>
		{
			DeleteProfile(cur);
		}));
		((ToolStripDropDown)menu).Show((Control)(object)btnMore, new Point(0, ((Control)btnMore).Height));
	}

	private bool NameTaken(string n)
	{
		if (!IsMouse)
		{
			return app.Store.Keyboard.Any((KbProfile p) => p.Name == n);
		}
		return app.Store.Mouse.Any((MouseProfile p) => p.Name == n);
	}

	private void SetStartingProfile(string name)
	{
		if (IsMouse)
		{
			app.SetDefaults(name, null);
		}
		else
		{
			app.SetDefaults(null, name);
		}
		if (!app.InGame)
		{
			app.ForceRecheck();
			app.ApplyNames(IsMouse ? name : app.ActiveMouse, IsMouse ? app.ActiveKeyboard : name, inGame: false);
		}
		LoadAll();
	}

	private void AddProfile()
	{
		string n = Ui.Prompt((IWin32Window)(object)this, "New profile", "Profile name", "New profile");
		if (n != null && !NameTaken(n))
		{
			if (IsMouse)
			{
				app.Store.Mouse.Add(new MouseProfile
				{
					Name = n,
					Dpi = (MP?.Dpi ?? 800),
					PollingRate = (MP?.PollingRate ?? 1000)
				});
			}
			else
			{
				app.Store.Keyboard.Add(new KbProfile
				{
					Name = n,
					Brightness = (KP?.Brightness ?? 100)
				});
			}
			SaveProfiles();
			SelectProfile(n);
		}
	}

	private void ImportProfiles()
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_0018: Expected Obj, but got Unknown
		//IL_005b: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0020: Invalid comparison between Unknown and I4
		OpenFileDialog dlg = new OpenFileDialog
		{
			Filter = "Synapse exports (*.synapse4;*.xml)|*.synapse4;*.xml",
			Multiselect = true
		};
		try
		{
			if ((int)((CommonDialog)dlg).ShowDialog((IWin32Window)(object)this) != 1)
			{
				return;
			}
			try
			{
				Importer.Run(((FileDialog)dlg).FileNames);
				app.Reload();
				LoadAll();
			}
			catch (Exception ex)
			{
				MessageBox.Show((IWin32Window)(object)this, "Import failed: " + ex.Message, "LiteSynapse");
			}
		}
		finally
		{
			((IDisposable)dlg)?.Dispose();
		}
	}

	private void RenameProfile(string cur)
	{
		string n = Ui.Prompt((IWin32Window)(object)this, "Rename profile", "New name", cur);
		if (n == null || n == cur || NameTaken(n))
		{
			return;
		}
		bool wasStart = cur == StartingProfile;
		if (IsMouse)
		{
			app.Store.Mouse.First((MouseProfile p) => p.Name == cur).Name = n;
			foreach (GameRule item in app.Games.Where((GameRule g) => g.Profile == cur))
			{
				item.Profile = n;
			}
			if (app.ActiveMouse == cur)
			{
				app.ActiveMouse = n;
			}
		}
		else
		{
			app.Store.Keyboard.First((KbProfile p) => p.Name == cur).Name = n;
			foreach (GameRule item2 in app.Games.Where((GameRule g) => g.KeyboardProfile == cur))
			{
				item2.KeyboardProfile = n;
			}
			if (app.ActiveKeyboard == cur)
			{
				app.ActiveKeyboard = n;
			}
		}
		if (wasStart)
		{
			if (IsMouse)
			{
				app.SetDefaults(n, null);
			}
			else
			{
				app.SetDefaults(null, n);
			}
		}
		SaveProfiles();
		app.SaveGames();
		LoadAll();
	}

	private void DuplicateProfile(string cur)
	{
		string n = cur + " copy";
		int i = 2;
		while (NameTaken(n))
		{
			n = $"{cur} copy {i++}";
		}
		JsonSerializerOptions opt = new JsonSerializerOptions();
		if (IsMouse)
		{
			MouseProfile c = JsonSerializer.Deserialize<MouseProfile>(JsonSerializer.Serialize(app.Store.Mouse.First((MouseProfile p) => p.Name == cur), opt), opt);
			c.Name = n;
			app.Store.Mouse.Add(c);
		}
		else
		{
			KbProfile c2 = JsonSerializer.Deserialize<KbProfile>(JsonSerializer.Serialize(app.Store.Keyboard.First((KbProfile p) => p.Name == cur), opt), opt);
			c2.Name = n;
			app.Store.Keyboard.Add(c2);
		}
		SaveProfiles();
		SelectProfile(n);
	}

	private void ExportProfile(string cur)
	{
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_0034: Expected Obj, but got Unknown
		//IL_0036: Unknown result type (might be due to invalid IL or missing references)
		//IL_003c: Invalid comparison between Unknown and I4
		SaveFileDialog dlg = new SaveFileDialog
		{
			FileName = cur + ".json",
			Filter = "JSON (*.json)|*.json"
		};
		try
		{
			if ((int)((CommonDialog)dlg).ShowDialog((IWin32Window)(object)this) == 1)
			{
				object p = (IsMouse ? ((object)app.Store.Mouse.First((MouseProfile x) => x.Name == cur)) : ((object)app.Store.Keyboard.First((KbProfile x) => x.Name == cur)));
				File.WriteAllText(((FileDialog)dlg).FileName, JsonSerializer.Serialize(p, new JsonSerializerOptions
				{
					WriteIndented = true
				}));
			}
		}
		finally
		{
			((IDisposable)dlg)?.Dispose();
		}
	}

	private void ResetProfile(string cur)
	{
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Invalid comparison between Unknown and I4
		if ((int)MessageBox.Show((IWin32Window)(object)this, "Reset \"" + cur + "\"? Button changes and macro keys in this profile will be cleared.", "LiteSynapse", (MessageBoxButtons)1) != 1)
		{
			return;
		}
		if (IsMouse)
		{
			app.Store.Mouse.First((MouseProfile p) => p.Name == cur).Buttons.Clear();
		}
		else
		{
			app.Store.Keyboard.First((KbProfile p) => p.Name == cur).MacroKeys.Clear();
		}
		SaveProfiles();
		app.ApplyNames(app.ActiveMouse, app.ActiveKeyboard, app.InGame);
		LoadAll();
	}

	private void DeleteProfile(string cur)
	{
		//IL_005e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0064: Invalid comparison between Unknown and I4
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		if (cur == StartingProfile)
		{
			MessageBox.Show((IWin32Window)(object)this, "\"" + cur + "\" is the starting profile (used when LiteSynapse starts and when no game is running). Choose another starting profile first, then delete this one.", "LiteSynapse");
		}
		else
		{
			if ((int)MessageBox.Show((IWin32Window)(object)this, "Delete \"" + cur + "\"?", "LiteSynapse", (MessageBoxButtons)1) != 1)
			{
				return;
			}
			if (IsMouse)
			{
				app.Store.Mouse.RemoveAll((MouseProfile p) => p.Name == cur);
				app.Games.RemoveAll((GameRule g) => g.Profile == cur);
				if (app.ActiveMouse == cur)
				{
					app.ApplyNames(app.DefaultMouse, app.ActiveKeyboard, inGame: false);
				}
			}
			else
			{
				app.Store.Keyboard.RemoveAll((KbProfile p) => p.Name == cur);
				foreach (GameRule item in app.Games.Where((GameRule g) => g.KeyboardProfile == cur))
				{
					item.KeyboardProfile = "";
				}
				if (app.ActiveKeyboard == cur)
				{
					app.ApplyNames(app.ActiveMouse, app.DefaultKeyboard, app.InGame);
				}
			}
			SaveProfiles();
			app.SaveGames();
			app.ForceRecheck();
			LoadAll();
		}
	}

	private string BaseLabel(string id)
	{
		if (!app.Base.TryGetValue(id, out var b))
		{
			return null;
		}
		return Program.KeyName(b.Sc, b.Ext);
	}

	private string LiveLine()
	{
		if (!app.LiveWrite)
		{
			if (app.Base.Count != 0)
			{
				return "";
			}
			return "26A0  Buttons aren't calibrated yet, so remapping is off. Click here to calibrate.";
		}
		if (app.LiveStatus.Length <= 0)
		{
			return "";
		}
		return "2713  " + app.LiveStatus;
	}

	private void Sync()
	{
		if (app.ActiveMouse != lastMouse || app.ActiveKeyboard != lastKb)
		{
			LoadAll();
		}
		else if (((Control)lblCalibWarn).Text != LiveLine())
		{
			((Control)lblCalibWarn).Text = LiveLine();
		}
	}

	private void LoadAll()
	{
		loading = true;
		try
		{
			lastMouse = app.ActiveMouse;
			lastKb = app.ActiveKeyboard;
			List<object> names = (IsMouse ? app.Store.Mouse.Select((MouseProfile p) => p.Name) : app.Store.Keyboard.Select((KbProfile p) => p.Name)).Cast<object>().ToList();
			cbProfile.Items.Clear();
			cbProfile.Items.AddRange(names);
			cbProfile.SelectedItem = (IsMouse ? app.ActiveMouse : app.ActiveKeyboard);
			MouseProfile mp = MP;
			topView.Profile = mp;
			sideView.Profile = mp;
			topView.BaseLabel = (sideView.BaseLabel = BaseLabel);
			((Control)topView).Invalidate();
			((Control)sideView).Invalidate();
			((Control)lblCalibWarn).Text = LiveLine();
			if (mp != null)
			{
				dpi.Value = mp.Dpi;
				((Control)lblDpi).Text = mp.Dpi.ToString();
				ChoiceButton[] array = polls;
				foreach (ChoiceButton obj in array)
				{
					obj.Selected = (int)((Control)obj).Tag == mp.PollingRate;
					((Control)obj).Invalidate();
				}
				nagaBright.Value = 100;
				nagaBrightOn.Checked = true;
			}
			KbProfile kp = KP;
			if (kp != null)
			{
				kbBright.Value = kp.Brightness;
				kbBrightOn.Checked = kp.Brightness > 0;
				snap.Checked = kp.SnapTap;
				foreach (var (key, c) in arrowCombos)
				{
					c.Items.Clear();
					c.Items.Add("(none)");
					c.Items.AddRange(app.Store.Macros.Select((Macro macro) => macro.Name).Cast<object>().ToList());
					c.SelectedItem = (kp.MacroKeys.TryGetValue(key, out var mn) ? mn : "(none)");
					if (c.SelectedIndex < 0)
					{
						c.SelectedIndex = 0;
					}
				}
			}
			gridCalib.Rows.Clear();
			string[] buttonIds = InputEngine.ButtonIds;
			foreach (string id in buttonIds)
			{
				DataGridViewRowCollection rows = gridCalib.Rows;
				string text2 = ((!id.StartsWith("Side")) ? (id switch
				{
					"TiltL" => "Wheel tilt left", 
					"TiltR" => "Wheel tilt right", 
					"Scroll" => "Scroll click", 
					"M1" => "M1 (upper)", 
					_ => "M2 (lower)", 
				}) : ("Side " + id.Substring(4)));
				rows.Add(new object[2]
				{
					text2,
					BaseLabel(id) ?? "not calibrated"
				});
			}
			// The profile's own values, not HID reads: this runs on the UI thread on every profile change (also
			// while a game is in front), and every device transfer belongs to the Service's device worker.
			MouseProfile mpNow = app.Store.Mouse.FirstOrDefault((MouseProfile x) => x.Name == app.ActiveMouse);
			KbProfile kpNow = app.Store.Keyboard.FirstOrDefault((KbProfile x) => x.Name == app.ActiveKeyboard);
			string dpiNow = (mpNow != null) ? mpNow.Dpi.ToString() : "?";
			string hz = (mpNow != null) ? mpNow.PollingRate.ToString() : "?";
			string br = (kpNow != null) ? kpNow.Brightness.ToString() : "?";
			if (top.Selected == 0)
			{
				((Control)lblHome1).Text = $"Profile: {app.ActiveMouse}\nDPI {dpiNow}   -   {hz} Hz\n{app.Base.Count}/{InputEngine.ButtonIds.Length} buttons calibrated";
				((Control)lblHome2).Text = $"Profile: {((app.ActiveKeyboard.Length == 0) ? "(none)" : app.ActiveKeyboard)}\nBrightness {br}%";
			}
			if (top.Selected == 5)
			{
				tgAuto.Checked = app.Auto;
				tgStartup.Checked = Service.IsStartup();
				tgLive.Checked = app.LiveWrite;
				LoadLog();
			}
			if (top.Selected == 2)
			{
				FillGames();
			}
			if (top.Selected != 1)
			{
				return;
			}
			lstMacros.Items.Clear();
			foreach (Macro m in app.Store.Macros)
			{
				lstMacros.Items.Add((object)m.Name);
			}
			if (lstMacros.Items.Count > 0)
			{
				((ListControl)lstMacros).SelectedIndex = 0;
			}
		}
		finally
		{
			loading = false;
		}
	}

	private void FillGames()
	{
		string q = ((Control)txtSearch).Text.Trim();
		((Control)gamesList).SuspendLayout();
		foreach (Control c in ((IEnumerable)((Control)gamesList).Controls).Cast<Control>().ToList())
		{
			((Control)gamesList).Controls.Remove(c);
			((Component)(object)c).Dispose();
		}
		foreach (GameRule g in app.Games.Where((GameRule gameRule) => q.Length == 0 || (gameRule.Profile ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) || gameRule.Exes.Any((string e) => e.Contains(q, StringComparison.OrdinalIgnoreCase))))
		{
			((Control)gamesList).Controls.Add(GameRow(g));
		}
		((Control)gamesList).ResumeLayout();
	}

	private void LoadLog()
	{
		try
		{
			if (!File.Exists(Cfg.LogFile))
			{
				((Control)txtLog).Text = "(empty)";
				return;
			}
			// Only the last lines, not the whole file (it grows to 1 MB): this is the UI thread.
			string text = Cfg.ReadLogTail(200).Replace("\n", Environment.NewLine);
			if (text != ((Control)txtLog).Text)
			{
				((Control)txtLog).Text = text;
				((TextBoxBase)txtLog).SelectionStart = ((TextBoxBase)txtLog).TextLength;
				((TextBoxBase)txtLog).ScrollToCaret();
			}
		}
		catch
		{
		}
	}

	private string PickRunning()
	{
		//IL_0088: Unknown result type (might be due to invalid IL or missing references)
		//IL_008d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0098: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ae: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d0: Expected Obj, but got Unknown
		//IL_00d0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00dc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f9: Unknown result type (might be due to invalid IL or missing references)
		//IL_010b: Expected Obj, but got Unknown
		//IL_017d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0183: Invalid comparison between Unknown and I4
		string[] procs = (from x in (from p in Process.GetProcesses().Where((Process p) =>
				{
					try
					{
						return p.MainWindowHandle != IntPtr.Zero && !string.IsNullOrEmpty(p.MainWindowTitle);
					}
					catch
					{
						return false;
					}
				})
				select p.ProcessName + ".exe").Distinct(StringComparer.OrdinalIgnoreCase)
			orderby x
			select x).ToArray();
		Form dlg = new Form
		{
			Text = "Pick the running game",
			Width = 380,
			Height = 460,
			StartPosition = (FormStartPosition)4,
			BackColor = Theme.Bg,
			ForeColor = Theme.Text
		};
		try
		{
			ListBox list = new ListBox
			{
				Dock = (DockStyle)5,
				BackColor = Theme.Card,
				ForeColor = Theme.Text,
				BorderStyle = (BorderStyle)0,
				Font = Theme.F(11f, (FontStyle)0)
			};
			ListBox.ObjectCollection items = list.Items;
			object[] array = procs;
			items.AddRange(array);
			PillButton pillButton = new PillButton();
			((Control)pillButton).Text = "ADD";
			((Control)pillButton).Dock = (DockStyle)2;
			((Control)pillButton).Height = 44;
			pillButton.Primary = true;
			PillButton ok = pillButton;
			((Control)ok).Click += (object? _, EventArgs _) =>
			{
				dlg.DialogResult = (DialogResult)1;
			};
			((Control)dlg).Controls.Add((Control)(object)list);
			((Control)dlg).Controls.Add((Control)(object)ok);
			return ((int)dlg.ShowDialog((IWin32Window)(object)this) != 1 || list.SelectedItem == null) ? null : ((string)list.SelectedItem);
		}
		finally
		{
			if (dlg != null)
			{
				((IDisposable)dlg).Dispose();
			}
		}
	}
}
