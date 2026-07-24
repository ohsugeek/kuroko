using System.Windows;

namespace Kuroko;

/// <summary>
/// 実行時に切り替え可能なUIローカライズ。言語別の ResourceDictionary
/// (Localization/Strings.&lt;code&gt;.xaml) を Application のマージ辞書として差し替える。
/// XAMLの静的テキストは {DynamicResource キー} で参照し、切替時に自動で更新される。
/// コード側のテキストは <see cref="T"/> で取得し、<see cref="LanguageChanged"/> を購読して再適用する。
/// </summary>
public static class Loc
{
    public sealed record LangOption(string Code, string Display);

    /// <summary>対応言語（日本語・英語）。</summary>
    public static readonly LangOption[] Available =
    {
        new("ja", "日本語"),
        new("en", "English"),
    };

    /// <summary>現在の言語コード。</summary>
    public static string Current { get; private set; } = "ja";

    /// <summary>言語が変わったときに発火する（コード側テキストの再適用に使う）。</summary>
    public static event Action? LanguageChanged;

    private static ResourceDictionary? _current;

    /// <summary>起動時に一度呼ぶ。MainWindow 生成より前に呼ぶこと。</summary>
    public static void Init(string? code)
    {
        Apply(Normalize(code));
    }

    /// <summary>言語を切り替える。変化があれば LanguageChanged を発火する。</summary>
    public static void SetLanguage(string? code)
    {
        var c = Normalize(code);
        if (c == Current && _current is not null)
        {
            return;
        }
        Apply(c);
        LanguageChanged?.Invoke();
    }

    /// <summary>キーに対応する現在言語の文字列。未定義ならキー自体を返す。</summary>
    public static string T(string key) =>
        Application.Current?.TryFindResource(key) as string ?? key;

    /// <summary>髪色プリセットの表示名。組み込み色は言語に追従、ユーザー命名はそのまま。</summary>
    public static string PresetDisplay(ColorPresetData p) =>
        string.IsNullOrEmpty(p.Builtin) ? p.Name : T("preset_" + p.Builtin);

    private static string Normalize(string? code) =>
        Available.Any(o => o.Code == code) ? code! : "ja";

    private static void Apply(string code)
    {
        var app = Application.Current;
        if (app is null)
        {
            Current = code;
            return;
        }
        var dict = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Localization/Strings.{code}.xaml", UriKind.Absolute),
        };
        if (_current is not null)
        {
            app.Resources.MergedDictionaries.Remove(_current);
        }
        app.Resources.MergedDictionaries.Add(dict);
        _current = dict;
        Current = code;
    }
}
