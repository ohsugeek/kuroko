using System.Runtime.InteropServices;
using KurokoEngine;
using OpenCvSharp;

// Kuroko OSSエンジン: カメラ取得 → 髪セグメンテーション(ONNX/DirectML) → 再着色 → UnityCapture仮想カメラ送信。
// GUIから名前付きパイプでライブ制御する。推論(重い)と出力(軽い)を別スレッドにして滑らかな30fps出力を狙う。
Log.Init();

// 多重起動を防ぐ。2つ以上動くと名前付きパイプを奪い合って「pipe error」になり、
// カメラも取り合って双方が不安定になる(GUIの再起動時に孤児プロセスが残ると実際に発生した)。
using var singleInstance = new Mutex(true, @"Local\KurokoEngineSingleInstance", out bool isFirstInstance);
if (!isFirstInstance)
{
    Log.Error("another KurokoEngine instance is already running; exiting");
    return;
}

int cameraIndex = args.Length > 0 && int.TryParse(args[0], out var c) ? c : 0;
// 512入力版。低解像度の再エクスポート(256/320/384)は髪をほぼ検出できなかったため512が必須。
// fpsのボトルネックは推論でなく再着色だった(高速化済み)ので、512(推論18fps)でも出力は分離構成で30fps。
const int InferenceSize = 512;
string model = Path.Combine(AppContext.BaseDirectory, "models", "resnet18.onnx");
Log.Info($"camera={cameraIndex} model_exists={File.Exists(model)} size={InferenceSize}");

var prms = new EngineParams();
new PipeServer(prms).Start();
using var vcam = new VirtualCamSender();
using var preview = new PreviewWriter();

// 共有状態(最新フレーム / 最新マスク)。所有権はSetに渡し、Getはクローンを返す。
var frameLock = new object();
Mat? sharedFrame = null;
var maskLock = new object();
Mat? sharedMask = null;

// --- 取得スレッド ---
var capThread = new Thread(() =>
{
    using var cap = new VideoCapture(cameraIndex, VideoCaptureAPIs.DSHOW);
    if (!cap.IsOpened())
    {
        Log.Error($"failed to open camera {cameraIndex}");
        return;
    }
    // 720pに固定(1080p再着色はP600のCPUでは重く30fpsを維持できないため)。
    // 再着色の実効解像度はマスク(320)＋720pブレンドで決まり、720pプレビューで品質は十分確認できる。
    cap.Set(VideoCaptureProperties.FrameWidth, 1280);
    cap.Set(VideoCaptureProperties.FrameHeight, 720);
    Log.Info("capture started");
    while (true)
    {
        var f = new Mat();
        if (!cap.Read(f) || f.Empty()) { f.Dispose(); continue; }
        // カメラが720p要求を無視して1080p等を返すことがあるため、720pへ強制して再着色負荷を抑える
        if (f.Width > 1280 || f.Height > 720)
        {
            var r = new Mat();
            Cv2.Resize(f, r, new Size(1280, 720));
            f.Dispose();
            f = r;
        }
        Mat? old;
        lock (frameLock) { old = sharedFrame; sharedFrame = f; }
        old?.Dispose();
    }
}) { IsBackground = true };
capThread.Start();

// --- 推論スレッド(髪マスク + 時間平滑化) ---
var infThread = new Thread(() =>
{
    using var seg = new HairSegmenter(model, inferenceSize: InferenceSize, useGpu: true);
    Mat? ema = null;
    const double alpha = 0.5; // EMA: ちらつき低減
    while (true)
    {
        Mat? frame;
        lock (frameLock) { frame = sharedFrame?.Clone(); }
        if (frame is null) { Thread.Sleep(5); continue; }
        try
        {
            using var raw = seg.Mask(frame);
            if (ema is null) ema = raw.Clone();
            else Cv2.AddWeighted(raw, alpha, ema, 1 - alpha, 0, ema);
            Mat? old;
            lock (maskLock) { old = sharedMask; sharedMask = ema.Clone(); }
            old?.Dispose();
        }
        catch (Exception ex) { Log.Error($"inference: {ex.Message}"); }
        finally { frame.Dispose(); }
    }
}) { IsBackground = true };
infThread.Start();

// --- 出力ループ(最新フレーム×最新マスクで再着色→仮想カメラ送信。約30fps) ---
Log.Info("output loop started");
long sent = 0, outFrames = 0;
var fpsWatch = System.Diagnostics.Stopwatch.StartNew();
while (true)
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    Mat? frame, mask;
    lock (frameLock) { frame = sharedFrame?.Clone(); }
    lock (maskLock) { mask = sharedMask?.Clone(); }
    if (frame is not null && mask is not null)
    {
        try
        {
            var settings = prms.Snapshot();
            if (outFrames % 90 == 89)
            {
                Log.Info($"diag maskMean={Cv2.Mean(mask).Val0:F3} frame={frame.Width}x{frame.Height} colorBGR=({settings.HairColorBgr.Val0},{settings.HairColorBgr.Val1},{settings.HairColorBgr.Val2})");
            }
            using var outBgr = Recolor.Apply(frame, mask, settings);
            preview.Write(outBgr); // GUIプレビューへは常に書き出す(コンシューマ有無に依らず)
            if (vcam.IsReady())
            {
                using var rgba = new Mat();
                Cv2.CvtColor(outBgr, rgba, ColorConversionCodes.BGR2RGBA);
                int w = rgba.Width, h = rgba.Height, size = w * h * 4;
                var buf = new byte[size];
                Marshal.Copy(rgba.Data, buf, 0, size);
                vcam.Send(w, h, buf, size);
                if (++sent % 300 == 0) Log.Info($"sent {sent} frames");
            }
        }
        catch (Exception ex) { Log.Error($"output: {ex.Message}"); }
    }
    frame?.Dispose();
    mask?.Dispose();

    int elapsed = (int)sw.ElapsedMilliseconds;
    if (++outFrames % 90 == 0)
    {
        Log.Info($"output {90000.0 / Math.Max(1, fpsWatch.ElapsedMilliseconds):F1} fps (処理 {elapsed} ms/frame)");
        fpsWatch.Restart();
    }
    int wait = 33 - elapsed; // ~30fps上限
    if (wait > 0) Thread.Sleep(wait);
}
