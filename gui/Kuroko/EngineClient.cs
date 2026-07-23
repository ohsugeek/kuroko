using System.IO;
using System.IO.Pipes;

namespace Kuroko;

/// <summary>
/// エンジン（realtime-camera-preview）へ名前付きパイプ \\.\pipe\kuroko で接続し、
/// 1行コマンド（color #RRGGBB / shift v / sat v / bri v）を送るクライアント。
/// エンジンが未起動でもバックグラウンドで接続を試み続け、接続状態を通知する。
/// </summary>
public sealed class EngineClient : IDisposable
{
    private const string PipeName = "kuroko";

    private NamedPipeClientStream? _pipe;
    private StreamWriter? _writer;
    private readonly CancellationTokenSource _cts = new();
    private volatile bool _connected;

    /// <summary>接続状態が変わったときに呼ばれる（true=接続, false=切断）。</summary>
    public event Action<bool>? ConnectionChanged;

    public bool IsConnected => _connected;

    public void Start()
    {
        Task.Run(() => ConnectLoopAsync(_cts.Token));
    }

    private async Task ConnectLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (!_connected)
            {
                try
                {
                    var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                    await pipe.ConnectAsync(500, ct);
                    _pipe = pipe;
                    _writer = new StreamWriter(pipe) { AutoFlush = true };
                    _connected = true;
                    Logger.Info("Engine pipe connected");
                    ConnectionChanged?.Invoke(true);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch
                {
                    // エンジン未起動。次のループで再試行する
                }
            }
            else if (_pipe is { IsConnected: false })
            {
                MarkDisconnected();
            }

            try
            {
                await Task.Delay(1000, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>コマンドを1行送る。未接続なら破棄してログに残す。</summary>
    public void Send(string command)
    {
        if (!_connected || _writer is null)
        {
            Logger.Warn($"Send dropped (engine not connected): {command}");
            return;
        }
        try
        {
            _writer.WriteLine(command);
            Logger.Info($"-> {command}");
        }
        catch (Exception ex)
        {
            Logger.Error("Send failed", ex);
            MarkDisconnected();
        }
    }

    private void MarkDisconnected()
    {
        if (!_connected)
        {
            return;
        }
        _connected = false;
        try { _writer?.Dispose(); } catch { }
        try { _pipe?.Dispose(); } catch { }
        _writer = null;
        _pipe = null;
        Logger.Warn("Engine pipe disconnected");
        ConnectionChanged?.Invoke(false);
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _writer?.Dispose(); } catch { }
        try { _pipe?.Dispose(); } catch { }
        _cts.Dispose();
    }
}
