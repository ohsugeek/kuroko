using System.IO;
using System.Text.Json;

namespace Kuroko;

/// <summary>髪色プリセット: 色の見た目を決める値（base色 + 色相シフト/彩度/発色/明度）。</summary>
public class ColorPresetData
{
    public string Name { get; set; } = "";
    public string Hex { get; set; } = "#1A1512";
    public double Shift { get; set; }
    public double Sat { get; set; }
    public double Bri { get; set; }
    /// <summary>発色: 0=元の明暗を保った自然な仕上がり / 1=金髪・銀髪など明るい色をはっきり出す</summary>
    public double Lift { get; set; } = 0.45;
}

/// <summary>フルプリセット: 髪色 + フィルタ系（検出しきい値/色ガイド/許容度/ぼかし）まで含む全設定。</summary>
public class FullPresetData : ColorPresetData
{
    public double Threshold { get; set; } = 0.26;
    public double Guide { get; set; } = 0.70;
    public double Tol { get; set; } = 0.50;
    public double Soft { get; set; } = 1.5;
}

public class PresetFile
{
    public List<ColorPresetData> ColorPresets { get; set; } = new();
    public List<FullPresetData> FullPresets { get; set; } = new();
}

/// <summary>
/// プリセットの永続化。%AppData%\Kuroko\presets.json に保存する（自動アップデートで消えない領域）。
/// 初回は BRAND.md のデフォルト髪色8色を投入する。
/// </summary>
public sealed class PresetStore
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Kuroko");
    private static readonly string FilePath = Path.Combine(Dir, "presets.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public PresetFile Data { get; private set; } = new();

    public void Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                Data = JsonSerializer.Deserialize<PresetFile>(json) ?? new PresetFile();
                Logger.Info($"Presets loaded: {Data.ColorPresets.Count} color, {Data.FullPresets.Count} full");
            }
            else
            {
                SeedDefaults();
                Save();
                Logger.Info("Presets seeded with defaults");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Preset load failed; using defaults", ex);
            SeedDefaults();
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
            Logger.Error("Preset save failed", ex);
        }
    }

    private void SeedDefaults()
    {
        Data = new PresetFile();
        foreach (var (name, hex) in DefaultColors)
        {
            Data.ColorPresets.Add(new ColorPresetData { Name = name, Hex = hex });
        }
    }

    // BRAND.md のデフォルト髪色（目安値）
    public static readonly (string Name, string Hex)[] DefaultColors =
    {
        ("黒", "#1A1512"),
        ("金髪", "#C8A45C"),
        ("パープル", "#8E5BB5"),
        ("シルバー", "#BCC0C4"),
        ("白", "#EAE7E1"),
        ("ミルクティー", "#B08D6A"),
        ("ピンク", "#DD7BA6"),
        ("ネイビー", "#2A3A5E"),
    };
}
