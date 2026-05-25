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
  - `RtspBaselineReceiver` (for `Baseline`, mode 4)
  - `BaselinePanoramaRenderer` (for `Baseline`, mode 4)

## 3) Protocol Parameters

- Shared memory name: `pano2stereo`
- UDP target host: `127.0.0.1`
- UDP target port: `50051`
- UDP payload fields: `u0`, `mode`, and optional `ipd`
- Mode values over protocol: `1` (`Mono`), `2` (`Pose-agnostic`), `3` (`Pose-aware`)
- Paper baseline mode: `4` (`Baseline`, Unity RTSP ingest, no SHM/UDP dependency)

## 3.1) Runtime Resolution Presets

- `1080`: `2184x1092`, Python argument `--downsample 1080`
- `2K`: `2884x1442`, Python argument `--downsample 2k`
- `4K`: `5768x2884`, Python argument `--downsample 4k`
- Use overlay buttons `1080` / `2K` / `4K` or hotkeys `F5` / `F6`.
- In SHM modes (`1`/`2`/`3`), Unity cannot resize the producer-side named shared memory. After selecting a preset, restart Python with the matching `--downsample` value.
- In `Baseline` mode (`4`), Unity applies the selected preset to `RtspBaselineReceiver.outputWidth` / `outputHeight` and restarts ffmpeg if the receiver is running.

## 3.2) Baseline RTSP Settings

Configure `RtspBaselineReceiver` in Inspector:
- `ffmpegExecutable`: `ffmpeg` (or absolute path to `ffmpeg.exe`)
- `rtspUrl`: e.g. `rtsp://10.20.35.30:28552/test`
- `outputWidth` / `outputHeight`: normally controlled by the runtime resolution preset
- `preferTcpTransport`: on (recommended for stability)
- `maxDecodeFps`: `0` keeps source FPS, non-zero limits decode FPS

Optional runtime overrides (player startup):
- Command line: `--rtsp-url rtsp://...` or `--rtsp-url=rtsp://...`
- Command line: `--ffmpeg-exe C:/tools/ffmpeg/bin/ffmpeg.exe`
- Environment variable: `P2SVR_RTSP_URL`
- Environment variable: `P2SVR_FFMPEG_EXE`

Press `4` in Play mode:
- switches to paper `Baseline` path,
- disables SHM receiver and UDP sender,
- renders mono ERP directly on the sphere.

## 4) Python Launch Example

```powershell
python src/pano2stereo.py `
  --source Data/test1.mp4 `
  --provider da2 `
  --repair-method hachaj_fast_gpu `
  --downsample 1080 `
  --output-method shm `
  --shm-name pano2stereo `
  --gaze-udp-port 50051 `
  --experiment-mode 3 `
  --shm-fps-cap 0 `
  --shm-fps-cap-fast 0 `
  --experiment-logging `
  --participant-id P01 `
  --clip-id test1 `
  --trial-id T01
```

## 5) Smoke Checklist

- Shared memory frame updates are visible in HMD.
- No torn frame usage in Unity receiver.
- Keyboard `1/2/3` changes SHM/UDP-backed modes: `Mono`, `Pose-agnostic`, `Pose-aware`.
- Keyboard `4` switches to paper `Baseline` mode.
- Overlay resolution preset matches Python `--downsample` in modes `1/2/3`.
- Keyboard `F5/F6` or overlay buttons switch `1080/2K/4K` presets.
- Python `experiment_log.jsonl` contains `mode_switch` events.
- In mode `4`, Python is not required to run.

## 6) G3 Acceptance

- Detailed gate-`G3` runbook: `docs/g3_acceptance.md`
- Includes:
  - `requested/sent/applied` overlay verification for mode switching
  - cardinal mapping check using `HeadPoseTracker` debug hotkeys (`F1/F2/F3/F4`)
  - artifact collection checklist for gate review

