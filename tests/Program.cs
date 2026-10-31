using HaloPixelLyric;

var checks = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new Exception(description);
    checks++;
}
void Near(double actual, double expected, string description) =>
    Check(Math.Abs(actual - expected) < 0.01, $"{description}: expected {expected}, actual {actual}");

var now = DateTimeOffset.UtcNow;
var clock = new PlaybackClock();
Near(clock.Update(10000, now, true, 1, now), 10000, "initial playing position");
Near(clock.Update(10000, now, true, 1, now.AddSeconds(2)), 12000, "sparse timeline extrapolates");
Near(clock.Update(10000, now, false, 1, now.AddSeconds(2)), 12000, "pause without timeline freezes at current position");
Near(clock.Update(10000, now, false, 1, now.AddSeconds(20)), 12000, "pause does not advance");
Near(clock.Update(10000, now, true, 1, now.AddSeconds(20)), 12000, "resume does not count pause time");
Near(clock.Update(10000, now, true, 1, now.AddSeconds(21)), 13000, "resume advances");
Near(clock.Update(5000, now.AddSeconds(21), true, 1, now.AddSeconds(21)), 5000, "backward seek reanchors");
Near(clock.Update(5000, now.AddSeconds(21), true, 2, now.AddSeconds(22)), 6000, "rate change preserves position");
Near(clock.Update(5000, now.AddSeconds(21), true, 2, now.AddSeconds(23)), 8000, "rate applies to extrapolation");
clock.Reset();
Near(clock.Update(0, now, true, 1, now), 0, "track reset drops previous anchor");

var settings = new DisplaySettings { StaticMaxUnits = 4, PrePushLeadMs = 350, PageLeadMs = 280 };
var scheduler = new LyricDisplayScheduler();
List<LyricLine> lines = [new(1000, 4000, "一二三四五六七八"), new(6000, 1000, "再见"), new(8000, 1000, "再见")];
Check(scheduler.GetText(lines, 600, settings) is null, "no lyric before pre-push window");
Check(scheduler.GetText(lines, 650, settings) == "一二三四", "pre-push first page");
Check(scheduler.GetText(lines, 1500, settings) is null, "same page is not resent");
Check(scheduler.GetText(lines, 2720, settings) == "五六七八", "page lead applies to second page");
Check(scheduler.GetText(lines, 1500, settings) == "一二三四", "backward seek restores first page");
Check(scheduler.GetText(lines, 6000, settings) == "再见", "next lyric");
Check(scheduler.GetText(lines, 8000, settings) == "再见", "identical lyric on another line is a new line");
Check(scheduler.GetText(lines, 8000, settings, force: true) == "再见", "clock restoration resends current page");
scheduler.Reset();
Check(scheduler.GetText(lines, 6000, settings) == "再见", "resume reset restores current page");
scheduler.Reset();
List<LyricLine> interlude = [new(0, 60000, "一二三四五六七八")];
Check(scheduler.GetText(interlude, 2000, settings) == "五六七八", "interlude clamp prevents excessively late paging");

var lrc = LyricLogic.ParseMixed("[00:01.25]你好\n[00:02.75]世界")!;
Near(lrc[0].StartMs, 1250, "LRC preserves fractional seconds");
Near(lrc[0].DurMs, 1500, "LRC duration preserves fractional seconds");
var pages = LyricLogic.SplitLyricPages("一二三四五六七，八九十，十一十二十三十四十五", settings);
Check(pages.All(p => LyricLogic.DisplayUnits(p) <= settings.StaticMaxUnits), "punctuation split never exceeds screen width");

var track = new MediaTrack("test", "测试歌曲", "歌手甲", "专辑甲", 200000);
Check(NeteaseLyricProvider.MatchScore(track, "测试歌曲", "歌手甲", "专辑甲", 201000) is not null, "exact metadata matches");
Check(NeteaseLyricProvider.MatchScore(track, "测试歌曲 (Live)", "歌手甲", "专辑甲", 200000) is null, "live version is rejected");
Check(NeteaseLyricProvider.MatchScore(track, "测试歌曲", "歌手乙", "专辑甲", 200000) is null, "cover artist is rejected");
Check(NeteaseLyricProvider.MatchScore(track, "测试歌曲", "歌手甲", "专辑甲", 220000) is null, "different duration rejected");
Check(NeteaseLyricProvider.MatchScore(track with { Artist = "", Album = "", DurationMs = 0 },
    "测试歌曲", "歌手甲", "专辑甲", 200000) is null, "title-only metadata is insufficient");
Check(NeteaseLyricProvider.MatchScore(track, "测试歌曲", "新艺名(歌手甲)", "专辑甲", 200000) is not null,
    "catalog artist alias in parentheses matches complete name");
Check(NeteaseLyricProvider.MatchScore(track, "测试歌曲", "歌手甲乙", "专辑甲", 200000) is null,
    "partial artist name is not a match");
var oldSettings = System.Text.Json.JsonSerializer.Deserialize<DisplaySettings>("{\"CdpPort\":9333,\"StaticMaxUnits\":15}")!;
Check(oldSettings.PlaybackSource == PlaybackSource.Cdp && oldSettings.CdpPort == 9333,
    "old settings preserve CDP default and custom port");
var restored = System.Text.Json.JsonSerializer.Deserialize<DisplaySettings>(
    System.Text.Json.JsonSerializer.Serialize(new DisplaySettings { PlaybackSource = PlaybackSource.Smtc }))!;
Check(restored.PlaybackSource == PlaybackSource.Smtc, "SMTC selection survives settings roundtrip");
var services = new List<FakeSyncService>();
var modes = new List<PlaybackSource>();
var bridge = new LyricService(mode =>
{
    modes.Add(mode);
    var service = new FakeSyncService();
    services.Add(service);
    return service;
});
string pushed = "";
bridge.LyricPushed += text => pushed = text;
bridge.Start();
Check(modes.SequenceEqual([PlaybackSource.Cdp]) && bridge.IsRunning, "default dispatch starts CDP");
services[0].Push("测试");
Check(pushed == "测试", "selected service events reach UI facade");
Check(!bridge.SelectSource(PlaybackSource.Smtc), "cannot switch while running");
bridge.Start();
Check(services.Count == 1, "repeated start cannot create duplicate writer");
bridge.Stop();
Check(bridge.IsRunning && services[0].StopRequested, "stop waits for backend clock restoration");
Check(!bridge.SelectSource(PlaybackSource.Smtc), "cannot switch during backend cleanup");
services[0].Finish();
Check(!bridge.IsRunning && bridge.SelectSource(PlaybackSource.Smtc), "can switch after cleanup completes");
bridge.Start();
Check(modes.SequenceEqual([PlaybackSource.Cdp, PlaybackSource.Smtc]) && bridge.ActiveSource == PlaybackSource.Smtc,
    "SMTC selected for next start");
services[0].Push("旧服务");
Check(pushed == "测试", "old service events detached");
services[1].Finish();
Check(!bridge.IsRunning && bridge.SelectSource(PlaybackSource.Cdp), "can switch back to CDP");
Check(!bridge.SelectSource((PlaybackSource)99), "invalid source rejected");
bridge.Start();
Check(modes[^1] == PlaybackSource.Cdp, "switch back starts CDP");
services[2].Finish();
Console.WriteLine($"PASS: {checks} timing, scheduling, matching, settings and lifecycle checks");

if (args.Contains("--smtc"))
{
    using var source = new SmtcMediaSource();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    await source.InitializeAsync(timeout.Token);
    var snapshot = await source.ReadAsync(timeout.Token);
    Console.WriteLine(snapshot is null ? "SMTC API OK; no active media track" :
        $"SMTC API OK; source={snapshot.Track.Source}; playing={snapshot.Playing}; timeline={snapshot.HasTimeline}; position={snapshot.PositionMs:F0}ms");
    if (snapshot is not null)
    {
        await Task.Delay(250, timeout.Token);
        var next = await source.ReadAsync(timeout.Token);
        if (next is not null && next.Track.Key == snapshot.Track.Key)
            Console.WriteLine($"SMTC second read OK; playing={next.Playing}; position delta={next.PositionMs - snapshot.PositionMs:F0}ms");
    }
}
if (args.Contains("--network"))
{
    using var provider = new NeteaseLyricProvider();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
    var result = await provider.FetchAsync(new MediaTrack("test", "起风了", "买辣椒也用券", "", 0), timeout.Token);
    Console.WriteLine($"NETWORK RESULT: {result.Description}");
    Check(result.Lines is { Count: > 0 }, "live search and lyric endpoint return timed lyrics");
    Console.WriteLine($"NETWORK PASS: {result.Description}");
}

sealed class FakeSyncService : IPlaybackSyncService
{
    public DisplaySettings Settings { get; set; } = new();
    public bool IsRunning { get; private set; }
    public bool StopRequested { get; private set; }
    public event Action<string>? Log;
    public event Action<string>? LyricPushed;
    public event Action<string>? NowPlayingChanged;
    public event Action? RunningChanged;
    public void Start()
    {
        IsRunning = true;
        Log?.Invoke("started");
        NowPlayingChanged?.Invoke("track");
        RunningChanged?.Invoke();
    }
    public void Stop() => StopRequested = true;
    public void Finish() { IsRunning = false; RunningChanged?.Invoke(); }
    public void Push(string text) => LyricPushed?.Invoke(text);
}
