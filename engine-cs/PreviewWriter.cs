using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using OpenCvSharp;

namespace KurokoEngine;

/// <summary>
/// 再着色フレームを共有メモリ「KurokoPreview」へ書き出し、GUIが直接プレビュー表示できるようにする。
/// 仮想カメラ(UnityCapture)をOpenCVで開けない問題を回避する、engine→GUIの直送経路。
/// 拡大プレビューでフルHDまで確認できるよう、フレームをネイティブ解像度(最大1920x1080)で送る。
/// レイアウト: [int width][int height][int seq][BGRA画素 w*h*4]。
/// </summary>
public sealed class PreviewWriter : IDisposable
{
    private const int MaxW = 1920;
    private const int MaxH = 1080;
    private const int Header = 12;
    private const int Capacity = MaxW * MaxH * 4;

    private readonly MemoryMappedFile _mmf;
    private readonly MemoryMappedViewAccessor _view;
    private readonly byte[] _buf = new byte[Capacity];
    private int _seq;

    public PreviewWriter()
    {
        _mmf = MemoryMappedFile.CreateOrOpen("KurokoPreview", Header + Capacity);
        _view = _mmf.CreateViewAccessor();
    }

    public void Write(Mat bgr)
    {
        int w = bgr.Width, h = bgr.Height;
        Mat src = bgr;
        Mat? resized = null;
        // 上限(1920x1080)を超える場合のみ縮小する
        if (w > MaxW || h > MaxH)
        {
            double s = Math.Min((double)MaxW / w, (double)MaxH / h);
            w = (int)(w * s);
            h = (int)(h * s);
            resized = new Mat();
            Cv2.Resize(bgr, resized, new Size(w, h));
            src = resized;
        }
        using var bgra = new Mat();
        Cv2.CvtColor(src, bgra, ColorConversionCodes.BGR2BGRA);
        int size = w * h * 4;
        Marshal.Copy(bgra.Data, _buf, 0, size);
        _view.Write(0, w);
        _view.Write(4, h);
        _view.WriteArray(Header, _buf, 0, size);
        _view.Write(8, ++_seq);
        resized?.Dispose();
    }

    public void Dispose()
    {
        _view.Dispose();
        _mmf.Dispose();
    }
}
