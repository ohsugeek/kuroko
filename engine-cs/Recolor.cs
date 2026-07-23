using OpenCvSharp;

namespace KurokoEngine;

/// <summary>再着色パラメータ。GUIのコマンドと対応（color/sat/bri/shift/threshold/guide/tol/soft）。</summary>
public sealed class RecolorSettings
{
    public Scalar HairColorBgr { get; set; } = new(0x12, 0x15, 0x1A); // #1A1512
    public double Sat { get; set; }        // 彩度 (-2..2, 0=基準)
    public double Bri { get; set; }        // 明度 (-2..2, 0=基準)
    public double Shift { get; set; }      // 色相シフト (0..1)
    public double Lift { get; set; } = 0.45;      // 発色 (0..1) 0=元の明暗を完全保持(自然) / 1=目標色の明るさへ全振り(鮮やか)
    public double Threshold { get; set; } = 0.26; // 検出しきい値 (0..1)
    public double Guide { get; set; } = 0.70;     // 色ガイド強度 (0..1)
    public double Tol { get; set; } = 0.50;       // 色許容度 (0..1)
    public double Soft { get; set; } = 1.5;       // エッジのぼかし (px)
}

/// <summary>
/// 髪のソフトマスクとフレームから、髪だけを目標色に再着色する。
/// マスク前処理(しきい値→フェザー)はOpenCV、色合成は画素1パスの最適化ループで高速に行う
/// (以前の多数のOpenCV中間処理は720pで約98ms→本実装で大幅短縮)。
/// </summary>
public static class Recolor
{
    // ハイライトのソフトロールオフ開始点と残り幅(1.0でハードにクリップせず質感を残す)
    private const double Knee = 0.75;
    private const double KneeSpan = 1.0 - Knee;

    public static unsafe Mat Apply(Mat frameBgr, Mat mask01, RecolorSettings s)
    {
        int h = frameBgr.Rows, w = frameBgr.Cols;

        // --- マスク前処理: しきい値 + フェザー ---
        using var m = new Mat();
        double t = Math.Clamp(s.Threshold, 0, 0.99);
        Cv2.Subtract(mask01, new Scalar(t), m);
        Cv2.Multiply(m, new Scalar(1.0 / (1.0 - t)), m);
        Cv2.Max(m, new Scalar(0), m);
        Cv2.Min(m, new Scalar(1), m);
        if (s.Soft > 0.0)
        {
            Cv2.GaussianBlur(m, m, new Size(0, 0), s.Soft);
        }

        // 髪の平均色(BGR)と平均の明るさ(色ガイドと発色リフトに使う)
        double avgB = 0, avgG = 0, avgR = 0, avgV = 0.3;
        {
            using var maskByte = new Mat();
            Cv2.Compare(m, new Scalar(0.5), maskByte, CmpTypes.GT);
            if (Cv2.CountNonZero(maskByte) > 0)
            {
                var avg = Cv2.Mean(frameBgr, maskByte);
                avgB = avg.Val0; avgG = avg.Val1; avgR = avg.Val2;
                avgV = Math.Max(Math.Max(avgR, Math.Max(avgG, avgB)) / 255.0, 0.05);
            }
        }

        // 目標色を HSV(H:0..360, S:0..1, V:0..1) に。
        // 彩度は目標色の正しいSを使う(以前はVを彩度に取り違えており、シルバー等の無彩色が赤くなっていた)。
        // VividBoost は、その取り違えで得ていた鮮やかさを有彩色でのみ再現するための係数(無彩色はS=0のまま)。
        const double VividBoost = 1.4;
        RgbToHsv(s.HairColorBgr.Val2, s.HairColorBgr.Val1, s.HairColorBgr.Val0,
            out double targetH, out double targetSat, out double targetVal);
        targetH = (targetH + s.Shift * 180.0) % 360.0;
        double targetS = Math.Clamp(targetSat * VividBoost * Math.Pow(2.0, s.Sat), 0, 1);
        double briFactor = Math.Pow(2.0, s.Bri * 0.4);
        // 発色リフト: 元の明暗比を保ったまま、全体の明るさを目標色へどれだけ寄せるか(指数補間)。
        // 0=元の明度を完全保持(最も自然) / 1=目標色の明るさへ全振り(金髪・銀髪がはっきり出る)
        double lift = Math.Pow(targetVal / avgV, Math.Clamp(s.Lift, 0, 1));
        double guide = s.Guide, tolLo = Math.Max(s.Tol * 0.4, 1e-3), tolHi = Math.Max(s.Tol, tolLo + 1e-3);
        double invDist = 1.0 / (255.0 * Math.Sqrt(3.0));

        var outMat = new Mat(h, w, MatType.CV_8UC3);
        byte* src = (byte*)frameBgr.DataPointer;
        float* mp = (float*)m.DataPointer;
        byte* dst = (byte*)outMat.DataPointer;
        long sStep = frameBgr.Step(), mStep = m.Step(), dStep = outMat.Step();

        for (int y = 0; y < h; y++)
        {
            byte* srow = src + y * sStep;
            float* mrow = (float*)((byte*)mp + y * mStep);
            byte* drow = dst + y * dStep;
            for (int x = 0; x < w; x++)
            {
                int i = x * 3;
                double a = mrow[x];
                byte b = srow[i], g = srow[i + 1], r = srow[i + 2];
                if (a > 0.002)
                {
                    if (guide > 0.0)
                    {
                        double db = b - avgB, dg = g - avgG, dr = r - avgR;
                        double d = Math.Sqrt(db * db + dg * dg + dr * dr) * invDist;
                        double tt = Math.Clamp((d - tolLo) / (tolHi - tolLo), 0, 1);
                        double ss = tt * tt * (3 - 2 * tt); // smoothstep
                        double ww = 1 - ss;
                        a *= 1 - guide * (1 - ww);
                    }
                    // 元のV(明るさ=テクスチャ)の比率を保ったまま、発色リフトで目標色の明るさへ寄せる。
                    // 明るい色でハイライトが白飛びして質感が潰れないよう、上端はソフトに丸める。
                    double v = Math.Max(r, Math.Max(g, b)) / 255.0;
                    double lv = v * briFactor * lift;
                    double nv = lv <= Knee ? lv : Knee + KneeSpan * (1 - Math.Exp(-(lv - Knee) / KneeSpan));
                    HsvToRgb(targetH, targetS, nv, out double nr, out double ng, out double nb);
                    double ia = 1 - a;
                    drow[i] = (byte)(b * ia + nb * 255.0 * a);
                    drow[i + 1] = (byte)(g * ia + ng * 255.0 * a);
                    drow[i + 2] = (byte)(r * ia + nr * 255.0 * a);
                }
                else
                {
                    drow[i] = b; drow[i + 1] = g; drow[i + 2] = r;
                }
            }
        }
        return outMat;
    }

    private static void RgbToHsv(double r, double g, double b, out double h, out double s, out double v)
    {
        r /= 255.0; g /= 255.0; b /= 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        v = max;
        s = max <= 0 ? 0 : d / max;
        if (d <= 0) { h = 0; return; }
        if (max == r) h = 60 * (((g - b) / d) % 6);
        else if (max == g) h = 60 * ((b - r) / d + 2);
        else h = 60 * ((r - g) / d + 4);
        if (h < 0) h += 360;
    }

    private static void HsvToRgb(double h, double s, double v, out double r, out double g, out double b)
    {
        double c = v * s;
        double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
        double mm = v - c;
        double r1, g1, b1;
        if (h < 60) { r1 = c; g1 = x; b1 = 0; }
        else if (h < 120) { r1 = x; g1 = c; b1 = 0; }
        else if (h < 180) { r1 = 0; g1 = c; b1 = x; }
        else if (h < 240) { r1 = 0; g1 = x; b1 = c; }
        else if (h < 300) { r1 = x; g1 = 0; b1 = c; }
        else { r1 = c; g1 = 0; b1 = x; }
        r = r1 + mm; g = g1 + mm; b = b1 + mm;
    }
}
