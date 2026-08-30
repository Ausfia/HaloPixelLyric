using System.Diagnostics;

namespace HaloPixelLyric;

/// <summary>主界面：启停控制、参数调整、日志显示、系统托盘。</summary>
public sealed class MainForm : Form
{
    private readonly LyricService _service = new() { Settings = DisplaySettings.Load() };
    private readonly NotifyIcon _tray;
    private readonly Button _btnToggle = new();
    private readonly Label _lblStatus = new();
    private readonly TextBox _txtLog = new();
    private readonly NumericUpDown[] _numStatics = new NumericUpDown[5];
    private readonly NumericUpDown _numLead, _numUnits, _numInterlude;

    public MainForm()
    {
        Text = "HaloPixelLyric — 网易云歌词 → 花再 PixelBar";
        Font = new Font("Microsoft YaHei UI", 9F);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(560, 640);

        // 状态与启停
        _lblStatus.AutoSize = true;
        _lblStatus.Location = new Point(16, 16);
        Controls.Add(_lblStatus);

        _btnToggle.Location = new Point(16, 42);
        _btnToggle.Size = new Size(110, 32);
        _btnToggle.Text = "启动";
        _btnToggle.Click += async (_, _) =>
        {
            if (_service.IsRunning) _service.Stop();
            else _service.Start();
            await Task.Yield();
        };
        Controls.Add(_btnToggle);

        // 参数区
        var lblParams = new Label {AutoSize = true, Text = "—— 可调参数（保存后对新歌词生效）——", Location = new Point(16, 92)};
        Controls.Add(lblParams);

        int row = 118;
        (string label, NumericUpDown num)[] fields =
        [
            ("翻页提前量 (毫秒)  抵消硬件动画延迟：抢拍调小、慢拍调大", _numLead = MakeNum(0, 1000, 10m)),
            ("屏幕容量 (全角单位)  实测 16；中文 16 字 / 英文 32 字符", _numUnits = MakeNum(8, 24, 0.5m)),
            ("间奏钳制倍数  官方句长超过估算 N 倍判为间奏", _numInterlude = MakeNum(1.5m, 10, 0.5m)),
        ];
        foreach (var (label, num) in fields)
        {
            var lbl = new Label {AutoSize = true, Text = label, Location = new Point(16, row)};
            Controls.Add(lbl);
            num.Location = new Point(430, row - 3);
            num.Width = 100;
            Controls.Add(num);
            row += 34;
        }
        _numLead.Value = _service.Settings.PageLeadMs;
        _numUnits.Value = (decimal)_service.Settings.StaticMaxUnits;
        _numInterlude.Value = (decimal)_service.Settings.InterludeFactor;

        var btnSave = new Button {Text = "保存参数", Location = new Point(16, row + 6), Size = new Size(110, 30)};
        btnSave.Click += (_, _) =>
        {
            _service.Settings.PageLeadMs = (int)_numLead.Value;
            _service.Settings.StaticMaxUnits = (double)_numUnits.Value;
            _service.Settings.InterludeFactor = (double)_numInterlude.Value;
            _service.Settings.Save();
            Log("[ok] 参数已保存（提前量等对新句立即生效，容量从下一句起生效）");
        };
        Controls.Add(btnSave);

        // 日志区
        var lblLog = new Label {AutoSize = true, Text = "—— 运行日志 ——", Location = new Point(16, row + 48)};
        Controls.Add(lblLog);
        _txtLog.Multiline = true;
        _txtLog.ReadOnly = true;
        _txtLog.ScrollBars = ScrollBars.Vertical;
        _txtLog.Font = new Font("Consolas", 9F);
        _txtLog.Location = new Point(16, row + 72);
        _txtLog.Size = new Size(528, ClientSize.Height - row - 88);
        _txtLog.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom | AnchorStyles.Right;
        Controls.Add(_txtLog);

        // 托盘
        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "HaloPixelLyric",
            Visible = true,
            ContextMenuStrip = new ContextMenuStrip(),
        };
        _tray.ContextMenuStrip.Items.Add("显示主界面", null, (_, _) => ShowFromTray());
        _tray.ContextMenuStrip.Items.Add("停止歌词服务", null, (_, _) => _service.Stop());
        _tray.ContextMenuStrip.Items.Add(new ToolStripSeparator());
        _tray.ContextMenuStrip.Items.Add("退出", null, (_, _) => ExitApp());
        _tray.DoubleClick += (_, _) => ShowFromTray();

        _service.Log += OnLog;
        _service.RunningChanged += OnRunningChanged;
        _service.ConfirmRestart += AskRestart;

        UpdateStatus();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // 窗口句柄就绪后再启动服务（事件回调依赖 BeginInvoke）
        if (!_service.IsRunning) _service.Start();
    }

    private static NumericUpDown MakeNum(decimal min, decimal max, decimal step)
    {
        var n = new NumericUpDown {Minimum = min, Maximum = max, DecimalPlaces = step < 1 ? 1 : 0, Increment = step};
        return n;
    }

    private async Task<bool> AskRestart(string message)
    {
        var result = MessageBox.Show(this, message, "需要重启网易云音乐", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
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
        _btnToggle.Text = running ? "停止" : "启动";
        _lblStatus.Text = running ? "状态：● 运行中（关闭窗口将最小化到托盘）" : "状态：○ 已停止";
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
        // 给服务一点时间恢复像素屏时钟模式
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
            // 关闭 = 最小化到托盘
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tray?.Dispose();
        base.Dispose(disposing);
    }
}
