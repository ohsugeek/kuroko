"""
髪色エフェクトのパラメータをリアルタイムに調整するGUIツール。

Tkinterの調整パネル(別ウィンドウ)で以下を操作しながら、
左(元映像)/右(加工後)を並べたプレビューで確認できる。
  - 使用するWebカメラ(ドロップダウン)
  - 目標髪色 R/G/B
  - ブレンド強度
  - 髪判定のしきい値
  - 境界の滑らかさ(guided filterの半径・なじみ具合)
  - 時間方向の安定化(チラつき抑制)

OpenCVのトラックバーは日本語ラベルの表示が環境によって不安定なため、
調整パネルはすべてTkinter側にまとめている。

使い方:
    python src/tune_gui.py

キー操作(プレビューウィンドウにフォーカスした状態で):
    q: 終了
    s: 現在の値を tuned_config.json に保存（main.py が自動で読み込む）
"""

from __future__ import annotations

import argparse
import json
import threading
import time
import tkinter as tk
from pathlib import Path
from tkinter import ttk

import cv2

from hair_recolor import HairRecolorPipeline, RecolorConfig, load_tuned_config

MODEL_PATH = "models/selfie_multiclass_256x256.tflite"
TUNED_CONFIG_PATH = Path("tuned_config.json")
WINDOW = "hair-recolor tuner (original | recolored)"


def list_cameras(max_index: int = 6) -> list[int]:
    found = []
    for i in range(max_index):
        cap = cv2.VideoCapture(i, cv2.CAP_DSHOW)
        if cap.isOpened():
            found.append(i)
        cap.release()
    return found or [0]


class ControlPanel:
    """カメラ選択・各種パラメータをまとめて操作するTkinterパネル。

    別スレッドでTkinterのイベントループを回し、スライダー操作のたびに
    このインスタンスの属性(単純なint)を更新する。プレビュー側のループは
    毎フレームこれらの属性を読むだけなので、GILの範囲でスレッドセーフとみなす。
    """

    def __init__(self, cameras: list[int], base_config: RecolorConfig, initial_camera: int | None = None):
        self.cameras = cameras
        self.selected_camera = initial_camera if initial_camera in cameras else cameras[0]

        b, g, r = base_config.target_color_bgr
        self.r = r
        self.g = g
        self.b = b
        self.blend_percent = round(base_config.blend_strength * 100)
        self.threshold_percent = round(base_config.confidence_threshold * 100)
        self.boundary_radius = base_config.boundary_radius
        self.boundary_eps = round(base_config.boundary_eps)
        self.smoothing_percent = round(base_config.temporal_smoothing * 100)
        self.shine_percent = round(base_config.shine_strength * 100)
        self.process_width = base_config.process_width
        self.infer_every_n = base_config.infer_every_n

        threading.Thread(target=self._run_ui, daemon=True).start()

    def _add_slider(self, parent, label_text: str, from_: int, to_: int, initial: int, on_change):
        frame = ttk.Frame(parent)
        frame.pack(fill="x", padx=10, pady=4)
        ttk.Label(frame, text=label_text).pack(anchor="w")
        scale = tk.Scale(
            frame,
            from_=from_,
            to=to_,
            orient="horizontal",
            command=lambda v: on_change(int(v)),
        )
        scale.set(initial)
        scale.pack(fill="x")

    def _run_ui(self):
        root = tk.Tk()
        root.title("パラメータ調整")
        root.geometry("300x420")

        ttk.Label(root, text="使用するWebカメラ:").pack(padx=10, pady=(10, 0), anchor="w")
        cam_var = tk.StringVar(value=f"Camera {self.selected_camera}")
        combo = ttk.Combobox(
            root,
            textvariable=cam_var,
            values=[f"Camera {i}" for i in self.cameras],
            state="readonly",
        )
        combo.pack(padx=10, pady=(0, 10), fill="x")

        def on_camera_select(_event=None):
            self.selected_camera = int(cam_var.get().removeprefix("Camera "))

        combo.bind("<<ComboboxSelected>>", on_camera_select)

        ttk.Separator(root, orient="horizontal").pack(fill="x", padx=10, pady=6)

        self._add_slider(root, "R", 0, 255, self.r, lambda v: setattr(self, "r", v))
        self._add_slider(root, "G", 0, 255, self.g, lambda v: setattr(self, "g", v))
        self._add_slider(root, "B", 0, 255, self.b, lambda v: setattr(self, "b", v))
        self._add_slider(root, "ブレンド強度 (%)", 0, 100, self.blend_percent, lambda v: setattr(self, "blend_percent", v))
        self._add_slider(root, "判定しきい値 (%)", 0, 100, self.threshold_percent, lambda v: setattr(self, "threshold_percent", v))
        self._add_slider(root, "境界の滑らかさ(半径)", 0, 30, self.boundary_radius, lambda v: setattr(self, "boundary_radius", v))
        self._add_slider(root, "境界のなじみ具合", 0, 500, self.boundary_eps, lambda v: setattr(self, "boundary_eps", v))
        self._add_slider(root, "時間方向の安定化 (%)", 0, 95, self.smoothing_percent, lambda v: setattr(self, "smoothing_percent", v))
        self._add_slider(root, "ツヤの残し方 (%)", 0, 100, self.shine_percent, lambda v: setattr(self, "shine_percent", v))
        self._add_slider(
            root, "処理解像度(幅px・小さいほど高速)", 160, 1280, self.process_width,
            lambda v: setattr(self, "process_width", v),
        )
        self._add_slider(
            root, "推論間引き(Nフレームに1回・大きいほど高速)", 1, 20, self.infer_every_n,
            lambda v: setattr(self, "infer_every_n", v),
        )

        self._root = root
        self.fps_label_var = tk.StringVar(value="表示FPS: -　処理FPS: -")
        ttk.Label(root, textvariable=self.fps_label_var, font=("", 11, "bold")).pack(padx=10, pady=(10, 10), anchor="w")

        root.mainloop()

    def update_fps(self, process_fps: float, display_fps: float):
        root = getattr(self, "_root", None)
        if root is not None:
            root.after(
                0,
                lambda: self.fps_label_var.set(f"表示FPS: {display_fps:.1f}　処理FPS: {process_fps:.1f}"),
            )


def open_camera(index: int, width: int, height: int, fps: int) -> cv2.VideoCapture:
    cap = cv2.VideoCapture(index, cv2.CAP_DSHOW)
    cap.set(cv2.CAP_PROP_FRAME_WIDTH, width)
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, height)
    cap.set(cv2.CAP_PROP_FPS, fps)
    return cap


class FrameGrabber:
    """cap.read()を専用スレッドで回し、メインループには常に最新フレームだけを渡す。

    Tkinterのスライダー操作中はメインスレッドがGILを取り合って一時的に詰まることがあるが、
    キャプチャを別スレッドに逃がしておけば、その間もカメラ側は取りこぼしなく読み進められる。
    """

    def __init__(self, cap: cv2.VideoCapture):
        self._cap = cap
        self._lock = threading.Lock()
        self._latest = None
        self._running = True
        self._thread = threading.Thread(target=self._loop, daemon=True)
        self._thread.start()

    def _loop(self):
        while self._running:
            ok, frame = self._cap.read()
            if ok:
                with self._lock:
                    self._latest = frame

    def read(self):
        with self._lock:
            if self._latest is None:
                return False, None
            return True, self._latest

    def stop(self):
        self._running = False
        self._thread.join(timeout=1.0)
        self._cap.release()


class Processor:
    """HairRecolorPipeline.processを専用スレッドで回し続ける。

    処理(数百ms/回)を表示ループから切り離すことで、元映像側は処理完了を待たずに
    カメラ本来の速さで表示できる。加工後の映像は処理が終わるたびに更新される
    (＝処理が遅い場合はその分だけ更新頻度が下がって見える。これが実際の処理速度)。
    """

    def __init__(self, pipeline: HairRecolorPipeline, grabber: FrameGrabber, panel: "ControlPanel"):
        self._pipeline = pipeline
        self._grabber = grabber
        self._panel = panel
        self._lock = threading.Lock()
        self._latest_recolored = None
        self._latest_mask = None
        self.fps_ema: float | None = None
        self._running = True
        self._thread = threading.Thread(target=self._loop, daemon=True)
        self._thread.start()

    def _apply_panel_to_config(self):
        cfg = self._pipeline.config
        panel = self._panel
        cfg.target_color_bgr = (panel.b, panel.g, panel.r)
        cfg.blend_strength = panel.blend_percent / 100.0
        cfg.confidence_threshold = panel.threshold_percent / 100.0
        cfg.boundary_radius = panel.boundary_radius
        cfg.boundary_eps = float(panel.boundary_eps)
        cfg.temporal_smoothing = panel.smoothing_percent / 100.0
        cfg.shine_strength = panel.shine_percent / 100.0
        cfg.process_width = panel.process_width
        cfg.infer_every_n = panel.infer_every_n

    def _loop(self):
        while self._running:
            ok, frame = self._grabber.read()
            if not ok:
                continue

            self._apply_panel_to_config()

            t0 = time.perf_counter()
            recolored, mask = self._pipeline.process(frame)
            dt = time.perf_counter() - t0
            instant_fps = 1.0 / dt if dt > 0 else 0.0

            with self._lock:
                self._latest_recolored = recolored
                self._latest_mask = mask
                self.fps_ema = instant_fps if self.fps_ema is None else 0.9 * self.fps_ema + 0.1 * instant_fps

    def read(self):
        """(recolored, mask, fps) を返す。まだ1回も処理が終わっていなければ (None, None, None)。"""
        with self._lock:
            return self._latest_recolored, self._latest_mask, self.fps_ema

    def set_grabber(self, grabber: FrameGrabber):
        """カメラ切り替え時に、読み取り元のFrameGrabberを差し替える。"""
        self._grabber = grabber

    def stop(self):
        self._running = False
        self._thread.join(timeout=2.0)


def save_config(config: RecolorConfig, camera_index: int) -> None:
    data = {
        "target_color_bgr": list(config.target_color_bgr),
        "confidence_threshold": config.confidence_threshold,
        "boundary_radius": config.boundary_radius,
        "boundary_eps": config.boundary_eps,
        "blend_strength": config.blend_strength,
        "temporal_smoothing": config.temporal_smoothing,
        "shine_strength": config.shine_strength,
        "process_width": config.process_width,
        "infer_every_n": config.infer_every_n,
        "camera_index": camera_index,
    }
    TUNED_CONFIG_PATH.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"保存しました: {TUNED_CONFIG_PATH}（main.py実行時に自動で読み込まれます）")


def parse_args():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--width", type=int, default=1280)
    parser.add_argument("--height", type=int, default=720)
    parser.add_argument("--fps", type=int, default=30)
    parser.add_argument("--display-max-width", type=int, default=1600, help="プレビュー表示の最大幅(処理解像度には影響しない)")
    return parser.parse_args()


def main():
    args = parse_args()
    base_config, tuned_camera_index = load_tuned_config()
    if tuned_camera_index is not None:
        print(f"tuned_config.json を読み込みました（前回の調整値を復元。camera_index={tuned_camera_index}）")

    cameras = list_cameras()
    print(f"検出したカメラ index: {cameras}")

    # WINDOW_KEEPRATIO: ウィンドウを手動でリサイズしても映像のアスペクト比を保つ
    cv2.namedWindow(WINDOW, cv2.WINDOW_NORMAL | cv2.WINDOW_KEEPRATIO)

    panel = ControlPanel(cameras, base_config, initial_camera=tuned_camera_index)
    current_cam_index = panel.selected_camera
    grabber = FrameGrabber(open_camera(current_cam_index, args.width, args.height, args.fps))

    print("カメラ・パラメータは「パラメータ調整」ウィンドウで操作。プレビューウィンドウで 'q'終了 / 's'保存")
    print("左(元映像)はカメラ本来の速さで更新される。右(加工後)は処理が終わるたびに更新されるため、")
    print("右側の更新頻度が実際の処理速度そのもの。")

    display_fps_ema = None
    last_report = time.perf_counter()
    displayed_frames = 0

    with HairRecolorPipeline(MODEL_PATH, base_config) as pipeline:
        processor = Processor(pipeline, grabber, panel)
        try:
            while True:
                selected_cam = panel.selected_camera
                if selected_cam != current_cam_index:
                    grabber.stop()
                    grabber = FrameGrabber(open_camera(selected_cam, args.width, args.height, args.fps))
                    processor.set_grabber(grabber)
                    current_cam_index = selected_cam

                t_display0 = time.perf_counter()
                ok, frame = grabber.read()
                if not ok:
                    continue

                recolored, _mask, process_fps = processor.read()
                if recolored is None or recolored.shape != frame.shape:
                    # 処理側がまだ1回も終わっていない、または解像度切り替え直後
                    recolored = frame

                side_by_side = cv2.hconcat([frame, recolored])

                # display-max-widthを超える場合、縦横同倍率で縮小してアスペクト比を保つ
                if side_by_side.shape[1] > args.display_max_width:
                    scale = args.display_max_width / side_by_side.shape[1]
                    side_by_side = cv2.resize(side_by_side, None, fx=scale, fy=scale)
                cv2.imshow(WINDOW, side_by_side)

                display_dt = time.perf_counter() - t_display0
                instant_display_fps = 1.0 / display_dt if display_dt > 0 else 0.0
                display_fps_ema = (
                    instant_display_fps if display_fps_ema is None else 0.9 * display_fps_ema + 0.1 * instant_display_fps
                )
                panel.update_fps(process_fps or 0.0, display_fps_ema)
                displayed_frames += 1

                now = time.perf_counter()
                if now - last_report >= 2.0:
                    print(
                        f"[表示fps(元映像側の更新速度)={display_fps_ema:.1f}]  "
                        f"[処理fps(加工後の更新速度)={(process_fps or 0):.1f}]"
                    )
                    last_report = now
                    displayed_frames = 0

                key = cv2.waitKey(1) & 0xFF
                if key == ord("q"):
                    break
                elif key == ord("s"):
                    save_config(pipeline.config, current_cam_index)
        finally:
            processor.stop()

    grabber.stop()
    cv2.destroyAllWindows()


if __name__ == "__main__":
    main()
