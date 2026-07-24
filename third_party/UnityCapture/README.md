# UnityCapture (vendored)

This folder contains the prebuilt DirectShow virtual-camera filter from
**UnityCapture** by Bernhard Schelling, bundled so Kuroko can set up its virtual
camera without a separate manual install.

- Upstream: https://github.com/schellingb/UnityCapture
- Files: `UnityCaptureFilter32.dll`, `UnityCaptureFilter64.dll` (from the upstream `Install/` folder)
- License: the filter (`UnityCaptureFilter`) is **MIT** (see upstream README). The Unity
  plugin part is zlib and is **not** included here.

Kuroko registers these DLLs from a stable per-user location on first run
(`%AppData%\Kuroko\vcam\`) via `regsvr32`, which requires a one-time UAC prompt.
Registration/removal is also available from the tray menu.

## MIT License (UnityCaptureFilter)

Copyright (C) 2018 Bernhard Schelling

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OF OR OTHER DEALINGS IN
THE SOFTWARE.
