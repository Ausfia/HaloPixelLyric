using HaloPixelLyric.Hid;
using HaloPixelLyric.Hid.Models;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace HaloPixelLyric;

/// <summary>
/// 歌词桥服务（v1.3 架构：原生进度事件流驱动）。
///
/// 数据链路：watcher v8 订阅 audioplayer.onPlayProgress（native 30Hz 推流）→
/// 轮询读取进度/歌词 → 进度对歌词时间轴定位当前行 → 按歌曲时间轴调度分页显示。
/// 与网易云 UI 状态完全解耦（托盘/关桌面歌词/页面不渲染均正常）；
/// 暂停/seek 由进度流天然正确（无墙钟换算），暂停超时自动恢复像素屏时钟。
/// </summary>
public sealed class LyricService
{
    private CancellationTokenSource? _cts;
    private CdpClient? _activeCdp;
    private string _lastTitle = "";
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };

    public DisplaySettings Settings { get; set; } = new();
    public bool IsRunning => _cts is { IsCancellationRequested: false };

    /// <summary>日志行</summary>
    public event Action<string>? Log;

    /// <summary>当前推上像素屏的歌词行（控制台页大字展示用）</summary>
    public event Action<string>? LyricPushed;

    /// <summary>当前歌曲标题变化（空字符串表示清空）</summary>
    public event Action<string>? NowPlayingChanged;

    /// <summary>运行状态变化</summary>
    public event Action? RunningChanged;

    /// <summary>需要用户确认重启网易云（问题文本, 回调）</summary>
    public event Func<string, Task<bool>>? ConfirmRestart;

    public void Start()
    {
        if (IsRunning) return;
        _cts = new CancellationTokenSource();
        RunningChanged?.Invoke();
        _ = RunAsync(_cts.Token);
    }

    public void Stop()
    {
        if (!IsRunning) return;
        _cts?.Cancel();
        _activeCdp?.Abort();   // 掐断当前 CDP 连接，让主循环立即走到恢复时钟的收尾
        _lastTitle = "";
        NowPlayingChanged?.Invoke("");
    }

    private static string? FindCloudMusicPath()
    {
        foreach (var p in Process.GetProcessesByName("cloudmusic"))
        {
            try { var path = p.MainModule?.FileName; if (!string.IsNullOrEmpty(path)) return path; }
            catch { }
        }
        string[] common =
        [
            @"C:\Program Files\Netease\CloudMusic\cloudmusic.exe",
            @"C:\Program Files (x86)\Netease\CloudMusic\cloudmusic.exe",
            @"D:\Program Files\Netease\CloudMusic\cloudmusic.exe",
            @"D:\Netease\CloudMusic\cloudmusic.exe",
            @"E:\CloudMusic\cloudmusic.exe",
            @"D:\CloudMusic\cloudmusic.exe",
        ];
        return common.FirstOrDefault(File.Exists);
    }

    private async Task<bool> EnsureCloudMusicAsync(CancellationToken ct)
    {
        try
        {
            var probe = await _http.GetAsync($"http://127.0.0.1:{Settings.CdpPort}/json/version", ct);
            if (probe.IsSuccessStatusCode) return true;
        }
        catch { }

        var path = FindCloudMusicPath();
        if (path is null)
        {
            Log?.Invoke("[错误] 未找到网易云音乐，请先安装或手动启动一次后重试。");
            return false;
        }

        var ok = ConfirmRestart is null || await ConfirmRestart(
            $"需要带调试参数重启网易云音乐以启用歌词同步。\r\n检测到：{path}\r\n\r\n现在重启吗？");
        if (!ok)
        {
            Log?.Invoke("[取消] 用户拒绝了重启网易云，服务未启动。");
            return false;
        }

        foreach (var p in Process.GetProcessesByName("cloudmusic"))
        {
            try { p.Kill(); } catch { }
        }
        await Task.Delay(2000, ct);
        Process.Start(new ProcessStartInfo(path, $"--remote-debugging-port={Settings.CdpPort}") { UseShellExecute = true });
        Log?.Invoke($"[info] 已带调试参数重启网易云：{path}，等待其启动...");
        for (var i = 0; i < 12; i++)
        {
            await Task.Delay(1000, ct);
            try
            {
                var probe = await _http.GetAsync($"http://127.0.0.1:{Settings.CdpPort}/json/version", ct);
                if (probe.IsSuccessStatusCode) return true;
            }
            catch { }
        }
        Log?.Invoke("[错误] 网易云已重启但调试口未出现，请重试。");
        return false;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var device = new HaloPixelDevice();
        if (!device.Initialize())
        {
            Log?.Invoke("[错误] 未找到「花再 Halo PixelBar」HID 设备，请确认音响 USB 已连接。");
            _cts = null;
            RunningChanged?.Invoke();
            return;
        }
        Log?.Invoke("[ok] 像素屏已连接");
        device.SetTextLayout(HaloPixelTextLayout.Center);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (!await EnsureCloudMusicAsync(ct))
                {
                    await Task.Delay(3000, ct);
                    continue;
                }

                string? wsUrl = null;
                try { wsUrl = await CdpClient.FindPageTargetWsUrlAsync(_http, Settings.CdpPort); }
                catch { }
                if (wsUrl is null)
                {
                    Log?.Invoke("[等待] 未发现网易云 CDP 页面，1 秒后重试...");
                    await Task.Delay(1000, ct);
                    continue;
                }

                try
                {
                    using var cdp = new CdpClient();
                    var injectGate = new SemaphoreSlim(1, 1);
                    async Task InjectAsync(string tag)
                    {
                        if (!await injectGate.WaitAsync(0)) return;
                        try
                        {
                            var result = await cdp.EvaluateAsync(WatcherScript.Code);
                            Log?.Invoke($"[ok] 事件订阅已注入（{tag}）：{result ?? "无返回"}");
                        }
                        catch (Exception ex) { Log?.Invoke($"[warn] 注入失败（{tag}）: {ex.Message}"); }
                        finally { injectGate.Release(); }
                    }

                    var lastContextChange = DateTime.MinValue;
                    cdp.ContextChanged += () =>
                    {
                        if ((DateTime.UtcNow - lastContextChange).TotalMilliseconds < 400) return;
                        lastContextChange = DateTime.UtcNow;
                        _ = Task.Run(async () => { await Task.Delay(400); await InjectAsync("上下文重建"); });
                    };

                    await cdp.ConnectAsync(wsUrl);
                    _activeCdp = cdp;
                    await cdp.EnableRuntimeAsync();
                    await InjectAsync("启动");

                    using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    var pollTask = Task.Run(() => PollLoopAsync(cdp, device, sessionCts.Token), sessionCts.Token);

                    Log?.Invoke("[ok] 轮询已启动，等待播放...");
                    await cdp.Disconnected;
                    sessionCts.Cancel();
                    try { await pollTask; } catch { }
                    if (!ct.IsCancellationRequested)
                        Log?.Invoke("[warn] CDP 被 watchdog 断开，立即重连...");
                }
                catch (System.Net.WebSockets.WebSocketException) { }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Log?.Invoke($"[warn] CDP 异常: {ex.Message}，快速重试...");
                    await Task.Delay(150, ct);
                }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            try { device.SetUIModel(HaloPixelUIModel.Clock); Log?.Invoke("[ok] 已停止，像素屏恢复时钟模式。"); }
            catch { }
            _cts = null;
            RunningChanged?.Invoke();
        }
    }

    /// <summary>进度驱动的显示调度主循环（歌曲时间轴，暂停/seek 天然正确）。</summary>
    private async Task PollLoopAsync(CdpClient cdp, HaloPixelDevice device, CancellationToken ct)
    {
        // 歌词状态
        List<LyricLine>? lines = null;
        string lyricKey = "";                 // 歌词源标识（playId+lyricFrom+hash），变化时重解析
        string lastLineText = "";             // 当前行文本（换行检测）
        string lastNoLyricTitle = "";         // 无歌词歌曲只提示一次
        string lastPlayId = "";               // 播放会话变化时重置无歌词提示
        // 显示队列（歌曲时间轴毫秒）
        List<(string Text, double AtMs)>? queue = null;
        int queueIndex = 0;
        double lastPushProgress = -1;
        // 暂停回时钟
        var pausedSince = DateTimeOffset.MinValue;
        var clockRestored = false;
        var lastPushAt = DateTimeOffset.MinValue;
        // 取词失败重试
        var lastFrom = "";
        var fromSince = DateTimeOffset.MinValue;
        var lastRetry = DateTimeOffset.MinValue;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var raw = await cdp.EvaluateAsync(
                    "window.__haloLyric ? JSON.stringify({id:window.__haloLyric.playId, p:window.__haloLyric.progress, pl:window.__haloLyric.playing, at:window.__haloLyric.progressAt, y:window.__haloLyric.lyricYrc, l:window.__haloLyric.lyricLrc, ti:window.__haloLyric.songTitle, f:window.__haloLyric.lyricFrom, d:window.__haloLyric.domLines}) : null");
                if (raw is not null)
                {
                    string playId = ""; double progress = -1; bool playing = false; long progressAt = 0;
                    string yrc = "", lrc = "", title = "", from = ""; string[]? dom = null;
                    try
                    {
                        using var doc = JsonDocument.Parse(raw);
                        var r = doc.RootElement;
                        playId = r.GetProperty("id").GetString() ?? "";
                        progress = r.GetProperty("p").GetDouble();
                        playing = r.GetProperty("pl").GetBoolean();
                        progressAt = r.TryGetProperty("at", out var atEl) ? atEl.GetInt64() : 0;
                        yrc = r.TryGetProperty("y", out var y) ? (y.GetString() ?? "") : "";
                        lrc = r.TryGetProperty("l", out var lv) ? (lv.GetString() ?? "") : "";
                        title = r.TryGetProperty("ti", out var t) ? (t.GetString() ?? "") : "";
                        from = r.TryGetProperty("f", out var f) ? (f.GetString() ?? "") : "";
                        dom = r.TryGetProperty("d", out var d) && d.ValueKind == JsonValueKind.Array
                            ? d.EnumerateArray().Select(x => x.GetString() ?? "").ToArray() : null;
                    }
                    catch { }

                    if (playId.Length > 0 && playId != lastPlayId)
                    {
                        lastPlayId = playId;
                        lastNoLyricTitle = "";
                    }

                    if (title.Length > 0 && !string.Equals(title, _lastTitle, StringComparison.Ordinal))
                    {
                        _lastTitle = title;
                        NowPlayingChanged?.Invoke(title);
                    }

                    var newKey = playId + "|" + from + "|" + (yrc.Length > 0 ? "y" + yrc.Length : "") + (lrc.Length > 0 ? "l" + lrc.Length : "");
                    // 取词失败/挂起自动重试（fetchLyrics 单次调用，网络偶发失败后若无重试将永久卡住）
                    if (lines is null && (from == "api-error" || from == "fetching"))
                    {
                        if (from != lastFrom) { lastFrom = from; fromSince = DateTimeOffset.UtcNow; }
                        else if ((DateTimeOffset.UtcNow - fromSince).TotalMilliseconds > 4000 &&
                                 (DateTimeOffset.UtcNow - lastRetry).TotalMilliseconds > 3000)
                        {
                            lastRetry = DateTimeOffset.UtcNow;
                            _ = cdp.EvaluateAsync("window.__haloLyric.retry && window.__haloLyric.retry()");
                            Log?.Invoke($"[重试] 歌词获取处于 {from}，重新请求");
                        }
                    }
                    else lastFrom = "";
                    if (lines is null || newKey != lyricKey)
                    {
                        lyricKey = newKey;
                        // 歌词源只用 API：yrc 字段优先；lrc 字段用混合解析器
                        // （实测会混装 JSON 元数据行 / yrc 逐字行 / 普通 LRC 行，短路式解析会丢正文）。
                        lines = LyricLogic.ParseYrc(yrc) ?? LyricLogic.ParseMixed(lrc);
                        lastLineText = "";
                        queue = null;
                        if (lines is not null)
                            Log?.Invoke($"[歌词源] {(from.Length > 0 ? from + " " : "")}{(yrc.Length > 0 ? "yrc" : "LRC")} 共 {lines.Count} 行{(title.Length > 0 ? $" · {title}" : "")}");
                        else if (from == "api-empty" && title.Length > 0 && lastNoLyricTitle != title)
                        {
                            // 无歌词的歌：显示一次歌名后安静（不再显示任何行）
                            lastNoLyricTitle = title;
                            lastLineText = title;
                            try { device.ShowText(LyricLogic.TrimToByteBudget(title, 55)); LyricPushed?.Invoke(title); } catch { }
                            Log?.Invoke($"[无歌词] {title}");
                        }
                    }

                    // ===== 暂停/停止/无歌词处理：断流或无词 → 3 秒后回时钟 =====
                    // onPlayProgress 暂停时静默停推（onPause 事件不可靠），progressAt 超时是最可靠的暂停信号；
                    // 无歌词（api-empty）同理：没有可显示的内容，回时钟比停留旧句/显示文案更自然
                    var streamAlive = playing && progress >= 0 && progressAt > 0
                        && (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - progressAt) < 2000;
                    // 无有效词 = API 完成但无行（api-empty 或数据格式解析失败）
                    var noLyric = lines is null && (from == "api-empty" || from == "api-lrc" || from == "api-yrc");
                    if (lines is not null || noLyric)
                    {
                        if (!streamAlive || noLyric)
                        {
                            if (pausedSince == DateTimeOffset.MinValue) pausedSince = DateTimeOffset.UtcNow;
                            if (!clockRestored && (DateTimeOffset.UtcNow - pausedSince).TotalSeconds >= 3)
                            {
                                try { device.SetUIModel(HaloPixelUIModel.Clock); clockRestored = true; Log?.Invoke("[暂停] 已恢复时钟模式"); } catch { }
                            }
                        }
                        else
                        {
                            pausedSince = DateTimeOffset.MinValue;
                            clockRestored = false;
                        }

                        // ===== 当前行定位（进度 → 时间轴；流活跃且有歌词时） =====
                        if (streamAlive && lines is not null)
                        {
                            LyricLine? current = null;
                            foreach (var ln in lines)
                            {
                                if (ln.StartMs <= progress * 1000 + 0.5) current = ln;
                                else break;
                            }
                            if (current is not null && current.Text != lastLineText)
                            {
                                lastLineText = current.Text;
                                // 行切换：构建显示队列（分页，每页时刻 = 行开始 + 占比×行时长 - 提前量）
                                var pages = LyricLogic.SplitLyricPages(current.Text, Settings);
                                var lineDur = current.DurMs > 0 ? current.DurMs : LyricLogic.EstimateHoldMs(current.Text, Settings);
                                var totalUnits = LyricLogic.DisplayUnits(current.Text);
                                queue = new List<(string, double)>();
                                double acc = 0;
                                foreach (var pg in pages)
                                {
                                    // 先记时刻再累加：第一页 = 行开始 - 提前量，后续页按前页时长顺延
                                    // （此前顺序颠倒，第一页被推迟整页时长 = "歌词慢一句"的根源）
                                    queue.Add((pg, current.StartMs + acc - Settings.PageLeadMs));
                                    acc += lineDur * LyricLogic.DisplayUnits(pg) / Math.Max(totalUnits, 1);
                                }
                                queueIndex = 0;
                                if (pages.Count > 1)
                                    Log?.Invoke($"[分句] 「{current.Text}」→ {pages.Count} 页（行 {current.StartMs:F0}ms +{lineDur:F0}ms）");
                            }

                            // ===== 到期推页（进度比对歌曲时间轴） =====
                            if (queue is not null)
                            {
                                var progressMs = progress * 1000;
                                var minHoldOk = lastPushAt == DateTimeOffset.MinValue
                                    || (DateTimeOffset.UtcNow - lastPushAt).TotalMilliseconds >= 600;
                                while (queueIndex < queue.Count && progressMs >= queue[queueIndex].AtMs && minHoldOk)
                                {
                                    var (text, _) = queue[queueIndex];
                                    try
                                    {
                                        device.ShowText(LyricLogic.TrimToByteBudget(text, 55));
                                        LyricPushed?.Invoke(text);
                                        Log?.Invoke($"[歌词]{(queue.Count > 1 ? $"({queueIndex + 1}/{queue.Count})" : "")} {text}");
                                        lastPushAt = DateTimeOffset.UtcNow;
                                    }
                                    catch (Exception ex) { Log?.Invoke($"[warn] HID 写入失败: {ex.Message}"); }
                                    queueIndex++;
                                    if (queueIndex < queue.Count) break;   // 每页至少间隔一个轮询周期
                                }
                                if (queueIndex >= queue.Count) queue = null;
                            }
                            lastPushProgress = progress;
                        }
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (System.Net.WebSockets.WebSocketException) { }
            catch (System.Net.Http.HttpRequestException) { }
            catch (Exception ex)
            {
                Log?.Invoke($"[错误] 轮询未处理异常 {ex.GetType().Name}: {ex.Message}");
            }
            try { await Task.Delay(60, ct); } catch (OperationCanceledException) { break; }
        }
    }
}
