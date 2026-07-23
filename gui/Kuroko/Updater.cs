using Velopack;
using Velopack.Sources;

namespace Kuroko;

/// <summary>
/// GitHub Releases からの自動アップデート。開発ビルド(未インストール)では何もしない。
/// 配布は BRAND.md / RELEASE.md の手順で vpk pack → GitHub Release にアップロードする。
/// </summary>
public static class Updater
{
    // Kuroko の配布元リポジトリ。private の場合はアクセストークンが必要（下の null を置き換える）。
    private const string RepoUrl = "https://github.com/ohsugeek/zoom-hair-recolor";
    private const string? AccessToken = null;

    /// <summary>更新を確認し、あれば適用して再起動する。結果メッセージ（再起動する場合は null）を返す。</summary>
    public static async Task<string?> CheckAndApplyAsync()
    {
        try
        {
            var mgr = new UpdateManager(new GithubSource(RepoUrl, AccessToken, prerelease: false));
            if (!mgr.IsInstalled)
            {
                return "開発ビルドのため、更新確認はスキップされます。";
            }
            var updates = await mgr.CheckForUpdatesAsync();
            if (updates is null)
            {
                return "最新版です。";
            }
            await mgr.DownloadUpdatesAsync(updates);
            mgr.ApplyUpdatesAndRestart(updates);
            return null; // 再起動するのでここには通常戻らない
        }
        catch (Exception ex)
        {
            Logger.Error("Update check failed", ex);
            return $"更新確認に失敗しました。\n{ex.Message}";
        }
    }
}
