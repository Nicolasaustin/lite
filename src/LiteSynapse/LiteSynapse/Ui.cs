using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LiteSynapse;

internal static class Ui
{
	public static string PrettyKey(string s)
	{
		if (s == "DISABLED")
		{
			return "Disabled";
		}
		if (!s.StartsWith("KEY_"))
		{
			return s;
		}
		return string.Join(" ", from w in s.Substring(4).Split('_')
			select (w.Length <= 1) ? w : (char.ToUpper(w[0]) + w.Substring(1).ToLower())).Replace("Hypen", "-").Replace("Equal", "=")
			.Replace("Spacebar", "Space")
			.Replace("Open Square Bracket", "[")
			.Replace("Close Square Bracket", "]")
			.Replace("Left Shift", "Shift")
			.Replace("Left Ctrl", "Ctrl")
			.Replace("Left Alt", "Alt");
	}

	[DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(nint h, int attr, ref int v, int size);

	public static void DarkTitleBar(Form f)
	{
		int v = 1;
		DwmSetWindowAttribute(((Control)f).Handle, 20, ref v, 4);
	}

	public static ContextMenuStrip Menu()
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_0037: Unknown result type (might be due to invalid IL or missing references)
		//IL_003f: Expected Obj, but got Unknown
		return new ContextMenuStrip
		{
			Renderer = (ToolStripRenderer)(object)new DarkRenderer(),
			BackColor = Color.Black,
			ForeColor = Color.White,
			Font = Theme.F(10f, (FontStyle)0),
			ShowImageMargin = false
		};
	}

	public static string Prompt(IWin32Window owner, string title, string label, string initial)
	{
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		//IL_0030: Unknown result type (might be due to invalid IL or missing references)
		//IL_0037: Unknown result type (might be due to invalid IL or missing references)
		//IL_0042: Unknown result type (might be due to invalid IL or missing references)
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0054: Unknown result type (might be due to invalid IL or missing references)
		//IL_005b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0062: Unknown result type (might be due to invalid IL or missing references)
		//IL_0078: Expected Obj, but got Unknown
		//IL_0078: Unknown result type (might be due to invalid IL or missing references)
		//IL_007d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0084: Unknown result type (might be due to invalid IL or missing references)
		//IL_008b: Unknown result type (might be due to invalid IL or missing references)
		//IL_009a: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a6: Expected Obj, but got Unknown
		//IL_00a7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ac: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00de: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f0: Unknown result type (might be due to invalid IL or missing references)
		//IL_0106: Expected Obj, but got Unknown
		//IL_01a0: Unknown result type (might be due to invalid IL or missing references)
		//IL_01aa: Expected Obj, but got Unknown
		//IL_01f3: Unknown result type (might be due to invalid IL or missing references)
		string result = null;
		Form dlg = new Form
		{
			Text = title,
			Width = 380,
			Height = 190,
			StartPosition = (FormStartPosition)4,
			BackColor = Theme.Bg,
			ForeColor = Theme.Text,
			FormBorderStyle = (FormBorderStyle)3,
			MaximizeBox = false,
			MinimizeBox = false,
			Font = Theme.F(10f, (FontStyle)0)
		};
		try
		{
			Label l = new Label
			{
				Text = label,
				AutoSize = true,
				Location = new Point(20, 18),
				ForeColor = Theme.Text
			};
			TextBox tb = new TextBox
			{
				Text = initial,
				Location = new Point(20, 46),
				Width = 330,
				BackColor = Color.FromArgb(18, 18, 18),
				ForeColor = Theme.Text,
				BorderStyle = (BorderStyle)1,
				Font = Theme.F(11f, (FontStyle)0)
			};
			PillButton pillButton = new PillButton();
			((Control)pillButton).Text = "OK";
			((Control)pillButton).Width = 90;
			((Control)pillButton).Location = new Point(160, 94);
			pillButton.Primary = true;
			PillButton ok = pillButton;
			PillButton pillButton2 = new PillButton();
			((Control)pillButton2).Text = "CANCEL";
			((Control)pillButton2).Width = 100;
			((Control)pillButton2).Location = new Point(255, 94);
			PillButton cancel = pillButton2;
			((Control)ok).Click += (object? _, EventArgs _) =>
			{
				result = ((Control)tb).Text.Trim();
				dlg.Close();
			};
			((Control)cancel).Click += (object? _, EventArgs _) =>
			{
				dlg.Close();
			};
			dlg.AcceptButton = null;
			((Control)tb).KeyDown += (object? _, KeyEventArgs k) =>
			{
				//IL_0001: Unknown result type (might be due to invalid IL or missing references)
				//IL_0008: Invalid comparison between Unknown and I4
				if ((int)k.KeyCode == 13)
				{
					result = ((Control)tb).Text.Trim();
					dlg.Close();
					k.SuppressKeyPress = true;
				}
			};
			((Control)dlg).Controls.AddRange(new Control[4]
			{
				(Control)l,
				(Control)tb,
				ok,
				cancel
			});
			dlg.Shown += (object? _, EventArgs _) =>
			{
				((Control)tb).Focus();
				((TextBoxBase)tb).SelectAll();
			};
			dlg.ShowDialog(owner);
			return string.IsNullOrEmpty(result) ? null : result;
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
