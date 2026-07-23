"""
Webカメラ映像の髪の毛だけをリアルタイムで黒髪化し、
仮想カメラとして出力する（Zoom等でそのまま選択できる）。

使い方:
  # まずはプレビューだけで精度確認（仮想カメラ不要）
  python src/main.py --preview-only

  # 仮想カメラへ出力（要: OBS Studio導入済み = OBS Virtual Cameraドライバがある状態）
  python src/main.py
"""

from __future__ import annotations

import argparse
import time

import cv2

from hair_recolor import HairRecolorPipeline, RecolorConfig, load_tuned_config

MODEL_PATH = "models/selfie_multiclass_256x256.tflite"


def parse_args():
    parser = argparse.ArgumentParser(description="リアルタイム黒髪化エフェクト")
    parser.add_argument(
        "--camera-index",
        type=int,
        default=None,
        help="Webカメラのデバイス番号(未指定時はtuned_config.jsonの値、それも無ければ0)",
    )
    parser.add_argument("--width", type=int, default=1280)
    parser.add_argument("--height", type=int, default=720)
    parser.add_argument("--fps", type=int, default=30)
    parser.add_argument(
        "--preview-only",
        action="store_true",
        help="仮想カメラへ出力せず、確認ウィンドウのみ表示する",
    )
    parser.add_argument(
        "--show-mask",
        action="store_true",
        help="検出した髪マスクを別ウィンドウで表示する（デバッグ用）",
    )
    return parser.parse_args()


class FpsMeter:
    """フレーム間隔からFPSを指数移動平均で算出する。"""

    def __init__(self, alpha: float = 0.1):
        self.alpha = alpha
        self.ema: float | None = None
        self._last: float | None = None

    def tick(self) -> float:
        now = time.perf_counter()
        if self._last is not None:
            dt = now - self._last
            instant = 1.0 / dt if dt > 0 else 0.0
            self.ema = instant if self.ema is None else (1 - self.alpha) * self.ema + self.alpha * instant
        self._last = now
        return self.ema or 0.0


def open_camera(index: int, width: int, height: int, fps: int) -> cv2.VideoCapture:
    cap = cv2.VideoCapture(index, cv2.CAP_DSHOW)
    cap.set(cv2.CAP_PROP_FRAME_WIDTH, width)
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, height)
    cap.set(cv2.CAP_PROP_FPS, fps)
    if not cap.isOpened():
        raise RuntimeError(f"カメラ index={index} を開けませんでした")
    return cap


def run_preview_only(args, pipeline: HairRecolorPipeline):
    cap = open_camera(args.camera_index, args.width, args.height, args.fps)
    print("プレビューモード。ウィンドウを選択して 'q' で終了。")
    fps_meter = FpsMeter()
    try:
        while True:
            ok, frame = cap.read()
            if not ok:
                continue
            recolored, mask = pipeline.process(frame)
            fps = fps_meter.tick()

            side_by_side = cv2.hconcat([frame, recolored])
            cv2.putText(side_by_side, f"FPS: {fps:.1f}", (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 1.0, (0, 255, 0), 2)
            cv2.imshow("original | recolored (q:終了)", side_by_side)
            if args.show_mask:
                cv2.imshow("hair mask", mask)

            if cv2.waitKey(1) & 0xFF == ord("q"):
                break
    finally:
        cap.release()
        cv2.destroyAllWindows()


def run_virtual_camera(args, pipeline: HairRecolorPipeline):
    import pyvirtualcam
    from pyvirtualcam import PixelFormat

    cap = open_camera(args.camera_index, args.width, args.height, args.fps)
    try:
        with pyvirtualcam.Camera(
            width=args.width, height=args.height, fps=args.fps, fmt=PixelFormat.BGR
        ) as cam:
            print(f"仮想カメラ起動: {cam.device}")
            print("Zoom側のカメラ設定でこのデバイスを選択してください。'q'で終了。")
            fps_meter = FpsMeter()
            while True:
                ok, frame = cap.read()
                if not ok:
                    continue
                recolored, mask = pipeline.process(frame)

                cam.send(recolored)
                cam.sleep_until_next_frame()
                fps = fps_meter.tick()

                # FPS表示はプレビュー用のコピーにのみ焼き込む(仮想カメラ送出映像は汚さない)
                preview = recolored.copy()
                cv2.putText(preview, f"FPS: {fps:.1f}", (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 1.0, (0, 255, 0), 2)
                cv2.imshow("preview (q:終了)", preview)
                if args.show_mask:
                    cv2.imshow("hair mask", mask)
                if cv2.waitKey(1) & 0xFF == ord("q"):
                    break
    finally:
        cap.release()
        cv2.destroyAllWindows()


def main():
    args = parse_args()
    config, tuned_camera_index = load_tuned_config()
    if args.camera_index is None:
        args.camera_index = tuned_camera_index if tuned_camera_index is not None else 0
    if tuned_camera_index is not None:
        print(f"tuned_config.json を読み込みました（camera_index={tuned_camera_index}）")

    with HairRecolorPipeline(MODEL_PATH, config) as pipeline:
        start = time.time()
        if args.preview_only:
            run_preview_only(args, pipeline)
        else:
            run_virtual_camera(args, pipeline)
        print(f"終了（実行時間 {time.time() - start:.1f}秒）")


if __name__ == "__main__":
    main()
