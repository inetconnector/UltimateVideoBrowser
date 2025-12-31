This folder is intended to ship the face recognition models with the app.

Required files (ONNX):
- face_detection_yunet_2023mar.onnx
- face_recognition_sface_2021dec.onnx

Optional file (download fallback only):
- postproc_yunet_top50_th6_320x320.onnx

Runtime behavior:
- If these files are present as MauiAssets, the app copies them to its cache directory on first run.
- If they are not embedded, the app will attempt to download them from a stable mirror.
