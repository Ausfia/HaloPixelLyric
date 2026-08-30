using System.Diagnostics;

namespace HaloPixelLyric;

/// <summary>
/// 主界面（产品形态）：左侧导航栏（控制台/显示参数/运行日志/关于）+ 右侧分页内容区。
/// 深色主题（Catppuccin Mocha），自绘图标与圆角按钮，布局按 96DPI 设计、OnLoad 按真实 DPI 缩放。
/// </summary>
public sealed class MainForm : Form
{
    private static readonly Color BaseBg = ColorTranslator.FromHtml("#1E1E2E");
    private static readonly Color RailBg = ColorTranslator.FromHtml("#181825");
    private static readonly Color CardBg = ColorTranslator.FromHtml("#181825");
    private static readonly Color FieldBg = ColorTranslator.FromHtml("#313244");
    private static readonly Color Border = ColorTranslator.FromHtml("#45475A");
    private static readonly Color TextMain = ColorTranslator.FromHtml("#CDD6F4");
    private static readonly Color TextDim = ColorTranslator.FromHtml("#7F849C");
    private static readonly Color Accent = ColorTranslator.FromHtml("#89B4FA");
    private static readonly Color Green = ColorTranslator.FromHtml("#A6E3A1");
    private static readonly Color Red = ColorTranslator.FromHtml("#F38BA8");
    private static readonly Color LogBg = ColorTranslator.FromHtml("#11111B");
    private static readonly Color NavHover = ColorTranslator.FromHtml("#24253A");
    private static readonly Color NavActive = ColorTranslator.FromHtml("#2A2B44");

    private readonly LyricService _service = new() { Settings = DisplaySettings.Load() };
    private readonly NotifyIcon _tray;

    private readonly List<(Panel Nav, Panel AccentBar, Panel Page)> _nav = [];
    private Panel _content = null!;

    // 控制台页
    private readonly Panel _statusPill = new();
    private readonly Label _lblPillDot = new();
    private readonly Label _lblPillText = new();
    private RoundButton _btnToggle = null!;
    private readonly Label _lblLyric = new();

    // 参数页
    private readonly NumericUpDown _numLead = new();
    private readonly NumericUpDown _numPre = new();
    private readonly NumericUpDown _numUnits = new();
    private readonly NumericUpDown _numInterlude = new();
    private readonly ModernSlider _sldLead = new();
    private readonly ModernSlider _sldPre = new();
    private readonly ModernSlider _sldUnits = new();
    private readonly ModernSlider _sldInterlude = new();
    private bool _syncingSlider;

    // 日志页
    private readonly TextBox _txtLog = new();

    public MainForm()
    {
        Text = "HaloPixelLyric";
        Font = new Font("Microsoft YaHei UI", 9.5F);
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(760, 580);
        BackColor = BaseBg;
        Icon = IconFactory.CreateAppIcon();

        BuildRail();
        BuildContent();
        SelectPage(0);

        _tray = new NotifyIcon
        {
            Icon = IconFactory.CreateAppIcon(),
            Text = "HaloPixelLyric",
            Visible = true,
            ContextMenuStrip = BuildTrayMenu(),
        };
        _tray.DoubleClick += (_, _) => ShowFromTray();

        _service.Log += OnLog;
        _service.RunningChanged += OnRunningChanged;
        _service.ConfirmRestart += AskRestart;
        _service.LyricPushed += OnLyricPushed;

        UpdateStatus();
    }

    // ================= 左侧导航栏 =================

    private void BuildRail()
    {
        var rail = new Panel { Location = new Point(0, 0), Size = new Size(200, 580), BackColor = RailBg };
        rail.Paint += (_, e) =>
        {
            using var pen = new Pen(Border);
            e.Graphics.DrawLine(pen, rail.Width - 1, 0, rail.Width - 1, rail.Height);
        };
        Controls.Add(rail);

        var pic = new PictureBox
        {
            Image = IconFactory.CreateAppBitmap(),
            SizeMode = PictureBoxSizeMode.Zoom,
            Location = new Point(20, 18),
            Size = new Size(40, 40),
            BackColor = Color.Transparent,
        };
        rail.Controls.Add(pic);

        var lblName = new Label
        {
            Text = "HaloPixelLyric",
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 11.5F, FontStyle.Bold),
            ForeColor = TextMain,
            Location = new Point(68, 26),
        };
        rail.Controls.Add(lblName);

        AddNavItem(rail, 0, "控制台", "\uE80F");
        AddNavItem(rail, 1, "显示参数", "\uE713");
        AddNavItem(rail, 2, "运行日志", "\uE8D6");
        AddNavItem(rail, 3, "关于", "\uE946");

        var lblHint = new Label
        {
            AutoSize = true,
            Text = "双击托盘图标显示窗口",
            Font = new Font("Microsoft YaHei UI", 8F),
            ForeColor = Color.FromArgb(90, TextDim),
            Location = new Point(20, rail.Height - 36),
        };
        rail.Controls.Add(lblHint);
    }

    private void AddNavItem(Panel rail, int index, string text, string glyph)
    {
        var nav = new Panel
        {
            Location = new Point(0, 84 + index * 48),
            Size = new Size(200, 44),
            BackColor = Color.Transparent,
            Cursor = Cursors.Hand,
            Tag = index,
        };
        var bar = new Panel { Location = new Point(0, 6), Size = new Size(3, 32), BackColor = Accent, Visible = false };
        nav.Controls.Add(bar);

        var lblGlyph = new Label
        {
            Text = glyph,
            AutoSize = true,
            ForeColor = TextDim,
            Location = new Point(18, 12),
            BackColor = Color.Transparent,
        };
        if (IconFactory.GlyphFont(11F) is { } gf) lblGlyph.Font = gf;
        else lblGlyph.Text = "·";
        nav.Controls.Add(lblGlyph);

        var lblText = new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = TextMain,
            Location = new Point(48, 13),
            BackColor = Color.Transparent,
        };
        nav.Controls.Add(lblText);

        var hover = false;
        void PaintState()
        {
            if (nav.BackColor == NavActive) return;
            nav.BackColor = hover ? NavHover : Color.Transparent;
        }
        nav.MouseEnter += (_, _) => { hover = true; PaintState(); };
        nav.MouseLeave += (_, _) => { hover = false; PaintState(); };
        nav.Click += (_, _) => SelectPage(index);
        // 子控件点击同样触发
        lblGlyph.Click += (_, _) => SelectPage(index);
        lblText.Click += (_, _) => SelectPage(index);
        lblGlyph.MouseEnter += (_, _) => { hover = true; PaintState(); };
        lblGlyph.MouseLeave += (_, _) => { hover = false; PaintState(); };
        lblText.MouseEnter += (_, _) => { hover = true; PaintState(); };
        lblText.MouseLeave += (_, _) => { hover = false; PaintState(); };

        rail.Controls.Add(nav);
        _nav.Add((nav, bar, null!));
    }

    private void SelectPage(int index)
    {
        for (var i = 0; i < _nav.Count; i++)
        {
            var (nav, bar, page) = _nav[i];
            var active = i == index;
            bar.Visible = active;
            nav.BackColor = active ? NavActive : Color.Transparent;
            if (page is not null) page.Visible = active;
        }
    }

    // ================= 内容区 =================

    private void BuildContent()
    {
        _content = new Panel { Location = new Point(200, 0), Size = new Size(560, 580), BackColor = BaseBg };
        Controls.Add(_content);

        var pages = new[] { BuildServicePage(), BuildParamsPage(), BuildLogPage(), BuildAboutPage() };
        for (var i = 0; i < pages.Length; i++)
            _nav[i] = (_nav[i].Nav, _nav[i].AccentBar, pages[i]);
    }

    private Label PageTitle(string text, int x = 24, int y = 20) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
        ForeColor = TextMain,
        Location = new Point(x, y),
    };

    private Panel MakeCard(Control parent, int x, int y, int w, int h, string title)
    {
        var card = new Panel { Location = new Point(x, y), Size = new Size(w, h), BackColor = CardBg };
        card.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var path = GraphicsExtensions.RoundedRect(0, 0, card.Width - 1, card.Height - 1, 10);
            using var pen = new Pen(Border);
            e.Graphics.DrawPath(pen, path);
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
        parent.Controls.Add(card);
        return card;
    }

    private Panel BuildServicePage()
    {
        var page = new Panel { Location = new Point(0, 0), Size = _content.Size, BackColor = BaseBg };
        _content.Controls.Add(page);

        page.Controls.Add(PageTitle("控制台"));

        // 状态胶囊与启停按钮：同高同基线（48px）
        // 注意：WinForms BackColor 不支持 alpha，半透明效果必须预混为实色（见 Blend）
        _statusPill.Size = new Size(300, 48);
        _statusPill.Location = new Point(24, 58);
        _statusPill.BackColor = Blend(BaseBg, Green, 0.16);
        _statusPill.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var path = GraphicsExtensions.RoundedRect(0, 0, _statusPill.Width - 1, _statusPill.Height - 1, 16);
            using var pen = new Pen(Blend(BaseBg, _lblPillDot.ForeColor, 0.35));   // 跟随状态色
            e.Graphics.DrawPath(pen, path);
        };
        page.Controls.Add(_statusPill);

        _lblPillDot.Text = "●";
        _lblPillDot.AutoSize = true;
        _lblPillDot.BackColor = Color.Transparent;
        _lblPillDot.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
        _lblPillDot.ForeColor = Green;
        _lblPillDot.Location = new Point(16, 15);
        _statusPill.Controls.Add(_lblPillDot);

        _lblPillText.AutoSize = true;
        _lblPillText.BackColor = Color.Transparent;
        _lblPillText.Font = new Font("Microsoft YaHei UI", 9.5F);
        _lblPillText.ForeColor = TextMain;
        _lblPillText.Location = new Point(38, 15);
        _statusPill.Controls.Add(_lblPillText);

        // 启停按钮（与胶囊同高 48、同基线 y=58）
        _btnToggle = RoundButton.Danger("停止", "■", 11.5f);
        _btnToggle.Location = new Point(344, 58);
        _btnToggle.Size = new Size(192, 48);
        _btnToggle.Clicked += (_, _) => { if (_service.IsRunning) _service.Stop(); else _service.Start(); };
        page.Controls.Add(_btnToggle);

        // 当前歌词卡片
        var lyricCard = MakeCard(page, 24, 130, 512, 180, "当前歌词");
        _lblLyric.Text = "等待播放…";
        _lblLyric.AutoSize = false;
        _lblLyric.TextAlign = ContentAlignment.MiddleCenter;
        _lblLyric.Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold);
        _lblLyric.ForeColor = TextMain;
        _lblLyric.BackColor = Color.Transparent;
        _lblLyric.Location = new Point(16, 40);
        _lblLyric.Size = new Size(lyricCard.Width - 32, 96);
        lyricCard.Controls.Add(_lblLyric);

        var lblLyricHint = new Label
        {
            AutoSize = true,
            Text = "像素屏上的内容与此处实时同步",
            ForeColor = TextDim,
            Font = new Font("Microsoft YaHei UI", 8.5F),
            Location = new Point(16, lyricCard.Height - 28),
        };
        lyricCard.Controls.Add(lblLyricHint);

        // 设备信息
        var lblDevice = new Label
        {
            AutoSize = true,
            Text = "设备：花再 Halo PixelBar（USB HID）｜ 通道：CDP :9222 ｜ 播放器：网易云音乐",
            ForeColor = TextDim,
            Font = new Font("Microsoft YaHei UI", 8.5F),
            Location = new Point(26, 326),
        };
        page.Controls.Add(lblDevice);

        return page;
    }

    private Panel BuildParamsPage()
    {
        var page = new Panel { Location = new Point(0, 0), Size = _content.Size, BackColor = BaseBg };
        _content.Controls.Add(page);

        page.Controls.Add(PageTitle("显示参数"));

        var card = MakeCard(page, 24, 66, 512, 376, "常用参数");

        var btnSave = RoundButton.Primary("保存参数", null, 9.5f);
        btnSave.Size = new Size(104, 32);
        btnSave.Location = new Point(card.Width - 104 - 14, 10);
        btnSave.Clicked += (_, _) =>
        {
            _service.Settings.PageLeadMs = (int)_numLead.Value;
            _service.Settings.PrePushLeadMs = (int)_numPre.Value;
            _service.Settings.StaticMaxUnits = (double)_numUnits.Value;
            _service.Settings.InterludeFactor = (double)_numInterlude.Value;
            _service.Settings.Save();
            Log("[ok] 参数已保存，对新歌词立即生效");
        };
        card.Controls.Add(btnSave);

        AddSliderRow(card, 0, "翻页提前量", "毫秒", _sldLead, _numLead, 0, 1000, 10m, 0);
        AddSliderRow(card, 1, "预推提前量", "毫秒", _sldPre, _numPre, 0, 1000, 10m, 0);
        AddSliderRow(card, 2, "屏幕容量", "全角单位", _sldUnits, _numUnits, 8, 24, 0.5m, 1);
        AddSliderRow(card, 3, "间奏钳制倍数", "倍", _sldInterlude, _numInterlude, 1.5m, 10, 0.5m, 1);

        var lblHint = new Label
        {
            AutoSize = true,
            ForeColor = TextDim,
            Font = new Font("Microsoft YaHei UI", 8.5F),
            Text = "翻页：句内换页时机｜预推：每句第一屏时机（锚点含编排偏差，通常需略大）｜容量：中文 16 字 / 英文 32 字符",
            Location = new Point(26, 454),
        };
        page.Controls.Add(lblHint);

        return page;
    }

    private void AddSliderRow(Control card, int row, string name, string unit,
        ModernSlider slider, NumericUpDown num,
        decimal min, decimal max, decimal step, int decimals)
    {
        const int rowH = 78;
        var top = 52 + row * rowH;

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

    private Panel BuildLogPage()
    {
        var page = new Panel { Location = new Point(0, 0), Size = _content.Size, BackColor = BaseBg };
        _content.Controls.Add(page);

        page.Controls.Add(PageTitle("运行日志"));

        var btnClear = RoundButton.Ghost("清空日志", null, 9f);
        btnClear.Size = new Size(100, 30);
        btnClear.Location = new Point(_content.Width - 100 - 24, 18);
        btnClear.Clicked += (_, _) => _txtLog.Clear();
        page.Controls.Add(btnClear);

        var card = MakeCard(page, 24, 66, 512, 480, "");
        _txtLog.Multiline = true;
        _txtLog.ReadOnly = true;
        _txtLog.ScrollBars = ScrollBars.Vertical;
        _txtLog.BackColor = LogBg;
        _txtLog.ForeColor = TextMain;
        _txtLog.BorderStyle = BorderStyle.None;
        _txtLog.Font = new Font("Consolas", 9F);
        _txtLog.Location = new Point(14, 14);
        _txtLog.Size = new Size(card.Width - 28, card.Height - 28);
        card.Controls.Add(_txtLog);

        return page;
    }

    private Panel BuildAboutPage()
    {
        var page = new Panel { Location = new Point(0, 0), Size = _content.Size, BackColor = BaseBg };
        _content.Controls.Add(page);

        page.Controls.Add(PageTitle("关于"));

        var card = MakeCard(page, 24, 66, 512, 220, "");
        var pic = new PictureBox
        {
            Image = IconFactory.CreateAppBitmap(),
            SizeMode = PictureBoxSizeMode.Zoom,
            Location = new Point(20, 24),
            Size = new Size(56, 56),
            BackColor = Color.Transparent,
        };
        card.Controls.Add(pic);

        var lblName = new Label
        {
            Text = "HaloPixelLyric",
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold),
            ForeColor = TextMain,
            Location = new Point(92, 28),
        };
        card.Controls.Add(lblName);

        var lblVer = new Label
        {
            Text = "版本 1.1.0 ｜ 完全 vibe coding 产物（GLM-5.3 协作完成）",
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 8.5F),
            ForeColor = TextDim,
            Location = new Point(92, 60),
        };
        card.Controls.Add(lblVer);

        var lblDesc = new Label
        {
            Text = "把网易云音乐正在显示的歌词行实时推送到花再 Halo PixelBar 像素屏。\r\n\r\n通过 CEF 标准调试接口（CDP）读取网页层歌词，\r\n不注入 DLL、不读写进程内存、不依赖网易云版本偏移。",
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 9F),
            ForeColor = TextMain,
            Location = new Point(20, 96),
            MaximumSize = new Size(470, 0),
        };
        card.Controls.Add(lblDesc);

        var btnGh = RoundButton.Primary("在 GitHub 上查看", null, 9.5f);
        btnGh.Size = new Size(160, 34);
        btnGh.Location = new Point(20, 170);
        btnGh.Clicked += (_, _) =>
            Process.Start(new ProcessStartInfo("https://github.com/Ausfia/HaloPixelLyric") { UseShellExecute = true });
        card.Controls.Add(btnGh);

        var lblCredit = new Label
        {
            Text = "Hid 通信层源自 HaloPixelToolBox（MIT）｜协议参考 halo-pixelbar-mcp（MIT）｜仅供学习研究",
            AutoSize = true,
            ForeColor = TextDim,
            Font = new Font("Microsoft YaHei UI", 8F),
            Location = new Point(26, 300),
        };
        page.Controls.Add(lblCredit);

        return page;
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

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        var scale = DeviceDpi / 96f;
        if (Math.Abs(scale - 1f) > 0.02f)
        {
            SuspendLayout();
            ApplyDpiScale(this, scale);
            ClientSize = new Size((int)(760 * scale), (int)(580 * scale));
            ResumeLayout();
        }
        _numLead.Value = _service.Settings.PageLeadMs;
        _numPre.Value = _service.Settings.PrePushLeadMs;
        _numUnits.Value = (decimal)_service.Settings.StaticMaxUnits;
        _numInterlude.Value = (decimal)_service.Settings.InterludeFactor;
    }

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
        // 默认不自动启动服务，由用户手动点击"启动"
    }

    private async Task<bool> AskRestart(string message)
    {
        var result = MessageBox.Show(this, message, "需要重启网易云音乐",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1);
        return result == DialogResult.Yes;
    }

    private void OnLog(string line)
    {
        // 文件日志（排障用，exe 同目录 halolyric.log，超 2MB 轮转为 .old）
        try
        {
            var path = System.IO.Path.Combine(AppContext.BaseDirectory, "halolyric.log");
            var info = new System.IO.FileInfo(path);
            if (info.Exists && info.Length > 2_000_000)
                System.IO.File.Move(path, path + ".old", overwrite: true);
            System.IO.File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {line}\r\n");
        }
        catch { }
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(() =>
        {
            if (_txtLog.TextLength > 60_000) _txtLog.Clear();
            _txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {line}\r\n");
        });
    }

    private void Log(string line) => OnLog(line);

    private void OnLyricPushed(string lyric)
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(() => _lblLyric.Text = lyric.Length > 40 ? lyric[..40] + "…" : lyric);
    }

    private void OnRunningChanged()
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(UpdateStatus);
    }

    /// <summary>半透明预混：WinForms BackColor 不支持 alpha，需把 overlay 按 alpha 混入 baseColor 得到实色。</summary>
    private static Color Blend(Color baseColor, Color overlay, double alpha) =>
        Color.FromArgb(
            (int)(baseColor.R * (1 - alpha) + overlay.R * alpha),
            (int)(baseColor.G * (1 - alpha) + overlay.G * alpha),
            (int)(baseColor.B * (1 - alpha) + overlay.B * alpha));

    private void UpdateStatus()
    {
        var running = _service.IsRunning;
        _lblPillDot.ForeColor = running ? Green : Red;
        _lblPillText.Text = running ? "运行中" : "已停止";
        _statusPill.BackColor = Blend(BaseBg, running ? Green : Red, 0.16);
        _btnToggle.SetDanger(running);
        _btnToggle.SetContent(running ? "停止" : "启动", running ? "■" : "▶");
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
