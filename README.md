# Kuroko（黒子）

Web会議のときだけ、髪をリアルタイムに黒く（または好きな色に）見せるWindows常駐アプリ。

現実では自由に髪を染めながら、Zoom等の会議では落ち着いた髪色で映る——そのためのツールです。
仮想カメラとして出力するので、会議アプリ側は「カメラを選ぶ」だけで使えます。

名前は歌舞伎・文楽の「黒子」から。黒をまとい、自らは見えずに本番を成立させる裏方に由来します。

## 特徴

- **髪だけをAIで自動検出**して追従。手動マスクのように頭の動きでズレることがない
- **自然な仕上がり**。髪本来の陰影・ツヤを残したまま色相と彩度だけを置き換える。
  「発色」つまみ1本で、自然寄り⇄はっきり発色を連続的に調整できる
- **GPU推論（DirectML）**。CUDA不要で、Windowsの多くのGPUで動作する
- **全パラメータをライブ調整**。色・彩度・明度・発色・色相・検出しきい値・色ガイド・許容度・ぼかし
- **プリセット**を2系統で保存（髪色のみ／フィルタ込みの全設定）
- **タスクトレイ常駐**。Windows起動時の自動開始、仮想カメラが使われたときの自動開始・停止に対応

## 動作環境

- Windows 10 / 11（x64）
- DirectX 12 対応GPU（DirectML経由で推論。控えめなGPUでも動作する）
- Webカメラ
- 仮想カメラドライバ [UnityCapture](https://github.com/schellingb/UnityCapture)（MIT）の登録が必要

## 使い方

1. UnityCapture の `Install/Install.bat` を管理者権限で実行し、「Unity Video Capture」を登録する
2. Kuroko を起動し、カメラを選んで「開始」を押す
3. 会議アプリ（Zoom等）のカメラ設定で **Unity Video Capture** を選ぶ

プレビューはアプリ内に表示され、「拡大」ボタンで別ウィンドウでも確認できます。
表示されている映像が、そのまま相手に届く映像です。

## ソースからのビルド

必要なもの: .NET 10 SDK、Visual Studio または `dotnet` CLI。

```bash
# 1) 髪セグメンテーションのモデルを配置する（大容量のためリポジトリには含まれない）
#    yakhyo/face-parsing の Releases から resnet18.onnx を取得し、以下へ置く
#    engine-cs/models/resnet18.onnx
#    ※512入力版が必須。低解像度に再エクスポートした版では髪を検出できない

# 2) エンジンとGUIをビルドする
dotnet build engine-cs/KurokoEngine.csproj -c Release
dotnet build gui/Kuroko/Kuroko.csproj -c Release

# 3) GUIを起動する（エンジンはGUIが自動で起動する）
./gui/Kuroko/bin/Release/net10.0-windows/Kuroko.exe
```

インストーラの作成は `pwsh gui/Kuroko/publish.ps1 -Version <x.y.z>` で行います
（詳細は [gui/Kuroko/RELEASE.md](gui/Kuroko/RELEASE.md)）。

## 構成

2プロセス構成です。エンジンがクラッシュしてもGUIが生き残るようにし、
ネイティブ依存をGUIから隔離しています。

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

単純に目標色で塗ると「塗り絵」のように不自然になります。Kurokoは元の明度 `V` の
**比率**を保ったまま、目標色の明るさへどれだけ寄せるかを指数補間で決めます。

```
新しい明度 = 元の明度 × (目標色の明度 / 髪の平均明度) ^ 発色
```

`発色 = 0` なら元の陰影を完全保持（最も自然）、`1` なら目標色の明るさへ全振り
（金髪・銀髪がはっきり出る）。指数補間なので、どの位置でも髪の明暗比＝質感が保たれます。

## Python版プロトタイプ（`src/`）

初期検討として MediaPipe Selfie Multiclass を使ったPython実装があります。
現在の本番エンジンは C# 版（`engine-cs/`）で、Python版は**参考用**です。

```bash
python -m venv venv
./venv/Scripts/pip install -r requirements.txt
./venv/Scripts/python src/tune_gui.py   # パラメータ調整GUI
./venv/Scripts/python src/main.py --preview-only
```

MediaPipeのモデルは以下で取得します。

```bash
curl -L -o models/selfie_multiclass_256x256.tflite \
  "https://storage.googleapis.com/mediapipe-models/image_segmenter/selfie_multiclass_256x256/float32/latest/selfie_multiclass_256x256.tflite"
```

## 既知の制約

- 強い逆光や激しい動きでは検出精度が落ちることがある
- 取り込みは720pに固定（1080pでの再着色はCPU負荷が高く30fpsを維持しにくいため）
- 現状はWindows専用（仮想カメラにDirectShowを使うため）

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
