using System.IO.MemoryMappedFiles;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Kuroko;

/// <summary>
/// エンジンが共有メモリ「KurokoPreview」に書き出す再着色フレームを読み、BitmapSource(凍結済み)にして
/// コールバックへ渡す。仮想カメラをOpenCVで開けない問題を回避する、engine→GUIの直送プレビュー。
/// レイアウト: [int width][int height][int seq][BGRA画素]。
/// </summary>
public sealed class PreviewReader
{
    private const string Name = "KurokoPreview";
    private const int Header = 12;

    private CancellationTokenSource? _cts;

    public void Start(Action<BitmapSource> onFrame)
    {
        Stop();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        Task.Run(() => Loop(onFrame, ct), ct);
    }

    private static void Loop(Action<BitmapSource> onFrame, CancellationToken ct)
    {
        MemoryMappedFile? mmf = null;
        MemoryMappedViewAccessor? view = null;
        byte[]? buf = null;
        int lastSeq = -1;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (view is null)
                {
                    try
                    {
                        mmf = MemoryMappedFile.OpenExisting(Name);
                        view = mmf.CreateViewAccessor();
                        Logger.Info("Preview: shared memory opened");
                    }
                    catch
                    {
                        Thread.Sleep(200); // エンジン未起動。再試行
                        continue;
                    }
                }

                int w = view.ReadInt32(0), h = view.ReadInt32(4), seq = view.ReadInt32(8);
                if (w <= 0 || h <= 0)
                {
                    Thread.Sleep(30);
                    continue;
                }
                if (seq == lastSeq)
                {
                    Thread.Sleep(15); // 新フレーム待ち
                    continue;
                }
                lastSeq = seq;

                int size = w * h * 4;
                if (buf is null || buf.Length < size) buf = new byte[size];
                view.ReadArray(Header, buf, 0, size);
                var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, buf, w * 4);
                bmp.Freeze();
                onFrame(bmp);
                Thread.Sleep(15);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Preview loop error", ex);
        }
        finally
        {
            view?.Dispose();
            mmf?.Dispose();
        }
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        _cts?.Dispose();
        _cts = null;
    }
}
