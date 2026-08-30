using System.Globalization;

namespace HaloPixelLyric;

/// <summary>
/// 主界面（深色主题）：头部品牌区、状态胶囊、启停按钮、滑块式参数卡（滑块+精确输入双向绑定）、
/// 日志控制台、系统托盘。布局按 96DPI 设计，OnLoad 按真实 DPI 统一缩放。
/// 配色：Catppuccin Mocha。
/// </summary>
public sealed class MainForm : Form
{
    private static readonly Color BaseBg = ColorTranslator.FromHtml("#1E1E2E");
    private static readonly Color CardBg = ColorTranslator.FromHtml("#181825");
    private static readonly Color FieldBg = ColorTranslator.FromHtml("#313244");
    private static readonly Color Border = ColorTranslator.FromHtml("#45475A");
    private static readonly Color TextMain = ColorTranslator.FromHtml("#CDD6F4");
    private static readonly Color TextDim = ColorTranslator.FromHtml("#7F849C");
    private static readonly Color Accent = ColorTranslator.FromHtml("#89B4FA");
    private static readonly Color Green = ColorTranslator.FromHtml("#A6E3A1");
    private static readonly Color Red = ColorTranslator.FromHtml("#F38BA8");
    private static readonly Color LogBg = ColorTranslator.FromHtml("#11111B");

    private readonly LyricService _service = new() { Settings = DisplaySettings.Load() };
    private readonly NotifyIcon _tray;
    private readonly Label _lblTitle = new();
    private readonly Label _lblSubtitle = new();
    private readonly Panel _statusPill = new();
    private readonly Label _lblPillDot = new();
    private readonly Label _lblPillText = new();
    private readonly Button _btnToggle = new();
    private readonly TextBox _txtLog = new();

    private readonly NumericUpDown _numLead = new();
    private readonly NumericUpDown _numUnits = new();
    private readonly NumericUpDown _numInterlude = new();
    private readonly ModernSlider _sldLead = new();
    private readonly ModernSlider _sldUnits = new();
    private readonly ModernSlider _sldInterlude = new();
    private bool _syncingSlider;

    public MainForm()
    {
        Text = "HaloPixelLyric";
        Font = new Font("Microsoft YaHei UI", 9.5F);
        // 布局按 96DPI 设计；OnLoad 按 DeviceDpi/96 手动统一缩放（WinForms 自动缩放在高 DPI 不可靠）
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(620, 740);
        BackColor = BaseBg;

        // ===== 头部品牌区 =====
        _lblTitle.Text = "HaloPixelLyric";
        _lblTitle.AutoSize = true;
        _lblTitle.Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold);
        _lblTitle.ForeColor = TextMain;
        _lblTitle.Location = new Point(24, 18);
        Controls.Add(_lblTitle);

        _lblSubtitle.Text = "网易云音乐歌词 → 花再 Halo PixelBar 像素屏";
        _lblSubtitle.AutoSize = true;
        _lblSubtitle.Font = new Font("Microsoft YaHei UI", 9F);
        _lblSubtitle.ForeColor = TextDim;
        _lblSubtitle.Location = new Point(26, 58);
        Controls.Add(_lblSubtitle);

        _btnToggle.Size = new Size(140, 46);
        _btnToggle.Location = new Point(ClientSize.Width - _btnToggle.Width - 24, 28);
        _btnToggle.Text = "停止";
        _btnToggle.FlatStyle = FlatStyle.Flat;
        _btnToggle.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
        _btnToggle.Cursor = Cursors.Hand;
        _btnToggle.Click += (_, _) => { if (_service.IsRunning) _service.Stop(); else _service.Start(); };
        Controls.Add(_btnToggle);

        // ===== 状态胶囊 =====
        _statusPill.Size = new Size(280, 34);
        _statusPill.Location = new Point(24, 100);
        _statusPill.BackColor = Color.FromArgb(50, Green.R, Green.G, Green.B);
        _statusPill.Paint += (_, e) =>
        {
            using var path = GraphicsExtensions.RoundedRect(0, 0, _statusPill.Width - 1, _statusPill.Height - 1, 15);
            using var pen = new Pen(Color.FromArgb(90, Green.R, Green.G, Green.B));
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.DrawPath(pen, path);
        };
        Controls.Add(_statusPill);

        _lblPillDot.Text = "●";
        _lblPillDot.AutoSize = true;
        _lblPillDot.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
        _lblPillDot.ForeColor = Green;
        _lblPillDot.Location = new Point(14, 8);
        _statusPill.Controls.Add(_lblPillDot);

        _lblPillText.AutoSize = true;
        _lblPillText.Font = new Font("Microsoft YaHei UI", 9F);
        _lblPillText.ForeColor = TextMain;
        _lblPillText.Location = new Point(32, 8);
        _statusPill.Controls.Add(_lblPillText);

        // ===== 参数卡片（滑块式） =====
        var cardParams = MakeCard(24, 152, ClientSize.Width - 48, 306, "显示参数");

        var btnSave = new Button
        {
            Text = "保存参数",
            Size = new Size(92, 30),
            Location = new Point(cardParams.Width - 92 - 14, 11),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(45, Accent.R, Accent.G, Accent.B),
            ForeColor = Accent,
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
            Cursor = Cursors.Hand,
        };
        btnSave.FlatAppearance.BorderColor = Accent;
        btnSave.FlatAppearance.MouseOverBackColor = Color.FromArgb(80, Accent.R, Accent.G, Accent.B);
        btnSave.Click += (_, _) =>
        {
            _service.Settings.PageLeadMs = (int)_numLead.Value;
            _service.Settings.StaticMaxUnits = (double)_numUnits.Value;
            _service.Settings.InterludeFactor = (double)_numInterlude.Value;
            _service.Settings.Save();
            Log("[ok] 参数已保存，对新歌词立即生效");
        };
        cardParams.Controls.Add(btnSave);

        // 每行：名称 + 数值输入（右）；滑块占满整行
        AddSliderRow(cardParams, 0, "翻页提前量", "毫秒", _sldLead, _numLead,
            0, 1000, 10m, 0, v => (object)(int)v);
        AddSliderRow(cardParams, 1, "屏幕容量", "全角单位", _sldUnits, _numUnits,
            8, 24, 0.5m, 1, v => (object)(double)v);
        AddSliderRow(cardParams, 2, "间奏钳制倍数", "倍", _sldInterlude, _numInterlude,
            1.5m, 10, 0.5m, 1, v => (object)(double)v);

        var lblHint = new Label
        {
            AutoSize = true,
            ForeColor = TextDim,
            Font = new Font("Microsoft YaHei UI", 8.5F),
            Text = "提前量：抢拍调小 / 慢拍调大（硬件动画差异）｜容量：中文 16 字 / 英文 32 字符",
            Location = new Point(26, 470),
        };
        Controls.Add(lblHint);

        // ===== 日志卡片 =====
        var cardLog = MakeCard(24, 498, ClientSize.Width - 48, ClientSize.Height - 498 - 24, "运行日志");

        _txtLog.Multiline = true;
        _txtLog.ReadOnly = true;
        _txtLog.ScrollBars = ScrollBars.Vertical;
        _txtLog.BackColor = LogBg;
        _txtLog.ForeColor = TextMain;
        _txtLog.BorderStyle = BorderStyle.None;
        _txtLog.Font = new Font("Consolas", 9F);
        _txtLog.Location = new Point(14, 40);
        _txtLog.Size = new Size(cardLog.Width - 28, cardLog.Height - 40 - 14);
        _txtLog.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        cardLog.Controls.Add(_txtLog);

        // ===== 托盘 =====
        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "HaloPixelLyric",
            Visible = true,
            ContextMenuStrip = BuildTrayMenu(),
        };
        _tray.DoubleClick += (_, _) => ShowFromTray();

        _service.Log += OnLog;
        _service.RunningChanged += OnRunningChanged;
        _service.ConfirmRestart += AskRestart;

        UpdateStatus();
    }

    private ContextMenuStrip BuildTrayMenu()
    {
        var menu = new ContextMenuStrip { BackColor = CardBg, ForeColor = TextMain, ShowImageMargin = false };
        var itemShow = new ToolStripMenuItem("显示主界面") { ForeColor = TextMain, BackColor = CardBg };
        itemShow.Click += (_, _) => ShowFromTray();
        var itemToggle = new ToolStripMenuItem("停止 / 启动") { ForeColor = TextMain, BackColor = CardBg };
        itemToggle.Click += (_, _) => { if (_service.IsRunning) _service.Stop(); else _service.Start(); };
        var itemExit = new ToolStripMenuItem("退出") { ForeColor = Red, BackColor = CardBg };
        itemExit.Click += (_, _) => ExitApp();
        menu.Items.Add(itemShow);
        menu.Items.Add(itemToggle);
        menu.Items.Add(new ToolStripSeparator { BackColor = Border });
        menu.Items.Add(itemExit);
        return menu;
    }

    /// <summary>深色卡片：自绘边框面板 + 左上标题。</summary>
    private Panel MakeCard(int x, int y, int w, int h, string title)
    {
        var card = new Panel
        {
            Location = new Point(x, y),
            Size = new Size(w, h),
            BackColor = CardBg,
        };
        card.Paint += (_, e) =>
        {
            using var pen = new Pen(Border);
            e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
        };
        var lbl = new Label
        {
            AutoSize = true,
            Text = title,
            ForeColor = TextDim,
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
            Location = new Point(14, 12),
        };
        card.Controls.Add(lbl);
        Controls.Add(card);
        return card;
    }

    /// <summary>
    /// 滑块参数行：第一行为「名称 + 数值输入 + 单位」，第二行为整行滑块；滑块与输入框双向绑定。
    /// </summary>
    private void AddSliderRow(Control card, int row, string name, string unit,
        ModernSlider slider, NumericUpDown num,
        decimal min, decimal max, decimal step, int decimals,
        Func<decimal, object> toSetting)
    {
        const int rowH = 78;
        var top = 50 + row * rowH;

        var lbl = new Label
        {
            AutoSize = true,
            Text = name,
            ForeColor = TextMain,
            Font = new Font("Microsoft YaHei UI", 9.5F),
            Location = new Point(14, top + 4),
        };
        card.Controls.Add(lbl);

        num.Minimum = min;
        num.Maximum = max;
        num.Increment = step;
        num.DecimalPlaces = decimals;
        num.BackColor = FieldBg;
        num.ForeColor = TextMain;
        num.BorderStyle = BorderStyle.FixedSingle;
        num.Font = new Font("Consolas", 10F);
        num.TextAlign = HorizontalAlignment.Center;
        num.Width = 84;
        // 隐藏原生上下箭头，视觉更干净（拖滑块或直接键入皆可）
        if (num.Controls.Count > 0) num.Controls[0].Visible = false;
        num.Location = new Point(card.Width - 170, top);
        num.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        card.Controls.Add(num);

        var lblUnit = new Label
        {
            AutoSize = true,
            Text = unit,
            ForeColor = TextDim,
            Location = new Point(card.Width - 80, top + 5),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        card.Controls.Add(lblUnit);

        slider.Minimum = min;
        slider.Maximum = max;
        slider.Step = step;
        slider.DecimalPlaces = decimals;
        slider.BackColor = CardBg;
        slider.Location = new Point(14, top + 30);
        slider.Size = new Size(card.Width - 28, 34);
        slider.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        card.Controls.Add(slider);

        // 双向绑定（带环路保护）
        slider.ValueChanged += (_, _) =>
        {
            if (_syncingSlider) return;
            _syncingSlider = true;
            num.Value = decimal.Clamp(slider.Value, num.Minimum, num.Maximum);
            _syncingSlider = false;
        };
        num.ValueChanged += (_, _) =>
        {
            if (_syncingSlider) return;
            _syncingSlider = true;
            slider.Value = num.Value;
            _syncingSlider = false;
        };
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        var scale = DeviceDpi / 96f;
        if (Math.Abs(scale - 1f) > 0.02f)
        {
            SuspendLayout();
            ApplyDpiScale(this, scale);
            ClientSize = new Size((int)(620 * scale), (int)(740 * scale));
            ResumeLayout();
        }
        _numLead.Value = _service.Settings.PageLeadMs;
        _numUnits.Value = (decimal)_service.Settings.StaticMaxUnits;
        _numInterlude.Value = (decimal)_service.Settings.InterludeFactor;
    }

    /// <summary>递归缩放控件树的位置与尺寸（字体由系统按 DPI 渲染，无需干预）。</summary>
    private static void ApplyDpiScale(Control root, float scale)
    {
        foreach (Control c in root.Controls)
        {
            c.SuspendLayout();
            c.Location = new Point((int)(c.Location.X * scale), (int)(c.Location.Y * scale));
            c.Size = new Size((int)(c.Size.Width * scale), (int)(c.Size.Height * scale));
            ApplyDpiScale(c, scale);
            c.ResumeLayout();
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (!_service.IsRunning) _service.Start();
    }

    private async Task<bool> AskRestart(string message)
    {
        var result = MessageBox.Show(this, message, "需要重启网易云音乐",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1);
        return result == DialogResult.Yes;
    }

    private void OnLog(string line)
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(() =>
        {
            if (_txtLog.TextLength > 60_000) _txtLog.Clear();
            _txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {line}\r\n");
        });
    }

    private void Log(string line) => OnLog(line);

    private void OnRunningChanged()
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(UpdateStatus);
    }

    private void UpdateStatus()
    {
        var running = _service.IsRunning;
        _lblPillDot.ForeColor = running ? Green : Red;
        _lblPillText.Text = running ? "运行中 · 关闭窗口最小化到托盘" : "已停止";
        _statusPill.BackColor = Color.FromArgb(50, running ? Green.R : Red.R, running ? Green.G : Red.G, running ? Green.B : Red.B);
        _btnToggle.Text = running ? "停止" : "启动";
        _btnToggle.ForeColor = running ? Red : Green;
        _btnToggle.FlatAppearance.BorderColor = running ? Red : Green;
        _btnToggle.BackColor = Color.FromArgb(40, running ? Red.R : Green.R, running ? Red.G : Green.G, running ? Red.B : Green.B);
        _tray.Text = running ? "HaloPixelLyric — 运行中" : "HaloPixelLyric — 已停止";
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void ExitApp()
    {
        if (_service.IsRunning) _service.Stop();
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (_service.IsRunning && DateTime.UtcNow < deadline)
            Application.DoEvents();
        _tray.Visible = false;
        Application.Exit();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        _tray.Visible = false;
        base.OnHandleDestroyed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tray?.Dispose();
        base.Dispose(disposing);
    }
}
