using Microsoft.Win32;

namespace Kuroko;

/// <summary>
/// Windows起動時の自動開始を HKCU の Run キーで登録/解除する（管理者権限不要）。
/// 起動時は "--tray" 付きで、最初からトレイに格納した状態で立ち上げる。
/// </summary>
public static class StartupRegistry
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Kuroko";

    public static void Set(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                return;
            }
            if (enable)
            {
                var exe = Environment.ProcessPath;
                if (exe is not null)
                {
                    key.SetValue(ValueName, $"\"{exe}\" --tray");
                    Logger.Info("Startup registration enabled");
                }
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                Logger.Info("Startup registration disabled");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Startup registry update failed", ex);
        }
    }
}
