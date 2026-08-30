namespace HaloPixelLyric;

/// <summary>
/// 注入网易云页面的歌词采集脚本（v5，轮询模式 + 演唱时长）。
/// 原理：监听 ul.lyric 内 li 的 class 变化，"line current" 即网易云正在显示的歌词行
/// （数据层驱动，与播放页视图是否可见无关）。
/// v5 新增：采集 li 的 data-time（本行开始时间）与下一行 data-time 的差值 =
/// 本句演唱总时长（sentenceMs），以及本行开始时刻 changedAt，
/// 供 C# 侧按演唱进度分配分页驻留时间。
/// 说明：不使用 console/binding 事件通道——该 CEF 定制内核不转发这些事件。
/// </summary>
public static class WatcherScript
{
    public const string Code = """
        (function () {
            if (window.__haloLyric && window.__haloLyric.v === 5) { return 'alive'; }
            if (window.__haloLyricTimer) { try { clearInterval(window.__haloLyricTimer); } catch (e) { } }
            var state = { observer: null, lastTime: null };

            function update() {
                var t = null, ms = 0;
                var li = document.querySelector('ul.lyric li.current');
                if (li) {
                    var ps = Array.prototype.slice.call(li.querySelectorAll('p'))
                        .map(function (p) { return (p.textContent || '').trim(); })
                        .filter(Boolean);
                    t = ps.length ? ps[0] : null;
                    var t0 = parseFloat(li.getAttribute('data-time'));
                    var sib = li.nextElementSibling;
                    var t1 = sib ? parseFloat(sib.getAttribute('data-time')) : NaN;
                    if (isFinite(t0) && isFinite(t1) && t1 > t0) ms = Math.round((t1 - t0) * 1000);
                    if (t0 !== state.lastTime) {
                        state.lastTime = t0;
                        window.__haloLyric.changedAt = Date.now();
                    }
                }
                window.__haloLyric.current = t;
                window.__haloLyric.sentenceMs = ms;
            }

            function mount() {
                if (state.observer) return true;
                var ul = document.querySelector('ul.lyric');
                if (!ul) return false;
                state.observer = new MutationObserver(function (muts) {
                    for (var i = 0; i < muts.length; i++) {
                        var m = muts[i];
                        if (m.type === 'attributes') {
                            if (/(^|\s)current(\s|$)/.test(String(m.target.className || ''))) { update(); return; }
                        } else if (m.type === 'childList') { update(); return; }
                    }
                });
                state.observer.observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['class'] });
                update();
                return true;
            }

            window.__haloLyric = { v: 5, current: null, sentenceMs: 0, changedAt: 0 };
            window.__haloLyricTimer = setInterval(function () { if (!mount()) update(); }, 2000);
            mount();
            update();
            return 'installed';
        })()
        """;
}
