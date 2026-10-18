using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace HaloPixelLyric;

/// <summary>歌词行（时间轴统一毫秒）。</summary>
public sealed record LyricLine(double StartMs, double DurMs, string Text);

/// <summary>显示逻辑：歌词解析、分页与时长计算（全部参数化，参数来自 DisplaySettings）。</summary>
public static class LyricLogic
{
    // ===== 歌词解析 =====

    private static readonly Regex YrcLineRegex = new(@"^\[(\d+),(\d+)\]", RegexOptions.Compiled);
    private static readonly Regex YrcWordRegex = new(@"\((\d+),(\d+),(\d+)\)([^\(]*)", RegexOptions.Compiled);
    private static readonly Regex LrcTimeRegex = new(@"\[(?:((?:\d+):))?(\d+(?:[.:]\d+)?)\]", RegexOptions.Compiled);

    /// <summary>
    /// 解析网易云逐字歌词（yrc）：行格式 [startMs,durMs]，字格式 (startMs,durMs,flag)字。
    /// 返回 null 表示输入为空或不含有效行。
    /// </summary>
    public static List<LyricLine>? ParseYrc(string yrc)
    {
        if (string.IsNullOrWhiteSpace(yrc)) return null;
        var result = new List<LyricLine>();
        foreach (var rawLine in yrc.Split('\n'))
        {
            var line = rawLine.Trim();
            // JSON 元数据行（{"t":ms,"c":[{"tx":"作词: "},...]}）：拼接 tx 文本，t 为行时间
            if (line.StartsWith('{'))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(line);
                    var jt = doc.RootElement.GetProperty("t").GetDouble();
                    var jsb = new StringBuilder();
                    if (doc.RootElement.TryGetProperty("c", out var cs))
                        foreach (var seg in cs.EnumerateArray())
                            if (seg.TryGetProperty("tx", out var tx))
                                jsb.Append(tx.GetString());
                    var jtext = jsb.ToString().Trim();
                    if (jtext.Length > 0) result.Add(new LyricLine(jt, 0, jtext));
                }
                catch { }
                continue;
            }
            var m = YrcLineRegex.Match(line);
            if (!m.Success) continue;
            if (!double.TryParse(m.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var start)) continue;
            if (!double.TryParse(m.Groups[2].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var dur)) continue;
            var body = line[m.Length..];
            var text = new StringBuilder();
            foreach (Match w in YrcWordRegex.Matches(body))
                text.Append(w.Groups[4].Value);
            var t = text.ToString().Trim();
            if (t.Length > 0) result.Add(new LyricLine(start, dur, t));
        }
        return result.Count > 0 ? result : null;
    }

    /// <summary>
    /// 解析普通 LRC：[mm:ss.xx]文本（支持一行多时间标签）。行时长 = 下一行开始 - 本行开始（末行 0）。
    /// 非法时间标签跳过（网易云 API 的 LRC 偶有边界格式）。
    /// </summary>
    public static List<LyricLine>? ParseLrc(string lrc)
    {
        if (string.IsNullOrWhiteSpace(lrc)) return null;
        var result = new List<LyricLine>();
        foreach (var rawLine in lrc.Split('\n'))
        {
            var line = rawLine.Trim();
            var times = new List<double>();
            foreach (Match m in LrcTimeRegex.Matches(line))
            {
                // 捕获组1带尾部冒号（如 "00:"），TrimEnd 后才能被 double.TryParse 接受
                var minStr = m.Groups[1].Success ? m.Groups[1].Value.TrimEnd(':') : "0";
                if (!double.TryParse(minStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var min)) continue;
                if (!double.TryParse(m.Groups[2].Value.Replace(':', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var sec)) continue;
                times.Add(Math.Floor(min * 60 + sec) * 1000);
                line = line.Remove(m.Index, m.Length);
            }
            var text = line.Trim();
            if (text.Length == 0) continue;
            foreach (var t in times) result.Add(new LyricLine(t, 0, text));
        }
        if (result.Count == 0) return null;
        result.Sort((a, b) => a.StartMs.CompareTo(b.StartMs));
        for (var i = 0; i < result.Count - 1; i++)
            result[i] = result[i] with { DurMs = Math.Max(0, result[i + 1].StartMs - result[i].StartMs) };
        return result;
    }

    /// <summary>
    /// 混合格式解析：网易云 API 的 lrc 字段实测会混装多种行格式——
    /// JSON 元数据行（{"t":ms,"c":[{tx}]}）、yrc 逐字行（[ms,ms](...)字）、普通 LRC 行（[mm:ss]文本）。
    /// 单一格式解析器遇到混合数据会短路丢行（如只解出"作词/作曲"元数据卡首行），此处逐行分类全收。
    /// </summary>
    public static List<LyricLine>? ParseMixed(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;
        var result = new List<LyricLine>();
        int cntJson = 0, cntYrc = 0, cntLrc = 0, cntEmpty = 0, cntSkip = 0;
        string firstSkipSample = "";
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) { cntEmpty++; continue; }

            if (line.StartsWith('{'))
            {
                if (TryParseJsonMeta(line, out var meta)) { result.Add(meta); cntJson++; }
                else cntSkip++;
                continue;
            }
            var ym = YrcLineRegex.Match(line);
            if (ym.Success)
            {
                if (!double.TryParse(ym.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var ystart)) { cntSkip++; if (firstSkipSample.Length == 0) firstSkipSample = line[..Math.Min(50, line.Length)]; continue; }
                if (!double.TryParse(ym.Groups[2].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var ydur)) { cntSkip++; if (firstSkipSample.Length == 0) firstSkipSample = line[..Math.Min(50, line.Length)]; continue; }
                var ysb = new StringBuilder();
                foreach (Match w in YrcWordRegex.Matches(line[ym.Length..]))
                    ysb.Append(w.Groups[4].Value);
                var yt = ysb.ToString().Trim();
                if (yt.Length > 0) { result.Add(new LyricLine(ystart, ydur, yt)); cntYrc++; }
                else cntSkip++;
                continue;
            }
            var times = new List<double>();
            foreach (Match m in LrcTimeRegex.Matches(line))
            {
                var minStr = m.Groups[1].Success ? m.Groups[1].Value.TrimEnd(':') : "0";
                if (!double.TryParse(minStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var min)) continue;
                if (!double.TryParse(m.Groups[2].Value.Replace(':', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var sec)) continue;
                times.Add(Math.Floor(min * 60 + sec) * 1000);
                line = line.Remove(m.Index, m.Length);
            }
            var text = line.Trim();
            if (text.Length == 0) { cntSkip++; if (firstSkipSample.Length == 0) firstSkipSample = "(no_text_after_ts)"; continue; }
            if (times.Count == 0) { cntSkip++; if (firstSkipSample.Length == 0) firstSkipSample = text[..Math.Min(50, text.Length)]; continue; }
            foreach (var t in times) { result.Add(new LyricLine(t, 0, text)); cntLrc++; }
        }
        System.Diagnostics.Debug.WriteLine($"ParseMixed: json={cntJson} yrc={cntYrc} lrc={cntLrc} empty={cntEmpty} skip={cntSkip} sample={firstSkipSample}");
        if (result.Count == 0) return null;
        result.Sort((a, b) => a.StartMs.CompareTo(b.StartMs));
        for (var i = 0; i < result.Count - 1; i++)
            if (result[i].DurMs == 0)
                result[i] = result[i] with { DurMs = Math.Max(0, result[i + 1].StartMs - result[i].StartMs) };
        return result;
    }

    private static bool TryParseJsonMeta(string line, out LyricLine result)
    {
        result = new LyricLine(0, 0, "");
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(line);
            if (!doc.RootElement.TryGetProperty("t", out var tEl)) return false;
            var jt = tEl.GetDouble();
            var jsb = new StringBuilder();
            if (doc.RootElement.TryGetProperty("c", out var cs))
                foreach (var seg in cs.EnumerateArray())
                    if (seg.TryGetProperty("tx", out var tx))
                        jsb.Append(tx.GetString());
            var jtext = jsb.ToString().Trim();
            if (jtext.Length == 0) return false;
            result = new LyricLine(jt, 0, jtext);
            return true;
        }
        catch { return false; }
    }

    /// <summary>DOM 歌词行（"秒\t文本" 字符串数组）转行列表；时长按相邻行推导。</summary>
    public static List<LyricLine>? ParseDomLines(string[]? domLines)
    {
        if (domLines is null || domLines.Length == 0) return null;
        var result = new List<LyricLine>();
        foreach (var s in domLines)
        {
            var i = s.IndexOf('\t');
            if (i <= 0) continue;
            if (!double.TryParse(s[..i], out var sec)) continue;
            var text = s[(i + 1)..].Trim();
            if (text.Length > 0) result.Add(new LyricLine(sec * 1000, 0, text));
        }
        if (result.Count == 0) return null;
        for (var i = 0; i < result.Count - 1; i++)
            result[i] = result[i] with { DurMs = Math.Max(0, result[i + 1].StartMs - result[i].StartMs) };
        return result;
    }

    public static double DisplayUnits(string text)
    {
        double units = 0;
        foreach (var c in text) units += c >= 0x2E80 ? 1 : 0.5;
        return units;
    }

    public static bool IsStaticText(string text, DisplaySettings s) =>
        DisplayUnits(text) <= s.StaticMaxUnits;

    /// <summary>按字数估算一句/一页的合理演唱驻留时长。</summary>
    public static int EstimateHoldMs(string text, DisplaySettings s)
    {
        var units = DisplayUnits(text);
        return IsStaticText(text, s)
            ? Math.Min(s.StaticMaxHoldMs, s.BaseHoldMs + s.PerCharHoldMs * text.Length)
            : Math.Min(8000, 900 + (int)(260 * units));
    }

    /// <summary>按字节预算截断（保持字符完整），避免超出 HID 64 字节报文。</summary>
    public static string TrimToByteBudget(string text, int maxBytes)
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

    /// <summary>
    /// 长句按屏宽分页：每页 ≤ StaticMaxUnits 显示单位；
    /// 切点均衡化（两页长度接近，避免孤字尾页），目标 = min(长度/2, 本页实容)，
    /// 在目标 ±7 字符窗口内优先选空格/标点（留在上一页尾部），无则按目标硬切。
    /// </summary>
    public static List<string> SplitLyricPages(string text, DisplaySettings s)
    {
        char[] splitChars = [' ', '　', '，', '。', '、', '！', '？', '!', '?', ',', '.', '·', '—', '…', '（', '）', '(', ')', '"', '"'];
        var pages = new List<string>();
        var remaining = text;
        while (DisplayUnits(remaining) > s.StaticMaxUnits && remaining.Length > 1)
        {
            // 本页从剩余文本开头实际能放下的字符数（全半角混合折算，半角=0.5单位）
            var charsFit = 0;
            double acc = 0;
            foreach (var ch in remaining)
            {
                acc += ch >= 0x2E80 ? 1 : 0.5;
                if (acc > s.StaticMaxUnits) break;
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
}
