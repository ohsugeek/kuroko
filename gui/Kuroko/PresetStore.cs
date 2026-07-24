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
    /// <summary>組み込み色のキー(black/blonde/…)。設定されていれば表示名を言語に追従させる。ユーザー作成はnull。</summary>
    public string? Builtin { get; set; }
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
                MigrateBuiltins();
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
        foreach (var (key, hex) in DefaultColors)
        {
            // Name は言語追従の Builtin で表示するため空でよい（フォールバック用に日本語名を入れておく）
            Data.ColorPresets.Add(new ColorPresetData { Builtin = key, Name = JaName(key), Hex = hex });
        }
    }

    // 旧バージョン（Builtin 無し・日本語名で保存）のプリセットを組み込みキーに紐付け直す。
    // これで既存ユーザーの8色も言語切替に追従する。ユーザーが改名した色は一致せずそのまま。
    private void MigrateBuiltins()
    {
        foreach (var p in Data.ColorPresets)
        {
            if (string.IsNullOrEmpty(p.Builtin) && JaToKey.TryGetValue(p.Name, out var key))
            {
                p.Builtin = key;
            }
        }
    }

    private static string JaName(string key) =>
        JaToKey.FirstOrDefault(kv => kv.Value == key).Key ?? key;

    private static readonly Dictionary<string, string> JaToKey = new()
    {
        ["黒"] = "black", ["金髪"] = "blonde", ["パープル"] = "purple", ["シルバー"] = "silver",
        ["白"] = "white", ["ミルクティー"] = "milktea", ["ピンク"] = "pink", ["ネイビー"] = "navy",
    };

    // BRAND.md のデフォルト髪色（組み込みキー, 目安HEX）
    public static readonly (string Key, string Hex)[] DefaultColors =
    {
        ("black", "#1A1512"),
        ("blonde", "#C8A45C"),
        ("purple", "#8E5BB5"),
        ("silver", "#BCC0C4"),
        ("white", "#EAE7E1"),
        ("milktea", "#B08D6A"),
        ("pink", "#DD7BA6"),
        ("navy", "#2A3A5E"),
    };
}
