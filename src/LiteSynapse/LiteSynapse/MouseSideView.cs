using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LiteSynapse;

internal class MouseSideView : Control
{
	public MouseProfile Profile;

	public Action<string, string> EditRequested;

	public Func<string, string> BaseLabel;

	private readonly List<(RectangleF rect, string id, string def)> hits = new List<(RectangleF, string, string)>();

	private string hover;

	public MouseSideView()
	{
		base.DoubleBuffered = true;
		base.BackColor = Theme.Bg;
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		//IL_0063: Unknown result type (might be due to invalid IL or missing references)
		//IL_006a: Expected Obj, but got Unknown
		//IL_01b0: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b7: Expected Obj, but got Unknown
		//IL_01d1: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d8: Expected Obj, but got Unknown
		//IL_020d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0214: Expected Obj, but got Unknown
		//IL_02f4: Unknown result type (might be due to invalid IL or missing references)
		//IL_02fb: Expected Obj, but got Unknown
		//IL_03f2: Unknown result type (might be due to invalid IL or missing references)
		//IL_03f9: Expected Obj, but got Unknown
		Graphics g = e.Graphics;
		Theme.Quality(g);
		Theme.DotGrid(g, base.ClientRectangle);
		hits.Clear();
		float cx = (float)base.Width / 2f;
		float cy = (float)base.Height / 2f - 4f;
		float s = Math.Clamp((float)base.Height / 380f, 0.8f, 1.5f);
		GraphicsPath path = new GraphicsPath();
		try
		{
			PointF[] pts = new PointF[8]
			{
				new PointF(cx - 50f * s, cy - 150f * s),
				new PointF(cx + 10f * s, cy - 165f * s),
				new PointF(cx + 40f * s, cy - 90f * s),
				new PointF(cx + 50f * s, cy + 50f * s),
				new PointF(cx + 20f * s, cy + 150f * s),
				new PointF(cx - 40f * s, cy + 165f * s),
				new PointF(cx - 62f * s, cy + 40f * s),
				new PointF(cx - 64f * s, cy - 80f * s)
			};
			path.AddClosedCurve(pts, 0.5f);
			LinearGradientBrush fb = new LinearGradientBrush(new RectangleF(cx - 70f * s, cy - 170f * s, 130f * s, 340f * s), Color.FromArgb(52, 52, 52), Color.FromArgb(16, 16, 16), 70f);
			try
			{
				g.FillPath((Brush)(object)fb, path);
				Pen op = new Pen(Color.FromArgb(90, 90, 90), 1.4f);
				try
				{
					g.DrawPath(op, path);
				}
				finally
				{
					((IDisposable)op)?.Dispose();
				}
			}
			finally
			{
				((IDisposable)fb)?.Dispose();
			}
		}
		finally
		{
			((IDisposable)path)?.Dispose();
		}
		SolidBrush kb = new SolidBrush(Theme.Green);
		try
		{
			Font nf = Theme.F(7f, (FontStyle)1);
			try
			{
				for (int r = 0; r < 4; r++)
				{
					for (int c = 0; c < 3; c++)
					{
						RectangleF rect = new RectangleF(cx - 36f * s + (float)(c * 26) * s, cy - 60f * s + (float)(r * 27) * s, 22f * s, 21f * s);
						GraphicsPath rp = Theme.Round(rect, 4f * s);
						try
						{
							g.FillPath((Brush)(object)kb, rp);
							Tx.Center(g, (r * 3 + c + 1).ToString(), nf, Color.Black, rect);
						}
						finally
						{
							((IDisposable)rp)?.Dispose();
						}
					}
				}
				Font f = Theme.F(12f, (FontStyle)1);
				try
				{
					Font cf = Theme.F(10f, (FontStyle)1);
					try
					{
						SolidBrush cb = new SolidBrush(Color.FromArgb(24, 24, 24));
						try
						{
							float rowGap = Math.Min(58f, (float)(base.Height - 90) / 6f);
							float top = cy - rowGap * 2.5f;
							for (int i = 0; i < 12; i++)
							{
								bool left = i < 6;
								int row = (left ? i : (i - 6));
								float y = top + (float)row * rowGap;
								float ccx = (left ? (cx - 175f * Math.Max(s, 0.9f)) : (cx + 175f * Math.Max(s, 0.9f)));
								string id = "Side" + (i + 1);
								bool hot = hover == id;
								RectangleF circle = new RectangleF(ccx - 16f, y - 16f, 32f, 32f);
								g.FillEllipse((Brush)(object)cb, circle);
								Pen rp2 = new Pen(hot ? Theme.Green : Color.FromArgb(120, 120, 120), hot ? 2f : 1.3f);
								try
								{
									g.DrawEllipse(rp2, circle);
								}
								finally
								{
									((IDisposable)rp2)?.Dispose();
								}
								Tx.Center(g, (i + 1).ToString(), cf, Color.White, circle);
								bool mapped = Profile != null && Profile.Buttons.ContainsKey(id);
								string def = BaseLabel?.Invoke(id) ?? DefaultKey(i);
								string text = MouseTopView.LabelFor(Profile, id, def);
								SizeF size = Tx.Measure(text, f);
								float tx = (left ? (ccx - 28f - size.Width) : (ccx + 28f));
								RectangleF rect2 = new RectangleF(tx, y - size.Height / 2f, size.Width, size.Height);
								Tx.Left(g, text, f, mapped ? Theme.Green : Theme.Text, RectangleF.Inflate(rect2, 2f, 2f));
								hits.Add((RectangleF.Union(RectangleF.Inflate(rect2, 8f, 6f), circle), id, def));
							}
						}
						finally
						{
							((IDisposable)cb)?.Dispose();
						}
					}
					finally
					{
						((IDisposable)cf)?.Dispose();
					}
				}
				finally
				{
					((IDisposable)f)?.Dispose();
				}
			}
			finally
			{
				((IDisposable)nf)?.Dispose();
			}
		}
		finally
		{
			((IDisposable)kb)?.Dispose();
		}
	}

	private static string DefaultKey(int i)
	{
		if (i >= 9)
		{
			return i switch
			{
				10 => "-", 
				9 => "0", 
				_ => "=", 
			};
		}
		return (i + 1).ToString();
	}

	protected override void OnMouseMove(MouseEventArgs e)
	{
		string h = null;
		foreach (var (r, id, _) in hits)
		{
			if (r.Contains(e.Location))
			{
				h = id;
				break;
			}
		}
		if (h != hover)
		{
			hover = h;
			base.Cursor = ((h != null) ? Cursors.Hand : Cursors.Default);
			base.Invalidate();
		}
	}

	protected override void OnMouseLeave(EventArgs e)
	{
		if (hover != null)
		{
			hover = null;
			base.Invalidate();
		}
	}

	protected override void OnMouseClick(MouseEventArgs e)
	{
		foreach (var (r, id, def) in hits)
		{
			if (r.Contains(e.Location))
			{
				EditRequested?.Invoke(id, def);
				break;
			}
		}
	}
}
