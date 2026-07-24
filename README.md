<!-- Language: **English** | [日本語](README.ja.md) -->

# Kuroko （黒子）

**Recolor your hair in real time — but only on video calls.**

Kuroko is a Windows tray app that repaints the hair in your webcam feed live, so you can
dye your hair any color in real life yet still show up with a natural, calm look on Zoom,
Meet, or Teams. It outputs a virtual camera, so in your meeting app you just **pick a camera** — no plugins.

The name comes from *kuroko* (黒子), the black-clad stage assistants of kabuki and bunraku
theater: dressed in black, unseen themselves, they make the real performance possible.

<p align="center">
  <img src="docs/images/gui.png" alt="Kuroko app window" width="720">
</p>

> **Before / After (real hair):**
> <!-- Add your own webcam before/after images here, e.g.: -->
> <!-- <img src="docs/images/before-after.png" alt="Before and after" width="720"> -->
> _Coming soon — sample images of hair being recolored live._

## Features

- **AI hair detection.** Segments only the hair and tracks it as you move — no manual masks
  that drift when you turn your head. (BiSeNet face-parsing, run on the GPU via ONNX Runtime + DirectML; no CUDA needed.)
- **Natural recoloring.** Keeps the hair's own shading and shine, replacing only hue and
  saturation. A single **"vividness"** slider blends continuously from natural to bold.
- **Everything is live-adjustable** — color, saturation, brightness, vividness, hue,
  detection threshold, color guide, tolerance, and edge feather.
- **Presets** in two flavors: color-only, and full (color + all filter settings).
- **Runs in the tray.** Optional start-on-boot and auto start/stop when a virtual camera is in use.

## Requirements

- Windows 10 / 11 (x64)
- A DirectX 12 capable GPU (runs on modest GPUs via DirectML)
- A webcam
- The [UnityCapture](https://github.com/schellingb/UnityCapture) virtual-camera driver (MIT)

> The app UI is currently Japanese only. English UI is on the roadmap.

## Install

1. Download **`Kuroko-win-Setup.exe`** from the [latest release](https://github.com/ohsugeek/kuroko/releases/latest) and run it.
2. Install [UnityCapture](https://github.com/schellingb/UnityCapture) and run its `Install/Install.bat` as administrator to register "Unity Video Capture".
3. Launch Kuroko, pick your camera, and click start.
4. In your meeting app's camera setting, choose **Unity Video Capture**.

Later versions update themselves from within the app (tray menu → check for updates).

## How it works

Two processes. If the engine crashes, the GUI survives; native dependencies stay isolated
from the UI.

```
[camera] → KurokoEngine.exe (C#)                         → [Unity Video Capture] → Zoom, etc.
             ├ hair segmentation  BiSeNet face-parsing ONNX (ONNX Runtime + DirectML)
             ├ recolor            HSV blend + color guide + feather
             └ output             UnityCapture shared memory
                    ↑ named pipe  \\.\pipe\kuroko  (live parameter updates)
                    ↓ shared mem  KurokoPreview     (preview frames)
           Kuroko.exe (WPF GUI)
```

Inference and output run on separate threads, so output stays around 30 fps even when
inference runs at ~18 fps.

### The recoloring model

Painting hair with a flat target color looks fake. Kuroko preserves the **ratio** of the
original brightness `V` and only decides how far to push it toward the target color's
brightness, via an exponential interpolation:

```
new V = V × (target V / average hair V) ^ vividness
```

At `vividness = 0` the original shading is fully preserved (most natural); at `1` it goes
all the way to the target brightness (bold colors like blonde or silver show clearly).
Because it's exponential, the hair's light-to-dark ratio — its texture — is kept at any setting.

## Build from source

You need the .NET 10 SDK.

```bash
# 1) Place the hair-segmentation model (kept out of the repo; it is large).
#    Get resnet18.onnx from yakhyo/face-parsing Releases and put it at:
#      engine-cs/models/resnet18.onnx
#    The 512-input version is required; lower-res re-exports fail to detect hair.

# 2) Build the engine and the GUI.
dotnet build engine-cs/KurokoEngine.csproj -c Release
dotnet build gui/Kuroko/Kuroko.csproj -c Release

# 3) Run the GUI (it launches the engine automatically).
./gui/Kuroko/bin/Release/net10.0-windows/Kuroko.exe
```

To build an installer: `pwsh gui/Kuroko/publish.ps1 -Version <x.y.z>`
(see [gui/Kuroko/RELEASE.md](gui/Kuroko/RELEASE.md)).

## Python prototype (`src/`)

An earlier proof of concept using MediaPipe Selfie Multiclass lives in `src/`. The
production engine is the C# version (`engine-cs/`); the Python code is kept for reference.

```bash
python -m venv venv
./venv/Scripts/pip install -r requirements.txt
./venv/Scripts/python src/tune_gui.py            # parameter-tuning GUI
./venv/Scripts/python src/main.py --preview-only
```

## Limitations

- Detection accuracy can drop under strong backlight or fast motion.
- Capture is fixed at 720p (recoloring at 1080p is too CPU-heavy to hold 30 fps).
- Windows only (the virtual camera uses DirectShow).

## Third-party components

| Component | Purpose | License |
|---|---|---|
| [face-parsing](https://github.com/yakhyo/face-parsing) (BiSeNet) | Hair segmentation | MIT |
| [UnityCapture](https://github.com/schellingb/UnityCapture) | Virtual camera output | MIT |
| ONNX Runtime (DirectML) | Inference | MIT |
| OpenCvSharp | Image processing | Apache-2.0 |
| Velopack | Packaging & auto-update | MIT |
| MediaPipe (Python prototype only) | Segmentation | Apache-2.0 |

Model weights follow the terms of their training data (CelebAMask-HQ). Check the license of
each dataset/model before redistribution or commercial use.

## License

[MIT](LICENSE) © 2026 Kenta Ohsugi
