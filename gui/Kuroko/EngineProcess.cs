using System.Diagnostics;
using System.IO;

namespace Kuroko;

/// <summary>
/// 処理エンジン(KurokoEngine.exe = OSS版。ONNX/DirectMLで髪セグメンテーション→再着色→仮想カメラ)の
/// プロセス寿命を管理する。カメラ選択など「ライブ変更できない設定」は、このプロセスを再起動して反映する。
/// </summary>
public sealed class EngineProcess
{
    private const string ExeName = "KurokoEngine.exe";

    private Process? _process;
    private string? _exePath;
    private string? _workDir;

    public bool IsRunning => _process is { HasExited: false };

    /// <summary>エンジンexeを探索する。dev配置(engine-cs/bin/{Release,Debug})と、同梱配置の両方を見る。</summary>
    public bool Locate()
    {
        // 1) 同梱配置: GUI と同じフォルダ、または engine サブフォルダ（インストーラ配布用）
        var baseDir = AppContext.BaseDirectory;
        foreach (var cand in new[]
                 {
                     Path.Combine(baseDir, ExeName),
                     Path.Combine(baseDir, "engine", ExeName),
                 })
        {
            if (File.Exists(cand))
            {
                _exePath = cand;
                _workDir = Path.GetDirectoryName(cand);
                Logger.Info($"Engine located (bundled): {cand}");
                return true;
            }
        }

        // 2) dev配置: リポジトリを上方向に辿って engine-cs/bin/{Release,Debug}/net10.0 を探す
        var dir = new DirectoryInfo(baseDir);
        while (dir is not null)
        {
            foreach (var conf in new[] { "Release", "Debug" })
            {
                var cand = Path.Combine(dir.FullName, "engine-cs", "bin", conf, "net10.0", ExeName);
                if (File.Exists(cand))
                {
                    _exePath = cand;
                    _workDir = Path.GetDirectoryName(cand);
                    Logger.Info($"Engine located (dev): {cand}");
                    return true;
                }
            }
            dir = dir.Parent;
        }

        Logger.Error("Engine exe not found (bundled or dev layout)");
        return false;
    }

    /// <summary>指定カメラindexでエンジンを起動する。既に起動中なら何もしない。</summary>
    public bool Start(int cameraIndex)
    {
        if (IsRunning)
        {
            return true;
        }
        if (_exePath is null && !Locate())
        {
            return false;
        }
        KillStrayEngines();
        try
        {
            var psi = new ProcessStartInfo
            {
                // C#エンジンはコンソール無し・引数はカメラindexのみ（モデルはexe隣のmodels/から読む）
                FileName = _exePath!,
                Arguments = $"{cameraIndex}",
                WorkingDirectory = _workDir!,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            _process = Process.Start(psi);
            Logger.Info($"Engine started: camera={cameraIndex}, pid={_process?.Id}");
            return _process is not null;
        }
        catch (Exception ex)
        {
            Logger.Error("Engine start failed", ex);
            return false;
        }
    }

    /// <summary>
    /// 前回のGUIが異常終了した等で取り残されたエンジンを掃除する。
    /// 残っているとカメラと名前付きパイプを奪い合い、新しいエンジンが起動できない。
    /// エンジンはこのGUIからしか起動しないため、同名プロセスは全て掃除対象としてよい。
    /// </summary>
    private void KillStrayEngines()
    {
        foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExeName)))
        {
            try
            {
                if (p.Id == _process?.Id) continue;
                Logger.Info($"Killing stray engine: pid={p.Id}");
                p.Kill(entireProcessTree: true);
                p.WaitForExit(3000);
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to kill stray engine pid={p.Id}", ex);
            }
            finally
            {
                p.Dispose();
            }
        }
    }

    public void Stop()
    {
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(3000);
                Logger.Info("Engine stopped");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Engine stop failed", ex);
        }
        finally
        {
            _process?.Dispose();
            _process = null;
        }
    }

    public bool Restart(int cameraIndex)
    {
        Stop();
        return Start(cameraIndex);
    }
}
