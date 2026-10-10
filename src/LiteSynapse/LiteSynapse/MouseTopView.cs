using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace LiteSynapse;

internal class MouseTopView : Control
{
	public MouseProfile Profile;

	public Action<string, string> EditRequested;

	public Func<string, string> BaseLabel;

	private readonly List<(RectangleF rect, Callout c)> hits = new List<(RectangleF, Callout)>();

	private string hover;

	public static readonly Callout[] Items = new Callout[10]
	{
		new Callout("LeftClick", "Left Click", Left: true, 0, new PointF(-34f, -98f), Editable: false),
		new Callout("ScrollUp", "Scroll Up", Left: true, 1, new PointF(0f, -99f), Editable: false),
		new Callout("TiltL", "Tilt Left", Left: true, 2, new PointF(-8f, -73f), Editable: true),
		new Callout("ScrollDown", "Scroll Down", Left: true, 3, new PointF(0f, -47f), Editable: false),
		new Callout("M1", "Button M1", Left: true, 4, new PointF(0f, -34f), Editable: true),
		new Callout("M2", "Button M2", Left: true, 5, new PointF(0f, -17f), Editable: true),
		new Callout("Cycle", "Cycle Up Profile", Left: true, 6, new PointF(-44f, 104f), Editable: false),
		new Callout("RightClick", "Right Click", Left: false, 0, new PointF(36f, -98f), Editable: false),
		new Callout("Scroll", "Scroll Click", Left: false, 1, new PointF(0f, -73f), Editable: true),
		new Callout("TiltR", "Tilt Right", Left: false, 2, new PointF(8f, -73f), Editable: true)
	};

	public MouseTopView()
	{
		base.DoubleBuffered = true;
		base.BackColor = Theme.Bg;
	}

	public static string LabelFor(MouseProfile p, string id, string def)
	{
		if (p == null || !p.Buttons.TryGetValue(id, out var t))
		{
			return def;
		}
		return string.Join(" + ", t.Mods.Concat(new string[1] { t.Key }).Select(Ui.PrettyKey));
	}

	private string TextFor(Callout c)
	{
		if (!c.Editable)
		{
			return c.Default;
		}
		return LabelFor(Profile, c.Id, BaseLabel?.Invoke(c.Id) ?? c.Default);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		//IL_016e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0175: Expected Obj, but got Unknown
		//IL_018c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0193: Expected Obj, but got Unknown
		//IL_0211: Unknown result type (might be due to invalid IL or missing references)
		//IL_0218: Expected Obj, but got Unknown
		//IL_0232: Unknown result type (might be due to invalid IL or missing references)
		//IL_0239: Expected Obj, but got Unknown
		//IL_0243: Unknown result type (might be due to invalid IL or missing references)
		//IL_024a: Expected Obj, but got Unknown
		//IL_031e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0325: Expected Obj, but got Unknown
		//IL_03d3: Unknown result type (might be due to invalid IL or missing references)
		//IL_03d9: Expected Obj, but got Unknown
		//IL_045e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0465: Expected Obj, but got Unknown
		//IL_04cb: Unknown result type (might be due to invalid IL or missing references)
		//IL_04d2: Expected Obj, but got Unknown
		//IL_0592: Unknown result type (might be due to invalid IL or missing references)
		//IL_0599: Expected Obj, but got Unknown
		//IL_05bc: Unknown result type (might be due to invalid IL or missing references)
		//IL_05c3: Expected Obj, but got Unknown
		//IL_06ff: Unknown result type (might be due to invalid IL or missing references)
		//IL_0706: Expected Obj, but got Unknown
		//IL_0695: Unknown result type (might be due to invalid IL or missing references)
		//IL_069c: Expected Obj, but got Unknown
		//IL_06b0: Unknown result type (might be due to invalid IL or missing references)
		//IL_06b7: Expected Obj, but got Unknown
		//IL_0909: Unknown result type (might be due to invalid IL or missing references)
		//IL_0910: Expected Obj, but got Unknown
		//IL_09c0: Unknown result type (might be due to invalid IL or missing references)
		//IL_09c7: Expected Obj, but got Unknown
		//IL_0a10: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a17: Expected Obj, but got Unknown
		//IL_0a47: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a4e: Expected Obj, but got Unknown
		Graphics g = e.Graphics;
		Theme.Quality(g);
		Theme.DotGrid(g, base.ClientRectangle);
		hits.Clear();
		float s = Math.Clamp((float)base.Height / 340f, 0.8f, 1.6f);
		float cx = (float)base.Width / 2f;
		float cy = (float)base.Height / 2f - 4f;
		PointF[] body = new PointF[10]
		{
			P(-50f, -128f),
			P(4f, -140f),
			P(54f, -128f),
			P(72f, -44f),
			P(90f, 34f),
			P(72f, 122f),
			P(2f, 150f),
			P(-64f, 124f),
			P(-88f, 40f),
			P(-74f, -44f)
		};
		GraphicsPath path = new GraphicsPath();
		try
		{
			path.AddClosedCurve(body, 0.55f);
			SolidBrush sh = new SolidBrush(Color.FromArgb(70, 0, 0, 0));
			try
			{
				g.TranslateTransform(4f, 6f);
				g.FillPath((Brush)(object)sh, path);
				g.ResetTransform();
				LinearGradientBrush fb = new LinearGradientBrush(new RectangleF(cx - 95f * s, cy - 145f * s, 190f * s, 300f * s), Color.FromArgb(58, 58, 58), Color.FromArgb(18, 18, 18), 80f);
				try
				{
					g.FillPath((Brush)(object)fb, path);
					Pen op = new Pen(Color.FromArgb(90, 90, 90), 1.4f);
					try
					{
						g.DrawPath(op, path);
						GraphicsPath hl = new GraphicsPath();
						try
						{
							hl.AddClosedCurve(new PointF[8]
							{
								P(-34f, -118f),
								P(0f, -128f),
								P(36f, -118f),
								P(46f, -60f),
								P(40f, 30f),
								P(0f, 20f),
								P(-40f, 30f),
								P(-48f, -60f)
							}, 0.5f);
							PathGradientBrush val = new PathGradientBrush(hl);
							val.CenterColor = Color.FromArgb(34, 255, 255, 255);
							val.SurroundColors = new Color[1] { Color.FromArgb(0, 255, 255, 255) };
							PathGradientBrush hb = val;
							try
							{
								g.FillPath((Brush)(object)hb, hl);
							}
							finally
							{
								((IDisposable)hb)?.Dispose();
							}
						}
						finally
						{
							((IDisposable)hl)?.Dispose();
						}
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
				((IDisposable)sh)?.Dispose();
			}
		}
		finally
		{
			((IDisposable)path)?.Dispose();
		}
		Pen seam = new Pen(Color.FromArgb(18, 18, 18), 2f);
		try
		{
			g.DrawLine(seam, P(0f, -139f), P(0f, -100f));
			g.DrawLine(seam, P(-60f, -50f), P(-9f, -50f));
			g.DrawLine(seam, P(66f, -50f), P(9f, -50f));
			Pen gp = new Pen(Theme.Green, 2f);
			try
			{
				GraphicsPath wp = Theme.Round(new RectangleF(cx - 7f * s, cy - 100f * s, 14f * s, 56f * s), 6f * s);
				try
				{
					SolidBrush wb = new SolidBrush(Color.FromArgb(70, 68, 214, 44));
					try
					{
						g.FillPath((Brush)(object)wb, wp);
						g.DrawPath(gp, wp);
					}
					finally
					{
						((IDisposable)wb)?.Dispose();
					}
				}
				finally
				{
					((IDisposable)wp)?.Dispose();
				}
				float[] array = new float[2] { -34f, -17f };
				foreach (float y in array)
				{
					GraphicsPath bp = Theme.Round(new RectangleF(cx - 7f * s, cy + y * s - 6f * s, 14f * s, 12f * s), 3f * s);
					try
					{
						SolidBrush bb = new SolidBrush(Color.FromArgb(20, 20, 20));
						try
						{
							g.FillPath((Brush)(object)bb, bp);
							Pen bpn = new Pen(Color.FromArgb(150, 150, 150), 1.2f);
							try
							{
								g.DrawPath(bpn, bp);
							}
							finally
							{
								((IDisposable)bpn)?.Dispose();
							}
						}
						finally
						{
							((IDisposable)bb)?.Dispose();
						}
					}
					finally
					{
						((IDisposable)bp)?.Dispose();
					}
				}
				for (int j = 0; j < 4; j++)
				{
					PointF k = P(78f + (float)j * 1.5f, -10 + j * 17);
					GraphicsPath kp = Theme.Round(new RectangleF(k.X - 6f * s, k.Y - 5f * s, 12f * s, 10f * s), 2f * s);
					try
					{
						SolidBrush kb = new SolidBrush(Color.FromArgb(40, 68, 214, 44));
						try
						{
							g.FillPath((Brush)(object)kb, kp);
							Pen kn = new Pen(Theme.Green, 1.2f);
							try
							{
								g.DrawPath(kn, kp);
							}
							finally
							{
								((IDisposable)kn)?.Dispose();
							}
						}
						finally
						{
							((IDisposable)kb)?.Dispose();
						}
					}
					finally
					{
						((IDisposable)kp)?.Dispose();
					}
				}
				Pen lp = new Pen(Theme.Green, 2.2f);
				try
				{
					g.DrawEllipse(lp, cx - 10f * s, cy + 48f * s, 20f * s, 20f * s);
				}
				finally
				{
					((IDisposable)lp)?.Dispose();
				}
				Font f = Theme.P(11f, (FontStyle)0);
				try
				{
					float gap = 190f * s;
					float rowH = Math.Clamp((float)base.Height / 7.6f, 38f, 56f);
					float top = cy - 140f * s + 6f;
					Callout[] items = Items;
					foreach (Callout c in items)
					{
						string text = TextFor(c);
						bool mapped = c.Editable && Profile != null && Profile.Buttons.ContainsKey(c.Id);
						bool hot = hover == c.Id;
						float y2 = top + (float)c.Row * rowH;
						PointF tgt = P(c.Target.X, c.Target.Y);
						SizeF size = Tx.Measure(text, f);
						float anchor = (c.Left ? (cx - gap) : (cx + gap));
						RectangleF rect = (c.Left ? new RectangleF(anchor - 16f - size.Width, y2 - size.Height / 2f, size.Width, size.Height) : new RectangleF(anchor + 16f, y2 - size.Height / 2f, size.Width, size.Height));
						if (hot)
						{
							GraphicsPath hp = Theme.Round(RectangleF.Inflate(rect, 9f, 4f), 4f);
							try
							{
								SolidBrush hb2 = new SolidBrush(Color.FromArgb(14, 14, 14));
								try
								{
									g.FillPath((Brush)(object)hb2, hp);
								}
								finally
								{
									((IDisposable)hb2)?.Dispose();
								}
							}
							finally
							{
								((IDisposable)hp)?.Dispose();
							}
						}
						Color col;
						if (c.Id == "LeftClick")
						{
							col = Theme.GreenDim;
						}
						else
						{
							col = (mapped ? Theme.Green : Theme.Text);
						}
						Tx.Left(g, text, f, col, RectangleF.Inflate(rect, 2f, 2f));
						float elbow = (c.Left ? (anchor + 48f) : (anchor - 48f));
						Pen lpen = new Pen(hot ? Theme.Green : Color.FromArgb(96, 96, 96), hot ? 1.4f : 1f);
						try
						{
							g.DrawLine(lpen, anchor, y2, elbow, y2);
							g.DrawLine(lpen, elbow, y2, tgt.X, tgt.Y);
							SolidBrush dot = new SolidBrush(hot ? Theme.Green : Color.FromArgb(150, 150, 150));
							try
							{
								g.FillEllipse((Brush)(object)dot, anchor - 3f, y2 - 3f, 6f, 6f);
								if (hot)
								{
									Pen rp = new Pen(Theme.Green, 2f);
									try
									{
										g.DrawEllipse(rp, tgt.X - 6f, tgt.Y - 6f, 12f, 12f);
									}
									finally
									{
										((IDisposable)rp)?.Dispose();
									}
								}
								else
								{
									g.FillEllipse((Brush)(object)dot, tgt.X - 2.5f, tgt.Y - 2.5f, 5f, 5f);
								}
								hits.Add((RectangleF.Inflate(rect, 14f, 9f), c));
							}
							finally
							{
								((IDisposable)dot)?.Dispose();
							}
						}
						finally
						{
							((IDisposable)lpen)?.Dispose();
						}
					}
				}
				finally
				{
				}
			}
			finally
			{
				((IDisposable)gp)?.Dispose();
			}
		}
		finally
		{
			((IDisposable)seam)?.Dispose();
		}
		PointF P(float x, float num)
		{
			return new PointF(cx + x * s, cy + num * s);
		}
	}

	protected override void OnMouseMove(MouseEventArgs e)
	{
		string h = null;
		foreach (var (r, c) in hits)
		{
			if (r.Contains(e.Location))
			{
				h = c.Id;
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
		foreach (var (r, c) in hits)
		{
			if (r.Contains(e.Location))
			{
				EditRequested?.Invoke(c.Id, (!c.Editable) ? c.Default : (BaseLabel?.Invoke(c.Id) ?? c.Default));
				break;
			}
		}
	}
}
