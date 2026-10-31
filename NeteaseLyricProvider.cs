using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace HaloPixelLyric;

public sealed record LyricResult(List<LyricLine>? Lines, string Description);

/// <summary>SMTC 不提供歌词或网易云歌曲 ID：按元数据匹配曲库，再读取带时间轴的歌词。</summary>
public sealed class NeteaseLyricProvider : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(12) };

    public NeteaseLyricProvider()
    {
        _http.DefaultRequestHeaders.Referrer = new Uri("https://music.163.com/");
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("HaloPixelLyric/1.4");
    }

    public async Task<LyricResult> FetchAsync(MediaTrack track, CancellationToken ct)
    {
        var query = Uri.EscapeDataString(track.Title + " " + track.Artist);
        using var search = await GetJsonAsync($"https://music.163.com/api/search/get/web?s={query}&type=1&limit=30", ct);
        if (!search.RootElement.TryGetProperty("result", out var result) ||
            !result.TryGetProperty("songs", out var songs) || songs.ValueKind != JsonValueKind.Array)
            return new(null, "曲库未找到匹配歌曲");

        long bestId = 0;
        double bestScore = double.MinValue;
        var ambiguous = false;
        foreach (var song in songs.EnumerateArray())
        {
            var title = song.GetProperty("name").GetString() ?? "";
            var artist = string.Join(" / ", song.GetProperty("artists").EnumerateArray()
                .Select(a => a.GetProperty("name").GetString() ?? ""));
            var album = song.TryGetProperty("album", out var a) && a.TryGetProperty("name", out var n)
                ? n.GetString() ?? "" : "";
            var duration = song.TryGetProperty("duration", out var d) ? d.GetDouble() : 0;
            var score = MatchScore(track, title, artist, album, duration);
            if (score is null) continue;
            if (score.Value > bestScore + 0.001)
            {
                bestScore = score.Value;
                bestId = song.GetProperty("id").GetInt64();
                ambiguous = false;
            }
            else if (Math.Abs(score.Value - bestScore) < 0.001) ambiguous = true;
        }
        if (bestId == 0 || ambiguous) return new(null, "没有足够可靠的曲库匹配，仅显示歌名");

        var baseUrl = $"https://music.163.com/api/song/lyric/v1?id={bestId}&yv=1&ytv=1&yrv=1&tv=-1&rv=-1";
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var doc = await GetJsonAsync(baseUrl + (attempt == 0 ? "&lv=1&kv=1" : "&lv=-1&kv=-1"), ct);
            var yrc = GetLyric(doc.RootElement, "yrc");
            var lrc = GetLyric(doc.RootElement, "lrc");
            var lines = LyricLogic.ParseYrc(yrc) ?? LyricLogic.ParseMixed(lrc);
            if (lines is not null)
            {
                lines.Sort((x, y) => x.StartMs.CompareTo(y.StartMs));
                return new(lines, $"网易云 ID {bestId} · {(yrc.Length > 0 ? "YRC" : "LRC")} · {lines.Count} 行");
            }
        }
        return new(null, $"网易云 ID {bestId} 无可用歌词");
    }

    /// <summary>保守匹配：歌名需相同；缺歌手时必须由专辑或高精度时长补足。</summary>
    internal static double? MatchScore(MediaTrack track, string title, string artist, string album, double durationMs)
    {
        if (Normalize(track.Title) != Normalize(title)) return null;
        var hasArtist = !string.IsNullOrWhiteSpace(track.Artist);
        if (hasArtist && !ArtistsMatch(track.Artist, artist)) return null;
        var albumMatch = !string.IsNullOrWhiteSpace(track.Album) && Normalize(track.Album) == Normalize(album);
        var hasDuration = track.DurationMs > 0 && durationMs > 0;
        var delta = hasDuration ? Math.Abs(track.DurationMs - durationMs) : 0;
        if (hasDuration && delta > 8000) return null;
        if (!hasArtist && !albumMatch && (!hasDuration || delta > 2000)) return null;
        return 100 + (hasArtist ? 40 : 0) + (albumMatch ? 20 : 0) + (hasDuration ? 10 - delta / 1000 : 0);
    }

    private static bool ArtistsMatch(string left, string right)
    {
        if (Normalize(left) == Normalize(right)) return true;
        char[] separators = ['/', ';', '；', '、', '&', '，', ','];
        var wanted = left.Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Normalize).Where(s => s.Length > 0).ToArray();
        // 曲库可能把旧艺名保留在括号中，例如“新艺名(旧艺名)”。只接受完整名字/别名，不做子串匹配。
        var actual = right.Normalize(NormalizationForm.FormKC)
            .Split([.. separators, '(', ')'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Normalize).ToHashSet();
        actual.Add(Normalize(right));
        return wanted.Length > 0 && wanted.All(actual.Contains);
    }

    private static string Normalize(string value) => string.Concat(value.Normalize(NormalizationForm.FormKC)
        .Where(char.IsLetterOrDigit)).ToUpperInvariant();

    private static string GetLyric(JsonElement root, string field) =>
        root.TryGetProperty(field, out var obj) && obj.ValueKind == JsonValueKind.Object &&
        obj.TryGetProperty("lyric", out var lyric) ? lyric.GetString() ?? "" : "";

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (doc.RootElement.TryGetProperty("code", out var code) && code.GetInt32() != 200)
        {
            var status = code.GetInt32();
            doc.Dispose();
            throw new HttpRequestException($"网易云接口返回 {status}");
        }
        return doc;
    }

    public void Dispose() => _http.Dispose();
}
