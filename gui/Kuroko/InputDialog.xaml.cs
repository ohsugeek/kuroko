using System.Windows;

namespace Kuroko;

/// <summary>プリセット名などを入力する、テーマ付きの小さなモーダルダイアログ。</summary>
public partial class InputDialog : Window
{
    public string ResponseText => Input.Text.Trim();

    public InputDialog(string prompt, string initial = "")
    {
        InitializeComponent();
        PromptText.Text = prompt;
        Input.Text = initial;
        Loaded += (_, _) => { Input.Focus(); Input.SelectAll(); };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ResponseText))
        {
            return;
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    /// <summary>名前を尋ねる。キャンセル時は null を返す。</summary>
    public static string? Ask(Window owner, string prompt, string initial = "")
    {
        var dlg = new InputDialog(prompt, initial) { Owner = owner };
        return dlg.ShowDialog() == true ? dlg.ResponseText : null;
    }
}
