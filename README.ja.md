<!-- Language: [English](README.md) | **日本語** -->

# Kuroko（黒子）

**現実では自由に髪を染め、Web会議のときだけ髪をリアルタイムに再着色する。**

Kuroko は、Webカメラ映像の髪だけをリアルタイムに塗り替える Windows 常駐アプリです。
現実では好きな髪色にしながら、Zoom・Meet・Teams では落ち着いた自然な髪色で映れます。
仮想カメラとして出力するので、会議アプリ側は **カメラを選ぶだけ**。プラグインは不要です。

名前は歌舞伎・文楽の「黒子」から。黒をまとい、自らは見えずに本番を成立させる裏方に由来します。

<p align="center">
  <img src="docs/images/gui.png" alt="Kuroko アプリ画面" width="720">
</p>

> **Before / After（実際の髪）:**
> <!-- ここに自分のWebカメラのBefore/After画像を追加してください。例: -->
> <!-- <img src="docs/images/before-after.png" alt="Before / After" width="720"> -->
> _準備中 — 実際に髪を再着色しているサンプル画像を掲載予定。_

## 特徴

- **髪だけをAIで自動検出**して追従。手動マスクのように頭を動かすとズレる、ということがない
  （BiSeNet face-parsing を ONNX Runtime + DirectML でGPU推論。CUDA不要）
- **自然な再着色**。髪本来の陰影・ツヤを残し、色相と彩度だけを置き換える。
  「発色」つまみ1本で、自然寄り⇄はっきり発色を連続的に調整
- **全パラメータをライブ調整** — 色・彩度・明度・発色・色相・検出しきい値・色ガイド強度・色許容度・エッジのぼかし
- **プリセット2系統** — 髪色のみ／フィルタ込みの全設定
- **タスクトレイ常駐**。Windows起動時の自動開始、仮想カメラ利用時の自動開始・停止に対応

## 動作環境

- Windows 10 / 11（x64）
- DirectX 12 対応GPU（控えめなGPUでも動作）
- Webカメラ
- 仮想カメラドライバ [UnityCapture](https://github.com/schellingb/UnityCapture)（MIT）

## インストール

1. [最新リリース](https://github.com/ohsugeek/kuroko/releases/latest) から **`Kuroko-win-Setup.exe`** を入手して実行
2. [UnityCapture](https://github.com/schellingb/UnityCapture) を導入し、`Install/Install.bat` を管理者権限で実行して「Unity Video Capture」を登録
3. Kuroko を起動し、カメラを選んで「開始」
4. 会議アプリのカメラ設定で **Unity Video Capture** を選ぶ

以降のバージョンはアプリ内（トレイメニュー「アップデートを確認」）から自動更新できます。

## 仕組み

2プロセス構成です。エンジンがクラッシュしてもGUIが生き残り、ネイティブ依存をGUIから隔離しています。

```
[カメラ] → KurokoEngine.exe (C#)                        → [Unity Video Capture] → Zoom等
             ├ 髪セグメンテーション  BiSeNet face-parsing ONNX (ONNX Runtime + DirectML)
             ├ 再着色              HSVブレンド＋色ガイド＋フェザー
             └ 出力                UnityCapture 共有メモリ
                    ↑ 名前付きパイプ \\.\pipe\kuroko（パラメータのライブ反映）
                    ↓ 共有メモリ KurokoPreview（プレビュー映像）
           Kuroko.exe (WPF GUI)
```

推論と出力をスレッド分離しているため、推論が18fpsでも出力は約30fpsを保ちます。

### 再着色の考え方

単純に目標色で塗ると「塗り絵」のように不自然になります。Kuroko は元の明度 `V` の
**比率**を保ったまま、目標色の明るさへどれだけ寄せるかを指数補間で決めます。

```
新しい明度 = 元の明度 × (目標色の明度 / 髪の平均明度) ^ 発色
```

`発色 = 0` なら元の陰影を完全保持（最も自然）、`1` なら目標色の明るさへ全振り
（金髪・銀髪がはっきり出る）。指数補間なので、どの位置でも髪の明暗比＝質感が保たれます。

## ソースからのビルド

.NET 10 SDK が必要です。

```bash
# 1) 髪セグメンテーションのモデルを配置（大容量のためリポジトリには含まれない）
#    yakhyo/face-parsing の Releases から resnet18.onnx を取得し、以下へ置く
#      engine-cs/models/resnet18.onnx
#    ※512入力版が必須。低解像度に再エクスポートした版では髪を検出できない

# 2) エンジンとGUIをビルド
dotnet build engine-cs/KurokoEngine.csproj -c Release
dotnet build gui/Kuroko/Kuroko.csproj -c Release

# 3) GUIを起動（エンジンはGUIが自動で起動する）
./gui/Kuroko/bin/Release/net10.0-windows/Kuroko.exe
```

インストーラの作成は `pwsh gui/Kuroko/publish.ps1 -Version <x.y.z>`
（詳細は [gui/Kuroko/RELEASE.md](gui/Kuroko/RELEASE.md)）。

## Python版プロトタイプ（`src/`）

初期検討として MediaPipe Selfie Multiclass を使ったPython実装が `src/` にあります。
現在の本番エンジンは C# 版（`engine-cs/`）で、Python版は参考用です。

```bash
python -m venv venv
./venv/Scripts/pip install -r requirements.txt
./venv/Scripts/python src/tune_gui.py            # パラメータ調整GUI
./venv/Scripts/python src/main.py --preview-only
```

## 既知の制約

- 強い逆光や激しい動きでは検出精度が落ちることがある
- 取り込みは720p固定（1080pでの再着色はCPU負荷が高く30fpsを維持しにくいため）
- Windows専用（仮想カメラにDirectShowを使うため）

## サードパーティ

| コンポーネント | 用途 | ライセンス |
|---|---|---|
| [face-parsing](https://github.com/yakhyo/face-parsing) (BiSeNet) | 髪セグメンテーション | MIT |
| [UnityCapture](https://github.com/schellingb/UnityCapture) | 仮想カメラ出力 | MIT |
| ONNX Runtime (DirectML) | 推論 | MIT |
| OpenCvSharp | 映像処理 | Apache-2.0 |
| Velopack | 配布・自動更新 | MIT |
| MediaPipe（Python版のみ） | セグメンテーション | Apache-2.0 |

モデルの重みは学習データ（CelebAMask-HQ）の利用条件に従います。再配布や商用利用の前に
各データセット・モデルのライセンスを確認してください。

## ライセンス

[MIT](LICENSE) © 2026 Kenta Ohsugi
