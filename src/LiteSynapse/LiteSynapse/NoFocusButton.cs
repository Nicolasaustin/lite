using System.Windows.Forms;

namespace LiteSynapse;

internal class NoFocusButton : Button
{
	public NoFocusButton()
	{
		base.SetStyle((ControlStyles)512, false);
		base.TabStop = false;
		base.FlatStyle = (FlatStyle)0;
		base.BackColor = Theme.Panel2;
		base.ForeColor = Theme.Text;
		base.FlatAppearance.BorderColor = Theme.Line;
	}
}
