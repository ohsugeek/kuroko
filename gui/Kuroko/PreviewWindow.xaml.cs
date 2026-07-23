using System.Windows;
using System.Windows.Media.Imaging;

namespace Kuroko;

/// <summary>プレビューを大きく表示する独立ウィンドウ(リサイズ可・Kurokoテーマ)。</summary>
public partial class PreviewWindow : Window
{
    public PreviewWindow()
    {
        InitializeComponent();
    }

    /// <summary>凍結済みBitmapSourceを表示する(MainWindowのプレビュー更新から呼ぶ)。</summary>
    public void SetFrame(BitmapSource bmp)
    {
        BigImage.Source = bmp;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
