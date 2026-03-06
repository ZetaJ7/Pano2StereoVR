# Setup Guide

## 1) Unity Environment

- Unity: `2022.3.62f3` (Hub variant `2022.3.62f3c1` is acceptable)
- XR Plugin: OpenXR
- Runtime: SteamVR OpenXR runtime (or your target OpenXR runtime)

## 2) Scene Baseline

- Create an inverted sphere around camera rig.
- Assign `Pano2Stereo/StereoPanorama` material on the sphere.
- Add these scripts to scene objects:
  - `SharedMemoryReceiver`
  - `StereoSphereRenderer`
  - `HeadPoseTracker`
  - `UdpGazeSender`
  - `ExperimentController`
  - `RtspBaselineReceiver` (for local baseline mode 4)
  - `BaselinePanoramaRenderer` (for local baseline mode 4)

## 3) Protocol Parameters

- Shared memory name: `pano2stereo`
- UDP target host: `127.0.0.1`
- UDP target port: `50051`
- Mode values over protocol: `1`, `2`, `3`
- Local-only baseline mode: `4` (Unity RTSP ingest, no SHM/UDP dependency)

## 3.1) Mode 4 RTSP Baseline Settings

Configure `RtspBaselineReceiver` in Inspector:
- `ffmpegExecutable`: `ffmpeg` (or absolute path to `ffmpeg.exe`)
- `rtspUrl`: e.g. `rtsp://10.20.35.30:28552/test`
- `outputWidth` / `outputHeight`: set to the stream target ERP size
- `preferTcpTransport`: on (recommended for stability)
- `maxDecodeFps`: `0` keeps source FPS, non-zero limits decode FPS

Optional runtime overrides (player startup):
- Command line: `--rtsp-url rtsp://...` or `--rtsp-url=rtsp://...`
- Command line: `--ffmpeg-exe C:/tools/ffmpeg/bin/ffmpeg.exe`
- Environment variable: `P2SVR_RTSP_URL`
- Environment variable: `P2SVR_FFMPEG_EXE`

Press `4` in Play mode:
- switches to local RTSP baseline path,
- disables SHM receiver and UDP sender,
- renders mono ERP directly on the sphere.

## 4) Python Launch Example

```powershell
python src/pano2stereo.py `
  --source Data/test1.mp4 `
  --output-method shm `
  --shm-name pano2stereo `
  --gaze-udp-port 50051 `
  --experiment-mode 3 `
  --experiment-logging `
  --participant-id P01 `
  --clip-id test1 `
  --trial-id T01
```

## 5) Smoke Checklist

- Shared memory frame updates are visible in HMD.
- No torn frame usage in Unity receiver.
- Keyboard `1/2/3` changes SHM/UDP-backed modes.
- Keyboard `4` switches to local RTSP baseline mode.
- Python `experiment_log.jsonl` contains `mode_switch` events.
- In mode `4`, Python is not required to run.

## 6) G3 Acceptance

- Detailed gate-`G3` runbook: `docs/g3_acceptance.md`
- Includes:
  - `requested/sent/applied` overlay verification for mode switching
  - cardinal mapping check using `HeadPoseTracker` debug hotkeys (`F1/F2/F3/F4`)
  - artifact collection checklist for gate review

