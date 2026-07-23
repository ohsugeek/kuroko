# Kuroko パッケージング（publish → エンジン同梱 → vpk pack）
# 使い方: pwsh gui/Kuroko/publish.ps1 -Version 0.1.0
# 前提: dotnet tool install -g vpk / エンジンは Release ビルド済み
param(
    [Parameter(Mandatory = $true)][string]$Version
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = Resolve-Path (Join-Path $here '..\..')

$publishDir = Join-Path $here 'publish'
$engineProj = Join-Path $repo 'engine-cs\KurokoEngine.csproj'
$modelPath = Join-Path $repo 'engine-cs\models\resnet18.onnx'
$releaseDir = Join-Path $here 'releases'

# 0) 髪セグメンテーションモデルの存在確認（大容量のためgit管理外。無いとエンジンが起動しない）
#    512入力版が必須。低解像度に再エクスポートした版は髪を検出できないので同梱しないこと
if (-not (Test-Path $modelPath)) {
    throw "モデルが見つかりません: $modelPath`n  yakhyo/face-parsing のReleasesから resnet18.onnx を取得して配置してください"
}

# 1) 既存publishを掃除してGUIをpublish（self-contained: 配布先に.NETランタイム不要）
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet publish (Join-Path $here 'Kuroko.csproj') -c Release -r win-x64 --self-contained -o $publishDir

# 2) C#エンジンを engine/ サブフォルダへ直接publishして同梱（EngineProcess.Locate が拾う）
#    ONNX Runtime(DirectML)・OpenCvSharp のネイティブDLLと models/ もpublishに含まれる
$engineDst = Join-Path $publishDir 'engine'
dotnet publish $engineProj -c Release -r win-x64 --self-contained -o $engineDst

if (-not (Test-Path (Join-Path $engineDst 'models\resnet18.onnx'))) {
    throw "同梱後にモデルが見当たりません: $engineDst\models\resnet18.onnx"
}
Write-Host "Engine bundled (self-contained): $engineDst"

# 3) vpk pack（Velopackのインストーラ/更新パッケージを生成）
vpk pack `
    --packId Kuroko `
    --packVersion $Version `
    --packDir $publishDir `
    --mainExe Kuroko.exe `
    --packTitle Kuroko `
    --icon (Join-Path $repo 'assets\kuroko.ico') `
    --outputDir $releaseDir

Write-Host "Done. Release assets: $releaseDir"
Write-Host "次: vpk upload github でGitHub Releaseへアップロード（RELEASE.md参照）"
