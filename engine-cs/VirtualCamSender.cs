using System.IO.MemoryMappedFiles;

namespace KurokoEngine;

/// <summary>
/// UnityCapture 仮想カメラ「Unity Video Capture」へ RGBA(top-down) フレームを送る。
/// shared.inl の送信側プロトコルをC#へ移植: 受信側(コンシューマ)が作る mutex/event/共有メモリを開き、
/// ヘッダ＋画素を書いて Sent イベントを立てる。コンシューマが居ない間は IsReady()=false で送らない。
/// </summary>
public sealed class VirtualCamSender : IDisposable
{
    private const string MutexName = "UnityCapture_Mutx";
    private const string WantName = "UnityCapture_Want";
    private const string SentName = "UnityCapture_Sent";
    private const string DataName = "UnityCapture_Data";
    private const int HeaderSize = 32;
    private const int FORMAT_UINT8 = 0;
    private const int RESIZEMODE_LINEAR = 1;
    private const int MIRRORMODE_DISABLED = 0;

    private Mutex? _mutex;
    private EventWaitHandle? _want;
    private EventWaitHandle? _sent;
    private MemoryMappedFile? _mmf;
    private MemoryMappedViewAccessor? _view;
    private bool _open;

    private bool Open()
    {
        if (_open)
        {
            return true;
        }
        try
        {
            if (!Mutex.TryOpenExisting(MutexName, out _mutex))
            {
                return false; // コンシューマ未接続
            }
            _want = new EventWaitHandle(false, EventResetMode.AutoReset, WantName); // 送信側が作成
            if (!EventWaitHandle.TryOpenExisting(SentName, out _sent))
            {
                Close();
                return false;
            }
            _mmf = MemoryMappedFile.OpenExisting(DataName, MemoryMappedFileRights.ReadWrite);
            _view = _mmf.CreateViewAccessor();
            _open = true;
            return true;
        }
        catch
        {
            Close();
            return false;
        }
    }

    public bool IsReady() => Open();

    /// <summary>RGBA(top-down)バイト列を送る。dataSize = width*height*4。</summary>
    public void Send(int width, int height, byte[] rgba, int dataSize)
    {
        if (!Open() || _view is null || _mutex is null || _sent is null)
        {
            return;
        }
        uint maxSize = _view.ReadUInt32(0);
        if (maxSize < (uint)dataSize)
        {
            return; // 共有バッファ容量不足
        }
        bool locked = false;
        try
        {
            _mutex.WaitOne();
            locked = true;
            _view.Write(4, width);
            _view.Write(8, height);
            _view.Write(12, width);              // stride はピクセル単位
            _view.Write(16, FORMAT_UINT8);
            _view.Write(20, RESIZEMODE_LINEAR);
            _view.Write(24, MIRRORMODE_DISABLED);
            _view.Write(28, 1000);               // timeout(ms)
            _view.WriteArray(HeaderSize, rgba, 0, dataSize);
        }
        catch
        {
            Close();
            return;
        }
        finally
        {
            if (locked) _mutex.ReleaseMutex();
        }
        _sent.Set();
    }

    private void Close()
    {
        _view?.Dispose();
        _mmf?.Dispose();
        _want?.Dispose();
        _sent?.Dispose();
        _mutex?.Dispose();
        _view = null; _mmf = null; _want = null; _sent = null; _mutex = null;
        _open = false;
    }

    public void Dispose() => Close();
}
