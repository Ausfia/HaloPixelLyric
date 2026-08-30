namespace HaloPixelLyric;

/// <summary>
/// 主界面（深色主题）：状态与启停、参数卡片、日志控制台、系统托盘。
/// 配色：Catppuccin Mocha 风格。
/// </summary>
public sealed class MainForm : Form
{
    // 配色
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
    private readonly Label _lblDot = new();
    private readonly Label _lblStatus = new();
    private readonly Button _btnToggle = new();
    private readonly TextBox _txtLog = new();
    private readonly NumericUpDown _numLead = new();
    private readonly NumericUpDown _numUnits = new();
    private readonly NumericUpDown _numInterlude = new();

    public MainForm()
    {
        Text = "HaloPixelLyric";
        Font = new Font("Microsoft YaHei UI", 9.5F);
        // 不使用 WinForms 自动缩放（在高 DPI 手写窗体下行为不可靠），
        // 改为 OnLoad 时按真实 DPI 手动统一缩放全部布局坐标（见 ApplyDpiScale）
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(600, 660);
        BackColor = BaseBg;

        // ===== 状态行 =====
        _lblDot.Text = "●";
        _lblDot.Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold);
        _lblDot.AutoSize = true;
        _lblDot.Location = new Point(24, 20);
        _lblDot.ForeColor = Green;
        Controls.Add(_lblDot);

        _lblStatus.AutoSize = true;
        _lblStatus.ForeColor = TextMain;
        _lblStatus.Location = new Point(52, 26);
        Controls.Add(_lblStatus);

        _btnToggle.Size = new Size(120, 38);
        _btnToggle.Location = new Point(ClientSize.Width - _btnToggle.Width - 24, 18);
        _btnToggle.Text = "停止";
        _btnToggle.FlatStyle = FlatStyle.Flat;
        _btnToggle.FlatAppearance.BorderColor = Red;
        _btnToggle.FlatAppearance.MouseOverBackColor = Color.FromArgb(60, Red.R, Red.G, Red.B);
        _btnToggle.BackColor = Color.FromArgb(40, Red.R, Red.G, Red.B);
        _btnToggle.ForeColor = Red;
        _btnToggle.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
        _btnToggle.Cursor = Cursors.Hand;
        _btnToggle.Click += (_, _) =>
        {
            if (_service.IsRunning) _service.Stop();
            else _service.Start();
        };
        Controls.Add(_btnToggle);

        // ===== 参数卡片 =====
        var cardParams = MakeCard(24, 72, ClientSize.Width - 48, 190, "显示参数");

        AddParamRow(cardParams, 1, "翻页提前量", _numLead, "毫秒", 0, 1000, 10m);
        AddParamRow(cardParams, 2, "屏幕容量", _numUnits, "全角单位", 8, 24, 0.5m);
        AddParamRow(cardParams, 3, "间奏钳制倍数", _numInterlude, "", 1.5m, 10, 0.5m);

        var btnSave = new Button
        {
            Text = "保存参数",
            Size = new Size(96, 30),
            Location = new Point(cardParams.Width - 96 - 16, 12),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(45, Accent.R, Accent.G, Accent.B),
            ForeColor = Accent,
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
            Cursor = Cursors.Hand,
        };
        btnSave.FlatAppearance.BorderColor = Accent;
        btnSave.FlatAppearance.MouseOverBackColor = Color.FromArgb(70, Accent.R, Accent.G, Accent.B);
        btnSave.Click += (_, _) =>
        {
            _service.Settings.PageLeadMs = (int)_numLead.Value;
            _service.Settings.StaticMaxUnits = (double)_numUnits.Value;
            _service.Settings.InterludeFactor = (double)_numInterlude.Value;
            _service.Settings.Save();
            Log("[ok] 参数已保存，对新歌词立即生效");
        };
        cardParams.Controls.Add(btnSave);

        var lblHint = new Label
        {
            AutoSize = true,
            ForeColor = TextDim,
            Font = new Font("Microsoft YaHei UI", 8.5F),
            Text = "提前量：抢拍调小 / 慢拍调大 ｜ 容量：中文 16 字 / 英文 32 字符 ｜ 倍数：间奏异常阈值",
            Location = new Point(24, 272),
        };
        Controls.Add(lblHint);

        // ===== 日志卡片 =====
        var cardLog = MakeCard(24, 300, ClientSize.Width - 48, ClientSize.Height - 300 - 24, "运行日志");

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
            Location = new Point(14, 10),
        };
        card.Controls.Add(lbl);
        Controls.Add(card);
        return card;
    }

    /// <summary>参数行：名称居左，数值控件靠右——说明文字独立在外部提示行，不再重叠。</summary>
    private void AddParamRow(Control card, int row, string name, NumericUpDown num, string unit,
        decimal min, decimal max, decimal step)
    {
        const int rowH = 40;
        var top = 44 + (row - 1) * rowH;

        var lbl = new Label
        {
            AutoSize = true,
            Text = name,
            ForeColor = TextMain,
            Location = new Point(14, top + 3),
        };
        card.Controls.Add(lbl);

        num.Minimum = min;
        num.Maximum = max;
        num.Increment = step;
        num.DecimalPlaces = step < 1 ? 1 : 0;
        num.BackColor = FieldBg;
        num.ForeColor = TextMain;
        num.BorderStyle = BorderStyle.FixedSingle;
        num.Width = 90;
        num.Location = new Point(card.Width - 190, top);
        num.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        card.Controls.Add(num);

        if (!string.IsNullOrEmpty(unit))
        {
            var lblUnit = new Label
            {
                AutoSize = true,
                Text = unit,
                ForeColor = TextDim,
                Location = new Point(card.Width - 190 + 98, top + 4),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
            };
            card.Controls.Add(lblUnit);
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (!_service.IsRunning) _service.Start();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // 按 DPI 手动缩放全部坐标：布局按 96DPI 设计，scale = DeviceDpi/96
        var scale = DeviceDpi / 96f;
        if (Math.Abs(scale - 1f) > 0.02f)
        {
            SuspendLayout();
            ApplyDpiScale(this, scale);
            ClientSize = new Size((int)(600 * scale), (int)(660 * scale));
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
            if (c is NumericUpDown n)
            {
                n.Width = (int)(n.Width * scale);
            }
            ApplyDpiScale(c, scale);
            c.ResumeLayout();
        }
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
        _lblDot.ForeColor = running ? Green : Red;
        _lblStatus.Text = running ? "运行中 — 关闭窗口将最小化到托盘" : "已停止";
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
