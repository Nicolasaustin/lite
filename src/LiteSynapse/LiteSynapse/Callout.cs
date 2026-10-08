using System.Drawing;

namespace LiteSynapse;

internal record Callout(string Id, string Default, bool Left, int Row, PointF Target, bool Editable);
