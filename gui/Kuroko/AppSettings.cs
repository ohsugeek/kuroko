using System.IO;
using System.Text.Json;

namespace Kuroko;

/// <summary>常駐運用の設定。%AppData%\Kuroko\settings.json に永続化する。</summary>
public class AppSettingsData
{
    public bool StartOnBoot { get; set; }         // Windows起動時に自動開始
    public bool MinimizeToTray { get; set; } = true; // 閉じるボタンでトレイに格納
    public bool AutoActivate { get; set; }        // 仮想カメラが使われたら自動で処理開始
    public string? CameraName { get; set; }       // 前回選んだカメラ名（次回起動時に復元）
    public string Language { get; set; } = "ja";   // UI言語（"ja" / "en"）
    public bool VcamPromptDeclined { get; set; }    // 仮想カメラの初回セットアップ案内を見送ったか
}

public sealed class SettingsStore
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Kuroko");
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public AppSettingsData Data { get; private set; } = new();

    public void Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                Data = JsonSerializer.Deserialize<AppSettingsData>(File.ReadAllText(FilePath)) ?? new();
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Settings load failed; using defaults", ex);
            Data = new();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Data, JsonOpts));
        }
        catch (Exception ex)
        {
            Logger.Error("Settings save failed", ex);
        }
    }
}
