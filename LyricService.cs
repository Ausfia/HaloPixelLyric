namespace HaloPixelLyric;

/// <summary>两种采集模式的公共服务契约；设备写入及停止收尾由所选服务负责。</summary>
public interface IPlaybackSyncService
{
    DisplaySettings Settings { get; set; }
    bool IsRunning { get; }
    event Action<string>? Log;
    event Action<string>? LyricPushed;
    event Action<string>? NowPlayingChanged;
    event Action? RunningChanged;
    void Start();
    void Stop();
}

/// <summary>在启动时选择 CDP 或 SMTC。旧服务完全退出后才允许选择/启动另一个模式。</summary>
public sealed class LyricService
{
    private readonly Func<PlaybackSource, IPlaybackSyncService> _createService;
    private IPlaybackSyncService? _active;

    public LyricService() : this(mode => mode == PlaybackSource.Smtc
        ? new SmtcLyricService() : new CdpLyricService()) { }

    internal LyricService(Func<PlaybackSource, IPlaybackSyncService> createService) => _createService = createService;

    public DisplaySettings Settings { get; set; } = new();
    public bool IsRunning => _active is not null;
    public PlaybackSource ActiveSource { get; private set; } = PlaybackSource.Cdp;
    public event Action<string>? Log;
    public event Action<string>? LyricPushed;
    public event Action<string>? NowPlayingChanged;
    public event Action? RunningChanged;
    public event Func<string, Task<bool>>? ConfirmRestart;

    public bool SelectSource(PlaybackSource source)
    {
        if (IsRunning || !Enum.IsDefined(source)) return false;
        Settings.PlaybackSource = source;
        return true;
    }

    public void Start()
    {
        if (IsRunning) return;
        ActiveSource = Settings.PlaybackSource == PlaybackSource.Smtc ? PlaybackSource.Smtc : PlaybackSource.Cdp;
        var service = _createService(ActiveSource);
        service.Settings = Settings;
        service.Log += ForwardLog;
        service.LyricPushed += ForwardLyric;
        service.NowPlayingChanged += ForwardTitle;
        void OnChanged()
        {
            if (!service.IsRunning)
            {
                service.Log -= ForwardLog;
                service.LyricPushed -= ForwardLyric;
                service.NowPlayingChanged -= ForwardTitle;
                service.RunningChanged -= OnChanged;
                if (ReferenceEquals(_active, service)) _active = null;
            }
            RunningChanged?.Invoke();
        }
        service.RunningChanged += OnChanged;
        if (service is CdpLyricService cdp) cdp.ConfirmRestart += RequestRestart;
        _active = service;
        Log?.Invoke($"[模式] {(ActiveSource == PlaybackSource.Smtc ? "Windows SMTC" : "网易云 CDP")}");
        service.Start();
    }

    public void Stop() => _active?.Stop();

    private Task<bool> RequestRestart(string message) => ConfirmRestart?.Invoke(message) ?? Task.FromResult(false);
    private void ForwardLog(string message) => Log?.Invoke(message);
    private void ForwardLyric(string text) => LyricPushed?.Invoke(text);
    private void ForwardTitle(string title) => NowPlayingChanged?.Invoke(title);
}
