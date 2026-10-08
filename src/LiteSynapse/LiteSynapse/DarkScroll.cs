using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LiteSynapse;

internal static class DarkScroll
{
	[DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
	private static extern int SetWindowTheme(nint h, string app, string idList);

	public static void Apply(Control c)
	{
		if (c.IsHandleCreated)
		{
			Go();
			return;
		}
		c.HandleCreated += (object? _, EventArgs _) =>
		{
			Go();
		};
		void Go()
		{
			try
			{
				SetWindowTheme(c.Handle, "DarkMode_Explorer", null);
			}
			catch
			{
			}
		}
	}
}
