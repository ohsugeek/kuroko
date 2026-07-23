using System.Globalization;
using OpenCvSharp;

namespace KurokoEngine;

/// <summary>
/// スレッドセーフな実行時パラメータ。GUIからのコマンド(color/sat/bri/shift/threshold/guide/tol/soft)を反映する。
/// </summary>
public sealed class EngineParams
{
    private readonly object _lock = new();
    private readonly RecolorSettings _s = new();

    public RecolorSettings Snapshot()
    {
        lock (_lock)
        {
            return new RecolorSettings
            {
                HairColorBgr = _s.HairColorBgr,
                Sat = _s.Sat, Bri = _s.Bri, Shift = _s.Shift, Lift = _s.Lift,
                Threshold = _s.Threshold, Guide = _s.Guide, Tol = _s.Tol, Soft = _s.Soft,
            };
        }
    }

    /// <summary>1行コマンドを反映する。</summary>
    public void Apply(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            return;
        }
        string cmd = parts[0];
        Log.Info($"param: {line}");
        lock (_lock)
        {
            if (cmd == "color")
            {
                if (TryParseHexBgr(parts[1], out var bgr)) _s.HairColorBgr = bgr;
            }
            else if (TryD(parts[1], out double v))
            {
                switch (cmd)
                {
                    case "sat": _s.Sat = v; break;
                    case "bri": _s.Bri = v; break;
                    case "shift": _s.Shift = v; break;
                    case "lift": _s.Lift = v; break;
                    case "threshold": _s.Threshold = v; break;
                    case "guide": _s.Guide = v; break;
                    case "tol": _s.Tol = v; break;
                    case "soft": _s.Soft = v; break;
                }
            }
        }
    }

    private static bool TryD(string s, out double v) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    private static bool TryParseHexBgr(string hex, out Scalar bgr)
    {
        bgr = default;
        hex = hex.TrimStart('#');
        if (hex.Length != 6) return false;
        try
        {
            int r = Convert.ToInt32(hex.Substring(0, 2), 16);
            int g = Convert.ToInt32(hex.Substring(2, 2), 16);
            int b = Convert.ToInt32(hex.Substring(4, 2), 16);
            bgr = new Scalar(b, g, r);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
