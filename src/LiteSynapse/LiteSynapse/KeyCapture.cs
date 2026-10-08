using System;
using System.Drawing;
using System.Windows.Forms;

namespace LiteSynapse;

internal class KeyCapture : Control
{
	public string KeyName;

	public event Action Captured;

	public KeyCapture()
	{
		base.SetStyle((ControlStyles)139778, true);
		base.TabStop = true;
		base.Height = 44;
		base.Cursor = Cursors.Hand;
	}

	protected override bool IsInputKey(Keys k)
	{
		return true;
	}

	protected override bool ProcessCmdKey(ref Message m, Keys keyData)
	{
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Expected I4, but got Unknown
		//IL_000a: Unknown result type (might be due to invalid IL or missing references)
		if (!base.Focused)
		{
			return base.ProcessCmdKey(ref m, keyData);
		}
		string n = Keys2.NameFromVk((int)(keyData & Keys.KeyCode));
		if (n != null)
		{
			KeyName = n;
			base.Invalidate();
			Captured?.Invoke();
		}
		return true;
	}

	protected override void OnMouseDown(MouseEventArgs e)
	{
		base.Focus();
		base.Invalidate();
	}

	protected override void OnGotFocus(EventArgs e)
	{
		base.Invalidate();
	}

	protected override void OnLostFocus(EventArgs e)
	{
		base.Invalidate();
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_0038: Expected Obj, but got Unknown
		Graphics g = e.Graphics;
		g.Clear(Color.FromArgb(18, 18, 18));
		Pen p = new Pen(base.Focused ? Theme.Green : Color.FromArgb(110, 110, 110));
		try
		{
			g.DrawRectangle(p, 0, 0, base.Width - 1, base.Height - 1);
			string t;
			if (KeyName != null)
			{
				t = Ui.PrettyKey(KeyName);
			}
			else
			{
				t = (base.Focused ? "Press a key..." : "Click here, then press a key");
			}
			Font f = Theme.F(11f, (FontStyle)0);
			try
			{
				Tx.Center(g, t, f, (KeyName != null) ? Theme.Text : Theme.Dim, new RectangleF(0f, 0f, base.Width, base.Height));
			}
			finally
			{
				((IDisposable)f)?.Dispose();
			}
		}
		finally
		{
			((IDisposable)p)?.Dispose();
		}
	}
}
