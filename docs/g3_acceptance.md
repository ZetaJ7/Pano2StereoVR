# G3 Acceptance Runbook

This runbook covers the next executable step for gate `G3`:
- mode switch verification with explicit `requested/sent/applied` signals,
- coordinate mapping cardinal-marker validation.

Unity baseline for this runbook: `2022.3.62f3` (Hub variant `2022.3.62f3c1`).

## 1) Scene wiring checklist

Create scene `Assets/Scenes/VRExperiment.unity` and wire components:

- `SharedMemoryReceiver` on an empty object, `shmName=pano2stereo`.
- `StereoSphereRenderer` on the inward sphere renderer, reference `SharedMemoryReceiver`.
- `HeadPoseTracker` on XR camera (or another object), bind `headTransform` to HMD camera.
- `UdpGazeSender` on an empty object:
  - `poseTracker` -> `HeadPoseTracker`
  - `host=127.0.0.1`, `port=50051`
- `ExperimentController` on an empty object:
  - `sharedMemoryReceiver` -> `SharedMemoryReceiver`
  - `udpGazeSender` -> `UdpGazeSender`
  - `headPoseTracker` -> `HeadPoseTracker`

## 2) B-step: mode switch verification

Run Python server first:

```powershell
python src/pano2stereo.py `
  --source Data/test1.mp4 `
  --output-method shm `
  --shm-name pano2stereo `
  --gaze-udp-port 50051 `
  --experiment-mode 3 `
  --experiment-logging
```

In Unity Play mode, press `1/2/3`.

Expected overlay behavior in `ExperimentController`:
- `Requested`: updates immediately on key press.
- `Sent`: updates when UDP `{"mode":N}` is sent.
- `Applied`: updates when SHM frame mode changes.
- `Last apply latency`: shows request-to-apply milliseconds.
- `Timeout`: appears only when `applyTimeoutSeconds` is exceeded.

Evidence files:
- Unity local log: `Application.persistentDataPath/g3_mode_validation.jsonl`
- Python log: `output/streaming/run_xxx/experiment_log.jsonl` (`mode_switch` events)

Acceptance for this part:
- repeated `1/2/3` switching produces `mode_requested -> mode_sent -> mode_applied`,
- timeout count remains `0` in normal conditions.

## 3) C-step: coordinate mapping cardinal check

Enable `HeadPoseTracker.enableDebugOverrideHotkeys`.

In Play mode:
- `F1` forces `u0=(1,0,0)` (center marker expected ahead).
- `F2` forces `u0=(0,1,0)` (left marker expected on left).
- `F3` forces `u0=(0,0,1)` (top marker expected upward).
- `F4` clears override and returns to live HMD tracking.

Use the `ExperimentController` overlay `u0` line to confirm active vector and source (`DebugOverride` or `HMD`).

Acceptance for this part:
- all three cardinal vectors map to expected ERP marker positions consistently.

## 4) Minimal artifact package for G3 review

- Screenshot or short capture showing overlay transitions during `1/2/3`.
- `g3_mode_validation.jsonl` snippet with request/sent/applied sequence.
- Python `experiment_log.jsonl` snippet with corresponding `mode_switch`.
- Short note for cardinal check results (`F1/F2/F3` all pass or exact failure case).
