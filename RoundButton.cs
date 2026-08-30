namespace HaloPixelLyric;

/// <summary>
/// 圆角按钮（自绘）：支持图标字形 + 文字、悬停/按下状态、主题色/危险色/幽灵色三种变体。
/// 所有样式参数经构造函数传入（避免 WinForms 序列化分析器告警）。
/// </summary>
public sealed class RoundButton : Control
{
    private readonly Color _fill, _text, _border;
    private readonly string? _glyph;
    private bool _hover, _pressed;

    public event EventHandler? Clicked;

    private RoundButton(string text, Color fill, Color textColor, Color border, string? glyph, float fontSize)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        _fill = fill;
        _text = textColor;
        _border = border;
        _glyph = glyph;
        Text = text;
        Size = new Size(150, 46);
        Font = new Font("Microsoft YaHei UI", fontSize, FontStyle.Bold);
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    /// <summary>主操作按钮：实心主色底。</summary>
    public static RoundButton Primary(string text, string? glyph = null, float fontSize = 10.5f)
        => new(text, ColorTranslator.FromHtml("#89B4FA"), ColorTranslator.FromHtml("#1E1E2E"),
               ColorTranslator.FromHtml("#89B4FA"), glyph, fontSize);

    /// <summary>危险/停止按钮：半透明红底红框。</summary>
    public static RoundButton Danger(string text, string? glyph = null, float fontSize = 10.5f)
    {
        var red = ColorTranslator.FromHtml("#F38BA8");
        return new(text, Color.FromArgb(45, red.R, red.G, red.B), red, red, glyph, fontSize);
    }

    /// <summary>启动/成功按钮：半透明绿底绿框。</summary>
    public static RoundButton Success(string text, string? glyph = null, float fontSize = 10.5f)
    {
        var green = ColorTranslator.FromHtml("#A6E3A1");
        return new(text, Color.FromArgb(45, green.R, green.G, green.B), green, green, glyph, fontSize);
    }

    /// <summary>幽灵按钮：仅边框文字，低强调。</summary>
    public static RoundButton Ghost(string text, string? glyph = null, float fontSize = 10.5f)
    {
        var accent = ColorTranslator.FromHtml("#89B4FA");
        return new(text, Color.FromArgb(35, accent.R, accent.G, accent.B), accent,
                   ColorTranslator.FromHtml("#45475A"), glyph, fontSize);
    }

    public void SetVariant(bool danger)
    {
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        var fill = _fill;
        if (_pressed) fill = Color.FromArgb(Math.Min(255, fill.A + 40), Darken(fill, 0.85f));
        else if (_hover) fill = Color.FromArgb(Math.Min(255, fill.A + 25), Lighten(fill, 1.15f));

        using var path = GraphicsExtensions.RoundedRect(1, 1, Width - 2, Height - 2, 10);
        using var brush = new SolidBrush(fill);
        g.FillPath(brush, path);
        using var pen = new Pen(_border);
        g.DrawPath(pen, path);

        var textX = 0;
        if (_glyph is not null && IconFactory.GlyphFont(12F) is { } gf)
        {
            using (gf)
            using (var tb = new SolidBrush(_text))
            {
                var size = g.MeasureString(_glyph, gf);
                g.DrawString(_glyph, gf, tb, 16, (Height - size.Height) / 2);
                textX = (int)size.Width + 4;
            }
        }
        using var textBrush = new SolidBrush(_text);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(textX, 0, Width - textX, Height),
            _text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        if (Focused)
        {
            using var focusPen = new Pen(Color.FromArgb(140, 205, 214, 244)) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot };
            using var fp = GraphicsExtensions.RoundedRect(3, 3, Width - 6, Height - 6, 8);
            g.DrawPath(focusPen, fp);
        }
    }

    private static Color Lighten(Color c, float f) =>
        Color.FromArgb(c.A,
            Math.Min(255, (int)(c.R * f)), Math.Min(255, (int)(c.G * f)), Math.Min(255, (int)(c.B * f)));

    private static Color Darken(Color c, float f) =>
        Color.FromArgb(c.A, (int)(c.R * f), (int)(c.G * f), (int)(c.B * f));

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); Focus(); base.OnMouseDown(e); }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        var wasPressed = _pressed;
        _pressed = false;
        Invalidate();
        base.OnMouseUp(e);
        if (wasPressed && ClientRectangle.Contains(e.Location)) Clicked?.Invoke(this, EventArgs.Empty);
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Space or Keys.Enter || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            Clicked?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }
}
