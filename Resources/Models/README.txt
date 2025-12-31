This folder is intended to ship the face recognition models with the app.

Required files (ONNX):
- face_detection_yunet_2023mar.onnx
- face_recognition_sface_2021dec.onnx

Runtime behavior:
- If these files are present as MauiAssets, the app copies them to its cache directory on first use.
- If they are not embedded, the app will attempt to download them from a stable KDE mirror.
