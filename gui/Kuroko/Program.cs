using Velopack;

namespace Kuroko;

/// <summary>
/// アプリのエントリポイント。Velopackのインストール/更新フックを最初に処理してから WPF を起動する。
/// （App.xaml は ApplicationDefinition ではなく Page としてビルドし、Main はここに一本化する）
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // 未インストール(開発ビルド)では素通りする。インストール/更新イベント時はここで処理して終了する。
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
