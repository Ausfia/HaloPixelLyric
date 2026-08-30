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
                                    "window.__haloLyric ? JSON.stringify({t: window.__haloLyric.current === null ? '' : window.__haloLyric.current, ms: window.__haloLyric.sentenceMs || 0, at: window.__haloLyric.changedAt || 0}) : null");
                                if (raw is not null)
                                {
                                    string sentence = "";
                                    long sentenceMs = 0, changedAt = 0;
                                    try
                                    {
                                        using var doc = JsonDocument.Parse(raw);
                                        sentence = doc.RootElement.GetProperty("t").GetString() ?? "";
                                        sentenceMs = doc.RootElement.GetProperty("ms").GetInt64();
                                        changedAt = doc.RootElement.GetProperty("at").GetInt64();
                                    }
                                    catch { }

                                    if (sentence.Length > 0)
                                    {
                                        var trimmed = sentence;   // 整句不预截断，截断在分页后进行
                                        if (trimmed != lastSentence)
                                        {
                                            lastSentence = trimmed;
                                            pendingPages = LyricLogic.SplitLyricPages(trimmed, Settings);
                                            pageIndex = 0;
                                            if (pendingPages.Count > 1)
                                                Log?.Invoke($"[分句] 「{trimmed}」→ {pendingPages.Count} 页（句长 {sentenceMs}ms）");

                                            var base0 = changedAt > 0 ? changedAt : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
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

                                    if (pendingPages is not null)
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
                            catch (ObjectDisposedException) { break; }
                            catch (Exception) { }
                            try { await Task.Delay(60, sessionCts.Token); } catch (OperationCanceledException) { break; }
                        }
                    }, sessionCts.Token);

                    Log?.Invoke("[ok] 轮询已启动，等待歌词...");
                    await cdp.Disconnected;
                    sessionCts.Cancel();
                    try { await pollTask; } catch { }
                    if (!ct.IsCancellationRequested)
                    {
                        Log?.Invoke("[warn] CDP 连接断开，1 秒后重连...");
                        await Task.Delay(1000, ct);
                    }
                }
                catch (Exception ex)
                {
                    Log?.Invoke($"[warn] CDP 异常: {ex.Message}，1 秒后重试...");
                    await Task.Delay(1000, ct);
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
