using System.Windows;
using System.Windows.Threading;

namespace Kuroko;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        Logger.Init();

        // 未処理例外もログに残す（デバッグ時に追えるように）
        DispatcherUnhandledException += (_, args) =>
        {
            Logger.Error("Unhandled UI exception", args.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                Logger.Error("Unhandled domain exception", ex);
            }
        };

        base.OnStartup(e);
    }
}
