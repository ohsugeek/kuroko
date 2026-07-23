"""
MediaPipeのSelfie Multiclassセグメンテーションモデルを使い、
Webカメラ映像から「髪の毛」領域だけをリアルタイムで検出し、
指定した色（既定=黒髪）へ自然に近づけるコア処理。

Selfie Multiclassモデルのクラス構成（公式仕様）:
  0: background（背景）
  1: hair（髪）
  2: body-skin（体の肌）
  3: face-skin（顔の肌）
  4: clothes（服）
  5: others（アクセサリ等）
"""

from __future__ import annotations

import json
from dataclasses import dataclass
from pathlib import Path

import cv2
import mediapipe as mp
import numpy as np
from mediapipe.tasks.python import vision
from mediapipe.tasks.python.core.base_options import BaseOptions

HAIR_CLASS_INDEX = 1


@dataclass
class RecolorConfig:
    # 目標の髪色（BGR）。既定はやや青みがかった自然な黒髪
    target_color_bgr: tuple = (25, 20, 18)
    # マスクの信頼度しきい値（これ未満は髪として扱わない）
    confidence_threshold: float = 0.65
    # 境界のedge-aware平滑化(guided filter)の半径。0で無効。
    # 元映像を手がかりに平滑化するため、後れ毛の輪郭を保ちつつノイズだけ抑えられる
    boundary_radius: int = 8
    # guided filterのeps。大きいほど強く平滑化される(境界を跨ぎやすくなる)
    boundary_eps: float = 200.0
    # 色の乗せ方の強さ（1.0で完全に目標色へブレンド、0で元映像のまま）
    blend_strength: float = 0.55
    # 時間方向の安定化(0=無効、1に近いほど過去フレームを強く保持しチラつきを抑える)
    temporal_smoothing: float = 0.5
    # ツヤの残し方(0=乗算のみ、1=オーバーレイのみ)。
    # 乗算だけだとハイライト(反射光)まで目標色の暗さに潰れてツヤが消えるため、
    # オーバーレイを混ぜて明部を明るく残す
    shine_strength: float = 0.6
    # セグメンテーション・境界平滑化を計算する際の処理幅(px)。
    # 撮影解像度がこれより大きい場合は縮小して計算し、マスクだけ最終的に元解像度へ戻す。
    # 最終的な色合成(_recolor)は常に元解像度で行うため画質はほぼ落ちず、処理は大幅に軽くなる。
    process_width: int = 640
    # 何フレームに1回セグメンテーション推論を行うか(1=毎フレーム)。
    # MediaPipeの推論自体がボトルネックになりやすいため、間引いた分は直前の推論結果の
    # マスクを再利用する。大きくするほど出力fpsは上がるが、動きへの追従がやや遅れる
    infer_every_n: int = 2


TUNED_CONFIG_PATH = Path("tuned_config.json")


def load_tuned_config() -> tuple[RecolorConfig, int | None]:
    """tune_gui.pyで保存した tuned_config.json があれば読み込む。

    Returns:
        (config, camera_index): camera_indexは未保存ならNone
    """
    defaults = RecolorConfig()
    if not TUNED_CONFIG_PATH.exists():
        return defaults, None

    data = json.loads(TUNED_CONFIG_PATH.read_text(encoding="utf-8"))
    config = RecolorConfig(
        target_color_bgr=tuple(data.get("target_color_bgr", defaults.target_color_bgr)),
        confidence_threshold=data.get("confidence_threshold", defaults.confidence_threshold),
        boundary_radius=data.get("boundary_radius", defaults.boundary_radius),
        boundary_eps=data.get("boundary_eps", defaults.boundary_eps),
        blend_strength=data.get("blend_strength", defaults.blend_strength),
        temporal_smoothing=data.get("temporal_smoothing", defaults.temporal_smoothing),
        shine_strength=data.get("shine_strength", defaults.shine_strength),
        process_width=data.get("process_width", defaults.process_width),
        infer_every_n=data.get("infer_every_n", defaults.infer_every_n),
    )
    return config, data.get("camera_index")


class HairRecolorPipeline:
    def __init__(self, model_path: str, config: RecolorConfig | None = None):
        self.config = config or RecolorConfig()
        options = vision.ImageSegmenterOptions(
            base_options=BaseOptions(model_asset_path=model_path),
            running_mode=vision.RunningMode.IMAGE,
            output_category_mask=False,
            output_confidence_masks=True,
        )
        self._segmenter = vision.ImageSegmenter.create_from_options(options)
        self._prev_mask: np.ndarray | None = None
        self._frame_counter = 0

    def close(self):
        self._segmenter.close()

    def __enter__(self):
        return self

    def __exit__(self, *_exc):
        self.close()

    def process(self, frame_bgr: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
        """1フレームを処理する。

        MediaPipeの推論コストが支配的なため、infer_every_n で間引き、間引いたフレームは
        直前のマスクを再利用する。推論するフレームでは process_width まで縮小して計算し、
        最後に元解像度へアップスケールしてから色合成する（速度優先、画質劣化は僅少）。

        Returns:
            (recolored_bgr, hair_mask_0to1): 加工後フレームと髪マスク（デバッグ表示用。元解像度）
        """
        cfg = self.config
        h, w = frame_bgr.shape[:2]

        should_infer = (
            cfg.infer_every_n <= 1
            or self._prev_mask is None
            or self._prev_mask.shape != (h, w)
            or self._frame_counter % cfg.infer_every_n == 0
        )
        self._frame_counter += 1

        if should_infer:
            if 0 < cfg.process_width < w:
                scale = cfg.process_width / w
                small = cv2.resize(
                    frame_bgr, (cfg.process_width, round(h * scale)), interpolation=cv2.INTER_AREA
                )
            else:
                small = frame_bgr

            rgb = cv2.cvtColor(small, cv2.COLOR_BGR2RGB)
            mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)

            result = self._segmenter.segment(mp_image)
            hair_mask = result.confidence_masks[HAIR_CLASS_INDEX].numpy_view().copy()

            # しきい値以下を0にしてノイズを抑える
            hair_mask = np.where(hair_mask >= cfg.confidence_threshold, hair_mask, 0.0)

            # 境界をedge-aware(guided filter)で平滑化。元映像(縮小版)をガイドにするため、
            # 単純なガウシアンぼかしと違い後れ毛の輪郭を保ったままノイズだけ抑えられる
            if cfg.boundary_radius > 0:
                gray_small = cv2.cvtColor(small, cv2.COLOR_BGR2GRAY)
                hair_mask = cv2.ximgproc.guidedFilter(
                    guide=gray_small,
                    src=hair_mask.astype(np.float32),
                    radius=cfg.boundary_radius,
                    eps=cfg.boundary_eps,
                )
                hair_mask = np.clip(hair_mask, 0.0, 1.0)

            # 色合成は常に元解像度で行うため、マスクをここでアップスケールする
            if hair_mask.shape != (h, w):
                hair_mask = cv2.resize(hair_mask, (w, h), interpolation=cv2.INTER_LINEAR)

            # 時間方向の安定化。前フレームのマスクとEMAでブレンドし、静止時のチラつきを抑える
            if cfg.temporal_smoothing > 0 and self._prev_mask is not None and self._prev_mask.shape == hair_mask.shape:
                hair_mask = cfg.temporal_smoothing * self._prev_mask + (1 - cfg.temporal_smoothing) * hair_mask
            self._prev_mask = hair_mask.copy()
        else:
            # 推論をスキップしたフレームは直前の推論結果のマスクをそのまま再利用する
            hair_mask = self._prev_mask

        recolored = self._recolor(frame_bgr, hair_mask)
        return recolored, hair_mask

    def _recolor(self, frame_bgr: np.ndarray, hair_mask: np.ndarray) -> np.ndarray:
        cfg = self.config
        frame_f = frame_bgr.astype(np.float32)
        l = frame_f / 255.0
        t = (np.array(cfg.target_color_bgr, dtype=np.float32) / 255.0)[None, None, :]

        # 乗算(multiply): 暗部は自然に暗くなるが、暗い目標色だとハイライト(ツヤ)まで
        # 目標色の暗さへ潰れてしまう(result = original * target なので上限が target)
        multiplied = l * t

        # オーバーレイ: 暗部は乗算と同様に暗くしつつ、明部(=ツヤ・反射光)はスクリーン側の
        # 明るさを残すため、艶っぽい見え方を保てる
        overlay = np.where(l < 0.5, 2 * l * t, 1 - 2 * (1 - l) * (1 - t))

        shine = cfg.shine_strength
        base = ((1 - shine) * multiplied + shine * overlay) * 255.0

        alpha = (hair_mask * cfg.blend_strength)[..., None]
        blended = frame_f * (1 - alpha) + base * alpha

        return np.clip(blended, 0, 255).astype(np.uint8)
