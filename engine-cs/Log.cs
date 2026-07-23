using System.IO;

namespace KurokoEngine;

/// <summary>エンジンの詳細ログ。exeと同じフォルダの engine.log へ追記(共有読み取り可)。</summary>
public static class Log
{
    private static readonly object Lock = new();
    private static readonly string Path =
        System.IO.Path.Combine(AppContext.BaseDirectory, "engine.log");

    public static void Init()
    {
        lock (Lock)
        {
            try { File.WriteAllText(Path, ""); } catch { }
        }
        Info("KurokoEngine started");
    }

    public static void Info(string msg) => Write("INFO", msg);
    public static void Error(string msg) => Write("ERROR", msg);

    private static void Write(string level, string msg)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}][{level}] {msg}";
        lock (Lock)
        {
            try
            {
                using var fs = new FileStream(Path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var sw = new StreamWriter(fs);
                sw.WriteLine(line);
            }
            catch { }
        }
    }
}
