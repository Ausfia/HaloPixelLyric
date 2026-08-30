using HaloPixelLyric;
using HaloPixelLyric.Hid;
using HaloPixelLyric.Hid.Models;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

// 按字节预算截断（保持字符完整），避免超出 HID 64 字节报文
static string TrimToByteBudget(string text, int maxBytes)
{
    if (Encoding.UTF8.GetByteCount(text) <= maxBytes) return text;
    var sb = new StringBuilder();
    var bytes = 0;
    foreach (var ch in text)
    {
        var b = Encoding.UTF8.GetByteCount([ch]);
        if (bytes + b > maxBytes) break;
        sb.Append(ch);
        bytes += b;
    }
    return sb.ToString();
}

// ---- 显示节奏参数（按体感可调） ----
// 混合显示：屏幕放得下的句子居中静态；放不下的用滚动滚一遍看全句
// 显示单位：全角字符=1，半角=0.5；≤StaticMaxUnits 判定为静态
// 实测屏宽：半角 32 字符（用户实测），全角 16 字（32×0.5），故取 16
const double StaticMaxUnits = 16.0;
// 静态句驻留：基础 + 每字符附加，上限
const int BaseHoldMs = 1800;
const int PerCharHoldMs = 80;
const int StaticMaxHoldMs = 3200;
// 滚动句驻留（等它滚完）：基础 + 每全角字符耗时
const int ScrollBaseMs = 900;
const int ScrollMsPerUnit = 260;
const int ScrollMaxHoldMs = 8000;

static double DisplayUnits(string text)
{
    double units = 0;
    foreach (var c in text) units += c >= 0x2E80 ? 1 : 0.5;
    return units;
}

static bool IsStaticText(string text) => DisplayUnits(text) <= StaticMaxUnits;

static int EstimateHoldMs(string text) =>
    IsStaticText(text)
        ? Math.Min(StaticMaxHoldMs, BaseHoldMs + PerCharHoldMs * text.Length)
        : Math.Min(ScrollMaxHoldMs, ScrollBaseMs + (int)(ScrollMsPerUnit * DisplayUnits(text)));

// ---- 长句分页 ----

/// <summary>
/// 长句按屏宽分页：每页 ≤ maxUnits 显示单位；
/// 切点均衡化（两页长度接近，避免孤字尾页）：目标 = min(长度/2, 屏宽)，
/// 在目标 ±7 字符窗口内优先选空格/标点（留在上一页尾部），无则按目标硬切。
/// </summary>
static List<string> SplitLyricPages(string text, double maxUnits)
{
    char[] splitChars = [' ', '　', '，', '。', '、', '！', '？', '!', '?', ',', '.', '·', '—', '…', '（', '）', '(', ')', '"', '"'];
    var pages = new List<string>();
    var remaining = text;
    while (DisplayUnits(remaining) > maxUnits && remaining.Length > 1)
    {
        // 本页从剩余文本开头实际能放下的字符数（全半角混合折算，半角=0.5单位）
        var charsFit = 0;
        double acc = 0;
        foreach (var ch in remaining)
        {
            acc += ch >= 0x2E80 ? 1 : 0.5;
            if (acc > maxUnits) break;
            charsFit++;
        }
        if (charsFit < 1) charsFit = 1;
        var targetChar = Math.Clamp((remaining.Length + 1) / 2, 1, charsFit);
        var best = -1;
        for (var offset = 0; offset <= 7 && best < 0; offset++)
        {
            int[] candidates = offset == 0 ? [targetChar] : [targetChar - offset, targetChar + offset];
            foreach (var pos in candidates)
            {
                if (pos > 0 && pos < remaining.Length && splitChars.Contains(remaining[pos - 1]))
                {
                    best = pos;
                    break;
                }
            }
        }
        if (best < 0 || best >= remaining.Length) best = targetChar;
        pages.Add(remaining[..best].TrimEnd());
        remaining = remaining[best..].TrimStart();
    }
    if (remaining.Length > 0) pages.Add(remaining);
    return pages;
}

Console.WriteLine("HaloPixelLyric — CDP 歌词桥（网易云 → 花再 PixelBar）  Ctrl+C 退出");

// 辅助模式：--clock 仅把像素屏恢复为时钟后退出
if (args.Length > 0 && args[0] == "--clock")
{
    var clockDevice = new HaloPixelDevice();
    if (clockDevice.Initialize()) { clockDevice.SetUIModel(HaloPixelUIModel.Clock); Console.WriteLine("[ok] 像素屏已恢复时钟模式"); }
    else Console.WriteLine("[错误] 未找到像素屏设备");
    return 0;
}

// 辅助模式：--calibrate 屏宽标定（默认汉字；--calibrate en 标定半角字符容量）
if (args.Length > 0 && args[0] == "--calibrate")
{
    var english = args.Length > 1 && args[1].Equals("en", StringComparison.OrdinalIgnoreCase);
    var cal = new HaloPixelDevice();
    if (!cal.Initialize()) { Console.WriteLine("[错误] 未找到像素屏设备"); return 1; }
    cal.SetTextLayout(HaloPixelTextLayout.Center);
    if (english)
    {
        Console.WriteLine("英文屏宽标定：依次显示 20~40 个小写字母（步进2），每个停 3.5 秒。");
        Console.WriteLine("请盯屏幕，记住「最后一个完整显示、没有被截断」的字符数。");
        for (var n = 20; n <= 40; n += 2)
        {
            var text = new string('n', n);
            try { cal.ShowText(text); }
            catch (Exception ex) { Console.WriteLine($"[warn] 发送失败: {ex.Message}"); }
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 屏幕上现在是 {n} 个半角字符");
            Thread.Sleep(3500);
        }
    }
    else
    {
        Console.WriteLine("屏宽标定：依次显示 4~14 个汉字（居中静态），每个停 3.5 秒。");
        Console.WriteLine("请盯屏幕，记住「最后一个完整显示、没有被截断」的字数，之后告诉程序作者。");
        for (var n = 4; n <= 14; n++)
        {
            var text = new string('测', n);
            try { cal.ShowText(text); }
            catch (Exception ex) { Console.WriteLine($"[warn] 发送失败: {ex.Message}"); }
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 屏幕上现在是 {n} 个汉字");
            Thread.Sleep(3500);
        }
    }
    Console.WriteLine("标定结束。");
    return 0;
}

// 辅助模式：--test-layout 逐个发送 EF 显示模式字节，观察屏幕效果找"上下进出"等动画
// 已知：mode=0x00 静态对齐(sub 0左/1中/2右/3拉伸)；mode=0x01 动态(sub 0左→右/1右→左滚动)
if (args.Length > 0 && args[0] == "--test-layout")
{
    var probe = new HaloPixelDevice();
    if (!probe.Initialize()) { Console.WriteLine("[错误] 未找到像素屏设备"); return 1; }
    Console.WriteLine("布局字节探测：每 4 秒发一个模式组合并推测试文本，观察屏幕效果。Ctrl+C 中止。");
    Console.WriteLine("已知：mode=00 静态(sub 0/1/2/3=左/中/右/拉伸)，mode=01 动态(sub 0/1=左到右/右到左滚动)");
    foreach (var (mode, sub) in new (byte, byte)[] { (0x00, 0x01), (0x01, 0x02), (0x01, 0x03), (0x01, 0x04), (0x01, 0x05), (0x02, 0x00), (0x02, 0x01), (0x03, 0x00) })
    {
        var payload = new byte[] { 0x01, 0xf0, 0xb4, 0xc8, 0x00, 0x02, mode, sub, 0xff };
        var packet = new List<byte> { 0x2e, 0xaa, 0xec, 0xef, 0x00, 0x09 };
        packet.AddRange(payload);
        int acc = 124;
        foreach (var b in payload) acc += b + 2;
        packet.Add((byte)(acc % 256));
        packet.Add(0x00);
        try
        {
            using var stream = probe.CurrentDevice?.Open();
            stream?.Write(HidPacketBuilder.Build(packet));
            probe.ShowText(TrimToByteBudget("这是一句测试滚动形态用的超长歌词", 55));
        }
        catch (Exception ex) { Console.WriteLine($"[warn] 发送失败: {ex.Message}"); }
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 已发送 mode=0x{mode:X2} sub=0x{sub:X2}  ← 现在屏幕上是这个效果");
        Thread.Sleep(4000);
    }
    Console.WriteLine("探测完毕。");
    return 0;
}

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
var device = new HaloPixelDevice();
if (!device.Initialize())
{
    Console.WriteLine("[错误] 未找到「花再 Halo PixelBar」HID 设备，请确认音响 USB 已连接后重试。");
    return 1;
}
Console.WriteLine("[ok] 像素屏已连接");

// 文本布局：滚动模式（长句由设备固件负责滚动）
device.SetTextLayout(HaloPixelTextLayout.ScrollRightToLeft);

using var exit = new ManualResetEventSlim(false);
Console.CancelKeyPress += (_, e) => { e.Cancel = true; exit.Set(); };

while (!exit.IsSet)
{
    string? wsUrl = null;
    try { wsUrl = await CdpClient.FindPageTargetWsUrlAsync(http, 9222); }
    catch { }

    if (wsUrl is null)
    {
        Console.WriteLine("[等待] 未发现网易云 CDP 页面。请用带 --remote-debugging-port=9222 参数启动网易云。3 秒后重试...");
        exit.Wait(3000);
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
                Console.WriteLine($"[ok] 歌词监听已注入（{tag}）：{result ?? "无返回"}");
            }
            catch (Exception ex) { Console.WriteLine($"[warn] 注入失败（{tag}）: {ex.Message}"); }
            finally { injectGate.Release(); }
        }

        // 页面内部导航会销毁 JS 上下文（WebSocket 不断），需要重新注入
        var lastContextChange = DateTime.MinValue;
        cdp.ContextChanged += () =>
        {
            if ((DateTime.UtcNow - lastContextChange).TotalMilliseconds < 400) return;
            lastContextChange = DateTime.UtcNow;
            _ = Task.Run(async () => { await Task.Delay(400); await InjectAsync("上下文重建"); });
        };

        await cdp.ConnectAsync(wsUrl);
        await cdp.EnableRuntimeAsync();
        await InjectAsync("启动");

        // 轮询 + 显示节奏控制：
        // 新行只进 pending；等上一句"驻留时长"走完才推屏；期间来的更新行直接覆盖
        // pending（积压时自然跳句显示最新，宁可跳句也不滞后）。
        using var sessionCts = new CancellationTokenSource();
        var lastSentence = string.Empty;      // 句子级判重（唱这句期间不重建分页）
        var lastPushed = string.Empty;        // 上一页文本（用于计算驻留时长）
        var lastPushAt = DateTimeOffset.MinValue;
        List<string>? pendingPages = null;    // 当前句的分页队列
        var pageIndex = 0;
        var pageDeadlines = new List<long>(); // 每页的切换时刻（Unix ms，基于网易云的本句开始时刻）
        var pageMinHoldMs = 1200;             // 每页最小驻留（快节奏歌曲自动缩短）

        void PushToDevice(string text)
        {
            try
            {
                device.SetTextLayout(HaloPixelTextLayout.Center);
                device.ShowText(TrimToByteBudget(text, 55));   // 协议兜底：分页后每页 ≤48 字节，此处实际不触发
            }
            catch (Exception ex) { Console.WriteLine($"[warn] HID 写入失败: {ex.Message}"); }
        }

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
                            var trimmed = sentence;   // 整句不预截断！截断必须在分页之后（每页天然 ≤16全角/32半角 = ≤48字节）
                            if (trimmed != lastSentence)
                            {
                                // 新句：生成分页队列，并按演唱时长分配每页的切换时刻
                                lastSentence = trimmed;
                                pendingPages = SplitLyricPages(trimmed, StaticMaxUnits);
                                pageIndex = 0;
                                if (pendingPages.Count > 1)
                                    Console.WriteLine($"[分句] 「{trimmed}」→ {pendingPages.Count} 页（句长 {sentenceMs}ms）");

                                var base0 = changedAt > 0 ? changedAt : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                                var totalUnits = DisplayUnits(trimmed);
                                double acc = 0;
                                pageDeadlines.Clear();
                                // 快节奏歌曲：每页最小驻留 = 句长/页数，钳在 [600, 1200]
                                pageMinHoldMs = (int)Math.Clamp(
                                    sentenceMs > 0 ? sentenceMs / (double)pendingPages.Count : 1200.0, 600, 1200);
                                foreach (var pg in pendingPages)
                                {
                                    if (sentenceMs > 0 && totalUnits > 0)
                                        acc += sentenceMs * DisplayUnits(pg) / totalUnits;      // 按字数占比分配演唱时长
                                    else
                                        acc += EstimateHoldMs(pg);                                // 无时间戳：按长度估算
                                    pageDeadlines.Add(base0 + (long)acc);
                                }
                            }
                        }
                    }

                    if (pendingPages is not null)
                    {
                        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                        // 第一页立即推；后续页到各自 deadline 推；每页至少驻留 1200ms
                        var due = pageIndex == 0
                            || (pageIndex - 1 < pageDeadlines.Count && nowMs >= pageDeadlines[pageIndex - 1]);
                        var minHoldOk = lastPushAt == DateTimeOffset.MinValue
                            || (DateTimeOffset.UtcNow - lastPushAt).TotalMilliseconds >= pageMinHoldMs;
                        if (due && minHoldOk)
                        {
                            var page = pendingPages[pageIndex];
                            PushToDevice(page);
                            Console.WriteLine($"[歌词]{(pendingPages.Count > 1 ? $"({pageIndex + 1}/{pendingPages.Count})" : "")} {page}");
                            lastPushed = page;
                            lastPushAt = DateTimeOffset.UtcNow;
                            pageIndex++;
                            if (pageIndex >= pendingPages.Count) pendingPages = null;
                        }
                    }
                }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex) { Console.WriteLine($"[dbg-tick] {ex.GetType().Name}: {ex.Message}"); }
                try { await Task.Delay(60, sessionCts.Token); } catch (OperationCanceledException) { break; }
            }
        }, sessionCts.Token);

        Console.WriteLine("[ok] 轮询已启动，等待歌词...");
        await cdp.Disconnected;      // 阻塞到连接断开（页面关闭/网易云退出）
        sessionCts.Cancel();
        try { await pollTask; } catch { }
        Console.WriteLine("[warn] CDP 连接断开，3 秒后重连...");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[warn] CDP 异常: {ex.Message}，3 秒后重试...");
    }
    exit.Wait(3000);
}

// 退出时把像素屏恢复为时钟模式，避免停留在最后一行歌词
try { device.SetUIModel(HaloPixelUIModel.Clock); } catch { }
Console.WriteLine("已退出，像素屏恢复时钟模式。");
return 0;
