# Setup Guide

## 1) Unity Environment

- Unity: `2022.3 LTS`
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

## 3) Protocol Parameters

- Shared memory name: `pano2stereo`
- UDP target host: `127.0.0.1`
- UDP target port: `50051`
- Mode values: `1`, `2`, `3`

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
- Keyboard `1/2/3` changes mode.
- Python `experiment_log.jsonl` contains `mode_switch` events.
