using HaloPixelLyric.Hid;
using HaloPixelLyric.Hid.Models;

namespace HaloPixelLyric;

/// <summary>SMTC 播放状态 → 曲库匹配/歌词 → 歌曲时间轴调度 → USB HID。</summary>
public sealed class SmtcLyricService : IPlaybackSyncService
{
    private CancellationTokenSource? _cts;
    public DisplaySettings Settings { get; set; } = new();
    // 结束收尾前保持运行状态，防止新旧服务交替写入 HID。
    public bool IsRunning => _cts is not null;
    public event Action<string>? Log;
    public event Action<string>? LyricPushed;
    public event Action<string>? NowPlayingChanged;
    public event Action? RunningChanged;

    public void Start()
    {
        if (IsRunning) return;
        _cts = new CancellationTokenSource();
        RunningChanged?.Invoke();
        var token = _cts.Token;
        _ = Task.Run(() => RunAsync(token));
    }

    public void Stop() => _cts?.Cancel();

    private async Task RunAsync(CancellationToken ct)
    {
        var device = new HaloPixelDevice();
        using var source = new SmtcMediaSource();
        using var lyrics = new NeteaseLyricProvider();
        CancellationTokenSource? fetchCts = null;
        Task<LyricResult>? fetchTask = null;
        List<LyricLine>? lines = null;
        var scheduler = new LyricDisplayScheduler();
        string trackKey = "";
        string reportedSource = "";
        string? lastIssue = null;
        var retryAt = DateTimeOffset.MaxValue;
        var attempts = 0;
        var clockRestored = false;
        var idleSince = DateTimeOffset.MinValue;
        var titleShown = false;
        var connected = false;
        MediaTrack? track = null;

        void ReportIssue(string message)
        {
            if (message != lastIssue) Log?.Invoke(message);
            lastIssue = message;
        }
        void BeginFetch(MediaTrack media)
        {
            CancelFetch();
            fetchCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            fetchTask = lyrics.FetchAsync(media, fetchCts.Token);
            attempts++;
            retryAt = DateTimeOffset.MaxValue;
        }
        void CancelFetch()
        {
            fetchCts?.Cancel();
            fetchCts?.Dispose();
            fetchCts = null;
            // 切歌后仍观察旧任务异常；旧结果永远不会进入新歌曲状态。
            if (fetchTask is not null)
                _ = fetchTask.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            fetchTask = null;
        }
        void PushText(string text)
        {
            var shown = LyricLogic.TrimToByteBudget(text, 55);
            device.ShowText(shown);
            clockRestored = false;
            LyricPushed?.Invoke(shown);
            Log?.Invoke($"[歌词] {shown}");
        }

        try
        {
            if (!device.Initialize())
            {
                Log?.Invoke("[错误] 未找到「花再 Halo PixelBar」HID 设备，请确认音响 USB 已连接。");
                return;
            }
            connected = true;
            device.SetTextLayout(HaloPixelTextLayout.Center);
            await source.InitializeAsync(ct);
            Log?.Invoke("[ok] SMTC 已连接，等待系统媒体会话；无需调试端口或重启播放器。");

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var snapshot = await source.ReadAsync(ct);
                    ct.ThrowIfCancellationRequested();
                    if ((snapshot?.Track.Key ?? "") != trackKey)
                    {
                        CancelFetch();
                        track = snapshot?.Track;
                        trackKey = track?.Key ?? "";
                        lines = null;
                        scheduler.Reset();
                        attempts = 0;
                        titleShown = false;
                        idleSince = DateTimeOffset.MinValue;
                        retryAt = DateTimeOffset.MaxValue;
                        NowPlayingChanged?.Invoke(track?.DisplayTitle ?? "");
                        if (track is not null)
                        {
                            Log?.Invoke($"[歌曲] {track.DisplayTitle}");
                            BeginFetch(track);
                        }
                    }
                    if (snapshot is not null && snapshot.Track.Source != reportedSource)
                    {
                        reportedSource = snapshot.Track.Source;
                        Log?.Invoke($"[SMTC] 媒体来源：{reportedSource}");
                    }
                    if (snapshot is null)
                        ReportIssue("[等待] 没有系统媒体会话，请播放音乐并启用播放器的系统媒体控制/SMTC 功能。");
                    else if (!snapshot.HasTimeline)
                        ReportIssue("[等待] 播放器没有提供有效 SMTC 时间轴，仅显示歌名，无法同步歌词。");
                    else lastIssue = null;

                    if (fetchTask is { IsCompleted: true })
                    {
                        var completed = fetchTask;
                        fetchTask = null;
                        try
                        {
                            var result = await completed;
                            lines = result.Lines;
                            scheduler.Reset();
                            Log?.Invoke($"[歌词源] {result.Description}");
                        }
                        catch (Exception ex) when (!ct.IsCancellationRequested)
                        {
                            Log?.Invoke($"[warn] 歌词获取失败：{ex.Message}");
                            if (attempts < 3) retryAt = DateTimeOffset.UtcNow.AddSeconds(5);
                        }
                    }
                    if (fetchTask is null && track is not null && DateTimeOffset.UtcNow >= retryAt)
                        BeginFetch(snapshot?.Track ?? track);

                    var canSync = snapshot is { Playing: true, HasTimeline: true } && lines is not null;
                    if (canSync)
                    {
                        idleSince = DateTimeOffset.MinValue;
                        var text = scheduler.GetText(lines!, snapshot!.PositionMs, Settings, clockRestored);
                        if (text is not null)
                        {
                            try { PushText(text); }
                            catch { scheduler.Reset(); throw; }
                        }
                    }
                    else
                    {
                        scheduler.Reset();
                        if (snapshot is { Playing: true } && (lines is null || !snapshot.HasTimeline) && !titleShown)
                        {
                            PushText(snapshot.Track.DisplayTitle);
                            titleShown = true;
                            idleSince = DateTimeOffset.MinValue;
                        }
                        if (idleSince == DateTimeOffset.MinValue) idleSince = DateTimeOffset.UtcNow;
                        if (!clockRestored && (DateTimeOffset.UtcNow - idleSince).TotalSeconds >= 3)
                        {
                            device.SetUIModel(HaloPixelUIModel.Clock);
                            clockRestored = true;
                            Log?.Invoke("[空闲] 像素屏已恢复时钟模式");
                        }
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    ReportIssue($"[warn] SMTC/HID 采集异常：{ex.Message}");
                    await Task.Delay(1000, ct);
                }
                await Task.Delay(60, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) { Log?.Invoke($"[错误] 同步服务启动失败：{ex.Message}"); }
        finally
        {
            CancelFetch();
            if (connected)
            {
                try { device.SetUIModel(HaloPixelUIModel.Clock); Log?.Invoke("[ok] 已停止，像素屏恢复时钟模式。"); }
                catch (Exception ex) { Log?.Invoke($"[warn] 恢复时钟失败：{ex.Message}"); }
            }
            NowPlayingChanged?.Invoke("");
            _cts?.Dispose();
            _cts = null;
            RunningChanged?.Invoke();
        }
    }
}
