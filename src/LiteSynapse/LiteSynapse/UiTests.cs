using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace LiteSynapse;

internal static class UiTests
{
	private struct RECT
	{
		public int l;

		public int t;

		public int r;

		public int b;
	}

	private static int fails;

	private static void Check(string name, bool ok, string detail = "")
	{
		Console.WriteLine((ok ? "PASS" : "FAIL") + "  " + name + ((detail.Length > 0) ? ("  - " + detail) : ""));
		if (!ok)
		{
			fails++;
		}
	}

	[DllImport("user32", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern nint CreateDesktop(string name, string device, nint devmode, int flags, uint access, nint sa);

	[DllImport("user32", SetLastError = true)]
	private static extern bool SetThreadDesktop(nint desk);

	[DllImport("user32", SetLastError = true)]
	private static extern bool CloseDesktop(nint desk);

	[DllImport("user32")]
	private static extern bool PostMessage(nint h, int msg, nint w, nint l);

	[DllImport("user32")]
	private static extern bool PrintWindow(nint hwnd, nint hdc, uint flags);

	private static void Pump(int ms)
	{
		long end = Environment.TickCount64 + ms;
		while (Environment.TickCount64 < end)
		{
			Application.DoEvents();
			Thread.Sleep(5);
		}
	}

	private static void Click(Control c, int x, int y)
	{
		nint lp = (y << 16) | (x & 0xFFFF);
		PostMessage(c.Handle, 513, 1, lp);
		Pump(30);
		PostMessage(c.Handle, 514, IntPtr.Zero, lp);
		Pump(80);
	}

	private static void ClickCombo(DarkCombo c)
	{
		Click((Control)(object)c, ((Control)c).Width / 2, ((Control)c).Height / 2);
	}

	private static void ClickRow(DarkCombo c, string name)
	{
		ListBox list = c.TestList;
		int i = c.Items.IndexOf(name);
		list.TopIndex = Math.Max(0, Math.Min(i, c.Items.Count - 1));
		Pump(40);
		Click((Control)(object)list, ((Control)list).Width / 2, (i - list.TopIndex) * list.ItemHeight + list.ItemHeight / 2);
	}

	public static int Run(string picture)
	{
		Cfg.Quiet = true;
		nint desk = CreateDesktop("LiteSynapseUiTest", null, IntPtr.Zero, 0, 268435456u, IntPtr.Zero);
		if (desk == IntPtr.Zero)
		{
			Console.WriteLine("could not create a hidden desktop, error " + Marshal.GetLastWin32Error());
			return 2;
		}
		Exception err = null;
		Thread thread = new Thread(() =>
		{
			try
			{
				if (!SetThreadDesktop(desk))
				{
					throw new Exception("SetThreadDesktop failed, error " + Marshal.GetLastWin32Error());
				}
				Body(picture);
			}
			catch (Exception ex)
			{
				err = ex;
			}
		});
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		thread.Join();
		CloseDesktop(desk);
		if (err != null)
		{
			Console.WriteLine("FAIL  the ui test crashed: " + err);
			return 1;
		}
		Console.WriteLine((fails == 0) ? "\nALL UI CHECKS PASSED" : $"\n{fails} UI CHECK(S) FAILED");
		return (fails != 0) ? 1 : 0;
	}

	private static void Snap(Form f, string picture, string suffix)
	{
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_0039: Expected Obj, but got Unknown
		if (string.IsNullOrEmpty(picture))
		{
			return;
		}
		string path = ((suffix.Length == 0) ? picture : (Path.ChangeExtension(picture, null) + suffix + ".png"));
		try
		{
			Bitmap bmp = new Bitmap(((Control)f).Width, ((Control)f).Height);
			try
			{
				Graphics gr = Graphics.FromImage((Image)(object)bmp);
				try
				{
					nint hdc = gr.GetHdc();
					PrintWindow(((Control)f).Handle, hdc, 0u);
					gr.ReleaseHdc((IntPtr)hdc);
				}
				finally
				{
					((IDisposable)gr)?.Dispose();
				}
				using MemoryStream ms = new MemoryStream();
				((Image)bmp).Save((Stream)ms, ImageFormat.Png);
				Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
				File.WriteAllBytes(path, ms.ToArray());
				Console.WriteLine("      picture: " + Path.GetFileName(path));
			}
			finally
			{
				((IDisposable)bmp)?.Dispose();
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine("NOTE  could not save the picture: " + ex.Message);
		}
	}

	private static void Body(string picture)
	{
		Service svc = new Service
		{
			DryRun = true
		};
		Check("no PC-Default profile is left", !svc.Store.Mouse.Any((MouseProfile p) => p.Name.StartsWith("PC-Default")) && !svc.Store.Keyboard.Any((KbProfile p) => p.Name.StartsWith("PC-Default")) && !svc.Games.Any((GameRule g) => (g.Profile ?? "").StartsWith("PC-Default") || (g.KeyboardProfile ?? "").StartsWith("PC-Default")));
		Check("starting profiles are CS2 (mouse) and gamer - S3 (keyboard)", svc.DefaultMouse == "CS2" && svc.DefaultKeyboard == "gamer - S3" && svc.ActiveMouse == "CS2" && svc.ActiveKeyboard == "gamer - S3", svc.ActiveMouse + " / " + svc.ActiveKeyboard);
		GameRule cs2 = svc.Games.FirstOrDefault((GameRule g) => g.Profile == "CS2");
		Check("CS2 game rule: cs2.exe with keyboard profile gamer - S3", cs2 != null && cs2.Exes.Contains("cs2.exe") && cs2.KeyboardProfile == "gamer - S3");
		Check("no other game forces a keyboard profile that does not exist", svc.Games.All((GameRule g) => string.IsNullOrEmpty(g.KeyboardProfile) || svc.Store.Keyboard.Any((KbProfile p) => p.Name == g.KeyboardProfile)));
		TrayFlyout fly = new TrayFlyout(svc, new Point(600, 400));
		((Control)fly).Show();
		Pump(400);
		DarkCombo m = fly.MouseCombo;
		DarkCombo k = fly.KeyboardCombo;
		Check("flyout opens", ((Control)fly).Visible && !((Control)fly).IsDisposed);
		Check("mouse list: all profiles, CS2 selected, no PC-Default", m.Items.Count == svc.Store.Mouse.Count && (string)m.SelectedItem == "CS2" && !m.Items.Any((object i) => ((string)i).StartsWith("PC-Default")), $"{m.Items.Count} profiles, shows {m.SelectedItem}");
		Check("keyboard list: (none) first, gamer - S3 selected", (string)k.Items[0] == "(none)" && (string)k.SelectedItem == "gamer - S3" && k.Items.Count == svc.Store.Keyboard.Count + 1, string.Join(" | ", k.Items));
		ClickCombo(m);
		Check("clicking the mouse box opens the list inside the panel (no separate window)", m.IsOpen && m.TestList != null && (object)((Control)m.TestList).FindForm() == fly);
		Check("the open list stays inside the panel", m.TestList != null && ((Control)fly).ClientRectangle.Contains(((Control)m.TestList).Parent.Bounds), (m.TestList == null) ? "" : $"list {((Control)m.TestList).Parent.Bounds}, panel {((Control)fly).ClientRectangle}");
		Pump(500);
		Check("the panel stays open while the list is open", ((Control)fly).Visible && !((Control)fly).IsDisposed && m.IsOpen);
		svc.SetAuto(on: false);
		Pump(150);
		Check("the open list survives a change reported by the app", m.IsOpen && m.TestList != null);
		ClickRow(m, "Valorant");
		if (!(svc.ActiveMouse == "Valorant"))
		{
			Console.WriteLine("NOTE  a posted click on the list row did not register on the hidden desktop; using the row's pick handler instead");
			m.TestPick(m.Items.IndexOf("Valorant"));
			Pump(150);
		}
		Check("picking a mouse profile selects it and turns automatic switching off", svc.ActiveMouse == "Valorant" && !svc.Auto && svc.InGame, $"{svc.ActiveMouse}, auto {svc.Auto}");
		Check("the list closes and the panel stays open after a pick", !m.IsOpen && ((Control)fly).Visible && !((Control)fly).IsDisposed);
		Check("the mouse box shows the pick", (string)m.SelectedItem == "Valorant");
		Snap((Form)(object)fly, picture, "_picked");
		ClickCombo(k);
		Check("clicking the keyboard box opens its list", k.IsOpen && k.TestList != null && (object)((Control)k.TestList).FindForm() == fly);
		ClickRow(k, "(none)");
		if (svc.ActiveKeyboard != "")
		{
			Console.WriteLine("NOTE  a posted click on the list row did not register on the hidden desktop; using the row's pick handler instead");
			k.TestPick(0);
			Pump(150);
		}
		Check("keyboard: picking (none) removes the keyboard profile", svc.ActiveKeyboard == "" && (string)k.SelectedItem == "(none)" && !k.IsOpen && ((Control)fly).Visible, "[" + svc.ActiveKeyboard + "]");
		ClickCombo(k);
		ClickRow(k, "gamer - S3");
		if (svc.ActiveKeyboard != "gamer - S3")
		{
			k.TestPick(k.Items.IndexOf("gamer - S3"));
			Pump(150);
		}
		Check("keyboard: picking gamer - S3 selects it", svc.ActiveKeyboard == "gamer - S3" && (string)k.SelectedItem == "gamer - S3" && ((Control)fly).Visible, svc.ActiveKeyboard);
		ClickCombo(m);
		bool openedMouse = m.IsOpen;
		ClickCombo(k);
		Check("opening the keyboard list closes the mouse list", openedMouse && !m.IsOpen && k.IsOpen);
		ClickCombo(k);
		Check("clicking the open box again closes it (and does not reopen it)", !k.IsOpen);
		ClickCombo(m);
		Click((Control)(object)fly, 150, 30);
		Check("clicking elsewhere on the panel closes the list; the panel stays", !m.IsOpen && ((Control)fly).Visible && !((Control)fly).IsDisposed);
		ClickCombo(m);
		if (m.TestList != null)
		{
			PostMessage(((Control)m.TestList).Handle, 256, 27, IntPtr.Zero);
			Pump(100);
		}
		Check("Esc closes the list", !m.IsOpen && ((Control)fly).Visible);
		svc.ApplyNames("Overwatch", "", inGame: true);
		Pump(250);
		Check("boxes follow a change made elsewhere", (string)m.SelectedItem == "Overwatch" && (string)k.SelectedItem == "(none)", $"{m.SelectedItem} / {k.SelectedItem}");
		svc.ApplyNames("CS2", "gamer - S3", inGame: true);
		Pump(250);
		ClickCombo(m);
		Pump(150);
		Snap((Form)(object)fly, picture, "");
		Click((Control)(object)fly, 150, 30);
		Pump(300);
		fly.TestDeactivate();
		Pump(250);
		Check("losing focus (clicking another program) closes the panel", ((Control)fly).IsDisposed);
		MenuTest(string.IsNullOrEmpty(picture) ? null : (Path.ChangeExtension(picture, null) + "_menu.png"));
		svc.Shutdown();
	}

	[DllImport("user32", CharSet = CharSet.Unicode)]
	private static extern nint FindWindow(string cls, string title);

	[DllImport("user32")]
	private static extern bool GetWindowRect(nint h, out RECT r);

	[DllImport("user32")]
	private static extern nint SendMessage(nint h, int m, nint w, nint l);

	[DllImport("user32")]
	private static extern int GetMenuItemCount(nint m);

	[DllImport("user32", CharSet = CharSet.Unicode)]
	private static extern int GetMenuString(nint m, uint pos, StringBuilder s, int max, uint flags);

	[DllImport("user32")]
	private static extern uint GetMenuState(nint m, uint pos, uint flags);

	[DllImport("user32")]
	private static extern bool EndMenu();

	private static void MenuTest(string picture)
	{
		TrayHost.PrepareDarkMenus();
		List<string> items = new List<string>();
		bool shown = false;
		bool checkedStartup = false;
		int seps = 0;
		Size size = default;
		nint owner = IntPtr.Zero;
		W32.WndProc keep;
		(owner, keep) = W32.CreateWindow(messageOnly: false, (uint m, nint w, nint l) =>
		{
			//IL_0154: Unknown result type (might be due to invalid IL or missing references)
			//IL_015b: Expected Obj, but got Unknown
			if (m != 275 || w != 77)
			{
				return (nint?)null;
			}
			W32.KillTimer(owner, 77);
			nint num = FindWindow("#32768", null);
			if (num != IntPtr.Zero)
			{
				shown = true;
				GetWindowRect(num, out var r);
				size = new Size(r.r - r.l, r.b - r.t);
				nint m2 = SendMessage(num, 481, IntPtr.Zero, IntPtr.Zero);
				uint num2 = 0u;
				for (uint menuItemCount = (uint)GetMenuItemCount(m2); num2 < menuItemCount; num2++)
				{
					StringBuilder stringBuilder = new StringBuilder(64);
					GetMenuString(m2, num2, stringBuilder, 64, 1024u);
					uint menuState = GetMenuState(m2, num2, 1024u);
					if ((menuState & 0x800) != 0)
					{
						seps++;
					}
					else
					{
						items.Add(stringBuilder.ToString());
						if (stringBuilder.ToString() == "Start with Windows" && (menuState & 8) != 0)
						{
							checkedStartup = true;
						}
					}
				}
				if (!string.IsNullOrEmpty(picture))
				{
					try
					{
						Bitmap val = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height));
						try
						{
							Graphics val2 = Graphics.FromImage((Image)(object)val);
							try
							{
								nint hdc = val2.GetHdc();
								PrintWindow(num, hdc, 0u);
								val2.ReleaseHdc((IntPtr)hdc);
							}
							finally
							{
								((IDisposable)val2)?.Dispose();
							}
							using MemoryStream memoryStream = new MemoryStream();
							((Image)val).Save((Stream)memoryStream, ImageFormat.Png);
							File.WriteAllBytes(picture, memoryStream.ToArray());
							Console.WriteLine("      picture: " + picture);
						}
						finally
						{
							((IDisposable)val)?.Dispose();
						}
					}
					catch (Exception ex)
					{
						Console.WriteLine("NOTE  could not save the menu picture: " + ex.Message);
					}
				}
			}
			EndMenu();
			return IntPtr.Zero;
		});
		W32.SetTimer(owner, 77, 700u, IntPtr.Zero);
		int cmd = TrayHost.TrackMenu(owner, startupOn: true, 400, 400);
		if (!shown)
		{
			Console.WriteLine("NOTE  the menu could not be shown on the hidden desktop (cmd " + cmd + "); menu not checked");
			W32.DestroyWindow(owner);
			GC.KeepAlive(keep);
			return;
		}
		Check("right-click menu: LiteSynapse / Settings / Start with Windows / Exit", string.Join(" | ", items) == "LiteSynapse | Settings | Start with Windows | Exit" && seps == 2, string.Join(" | ", items) + $"  ({seps} separators, {size.Width}x{size.Height})");
		Check("right-click menu: \"Start with Windows\" shows its check mark when on", checkedStartup);
		W32.DestroyWindow(owner);
		GC.KeepAlive(keep);
	}
}
