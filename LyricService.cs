using HaloPixelLyric.Hid;
using HaloPixelLyric.Hid.Models;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

namespace HaloPixelLyric;

/// <summary>
/// 歌词桥服务：可启动/停止的常驻循环。
/// 启动时确保网易云带调试参数运行（必要时自动重启网易云），然后注入监听、轮询推屏。
/// </summary>
public sealed class LyricService
{
    private CancellationTokenSource? _cts;
    private CdpClient? _activeCdp;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };

    public DisplaySettings Settings { get; set; } = new();
    public bool IsRunning => _cts is { IsCancellationRequested: false };

    /// <summary>日志行（含歌词推送与诊断信息）</summary>
    public event Action<string>? Log;

    /// <summary>当前推上像素屏的歌词行（控制台页大字展示用）</summary>
    public event Action<string>? LyricPushed;

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
        // 调试口已开
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
        var lastPushed = string.Empty;

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
                            Log?.Invoke($"[ok] 歌词监听已注入（{tag}）：{result ?? "无返回"}");
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
                    var lastSentence = string.Empty;
                    var lastAnchor = 0L;          // 最近一次句开始的墙钟锚（Unix ms）
                    var prePushedFrom = string.Empty;   // 预推发起时 current 停留的旧句（等待正式切换期间忽略它）
                    var prePushStreak = 0;              // 预推连续命中计数（降噪统计用）
                    var lastMissedSentence = string.Empty;  // 预推错过诊断去重
                    var lastPushAt = DateTimeOffset.MinValue;
                    List<string>? pendingPages = null;
                    var pageIndex = 0;
                    var pageDeadlines = new List<long>();
                    var pageMinHoldMs = 1200;

                    var pollTask = Task.Run(async () =>
                    {
                        while (!sessionCts.IsCancellationRequested)
                        {
                            try
                            {
                                var raw = await cdp.EvaluateAsync(
                                    "window.__haloLyric ? JSON.stringify({t: window.__haloLyric.current === null ? '' : window.__haloLyric.current, ms: window.__haloLyric.sentenceMs || 0, at: window.__haloLyric.changedAt || 0, nx: window.__haloLyric.nextText === null ? '' : window.__haloLyric.nextText}) : null");
                                if (raw is not null)
                                {
                                    string sentence = "";
                                    string nextText = "";
                                    long sentenceMs = 0, changedAt = 0;
                                    try
                                    {
                                        using var doc = JsonDocument.Parse(raw);
                                        sentence = doc.RootElement.GetProperty("t").GetString() ?? "";
                                        sentenceMs = doc.RootElement.GetProperty("ms").GetInt64();
                                        changedAt = doc.RootElement.GetProperty("at").GetInt64();
                                        nextText = doc.RootElement.GetProperty("nx").GetString() ?? "";
                                    }
                                    catch { }

                                    if (sentence.Length > 0)
                                    {
                                        var trimmed = sentence;   // 整句不预截断，截断在分页后进行
                                        if (trimmed != lastSentence)
                                        {
                                            if (prePushedFrom.Length > 0 && trimmed == prePushedFrom)
                                            {
                                                // 预推等待期：current 仍停留在旧句，忽略（避免把预推队列拉回去）
                                            }
                                            else
                                            {
                                            lastSentence = trimmed;
                                            var anchorNow = changedAt > 0 ? changedAt : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                                            lastAnchor = anchorNow;
                                            if (prePushedFrom.Length > 0)
                                            {
                                                // 走到常规路径且此前有未命中的预推（切到了别的句/拖动）：报落空并重置统计
                                                Log?.Invoke($"[预推落空] 已回退正式路径（连续命中 {prePushStreak} 句后中断）");
                                                prePushStreak = 0;
                                            }
                                            prePushedFrom = string.Empty;   // 常规新句处理：清除预推状态
                                            pendingPages = LyricLogic.SplitLyricPages(trimmed, Settings);
                                            pageIndex = 0;
                                            if (pendingPages.Count > 1)
                                                Log?.Invoke($"[分句] 「{trimmed}」→ {pendingPages.Count} 页（句长 {sentenceMs}ms）");

                                            var base0 = anchorNow;
                                            var totalUnits = LyricLogic.DisplayUnits(trimmed);
                                            double acc = 0;
                                            pageDeadlines.Clear();
                                            var estimateMs = LyricLogic.EstimateHoldMs(trimmed, Settings);
                                            var effectiveMs = sentenceMs;
                                            if (sentenceMs > estimateMs * Settings.InterludeFactor)
                                            {
                                                Log?.Invoke($"[间奏] 官方时长 {sentenceMs}ms 异常（估算 {estimateMs}ms），回退估算值");
                                                effectiveMs = 0;
                                            }
                                            pageMinHoldMs = (int)Math.Clamp(
                                                effectiveMs > 0 ? effectiveMs / (double)pendingPages.Count : 1200.0, 600, 1200);
                                            foreach (var pg in pendingPages)
                                            {
                                                if (effectiveMs > 0 && totalUnits > 0)
                                                    acc += effectiveMs * LyricLogic.DisplayUnits(pg) / totalUnits;
                                                else
                                                    acc += estimateMs * LyricLogic.DisplayUnits(pg) / Math.Max(totalUnits, 1);
                                                pageDeadlines.Add(base0 + (long)acc - Settings.PageLeadMs);
                                            }
                                            }
                                        }
                                    }

                                    // ===== 预推命中检测 =====
                                    // 正式切换到了预推的句子（trimmed == lastSentence 不会触发上面的新句分支）：
                                    // 不重排不重推（第一页已在预推时上屏），仅用真实切换时刻更新锚点，供下一句预推更准。
                                    // 命中不逐句打日志（太吵），连续命中每 20 句报一次统计。
                                    if (prePushedFrom.Length > 0 && sentence.Length > 0 && sentence == lastSentence)
                                    {
                                        prePushedFrom = string.Empty;
                                        if (changedAt > 0) lastAnchor = changedAt;
                                        // 预推命中：此刻拿到该句真实时长，用真实锚点重排剩余页时刻
                                        //（预推时后续页按估算句长排的，长句估算偏短会翻早）
                                        // deadline 语义：deadline[j] = 推第 j+2 页的时刻 = baseAt + 累计时长 - 提前量，
                                        // 必须从第 1 页起全量重填（之前误把已推页时长重复累加，导致翻页滞后/卡死）
                                        if (pendingPages is not null && pageIndex < pendingPages.Count && sentenceMs > 0)
                                        {
                                            var baseAt = changedAt > 0 ? changedAt : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                                            var totalUnits = LyricLogic.DisplayUnits(sentence);
                                            pageDeadlines.Clear();
                                            double acc = 0;
                                            for (var i = 0; i < pendingPages.Count; i++)
                                            {
                                                acc += sentenceMs * LyricLogic.DisplayUnits(pendingPages[i]) / totalUnits;
                                                pageDeadlines.Add(baseAt + (long)acc - Settings.PageLeadMs);
                                            }
                                        }
                                        prePushStreak++;
                                        if (prePushStreak % 20 == 0)
                                            Log?.Invoke($"[预推] 已连续命中 {prePushStreak} 句");
                                    }

                                    // ===== 下一句第一页预推 =====
                                    // 当前句所有页已推完、拿到下一句文本时：在「下一句开始时刻 − 提前量」主动推它的第一页，
                                    // 抵消每句首屏的检测+动画延迟（预测锚 = 本句开始 + 句长）。
                                    // 暂停/拖动防护：超过预测时刻 +1.5s 当前句仍未切换则放弃，等正式切换覆盖。
                                    if (pendingPages is null && sentenceMs > 0 && lastAnchor > 0
                                        && nextText.Length > 0 && nextText != lastSentence)
                                    {
                                        var nextAt = lastAnchor + sentenceMs;
                                        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                                        // 提前量对短句自适应：间隔小于预设提前量时钳制为本句间隔，
                                        // 否则窗口整体落在过去必然错过（如编曲信息行间隔仅百余毫秒）
                                        var leadMs = Math.Min(Settings.PrePushLeadMs, sentenceMs);
                                        if (nowMs >= nextAt - leadMs && nowMs <= nextAt + 1500)
                                        {
                                            var pages = LyricLogic.SplitLyricPages(nextText, Settings);
                                            // 以预测时刻为基准排后续页（下一句时长未知，按字数估算）
                                            var totalUnits = LyricLogic.DisplayUnits(nextText);
                                            var estimateMs = LyricLogic.EstimateHoldMs(nextText, Settings);
                                            double acc = 0;
                                            pageDeadlines.Clear();
                                            pageMinHoldMs = (int)Math.Clamp(estimateMs / (double)pages.Count, 600, 1200);
                                            foreach (var pg in pages)
                                            {
                                                acc += estimateMs * LyricLogic.DisplayUnits(pg) / Math.Max(totalUnits, 1);
                                                pageDeadlines.Add(nextAt + (long)acc - leadMs);
                                            }
                                            lastSentence = nextText;   // 正式切换到来时 trimmed==lastSentence，直接沿用此队列
                                            prePushedFrom = sentence;  // 记住发起预推时的旧句，等待期内忽略它
                                            // 单页句没有后续页要管：队列直接置空，否则推页分支永远无法释放它，
                                            // 导致下一句无法预推（交替跳句 bug）
                                            pendingPages = pages.Count > 1 ? pages : null;
                                            pageIndex = 1;             // 第一页马上推，后续页按上面排的 deadline
                                            PushToDevice(pages[0]);
                                            lastPushAt = DateTimeOffset.UtcNow;
                                            Log?.Invoke($"[预推] {pages[0]}");
                                        }
                                        else if (nowMs > nextAt + 1500 && lastMissedSentence != nextText)
                                        {
                                            // 诊断：预推错过（每句只报一次），定位是哪条条件挡路
                                            lastMissedSentence = nextText;
                                            Log?.Invoke($"[dbg-预推错过] 窗口已过 {nowMs - nextAt}ms | anchorDiff={(changedAt > 0 ? changedAt - lastAnchor : -999)}ms sentenceMs={sentenceMs} | next={(nextText.Length > 12 ? nextText[..12] : nextText)}");
                                        }
                                    }

                                    if (pendingPages is not null)
                                    {
                                        // 防御：页索引越界即视为本句播完，释放队列
                                        if (pageIndex >= pendingPages.Count)
                                        {
                                            pendingPages = null;
                                        }
                                        else
                                        {
                                        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                                        var due = pageIndex == 0
                                            || (pageIndex - 1 < pageDeadlines.Count && nowMs >= pageDeadlines[pageIndex - 1]);
                                        var minHoldOk = lastPushAt == DateTimeOffset.MinValue
                                            || (DateTimeOffset.UtcNow - lastPushAt).TotalMilliseconds >= pageMinHoldMs;
                                        if (due && minHoldOk)
                                        {
                                            var page = pendingPages[pageIndex];
                                            PushToDevice(page);
                                            Log?.Invoke($"[歌词]{(pendingPages.Count > 1 ? $"({pageIndex + 1}/{pendingPages.Count})" : "")} {page}");
                                            lastPushed = page;
                                            lastPushAt = DateTimeOffset.UtcNow;
                                            pageIndex++;
                                            if (pageIndex >= pendingPages.Count) pendingPages = null;
                                        }
                                        }
                                    }
                                }
                            }
                            catch (OperationCanceledException) { break; }
                            catch (ObjectDisposedException) { break; }
                            catch (System.Net.WebSockets.WebSocketException) { /* 连接断开瞬态，由 Disconnected 驱动重连 */ }
                            catch (System.Net.Http.HttpRequestException) { /* 同上 */ }
                            catch (Exception ex)
                            {
                                // 程序错误必须暴露——曾经的裸吞把页索引越界埋成了"隔句跳"的隐性 bug
                                Log?.Invoke($"[错误] 轮询未处理异常 {ex.GetType().Name}: {ex.Message}");
                            }
                            try { await Task.Delay(60, sessionCts.Token); } catch (OperationCanceledException) { break; }
                        }
                    }, sessionCts.Token);

                    Log?.Invoke("[ok] 轮询已启动，等待歌词...");
                    await cdp.Disconnected;
                    sessionCts.Cancel();
                    try { await pollTask; } catch { }
                    if (!ct.IsCancellationRequested)
                    {
                        // 网易云 CEF 有 ~31s 周期的 watchdog 踢调试连接，属不可抗力：
                        // 立即重连（无固定等待），恢复窗口 ≈0.2s
                        Log?.Invoke("[warn] CDP 被 watchdog 断开，立即重连...");
                    }
                }
                catch (Exception ex)
                {
                    Log?.Invoke($"[warn] CDP 异常: {ex.Message}，快速重试...");
                    await Task.Delay(150, ct);   // 服务端释放旧连接的短窗口，快速退避
                }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            // 退出时恢复时钟模式（使用最后推过的设备）
            try { device.SetUIModel(HaloPixelUIModel.Clock); Log?.Invoke("[ok] 已停止，像素屏恢复时钟模式。"); }
            catch { }
            _cts = null;
            RunningChanged?.Invoke();
        }

        void PushToDevice(string text)
        {
            try
            {
                device.SetTextLayout(HaloPixelTextLayout.Center);
                device.ShowText(LyricLogic.TrimToByteBudget(text, 55));
                LyricPushed?.Invoke(text);
            }
            catch (Exception ex) { Log?.Invoke($"[warn] HID 写入失败: {ex.Message}"); }
        }
    }
}
