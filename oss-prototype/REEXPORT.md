# 髪セグメンテーションモデルの低解像度再エクスポート

P600では512入力=約18fps。**同じBiSeNet resnet18の重みを低解像度で再エクスポート**すると品質ほぼ同等で高速化できる。
実測(DirectML, Quadro P600): 256=58fps / 320=40fps / 384=29fps / 512=18fps。**320を採用**(engine-cs)。

## 手順（再現用）

```bash
# 1) モデル定義と重みを取得
git clone --depth 1 https://github.com/yakhyo/face-parsing.git fp-repo   # MIT
curl -L -o fp-repo/weights/resnet18.pt \
  https://github.com/yakhyo/face-parsing/releases/download/weights/resnet18.pt

# 2) torch(CPU)で任意の入力サイズにエクスポート（fp-repo ディレクトリ内で実行）
#    export_sizes.py: BiSeNet(19,'resnet18') に resnet18.pt を読み込み、
#    torch.onnx.export で 256/320/384 を出力（dynamic_axes は batch のみ）
python export_sizes.py   # → ../models/resnet18_{256,320,384}.onnx

# 3) 採用サイズを engine-cs/models/ へ配置し、Program.cs の InferenceSize と一致させる
cp models/resnet18_320.onnx ../engine-cs/models/
```

- 髪クラスは 17（このモデル固有。標準CelebAMaskの13ではない）。
- モデルは大容量のためgit管理外。上記で再生成できる。
- FP16化はP600(Pascal)ではFP16が遅く効果なし（むしろ微減）だった。低解像度化が有効。
