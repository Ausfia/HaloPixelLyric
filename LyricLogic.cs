using System.Text;

namespace HaloPixelLyric;

/// <summary>显示逻辑：分页与时长计算（全部参数化，参数来自 DisplaySettings）。</summary>
public static class LyricLogic
{
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
