namespace HaloPixelLyric;

/// <summary>
/// 注入网易云页面的采集脚本（v12，幂等重装设计）。
///
/// 要点：每次 CDP 重连都全量执行本脚本（不设 alive-skip）——
/// registerCall 重注册（自愈探针/旧版残留对回调的覆盖）、channel.call hook 基于保存的
/// 原始函数重建（防叠加）、歌词/标题/激活会话状态从旧对象继承（避免重装后 1~2 秒取词空窗）。
///
/// 数据链路（全部与网易云 UI 状态无关）：
/// 1. onPlayProgress（native 30Hz：[playId, 秒, 状态]）——进度唯一驱动源
/// 2. 会话甄别：新 playId 需连续 3 次进度递增才采纳（过滤托盘时混入的预加载会话，其进度恒 0）
/// 3. 采纳/切歌 → 页面内 fetch 网易云歌词 API（yv=1 参数取逐字 yrc，lv=1 普通 LRC）
/// 4. hook channel.call：setInfo（歌名）、setTotalTime
/// </summary>
public static class WatcherScript
{
    public const string Code = """
        (function () {
            var old = window.__haloLyric;
            var S = window.__haloLyric = {
                v: 12,
                playId: '',
                progress: -1,
                playing: false,
                progressAt: 0,
                lyricYrc: '',
                lyricLrc: '',
                lyricFrom: '',
                songTitle: '',
                totalTime: 0,
                _lastFetchedSong: '',
                _activeId: ''
            };
            // 继承旧状态：歌词/标题/已取词标记/激活会话（重装不空窗、不重发请求）
            if (old) {
                S.lyricYrc = old.lyricYrc || '';
                S.lyricLrc = old.lyricLrc || '';
                S.lyricFrom = old.lyricFrom || '';
                S.songTitle = old.songTitle || '';
                S._lastFetchedSong = old._lastFetchedSong || '';
                S._activeId = old._activeId || '';
                S.playId = old.playId || '';   // 漏继承会导致"已激活分支不设 playId"→ C# 拿到空 id
            }
            S.progress = -1;   // 进度需重新确认（新回调首次推送即恢复）

            function fetchLyrics(songId) {
                S.lyricFrom = 'fetching';
                S.lyricYrc = '';
                S.lyricLrc = '';
                fetch('https://music.163.com/api/song/lyric/v1?id=' + songId + '&lv=1&kv=1&yv=1&ytv=1&yrv=1&tv=-1&rv=-1', { credentials: 'include' })
                    .then(function (r) { return r.json(); })
                    .then(function (d) {
                        if (d && d.yrc && d.yrc.lyric) { S.lyricYrc = String(d.yrc.lyric); S.lyricFrom = 'api-yrc'; }
                        else if (d && d.lrc && d.lrc.lyric) { S.lyricLrc = String(d.lrc.lyric); S.lyricFrom = 'api-lrc'; }
                        else S.lyricFrom = 'api-empty';
                    })
                    .catch(function (e) { S.lyricFrom = 'api-error'; });
            }
            S.retry = function () { if (S._lastFetchedSong) fetchLyrics(S._lastFetchedSong); };

            // ===== 进度事件：会话甄别 + 状态更新 =====
            var tracks = {};   // 候选会话 -> {last, streak}（不跨重装保留，甄别 90ms 内完成）
            try {
                channel.registerCall('audioplayer.onPlayProgress', function (id, sec, st) {
                    var pid = String(id);
                    var p = Number(sec) || 0;
                    if (pid !== S._activeId) {
                        var t = tracks[pid] || (tracks[pid] = { last: -1, streak: 0 });
                        if (p > t.last + 0.02) t.streak++; else t.streak = 0;
                        t.last = p;
                        if (t.streak < 3) return;
                        S._activeId = pid;
                        tracks = {};
                        S.playId = pid;
                        S.progress = p;
                        S.playing = true;
                        S.progressAt = Date.now();
                        var songId = pid.split('_')[0];
                        if (songId && songId !== S._lastFetchedSong) {
                            S._lastFetchedSong = songId;
                            S.songTitle = '';
                            fetchLyrics(songId);
                        }
                        return;
                    }
                    S.progress = p;
                    S.playing = Number(st) !== 0;
                    S.progressAt = Date.now();
                });
            } catch (e) { }

            // ===== hook channel.call（基于原始函数重建，防叠加） =====
            if (!window.__haloOrigCall) window.__haloOrigCall = channel.call;
            var origCall = window.__haloOrigCall;
            channel.call = function () {
                var api = String(arguments[0]);
                try {
                    if (api === 'player.setInfo' && arguments[2] && arguments[2][0]) {
                        S.songTitle = (arguments[2][0].songName || '') + ' - ' + (arguments[2][0].artistName || '');
                    } else if (api === 'player.setTotalTime' && arguments[2]) {
                        S.totalTime = Number(arguments[2][0]) || 0;
                    }
                } catch (e) { }
                return origCall.apply(this, arguments);
            };
            return 'installed';
        })()
        """;
}
