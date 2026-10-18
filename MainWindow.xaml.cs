using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using HaloPixelLyric.Ui;
using Wf = System.Windows.Forms;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using Point = System.Windows.Point;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using RadioButton = System.Windows.Controls.RadioButton;

namespace HaloPixelLyric;

/// <summary>
/// 主窗口（苹果风格重构版）。
/// 视觉：半透明侧栏 + 卡片内容区 + 自绘矢量图标；动效：启动序列、页面切换、状态呼吸灯、
/// 歌词淡入淡出、Toast、内嵌确认框。业务逻辑仍由 <see cref="LyricService"/> 承担。
/// </summary>
public partial class MainWindow : Window
{
    private static readonly CubicEase EaseOut = new() { EasingMode = EasingMode.EaseOut };

    private readonly DisplaySettings _settings;
    private readonly LyricService _service;
    private readonly ObservableCollection<LogItem> _logs = [];
    private readonly List<Grid> _pages;
    private readonly Wf.NotifyIcon _tray;
    private readonly Wf.ContextMenuStrip _trayMenu;
    private readonly Wf.ToolStripMenuItem _trayToggleItem;
    private readonly Wf.ToolStripMenuItem _trayExitItem;

    private bool _dark;
    private bool _exitRequested;
    private bool _suppressNav;
    private bool _logAutoScroll = true;
    private int _currentPage;
    private int _lyricVersion;
    private string _lastLyric = "";
    private string _nowTitle = "";
    private ScrollViewer? _logScroll;
    private TaskCompletionSource<bool>? _confirmTcs;
    private CancellationTokenSource? _toastCts;

    public MainWindow(DisplaySettings settings, bool dark)
    {
        _settings = settings;
        _dark = dark;
        _service = new LyricService { Settings = settings };

        InitializeComponent();

        _pages = [PageHome, PageParams, PageLog, PageAbout];
        LogList.ItemsSource = _logs;
        StateChanged += (_, _) => UpdateMaxButton();

        VersionBadge.Text = "v" + AppVersion;
        AboutVersionText.Text = "v" + AppVersion;
        UpdateThemeIcon();

        LoadSettingsIntoUi();
        InitNav();
        UpdateMaxButton();
        OsText.Text = Environment.OSVersion.VersionString;
        RuntimeText.Text = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;

        _trayMenu = BuildTrayMenu();
        _tray = new Wf.NotifyIcon
        {
            Icon = IconFactory.CreateAppIcon(),
            Text = "HaloPixelLyric",
            Visible = true,
            ContextMenuStrip = _trayMenu,
        };
        _tray.DoubleClick += (_, _) => ShowFromTray();
        _trayToggleItem = (Wf.ToolStripMenuItem)_trayMenu.Items[1];
        _trayExitItem = (Wf.ToolStripMenuItem)_trayMenu.Items[3];
        UpdateTrayTheme();

        _service.Log += OnServiceLog;
        _service.RunningChanged += OnRunningChanged;
        _service.LyricPushed += OnLyricPushed;
        _service.NowPlayingChanged += OnNowPlaying;
        _service.ConfirmRestart += OnConfirmRestart;

        UpdateStatus();
        Loaded += MainWindow_Loaded;
        SourceInitialized += (_, _) =>
        {
            WindowEffects.Apply(this, _dark);
            var handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(handle)?.AddHook(WindowProc);
        };

        Log("[ok] HaloPixelLyric 界面已就绪");
    }

    private static string AppVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v is null ? "1.4.0" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    // ================= 初始化 =================

    private void InitNav()
    {
        _suppressNav = true;
        NavHome.IsChecked = true;
        _suppressNav = false;
        _currentPage = 0;
        MoveNavHighlight(0, false);
    }

    private void LoadSettingsIntoUi()
    {
        LeadSlider.Value = _settings.PageLeadMs;
        PreSlider.Value = _settings.PrePushLeadMs;
        UnitsSlider.Value = _settings.StaticMaxUnits;
        InterludeSlider.Value = _settings.InterludeFactor;
        UpdateParamLabels();
    }

    // ================= 启动动画 =================

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        HookLogScroll();
        await PlaySplashAsync();

        if (_service.IsRunning) UpdateStatus();
    }

    private void HookLogScroll()
    {
        LogList.ApplyTemplate();
        _logScroll = FindVisualChild<ScrollViewer>(LogList);
        if (_logScroll is not null)
            _logScroll.ScrollChanged += LogScroll_ScrollChanged;
    }

    private async Task PlaySplashAsync()
    {
        SplashLogo.RenderTransform = new ScaleTransform(0.84, 0.84);
        SplashLogo.Opacity = 0;
        SplashName.RenderTransform = new TranslateTransform(0, 6);
        SplashSub.RenderTransform = new TranslateTransform(0, 6);

        var logoScale = (ScaleTransform)SplashLogo.RenderTransform;
        SplashLogo.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)) { EasingFunction = EaseOut });
        var scaleAnim = new DoubleAnimation(0.84, 1, TimeSpan.FromMilliseconds(520))
        {
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.28 },
        };
        logoScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
        logoScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);

        await Task.Delay(130);

        SplashName.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)) { EasingFunction = EaseOut });
        ((TranslateTransform)SplashName.RenderTransform).BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(6, 0, TimeSpan.FromMilliseconds(340)) { EasingFunction = EaseOut });
        SplashSub.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320)) { EasingFunction = EaseOut });
        ((TranslateTransform)SplashSub.RenderTransform).BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(6, 0, TimeSpan.FromMilliseconds(360)) { EasingFunction = EaseOut });

        SplashProgress.BeginAnimation(WidthProperty,
            new DoubleAnimation(0, 160, TimeSpan.FromMilliseconds(760))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            });

        await Task.Delay(840);

        var mainTransform = new TranslateTransform(0, 8);
        MainRoot.RenderTransform = mainTransform;
        MainRoot.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(360)) { EasingFunction = EaseOut });
        mainTransform.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(420)) { EasingFunction = EaseOut });

        var splashFade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(320))
        {
            BeginTime = TimeSpan.FromMilliseconds(60),
        };
        splashFade.Completed += (_, _) => Splash.Visibility = Visibility.Collapsed;
        Splash.BeginAnimation(OpacityProperty, splashFade);
    }

    // ================= 导航与页面切换 =================

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppressNav) return;
        if (sender is not RadioButton { Tag: string tag } || !int.TryParse(tag, out var index)) return;
        SelectPage(index, true);
    }

    private void SelectPage(int index, bool animate)
    {
        if (index == _currentPage && _pages[index].Visibility == Visibility.Visible) return;

        var previous = _currentPage;
        _currentPage = index;
        MoveNavHighlight(index, animate);

        var next = _pages[index];
        var prev = _pages[previous];

        if (!animate)
        {
            foreach (var page in _pages)
            {
                page.Visibility = Visibility.Collapsed;
                page.Opacity = 1;
            }
            next.Visibility = Visibility.Visible;
            return;
        }

        foreach (var page in _pages)
        {
            if (!ReferenceEquals(page, prev) && !ReferenceEquals(page, next))
            {
                page.Visibility = Visibility.Collapsed;
                page.Opacity = 1;
            }
        }

        next.Visibility = Visibility.Visible;
        next.Opacity = 0;
        var translate = EnsureTranslate(next);
        translate.Y = 14;
        next.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240)) { EasingFunction = EaseOut });
        translate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(300)) { EasingFunction = EaseOut });

        if (!ReferenceEquals(prev, next) && prev.Visibility == Visibility.Visible)
        {
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(110));
            fade.Completed += (_, _) =>
            {
                prev.Visibility = Visibility.Collapsed;
                prev.Opacity = 1;
            };
            prev.BeginAnimation(OpacityProperty, fade);
        }
    }

    private void MoveNavHighlight(int index, bool animate)
    {
        var target = index * 44.0;
        var transform = NavHighlightTransform;
        transform.BeginAnimation(TranslateTransform.YProperty, null);
        if (!animate)
        {
            transform.Y = target;
            return;
        }
        transform.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(transform.Y, target, TimeSpan.FromMilliseconds(280)) { EasingFunction = EaseOut });
    }

    private void HomeLogBtn_Click(object sender, RoutedEventArgs e) => NavLog.IsChecked = true;

    // ================= 服务状态 =================

    private void ToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_service.IsRunning) _service.Stop();
        else _service.Start();
        UpdateStatus();
    }

    private void OnRunningChanged()
    {
        if (Dispatcher.CheckAccess()) UpdateStatus();
        else Dispatcher.BeginInvoke(new Action(UpdateStatus));
    }

    private void UpdateStatus()
    {
        var running = _service.IsRunning;

        HeroStatusText.Text = running ? "运行中" : "已停止";
        HeroDot.Fill = (Brush)FindResource(running ? "SuccessBrush" : "DangerBrush");
        HeroTitle.Text = running ? "正在同步" : "准备就绪";
        HeroSubtitle.Text = running ? "网易云歌词已实时联动像素屏" : "点击启动，让像素屏跟着歌词呼吸";
        SidebarStatusText.Text = running ? "同步中" : "未启动";
        SidebarStatusDot.Fill = (Brush)FindResource(running ? "SuccessBrush" : "DangerBrush");
        SidebarStatusDetail.Text = running ? "花再 Halo PixelBar · 已连接" : "花再 Halo PixelBar";
        ToggleBtn.Content = running ? "停止同步" : "启动同步";
        ToggleBtn.Style = (Style)FindResource(running ? "HeroGlassButton" : "HeroPrimaryButton");
        _tray.Text = running ? "HaloPixelLyric — 运行中" : "HaloPixelLyric — 已停止";
        if (_trayToggleItem is not null)
            _trayToggleItem.Text = running ? "停止同步" : "启动同步";

        SetPulse(running);
        SetWaveform(running);

        if (!running)
        {
            _lastLyric = "";
            _nowTitle = "";
            LyricText.Text = "同步已停止";
            LyricText.Opacity = 0.5;
            LyricIdleIcon.Visibility = Visibility.Visible;
            SongTitleText.Text = "";
            NowPlayingText.Text = "等待播放…";
        }
        else
        {
            NowPlayingText.Text = string.IsNullOrEmpty(_nowTitle) ? "正在播放" : "♪ " + _nowTitle;
            if (string.IsNullOrEmpty(_lastLyric))
            {
                LyricText.Text = "等待播放…";
                LyricText.Opacity = 0.5;
                LyricIdleIcon.Visibility = Visibility.Visible;
            }
        }
    }

    private void SetPulse(bool on)
    {
        foreach (var dot in new FrameworkElement[] { HeroDot, SidebarStatusDot })
        {
            if (dot.RenderTransform is not ScaleTransform scale)
            {
                scale = new ScaleTransform(1, 1);
                dot.RenderTransform = scale;
            }
            dot.RenderTransformOrigin = new Point(0.5, 0.5);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            dot.BeginAnimation(OpacityProperty, null);
            scale.ScaleX = scale.ScaleY = 1;
            dot.Opacity = 1;
            if (!on) continue;

            var duration = TimeSpan.FromMilliseconds(1100);
            var pulse = new DoubleAnimation(1, 1.32, duration)
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
            dot.BeginAnimation(OpacityProperty,
                new DoubleAnimation(1, 0.6, duration)
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                });
        }
    }

    private void SetWaveform(bool active)
    {
        var bars = new[] { Wave1, Wave2, Wave3, Wave4, Wave5, Wave6, Wave7 };
        for (var i = 0; i < bars.Length; i++)
        {
            var bar = bars[i];
            if (bar.RenderTransform is not ScaleTransform scale)
            {
                scale = new ScaleTransform(1, 1);
                bar.RenderTransform = scale;
            }
            bar.RenderTransformOrigin = new Point(0.5, 1);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

            if (!active)
            {
                scale.ScaleY = 0.32;
                bar.Opacity = 0.55;
                continue;
            }

            bar.Opacity = 0.95;
            scale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.28, 1, TimeSpan.FromMilliseconds(520 + i * 55))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    BeginTime = TimeSpan.FromMilliseconds(i * 70),
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                });
        }
    }

    private void OnLyricPushed(string line) => Dispatcher.BeginInvoke(new Action(() => SetLyric(line)));

    private void SetLyric(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || line == _lastLyric) return;
        _lastLyric = line;

        var version = ++_lyricVersion;
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(110)) { EasingFunction = EaseOut };
        fadeOut.Completed += (_, _) =>
        {
            if (version != _lyricVersion) return;
            LyricText.Text = line;
            LyricText.Opacity = 1;
            LyricIdleIcon.Visibility = Visibility.Collapsed;
            var translate = EnsureTranslate(LyricText);
            translate.Y = 8;
            LyricText.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = EaseOut });
            translate.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = EaseOut });
        };
        LyricText.BeginAnimation(OpacityProperty, fadeOut);
    }

    private void OnNowPlaying(string title) => Dispatcher.BeginInvoke(new Action(() =>
    {
        var hasTitle = !string.IsNullOrWhiteSpace(title);
        _nowTitle = hasTitle ? title : "";
        NowPlayingText.Text = hasTitle ? "♪ " + title : (_service.IsRunning ? "正在播放" : "等待播放…");
        SongTitleText.Text = hasTitle ? title : "";
    }));

    private Task<bool> OnConfirmRestart(string message)
    {
        return Dispatcher.InvokeAsync(() =>
            ShowConfirmAsync("需要重启网易云音乐", message, "重启", "取消")).Task.Unwrap();
    }

    // ================= 参数 =================

    private void ParamSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        UpdateParamLabels();

    private void UpdateParamLabels()
    {
        if (LeadValue is null || PreValue is null || UnitsValue is null || InterludeValue is null) return;
        if (LeadSlider is null || PreSlider is null || UnitsSlider is null || InterludeSlider is null) return;
        LeadValue.Text = $"{LeadSlider.Value:F0} ms";
        PreValue.Text = $"{PreSlider.Value:F0} ms";
        UnitsValue.Text = $"{UnitsSlider.Value:F1} 字";
        InterludeValue.Text = $"{InterludeSlider.Value:F1} ×";
    }

    private void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        _settings.PageLeadMs = (int)Math.Round(LeadSlider.Value);
        _settings.PrePushLeadMs = (int)Math.Round(PreSlider.Value);
        _settings.StaticMaxUnits = Math.Round(UnitsSlider.Value, 1);
        _settings.InterludeFactor = Math.Round(InterludeSlider.Value, 1);
        _settings.Save();
        Log("[ok] 参数已保存，对新歌词立即生效");
        ShowToast("参数已保存，对新歌词立即生效");
    }

    private void ResetBtn_Click(object sender, RoutedEventArgs e)
    {
        var defaults = new DisplaySettings();
        LeadSlider.Value = defaults.PageLeadMs;
        PreSlider.Value = defaults.PrePushLeadMs;
        UnitsSlider.Value = defaults.StaticMaxUnits;
        InterludeSlider.Value = defaults.InterludeFactor;
        ShowToast("已恢复默认值，点击“保存参数”后生效");
    }

    // ================= 日志 =================

    private void OnServiceLog(string line)
    {
        WriteFileLog(line);
        if (Dispatcher.CheckAccess()) AddLog(line);
        else Dispatcher.BeginInvoke(new Action(() => AddLog(line)));
    }

    private void Log(string line) => OnServiceLog(line);

    private static void WriteFileLog(string line)
    {
        try
        {
            var path = System.IO.Path.Combine(AppContext.BaseDirectory, "halolyric.log");
            var info = new System.IO.FileInfo(path);
            if (info.Exists && info.Length > 2_000_000)
                System.IO.File.Move(path, path + ".old", overwrite: true);
            System.IO.File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {line}\r\n");
        }
        catch { }
    }

    private void AddLog(string line)
    {
        var level = line.Contains("[错误]") ? "err"
            : line.Contains("[warn]") || line.StartsWith("[重试]") ? "warn"
            : line.StartsWith("[ok]") ? "ok"
            : line.StartsWith("[dbg]") ? "dbg"
            : "info";

        var item = new LogItem(DateTime.Now.ToString("HH:mm:ss"), line, level);
        _logs.Add(item);
        if (_logs.Count > 2500) _logs.RemoveAt(0);
        LogEmpty.Visibility = Visibility.Collapsed;

        if (_logAutoScroll) _logScroll?.ScrollToEnd();
    }

    private void LogScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_logScroll is null) return;
        if (Math.Abs(e.ExtentHeightChange) > 0.01)
        {
            if (_logAutoScroll) _logScroll.ScrollToEnd();
            return;
        }
        if (Math.Abs(e.VerticalChange) > 0.01)
            _logAutoScroll = _logScroll.VerticalOffset >= _logScroll.ScrollableHeight - 24;
    }

    private void ClearLogBtn_Click(object sender, RoutedEventArgs e)
    {
        _logs.Clear();
        LogEmpty.Visibility = Visibility.Visible;
        _logAutoScroll = true;
    }

    private void OpenLogBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = System.IO.Path.Combine(AppContext.BaseDirectory, "halolyric.log");
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log("[错误] 打开日志文件失败：" + ex.Message);
        }
    }

    // ================= 关于 =================

    private void GithubBtn_Click(object sender, RoutedEventArgs e) =>
        OpenUrl("https://github.com/Ausfia/HaloPixelLyric");

    private void ReleasesBtn_Click(object sender, RoutedEventArgs e) =>
        OpenUrl("https://github.com/Ausfia/HaloPixelLyric/releases");

    private void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Log("[错误] 打开链接失败：" + ex.Message); }
    }

    // ================= Toast / 对话框 =================

    private async void ShowToast(string message)
    {
        _toastCts?.Cancel();
        var cts = new CancellationTokenSource();
        _toastCts = cts;

        ToastText.Text = message;
        Toast.Visibility = Visibility.Visible;
        Toast.Opacity = 0;
        var translate = EnsureTranslate(Toast);
        translate.Y = -8;
        Toast.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = EaseOut });
        translate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(-8, 0, TimeSpan.FromMilliseconds(240)) { EasingFunction = EaseOut });

        try { await Task.Delay(1900, cts.Token); }
        catch { return; }

        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(240));
        fade.Completed += (_, _) =>
        {
            if (_toastCts == cts) Toast.Visibility = Visibility.Collapsed;
        };
        Toast.BeginAnimation(OpacityProperty, fade);
    }

    private Task<bool> ShowConfirmAsync(string title, string message, string okText = "确定", string cancelText = "取消")
    {
        _confirmTcs?.TrySetResult(false);
        var tcs = new TaskCompletionSource<bool>();
        _confirmTcs = tcs;

        DialogTitle.Text = title;
        DialogMessage.Text = message;
        DialogOk.Content = okText;
        DialogCancel.Content = cancelText;
        DialogOverlay.Visibility = Visibility.Visible;
        DialogOverlay.Opacity = 0;
        DialogScale.ScaleX = DialogScale.ScaleY = 0.94;

        DialogOverlay.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)) { EasingFunction = EaseOut });
        var scaleAnim = new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = EaseOut };
        DialogScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
        DialogScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);

        return tcs.Task;
    }

    private void DialogOk_Click(object sender, RoutedEventArgs e) => CloseDialog(true);

    private void DialogCancel_Click(object sender, RoutedEventArgs e) => CloseDialog(false);

    private void CloseDialog(bool result)
    {
        var tcs = _confirmTcs;
        _confirmTcs = null;
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(130));
        fade.Completed += (_, _) => DialogOverlay.Visibility = Visibility.Collapsed;
        DialogOverlay.BeginAnimation(OpacityProperty, fade);
        tcs?.TrySetResult(result);
    }

    // ================= 窗口控制 =================

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

    private void MinBtn_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaxBtn_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void UpdateMaxButton()
    {
        var maximized = WindowState == WindowState.Maximized;
        MaxBtn.Content = maximized ? "" : "";
        MaxBtn.ToolTip = maximized ? "还原" : "最大化";
    }

    private void ThemeBtn_Click(object sender, RoutedEventArgs e)
    {
        _dark = !_dark;
        ThemeManager.Apply(_dark);
        WindowEffects.Apply(this, _dark);
        UpdateThemeIcon();
        _settings.Theme = _dark ? "dark" : "light";
        _settings.Save();
        UpdateTrayTheme();
    }

    private void UpdateThemeIcon()
    {
        ThemeBtn.ToolTip = _dark ? "切换到浅色外观" : "切换到深色外观";
        MoonIcon.Visibility = _dark ? Visibility.Collapsed : Visibility.Visible;
        SunIcon.Visibility = _dark ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exitRequested)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        _confirmTcs?.TrySetResult(false);
        _tray.Visible = false;
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _tray.Visible = false;
        _tray.Dispose();
        base.OnClosed(e);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (DialogOverlay.Visibility == Visibility.Visible)
        {
            if (e.Key == Key.Escape) { CloseDialog(false); e.Handled = true; return; }
            if (e.Key == Key.Enter) { CloseDialog(true); e.Handled = true; return; }
        }
        base.OnPreviewKeyDown(e);
    }

    // ================= 托盘 =================

    private Wf.ContextMenuStrip BuildTrayMenu()
    {
        var menu = new Wf.ContextMenuStrip
        {
            ShowImageMargin = false,
            Font = new System.Drawing.Font("Microsoft YaHei UI", 9F),
            Padding = new Wf.Padding(2, 4, 2, 4),
        };

        var show = new Wf.ToolStripMenuItem("显示主界面");
        show.Click += (_, _) => ShowFromTray();

        var toggle = new Wf.ToolStripMenuItem("启动同步");
        toggle.Click += (_, _) =>
        {
            if (_service.IsRunning) _service.Stop();
            else _service.Start();
            UpdateStatus();
        };

        var exit = new Wf.ToolStripMenuItem("退出");
        exit.Click += (_, _) => ExitApp();

        menu.Items.Add(show);
        menu.Items.Add(toggle);
        menu.Items.Add(new Wf.ToolStripSeparator());
        menu.Items.Add(exit);
        return menu;
    }

    private void UpdateTrayTheme()
    {
        var background = _dark
            ? System.Drawing.Color.FromArgb(38, 38, 43)
            : System.Drawing.Color.FromArgb(252, 252, 253);
        var foreground = _dark
            ? System.Drawing.Color.FromArgb(245, 245, 247)
            : System.Drawing.Color.FromArgb(29, 29, 31);

        _trayMenu.BackColor = background;
        _trayMenu.ForeColor = foreground;
        foreach (Wf.ToolStripItem item in _trayMenu.Items)
        {
            item.BackColor = background;
            item.ForeColor = foreground;
        }
        if (_trayExitItem is not null)
            _trayExitItem.ForeColor = System.Drawing.Color.FromArgb(255, 59, 48);
    }

    private async void ExitApp()
    {
        if (_exitRequested) return;
        _exitRequested = true;
        _tray.Visible = false;

        var wasRunning = _service.IsRunning;
        if (wasRunning) _service.Stop();
        if (wasRunning) await Task.Delay(450);

        Application.Current.Shutdown();
    }

    // ================= 工具 =================

    private static TranslateTransform EnsureTranslate(UIElement element)
    {
        if (element.RenderTransform is TranslateTransform transform) return transform;
        var created = new TranslateTransform();
        element.RenderTransform = created;
        return created;
    }

    // ================= 最大化工作区修正（WindowChrome 会外扩 resize border） =================

    private const int WmGetMinMaxInfo = 0x0024;
    private const uint MonitorDefaultToNearest = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmGetMinMaxInfo) return IntPtr.Zero;

        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero) return IntPtr.Zero;

        var info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref monitorInfo)) return IntPtr.Zero;

        // 用工作区（排除任务栏）作为最大化边界
        info.MaxPosition.X = monitorInfo.Work.Left - monitorInfo.Monitor.Left;
        info.MaxPosition.Y = monitorInfo.Work.Top - monitorInfo.Monitor.Top;
        info.MaxSize.X = monitorInfo.Work.Right - monitorInfo.Work.Left;
        info.MaxSize.Y = monitorInfo.Work.Bottom - monitorInfo.Work.Top;
        Marshal.StructureToPtr(info, lParam, false);
        handled = true;
        return IntPtr.Zero;
    }

    private static T? FindVisualChild<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (FindVisualChild<T>(child) is { } nested) return nested;
        }
        return null;
    }
}

/// <summary>日志条目（时间 / 正文 / 级别）。</summary>
public sealed record LogItem(string Time, string Text, string Level);
