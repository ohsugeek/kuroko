using System.IO;
using System.IO.Pipes;

namespace KurokoEngine;

/// <summary>
/// 名前付きパイプ \\.\pipe\kuroko のサーバー。GUIがクライアントとして接続し、改行区切りでコマンドを送る。
/// クライアント切断後は再度接続を待つ。受けたコマンドは EngineParams へ反映する。
/// </summary>
public sealed class PipeServer
{
    private const string PipeName = "kuroko";
    private readonly EngineParams _params;
    private volatile bool _running = true;

    public PipeServer(EngineParams p) => _params = p;

    public void Start()
    {
        var t = new Thread(Loop) { IsBackground = true };
        t.Start();
    }

    public void Stop() => _running = false;

    private void Loop()
    {
        while (_running)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.None);
                Log.Info("pipe: waiting for client");
                server.WaitForConnection();
                Log.Info("pipe: client connected");
                using var reader = new StreamReader(server);
                string? line;
                while (_running && (line = reader.ReadLine()) is not null)
                {
                    if (line.Length == 0)
                    {
                        continue;
                    }
                    _params.Apply(line);
                }
                Log.Info("pipe: client disconnected");
            }
            catch (Exception ex)
            {
                Log.Error($"pipe error: {ex.Message}");
                Thread.Sleep(500);
            }
        }
    }
}
