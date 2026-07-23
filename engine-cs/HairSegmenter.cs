using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace KurokoEngine;

/// <summary>
/// BiSeNet face-parsing(ONNX)で髪のソフトマスク(0..1)を作る。ONNX Runtime + DirectML でGPU推論する。
/// 髪クラスは実測により 17（このモデル固有。標準CelebAMaskの13ではない）。
/// </summary>
public sealed class HairSegmenter : IDisposable
{
    private const int HairClass = 17;
    private static readonly float[] Mean = { 0.485f, 0.456f, 0.406f };
    private static readonly float[] Std = { 0.229f, 0.224f, 0.225f };

    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly int _size;
    private readonly int _numClasses;

    public HairSegmenter(string modelPath, int inferenceSize = 512, bool useGpu = true)
    {
        _size = inferenceSize;
        var opts = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            // DirectMLの制約: メモリパターン無効＋逐次実行
            EnableMemoryPattern = false,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
        };
        if (useGpu)
        {
            try { opts.AppendExecutionProvider_DML(0); }
            catch (Exception ex) { Console.Error.WriteLine($"DirectML unavailable, fallback to CPU: {ex.Message}"); }
        }
        _session = new InferenceSession(modelPath, opts);
        _inputName = _session.InputMetadata.Keys.First();
        // 出力は [1, C, H, W]（Cがクラス数）
        var outDims = _session.OutputMetadata.Values.First().Dimensions;
        _numClasses = outDims.Length >= 2 && outDims[1] > 0 ? outDims[1] : 19;
    }

    /// <summary>フレーム(BGR)から髪のソフトマスク(CV_32FC1, フレームと同サイズ, 0..1)を返す。</summary>
    public Mat Mask(Mat frameBgr)
    {
        var input = Preprocess(frameBgr);
        using var results = _session.Run(new[] { NamedOnnxValue.CreateFromTensor(_inputName, input) });
        var logits = (DenseTensor<float>)results.First().AsTensor<float>(); // [1, C, S, S]
        var span = logits.Buffer.Span;

        int s = _size, plane = s * s, c = _numClasses;
        var hair = new float[plane];
        int hairOff = HairClass * plane;
        // 髪ソフトマスク = sigmoid(hairLogit - max(他クラスlogit))。
        // 各チャネルplaneを連続アクセスして最大値を取る(チャネル外側ループ=キャッシュ効率が良い)。
        var maxOther = new float[plane];
        Array.Fill(maxOther, float.NegativeInfinity);
        for (int k = 0; k < c; k++)
        {
            if (k == HairClass) continue;
            int off = k * plane;
            for (int p = 0; p < plane; p++)
            {
                float v = span[off + p];
                if (v > maxOther[p]) maxOther[p] = v;
            }
        }
        for (int p = 0; p < plane; p++)
        {
            hair[p] = 1f / (1f + MathF.Exp(maxOther[p] - span[hairOff + p]));
        }

        var small = new Mat(s, s, MatType.CV_32FC1);
        Marshal.Copy(hair, 0, small.Data, plane);
        var full = new Mat();
        Cv2.Resize(small, full, frameBgr.Size(), 0, 0, InterpolationFlags.Linear);
        small.Dispose();
        return full;
    }

    private DenseTensor<float> Preprocess(Mat frameBgr)
    {
        using var resized = new Mat();
        Cv2.Resize(frameBgr, resized, new Size(_size, _size));
        using var rgb = new Mat();
        Cv2.CvtColor(resized, rgb, ColorConversionCodes.BGR2RGB);

        int s = _size, plane = s * s, len = plane * 3;
        var buf = new byte[len];
        Marshal.Copy(rgb.Data, buf, 0, len); // 連続メモリ(HWC, 8U)

        var t = new DenseTensor<float>(new[] { 1, 3, s, s });
        var dst = t.Buffer.Span;
        for (int y = 0; y < s; y++)
        {
            for (int x = 0; x < s; x++)
            {
                int srcp = (y * s + x) * 3;
                int p = y * s + x;
                for (int ch = 0; ch < 3; ch++)
                {
                    float v = buf[srcp + ch] / 255f;
                    dst[ch * plane + p] = (v - Mean[ch]) / Std[ch];
                }
            }
        }
        return t;
    }

    public void Dispose() => _session.Dispose();
}
