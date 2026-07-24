using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Kuroko;

/// <summary>
/// 同梱した UnityCapture の DirectShow フィルタ(仮想カメラ)を、個別インストール無しで
/// セットアップするためのヘルパー。DLLを安定した配置先(%AppData%\Kuroko\vcam)へ置き、
/// regsvr32 で登録する。登録はCOM(HKLM)への書き込みを伴うため、管理者権限(UAC)が一度必要。
///
/// 配置先をアプリ本体(Velopackはバージョンごとにフォルダが変わる)と分けているのは、
/// 更新のたびに登録パスが切れるのを避けるため。
/// </summary>
public static class VirtualCamInstaller
{
    // 同梱元(アプリ出力の vcam-src/)。publish時もCopyToOutputで含まれる。
    private static string SourceDir => Path.Combine(AppContext.BaseDirectory, "vcam-src");

    // 登録に使う安定した配置先(設定ファイルと同じ Roaming 配下。更新をまたいで残る)
    private static string TargetDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Kuroko", "vcam");

    // 64bit を先に(regsvr32 はDLLのbitnessを検出し適切な版へ自動移譲する)
    private static readonly string[] Dlls = { "UnityCaptureFilter64.dll", "UnityCaptureFilter32.dll" };

    private const string VideoInputCategory = "{860BB310-5D01-11d0-BD3B-00A0C911CE86}";

    /// <summary>仮想カメラが使える状態か(登録済みで実体DLLが存在する)。</summary>
    public static bool IsInstalled() =>
        GetRegisteredFilterPath() is string p && File.Exists(p);

    /// <summary>同梱DLLが手元にあるか(publishに含まれているか)。</summary>
    public static bool BundlePresent() =>
        Dlls.All(d => File.Exists(Path.Combine(SourceDir, d)));

    /// <summary>
    /// 仮想カメラをセットアップ(修復)する。DLLを配置先へコピーし、1回のUACで登録する。
    /// 成功可否は登録状態を再確認して返す。
    /// </summary>
    public static bool Install()
    {
        if (!BundlePresent())
        {
            Logger.Error($"UnityCapture bundle not found in {SourceDir}");
            return false;
        }
        try
        {
            Directory.CreateDirectory(TargetDir);
            foreach (var d in Dlls)
            {
                File.Copy(Path.Combine(SourceDir, d), Path.Combine(TargetDir, d), overwrite: true);
            }
        }
        catch (Exception ex)
        {
            // 既に登録済みDLLがZoom等にロードされているとコピーに失敗しうる。既存があればそのまま続行。
            Logger.Error("Copying vcam DLLs failed (continuing if already present)", ex);
            if (!Dlls.All(d => File.Exists(Path.Combine(TargetDir, d))))
            {
                return false;
            }
        }

        string dll64 = Path.Combine(TargetDir, "UnityCaptureFilter64.dll");
        string dll32 = Path.Combine(TargetDir, "UnityCaptureFilter32.dll");
        // 1回のUACで 64/32 両方を登録する(regsvr32 /s = 無音)
        bool launched = RunElevated("cmd.exe", $"/c regsvr32 /s \"{dll64}\" & regsvr32 /s \"{dll32}\"");
        Logger.Info($"vcam register launched={launched}, installed={IsInstalled()}");
        return IsInstalled();
    }

    /// <summary>仮想カメラの登録を解除する(1回のUAC)。</summary>
    public static bool Uninstall()
    {
        string dll64 = Path.Combine(TargetDir, "UnityCaptureFilter64.dll");
        string dll32 = Path.Combine(TargetDir, "UnityCaptureFilter32.dll");
        RunElevated("cmd.exe", $"/c regsvr32 /s /u \"{dll64}\" & regsvr32 /s /u \"{dll32}\"");
        return !IsInstalled();
    }

    // 登録済み「Unity Video Capture」フィルタの実体DLLパスを返す(未登録なら null)。
    private static string? GetRegisteredFilterPath()
    {
        foreach (var classes in new[] { @"SOFTWARE\Classes\CLSID", @"SOFTWARE\WOW6432Node\Classes\CLSID" })
        {
            try
            {
                using var inst = Registry.LocalMachine.OpenSubKey($@"{classes}\{VideoInputCategory}\Instance");
                if (inst is null) continue;
                foreach (var sub in inst.GetSubKeyNames())
                {
                    using var k = inst.OpenSubKey(sub);
                    if (k?.GetValue("FriendlyName") is not string fn ||
                        !fn.Contains("Unity Video Capture", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    using var ips = Registry.LocalMachine.OpenSubKey($@"{classes}\{sub}\InprocServer32");
                    if (ips?.GetValue(null) is string dll && !string.IsNullOrWhiteSpace(dll))
                    {
                        return dll;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Registry probe failed under {classes}", ex);
            }
        }
        return null;
    }

    // 管理者権限でコマンドを実行する。UACをキャンセルすると Win32Exception(1223) が出るので false を返す。
    private static bool RunElevated(string file, string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            using var p = Process.Start(psi);
            p?.WaitForExit();
            return p is not null && p.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Logger.Error("Elevated command failed or was cancelled", ex);
            return false;
        }
    }
}
