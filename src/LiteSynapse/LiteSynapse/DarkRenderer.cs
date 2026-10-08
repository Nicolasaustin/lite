using System.Drawing;
using System.Windows.Forms;

namespace LiteSynapse;

internal class DarkRenderer : ToolStripProfessionalRenderer
{
	private class DarkColors : ProfessionalColorTable
	{
		public override Color ToolStripDropDownBackground => Color.Black;

		public override Color MenuItemSelected => Color.FromArgb(40, 40, 40);

		public override Color MenuItemBorder => Color.Black;

		public override Color MenuBorder => Color.FromArgb(60, 60, 60);

		public override Color ImageMarginGradientBegin => Color.Black;

		public override Color ImageMarginGradientMiddle => Color.Black;

		public override Color ImageMarginGradientEnd => Color.Black;

		public override Color SeparatorDark => Color.FromArgb(70, 70, 70);

		public override Color SeparatorLight => Color.FromArgb(70, 70, 70);
	}

	public DarkRenderer()
		: base((ProfessionalColorTable)(object)new DarkColors())
	{
		base.RoundedEdges = false;
	}

	protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
	{
		e.TextColor = Color.White;
		base.OnRenderItemText(e);
	}
}
