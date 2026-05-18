using System.Drawing.Drawing2D;

namespace D2RExactMirror;

internal sealed class DirectionOverlayForm : Form
{
    private Point? _anchor;
    private Point? _cursor;
    private Color _color = Color.Lime;

    internal DirectionOverlayForm(Color color)
    {
        _color = color;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;
        DoubleBuffered = true;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    internal void UpdateOverlay(IntPtr sourceWindow, Point? anchor, Point? cursor)
    {
        if (!NativeMethods.IsWindow(sourceWindow) ||
            !NativeMethods.GetClientRect(sourceWindow, out var rect))
        {
            Hide();
            return;
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
        {
            Hide();
            return;
        }

        var origin = new NativeMethods.POINT { X = 0, Y = 0 };
        if (!NativeMethods.ClientToScreen(sourceWindow, ref origin))
        {
            Hide();
            return;
        }

        Bounds = new Rectangle(origin.X, origin.Y, width, height);
        _anchor = anchor;
        _cursor = cursor;

        if (!Visible) Show();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_anchor is not { } anchor || _cursor is not { } cursor) return;

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using var pen = new Pen(_color, 2)
        {
            EndCap = LineCap.ArrowAnchor,
            StartCap = LineCap.Round
        };
        using var dotBrush = new SolidBrush(_color);
        using var glowPen = new Pen(Color.FromArgb(140, Color.Black), 4);

        e.Graphics.DrawLine(glowPen, anchor, cursor);
        e.Graphics.DrawLine(pen, anchor, cursor);
        e.Graphics.FillEllipse(dotBrush, anchor.X - 4, anchor.Y - 4, 8, 8);
    }
}
