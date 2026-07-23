#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""OSS髪セグメンテーション(BiSeNet face-parsing, ONNX)のde-risk計測。

目的: Quadro P600 で DirectML(GPU) 推論が実時間(>=~15fps)に出るか、
      髪マスクの見た目が使えるかを実測する。Banuba代替の可否判断用。
"""
import time
import cv2
import numpy as np
import onnxruntime as ort

MODEL = "models/resnet18.onnx"
SIZE = 512
HAIR_CLASS = 17  # このモデル(yakhyo BiSeNet)の実測での髪インデックス(標準の13ではない)

MEAN = np.array([0.485, 0.456, 0.406], np.float32)
STD = np.array([0.229, 0.224, 0.225], np.float32)


def preprocess(bgr):
    img = cv2.resize(bgr, (SIZE, SIZE))
    rgb = cv2.cvtColor(img, cv2.COLOR_BGR2RGB).astype(np.float32) / 255.0
    rgb = (rgb - MEAN) / STD
    return np.transpose(rgb, (2, 0, 1))[None].astype(np.float32)


def make(providers):
    return ort.InferenceSession(MODEL, providers=providers)


def main():
    info = make(["CPUExecutionProvider"])
    inp = info.get_inputs()[0]
    out = info.get_outputs()[0]
    print(f"input : {inp.name} {inp.shape} {inp.type}")
    print(f"output: {out.name} {out.shape} {out.type}")

    # カメラ(BRIO=index 1)から1フレーム取得。取れなければグレー画像
    frame = None
    cap = cv2.VideoCapture(1, cv2.CAP_DSHOW)
    if cap.isOpened():
        for _ in range(10):
            ok, f = cap.read()
            if ok and f is not None:
                frame = f
    cap.release()
    if frame is None:
        frame = np.full((720, 1280, 3), 127, np.uint8)
        print("WARN: カメラから取得できず。グレー画像で計測(品質画像は無意味)")
    else:
        print(f"captured frame: {frame.shape}")

    x = preprocess(frame)

    def bench(providers, name, n=50):
        sess = make(providers)
        iname = sess.get_inputs()[0].name
        for _ in range(5):
            sess.run(None, {iname: x})  # warmup
        t = time.perf_counter()
        for _ in range(n):
            res = sess.run(None, {iname: x})
        dt = (time.perf_counter() - t) / n
        print(f"{name:16s}: {dt*1000:6.1f} ms/inf  ->  {1/dt:5.1f} fps")
        return sess, res

    dml_sess, _ = bench(["DmlExecutionProvider"], "DirectML(GPU)")
    try:
        bench(["CPUExecutionProvider"], "CPU")
    except Exception as e:
        print(f"CPU bench skipped: {e}")

    # 品質: 髪マスクを重畳して保存
    iname = dml_sess.get_inputs()[0].name
    logits = dml_sess.run(None, {iname: x})[0]  # [1,19,512,512]
    cls = np.argmax(logits[0], axis=0).astype(np.uint8)
    mask = (cls == HAIR_CLASS).astype(np.uint8) * 255
    mask_full = cv2.resize(mask, (frame.shape[1], frame.shape[0]), interpolation=cv2.INTER_LINEAR)
    overlay = frame.copy()
    sel = mask_full > 127
    overlay[sel] = (0.45 * overlay[sel] + 0.55 * np.array([180, 90, 220])).astype(np.uint8)
    cv2.imwrite("hair_overlay.png", overlay)
    cv2.imwrite("hair_mask.png", mask_full)
    print(f"saved hair_overlay.png / hair_mask.png ; hair pixels = {int(sel.sum())}")


if __name__ == "__main__":
    main()
