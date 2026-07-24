using System.Windows;

namespace Kuroko;

/// <summary>
/// Kuroko テーマのモーダルダイアログ。確認(はい/キャンセル)と通知(OKのみ)の両方に使う。
/// Windows標準の MessageBox の代わりに使い、ブランドの見た目を保つ。
/// </summary>
public partial class ConfirmDialog : Window
{
    private ConfirmDialog(string message, string okText, bool showCancel)
    {
        InitializeComponent();
        MessageText.Text = message;
        OkButton.Content = okText;
        CancelButton.Visibility = showCancel ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    /// <summary>確認ダイアログ。OK押下で true。</summary>
    public static bool Confirm(Window owner, string message, string? okText = null)
    {
        var dlg = new ConfirmDialog(message, okText ?? Loc.T("S_delete"), showCancel: true) { Owner = owner };
        return dlg.ShowDialog() == true;
    }

    /// <summary>通知ダイアログ（OKのみ）。</summary>
    public static void Info(Window owner, string message)
    {
        var dlg = new ConfirmDialog(message, Loc.T("S_ok"), showCancel: false) { Owner = owner };
        dlg.ShowDialog();
    }
}
