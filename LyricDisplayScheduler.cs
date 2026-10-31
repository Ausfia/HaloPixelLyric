namespace HaloPixelLyric;

/// <summary>按歌曲进度选择歌词页；不依赖墙钟，因此暂停、跳转、重复文本均可正确定位。</summary>
public sealed class LyricDisplayScheduler
{
    private int _lineIndex = -1;
    private int _pageIndex = -1;

    public void Reset() => (_lineIndex, _pageIndex) = (-1, -1);

    public string? GetText(IReadOnlyList<LyricLine> lines, double positionMs, DisplaySettings settings, bool force = false)
    {
        var index = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].StartMs <= positionMs + settings.PrePushLeadMs) index = i;
            else break;
        }
        if (index < 0) return null;
        var line = lines[index];
        var pages = LyricLogic.SplitLyricPages(line.Text, settings);
        if (pages.Count == 0) return null;
        var estimate = LyricLogic.EstimateHoldMs(line.Text, settings);
        var duration = line.DurMs > 0 ? line.DurMs : estimate;
        if (duration > estimate * settings.InterludeFactor) duration = estimate;
        var totalUnits = pages.Sum(LyricLogic.DisplayUnits);
        var page = 0;
        double elapsed = 0;
        for (var i = 1; i < pages.Count; i++)
        {
            elapsed += duration * LyricLogic.DisplayUnits(pages[i - 1]) / Math.Max(totalUnits, 1);
            if (positionMs >= line.StartMs + elapsed - settings.PageLeadMs) page = i;
            else break;
        }
        if (!force && index == _lineIndex && page == _pageIndex) return null;
        _lineIndex = index;
        _pageIndex = page;
        return pages[page];
    }
}
