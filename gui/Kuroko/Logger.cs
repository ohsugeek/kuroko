using System.IO;

namespace Kuroko;

/// <summary>
/// GUIの詳細ログをファイルへ出力する。デバッグ時に外部（Claude等）から追えるよう、
/// 実行ファイルと同じフォルダの kuroko-gui.log へ追記する。共有読み取り可・逐次フラッシュ。
/// </summary>
public static class Logger
{
    private static readonly object _lock = new();
    private static readonly string _path =
        Path.Combine(AppContext.BaseDirectory, "kuroko-gui.log");

    public static void Init()
    {
        lock (_lock)
        {
            try
            {
                // 起動ごとにログを開始（前回分は残さず切り詰め）
                File.WriteAllText(_path, "");
            }
            catch
            {
                // ログ初期化失敗はアプリ動作を妨げない
            }
        }
        Info($"Kuroko GUI started. log={_path}");
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message) => Write("ERROR", message);

    public static void Error(string message, Exception ex) =>
        Write("ERROR", $"{message}: {ex.GetType().Name} {ex.Message}");

    private static void Write(string level, string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}][{level}] {message}";
        lock (_lock)
        {
            try
            {
                // 実行中も他プロセスから読めるよう共有読み取りで開く
                using var fs = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var sw = new StreamWriter(fs);
                sw.WriteLine(line);
            }
            catch
            {
                // ログ書き込み失敗はアプリ動作を妨げない
            }
        }
    }
}
