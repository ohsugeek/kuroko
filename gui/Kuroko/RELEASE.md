# Kuroko 配布・自動アップデート手順

Velopack を使い、GitHub Releases から配布・自動更新する。アプリ内のコード（`Program.cs` の
`VelopackApp.Build().Run()` と `Updater.cs`）は実装済み。以下はリリース作成の手順で、**本人が実施する**。

## 前提

- `vpk` ツールを導入: `dotnet tool install -g vpk`
- 配布は GitHub リポジトリ `ohsugeek/zoom-hair-recolor` の Release を使う
- **private リポジトリのため、自動アップデートには次のどちらかが必要**:
  - Release を public にする（`Updater.cs` の `AccessToken` は null のままでよい）、または
  - `Updater.cs` の `AccessToken` に read 権限の PAT を設定する（トークンを配布物に埋めることになる点に注意）

## 重要: エンジンの同梱とライセンス

- GUI 単体では動かない。C#エンジン（`KurokoEngine.exe` ＋ ONNX Runtime/DirectML/OpenCvSharp のネイティブDLL
  ＋ `models/resnet18.onnx`）を **`engine/` サブフォルダに同梱**する（`EngineProcess.Locate()` が拾う）。
  `publish.ps1` がエンジンを `engine/` へ直接 self-contained publish するので、手作業でのコピーは不要。
- **モデル `engine-cs/models/resnet18.onnx` は大容量のため git 管理外**。手元に無いと `publish.ps1` は
  冒頭でエラー終了する。[yakhyo/face-parsing](https://github.com/yakhyo/face-parsing) の Releases から取得して配置する。
  **512入力版が必須**（低解像度に再エクスポートした版は髪を検出できない）。
- **Banuba を脱却したため、ライセンス上の配布制約は解消**（BiSeNet face-parsing=MIT、UnityCapture=MIT、
  ONNX Runtime/OpenCvSharp もパーミッシブ）。第三者配布も原理的に可能。ただし**モデル重みの
  ライセンス（学習データ CelebAMask-HQ の利用条件を含む）は一般配布の前に要確認**。
- パッケージサイズは Setup.exe で約 225MB（内訳: モデル53MB、DirectML 18MB、ONNX Runtime 17MB、
  GUI/エンジン双方の self-contained .NET ランタイム）。縮めたい場合は framework-dependent 化を検討する
  （配布先に .NET 10 ランタイムの導入が必要になる）。

## 手順（PowerShell）

```powershell
# 1) パッケージング（GUIをpublish → エンジン同梱 → vpk pack）
pwsh gui/Kuroko/publish.ps1 -Version 0.1.0

# 2) 生成物を GitHub Release にアップロード（vpk が直接アップロードもできる）
vpk upload github `
  --repoUrl https://github.com/ohsugeek/zoom-hair-recolor `
  --publish --releaseName "Kuroko 0.1.0" --tag v0.1.0 `
  --token <GitHub PAT>
```

## 動作

- 初回はダウンロードした `Setup.exe`（Velopack生成）でインストール。以後アプリ本体は `%LocalAppData%\Kuroko` に入る。
- トレイメニュー「アップデートを確認」で最新Releaseを取得・適用して再起動する（開発ビルドでは素通り）。
- 起動時に自動チェックしたい場合は `Updater.CheckAndApplyAsync()` を `App.OnStartup` から呼べばよい（現状は手動確認のみ）。
