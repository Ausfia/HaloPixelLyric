using Windows.Media.Control;

namespace HaloPixelLyric;

public sealed record MediaTrack(string Source, string Title, string Artist, string Album, double DurationMs)
{
    public string Key => Source + "\n" + Title + "\n" + Artist + "\n" + Album;
    public string DisplayTitle => string.IsNullOrWhiteSpace(Artist) ? Title : $"{Title} - {Artist}";
}

public sealed record MediaSnapshot(MediaTrack Track, bool Playing, double PositionMs, bool HasTimeline);

/// <summary>读取 Windows 全局媒体会话。事件只标记失效，WinRT 调用统一在采集循环执行。</summary>
public sealed class SmtcMediaSource : IDisposable
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private MediaTrack? _track;
    private int _mediaDirty = 1;
    private readonly PlaybackClock _clock = new();

    public async Task InitializeAsync(CancellationToken ct)
    {
        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(ct);
    }

    public async Task<MediaSnapshot?> ReadAsync(CancellationToken ct)
    {
        if (_manager is null) return null;
        var sessions = _manager.GetSessions();
        // 跟随系统当前会话；当前会话停止时，优先另一个正在播放的会话。
        var current = _manager.GetCurrentSession();
        var selected = current is not null && IsPlaying(current) ? current
            : sessions.FirstOrDefault(IsPlaying) ?? current ?? sessions.FirstOrDefault();
        if (!Equals(selected, _session))
        {
            Detach();
            _session = selected;
            if (_session is not null) _session.MediaPropertiesChanged += OnMediaChanged;
        }
        if (_session is null) return null;

        var timeline = _session.GetTimelineProperties();
        var playback = _session.GetPlaybackInfo();
        if (_track is null || Interlocked.Exchange(ref _mediaDirty, 0) != 0)
        {
            var media = await _session.TryGetMediaPropertiesAsync().AsTask(ct);
            var track = new MediaTrack(_session.SourceAppUserModelId, media.Title?.Trim() ?? "",
                media.Artist?.Trim() ?? "", media.AlbumTitle?.Trim() ?? "", timeline.EndTime.TotalMilliseconds);
            if (track.Key != _track?.Key) _clock.Reset();
            _track = track;
        }
        if (string.IsNullOrWhiteSpace(_track.Title)) return null;
        if (timeline.EndTime.TotalMilliseconds > 0)
            _track = _track with { DurationMs = timeline.EndTime.TotalMilliseconds };
        var playing = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        var hasTimeline = timeline.LastUpdatedTime > DateTimeOffset.UnixEpoch;
        var position = _clock.Update(timeline.Position.TotalMilliseconds, timeline.LastUpdatedTime,
            playing, playback.PlaybackRate ?? 1, DateTimeOffset.UtcNow);
        if (_track.DurationMs > 0) position = Math.Min(position, _track.DurationMs);
        return new MediaSnapshot(_track, playing, position, hasTimeline);
    }

    private static bool IsPlaying(GlobalSystemMediaTransportControlsSession session) =>
        session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

    private void OnMediaChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) =>
        Interlocked.Exchange(ref _mediaDirty, 1);

    private void Detach()
    {
        if (_session is not null) _session.MediaPropertiesChanged -= OnMediaChanged;
        _session = null;
        _track = null;
        _mediaDirty = 1;
        _clock.Reset();
    }

    public void Dispose() => Detach();
}

/// <summary>SMTC Position 是 LastUpdatedTime 时刻的快照；播放时外推，状态切换时重新锚定。</summary>
public sealed class PlaybackClock
{
    private bool _initialized;
    private bool _playing;
    private double _position;
    private double _reportedPosition;
    private double _rate = 1;
    private DateTimeOffset _reportedAt;
    private DateTimeOffset _anchorAt;

    public void Reset() => _initialized = false;

    public double Update(double positionMs, DateTimeOffset updatedAt, bool playing, double rate, DateTimeOffset now)
    {
        if (!double.IsFinite(rate) || rate <= 0) rate = 1;
        var changed = !_initialized || positionMs != _reportedPosition || updatedAt != _reportedAt;
        if (changed)
        {
            // 某些播放器不更新时间戳：此时以收到快照的时间为锚，避免从零日期外推。
            var elapsed = updatedAt > DateTimeOffset.UnixEpoch ? Math.Max(0, (now - updatedAt).TotalMilliseconds) : 0;
            _position = Math.Max(0, positionMs) + (playing ? elapsed * rate : 0);
            _anchorAt = now;
        }
        else if (playing != _playing || rate != _rate)
        {
            _position = Current(now);
            _anchorAt = now;
        }
        _initialized = true;
        _playing = playing;
        _rate = rate;
        _reportedPosition = positionMs;
        _reportedAt = updatedAt;
        return Current(now);
    }

    private double Current(DateTimeOffset now) => Math.Max(0,
        _position + (_playing ? Math.Max(0, (now - _anchorAt).TotalMilliseconds) * _rate : 0));
}
