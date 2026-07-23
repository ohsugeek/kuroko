using System.Runtime.InteropServices;

namespace Kuroko;

/// <summary>
/// 仮想カメラ「Unity Video Capture」を誰か(Zoom等)が開いているかを監視する。
/// UnityCapture の共有メモリmutex(受信側=コンシューマが作成する)を OpenMutex で覗き、
/// 存在すれば「利用中」と判断する。存在の変化をイベントで通知する。
/// 用途: 自動開始(エンジン停止中に利用が始まったら処理を開始する)。
/// </summary>
public sealed class VirtualCamWatcher
{
    // shared.inl の capnum=0 は mutex名が "UnityCapture_Mutx"（末尾'0'がNUL終端に置換される）
    private const string MutexName = "UnityCapture_Mutx";
    private const uint SYNCHRONIZE = 0x00100000;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern IntPtr OpenMutexA(uint desiredAccess, bool inheritHandle, string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    private CancellationTokenSource? _cts;
    private bool _lastActive;

    /// <summary>コンシューマの有無が変わったときに呼ばれる（true=利用開始, false=利用終了）。</summary>
    public event Action<bool>? ActiveChanged;

    public static bool IsConsumerActive()
    {
        IntPtr h = OpenMutexA(SYNCHRONIZE, false, MutexName);
        if (h != IntPtr.Zero)
        {
            CloseHandle(h);
            return true;
        }
        return false;
    }

    public void Start()
    {
        Stop();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                bool active = IsConsumerActive();
                if (active != _lastActive)
                {
                    _lastActive = active;
                    ActiveChanged?.Invoke(active);
                }
                try { await Task.Delay(1000, ct); } catch { return; }
            }
        }, ct);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }
}
