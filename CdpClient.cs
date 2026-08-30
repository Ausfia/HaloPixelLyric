using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace HaloPixelLyric;

/// <summary>
/// Chrome DevTools Protocol 客户端：连接网易云 CEF 页面，接收 binding 事件。
/// </summary>
public sealed class CdpClient : IDisposable
{
    private ClientWebSocket _ws = new();
    private CancellationTokenSource _cts = new();
    private int _nextId;

    /// <summary>JS 执行上下文变化（页面内部导航会销毁重建 context，需重新注入）</summary>
    public event Action? ContextChanged;

    public Task Disconnected { get; private set; } = Task.CompletedTask;

    private readonly Dictionary<int, TaskCompletionSource<string?>> _pending = new();

    /// <summary>在 CDP 端口上查找网易云页面 target。</summary>
    public static async Task<string?> FindPageTargetWsUrlAsync(HttpClient http, int port)
    {
        try
        {
            var targets = await http.GetFromJsonAsync<JsonElement[]>($"http://localhost:{port}/json");
            if (targets is null) return null;
            foreach (var t in targets)
            {
                if (!t.TryGetProperty("type", out var type) || type.GetString() != "page") continue;
                if (!t.TryGetProperty("url", out var url)) continue;
                var u = url.GetString();
                if (u is not null && u.StartsWith("orpheus://", StringComparison.OrdinalIgnoreCase))
                    return t.GetProperty("webSocketDebuggerUrl").GetString();
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
        }
        return null;
    }

    public async Task ConnectAsync(string wsUrl)
    {
        // CDP 返回的 host 是 localhost，钉死 IPv4 避免解析差异导致握手挂起
        wsUrl = wsUrl.Replace("localhost", "127.0.0.1");
        _ws = new ClientWebSocket();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await _ws.ConnectAsync(new Uri(wsUrl), timeout.Token);
        Disconnected = ReceiveLoopAsync(_cts.Token);
    }

    public Task EnableRuntimeAsync() => SendAsync(new { method = "Runtime.enable" });

    /// <summary>执行表达式并等待结果（响应 id 匹配）。</summary>
    public async Task<string?> EvaluateAsync(string expression)
    {
        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;
        try
        {
            await SendAsync(new { id, method = "Runtime.evaluate", @params = new { expression, returnByValue = true } });
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            cts.Token.Register(() => tcs.TrySetResult(null));
            return await tcs.Task;
        }
        finally { _pending.Remove(id); }
    }

    private async Task SendAsync(object message)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
        await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, _cts.Token);
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        var sb = new StringBuilder();
        try
        {
            while (!ct.IsCancellationRequested && _ws.State == WebSocketState.Open)
            {
                sb.Clear();
                WebSocketReceiveResult result;
                do
                {
                    result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                        throw new WebSocketException("server closed");
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                } while (!result.EndOfMessage);

                using var doc = JsonDocument.Parse(sb.ToString());
                var root = doc.RootElement;
                if (root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number)
                {
                    // 命令响应
                    if (_pending.TryGetValue(idEl.GetInt32(), out var tcs))
                    {
                        string? value = null;
                        if (root.TryGetProperty("result", out var resp) &&
                            resp.TryGetProperty("result", out var inner) &&
                            inner.TryGetProperty("value", out var valueEl))
                            value = valueEl.ValueKind == JsonValueKind.String ? valueEl.GetString() : valueEl.ToString();
                        tcs.TrySetResult(value);
                    }
                }
                else if (root.TryGetProperty("method", out var methodEl))
                {
                    switch (methodEl.GetString())
                    {
                        case "Runtime.executionContextsCleared":
                        case "Runtime.executionContextCreated":
                        case "Runtime.executionContextDestroyed":
                            ContextChanged?.Invoke();
                            break;
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            // 记录断开细节便于诊断（页面导航重建 / 被其他客户端顶替 / 网易云退出）
            var closeStatus = _ws.CloseStatus?.ToString() ?? "abnormal";
            Console.WriteLine($"[dbg-cdp] 连接结束: {ex.GetType().Name} closeStatus={closeStatus}");
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _ws.Abort(); } catch { }
        _cts.Dispose();
    }
}
