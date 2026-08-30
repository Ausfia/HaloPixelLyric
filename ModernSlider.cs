using System.Globalization;

namespace HaloPixelLyric;

/// <summary>
/// 自绘滑块控件：贴合深色主题（细轨道 + 主色填充 + 圆形拇指），支持键盘与拖拽。
/// </summary>
public sealed class ModernSlider : Control
{
    private static readonly Color TrackColor = ColorTranslator.FromHtml("#45475A");
    private static readonly Color FillColor = ColorTranslator.FromHtml("#89B4FA");
    private static readonly Color ThumbColor = ColorTranslator.FromHtml("#CDD6F4");

    private decimal _min = 0, _max = 100, _step = 1, _value = 50;
    private bool _dragging;
    private bool _shouldSerializeMinimum, _shouldSerializeMaximum, _shouldSerializeStep, _shouldSerializeValue, _shouldSerializeDecimalPlaces;

    public decimal Minimum { get => _min; set { _min = value; _shouldSerializeMinimum = true; ClampValue(); Invalidate(); } }
    public decimal Maximum { get => _max; set { _max = value; _shouldSerializeMaximum = true; ClampValue(); Invalidate(); } }
    public decimal Step { get => _step; set { _step = value <= 0 ? 1 : value; _shouldSerializeStep = true; } }
    public int DecimalPlaces { get => DecimalPlacesInternal; set { DecimalPlacesInternal = value; _shouldSerializeDecimalPlaces = true; } }
    private int DecimalPlacesInternal { get; set; }

    private bool ShouldSerializeMinimum() => _shouldSerializeMinimum;
    private bool ShouldSerializeMaximum() => _shouldSerializeMaximum;
    private bool ShouldSerializeStep() => _shouldSerializeStep;
    private bool ShouldSerializeValue() => _shouldSerializeValue;
    private bool ShouldSerializeDecimalPlaces() => _shouldSerializeDecimalPlaces;

    public decimal Value
    {
        get => _value;
        set
        {
            var snapped = Math.Round(value / _step, 6);
            var v = Math.Clamp(snapped * _step, _min, _max);
            if (v == _value) return;
            _value = v;
            _shouldSerializeValue = true;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? ValueChanged;

    public ModernSlider()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    private float ThumbX
    {
        get
        {
            var range = _max - _min;
            var frac = range == 0 ? 0 : (float)((_value - _min) / range);
            return 10 + frac * Math.Max(1, Width - 20);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var mid = Height / 2f;
        var thumbX = ThumbX;

        // 轨道
        using var trackBrush = new SolidBrush(TrackColor);
        g.FillRoundedRectangle(trackBrush, 6, mid - 2.5f, Width - 12, 5, 2.5f);
        // 已填充
        using var fillBrush = new SolidBrush(FillColor);
        if (thumbX > 10)
            g.FillRoundedRectangle(fillBrush, 6, mid - 2.5f, thumbX - 6, 5, 2.5f);
        // 拇指
        using var thumbBrush = new SolidBrush(ThumbColor);
        g.FillEllipse(thumbBrush, thumbX - 8, mid - 8, 16, 16);
        using var ring = new Pen(Color.FromArgb(60, 0, 0, 0));
        g.DrawEllipse(ring, thumbX - 8, mid - 8, 16, 16);

        if (Focused)
        {
            using var focusPen = new Pen(Color.FromArgb(120, FillColor)) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot };
            g.DrawRectangle(focusPen, 1, 1, Width - 2, Height - 2);
        }
        base.OnPaint(e);
    }

    private void SetValueFromMouse(int x)
    {
        var frac = Math.Clamp((x - 10f) / Math.Max(1, Width - 20), 0f, 1f);
        Value = _min + (decimal)frac * (_max - _min);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        Capture = true;
        _dragging = true;
        SetValueFromMouse(e.X);
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging) SetValueFromMouse(e.X);
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _dragging = false;
        Capture = false;
        base.OnMouseUp(e);
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Left or Keys.Down) Value -= _step;
        else if (e.KeyCode is Keys.Right or Keys.Up) Value += _step;
        else if (e.KeyCode == Keys.Home) Value = _min;
        else if (e.KeyCode == Keys.End) Value = _max;
        else { base.OnKeyDown(e); return; }
        e.Handled = true;
    }

    private void ClampValue()
    {
        _value = Math.Clamp(_value, _min, _max);
    }

    public string ValueText => _value.ToString(DecimalPlaces > 0 ? "0.0" : "0", CultureInfo.InvariantCulture);
}

internal static class GraphicsExtensions
{
    public static void FillRoundedRectangle(this Graphics g, Brush brush, float x, float y, float w, float h, float r)
    {
        using var path = RoundedRect(x, y, w, h, r);
        g.FillPath(brush, path);
    }

    public static System.Drawing.Drawing2D.GraphicsPath RoundedRect(float x, float y, float w, float h, float r)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        var d = r * 2;
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + w - d, y, d, d, 270, 90);
        path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        path.AddArc(x, y + h - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
