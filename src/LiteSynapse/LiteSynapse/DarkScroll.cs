using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LiteSynapse;

internal static class DarkScroll
{
	[DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
	private static extern int SetWindowTheme(nint h, string app, string idList);

	// Re-applied whenever the control gets a new window, since a recreated handle starts with the default
	// (light) theme again.
	public static void Apply(Control c)
	{
		if (c.IsHandleCreated)
		{
			Go();
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
				// A grid's scroll bars are child controls of their own.
				foreach (Control child in c.Controls)
				{
					if (child is ScrollBar)
					{
						SetWindowTheme(child.Handle, "DarkMode_Explorer", null);
					}
				}
			}
			catch
			{
			}
		}
	}
}
