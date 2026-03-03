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
  --shm-fps-cap 16 `
  --shm-fps-cap-fast 12 `
  --gaze-udp-port 50051 `
  --experiment-mode 3 `
  --save-interval 0 `
  --experiment-logging
```

In Unity Play mode, press `1/2/3`.

Expected overlay behavior in `ExperimentController`:
- `Requested`: updates immediately on key press.
- `Sent`: updates when UDP `{"mode":N}` is sent.
- `Applied`: updates when SHM frame mode changes.
- Re-pressing the currently applied mode is treated as a no-op and logged as `mode_requested` with detail `keyboard_already_applied` (no timeout expected).
- `Last apply latency`: shows request-to-apply milliseconds.
- `Timeout`: appears only when `applyTimeoutSeconds` is exceeded.
- `Perf`: shows `unity_fps`, `shm_recv_fps` (accepted/read FPS), and `shm_seq_fps` (writer-side observed FPS).
- If `mode 1/2` appears much choppier than mode 3 in Unity, compare `shm_recv_fps` vs `shm_seq_fps`:
  - `shm_seq_fps` high + `shm_recv_fps` low usually means reader-side seqlock contention (not Python generation slowdown).
  - Use `--shm-fps-cap 16 --shm-fps-cap-fast 12` to stabilize reader acceptance on Windows.
- `Render`: shows sphere renderer status (`target/enabled/visible/tex_bound`) and bound texture size.
- `SHM Preview`: bottom-right direct preview of shared-memory texture (for quick debug even when sphere path is wrong).
- `Orientation`: shows `flip_x/flip_y/swap_eyes`; use `F7` to toggle `flip_x`, `F8` to toggle `swap_eyes`.

Evidence files:
- Unity local log: `Application.persistentDataPath/g3_mode_validation.jsonl`
  - mode chain: `mode_requested / mode_sent / mode_applied`
  - u0 override chain: `u0_override_set / u0_override_cleared` with `u0_x/u0_y/u0_z/u0_source`
- Python log: `output/streaming/run_xxx/experiment_log.jsonl` (`mode_switch` events)
  - gaze apply events in mode 3: `u0_applied` with `u0=[x,y,z]`

Acceptance for this part:
- repeated `1/2/3` switching produces `mode_requested -> mode_sent -> mode_applied`,
- timeout count remains `0` in normal conditions.

If Game view has no panorama but SHM counters increase:
- check `Render.visible`; if `no`, camera/sphere relation is wrong.
- check `Render.tex_bound`; if `no`, texture bind path is wrong.
- verify `SHM Preview` is updating; if yes, SHM ingest is healthy and issue is render placement/material.

## 3) C-step: coordinate mapping cardinal check

Enable `HeadPoseTracker.enableDebugOverrideHotkeys`.

In Play mode:
- `F1` forces `u0=(1,0,0)` (center marker expected ahead).
- `F2` forces `u0=(0,1,0)` (left marker expected on left).
- `F3` forces `u0=(0,0,1)` (top marker expected upward).
- `F4` clears override and returns to live HMD tracking.

Use the `ExperimentController` overlay `u0` line to confirm active vector and source (`DebugOverride` or `HMD`).
No-screenshot acceptance is allowed if logs show:
- Unity `u0_override_set` events for `F1_center/F2_left/F3_top` with matching `u0_x/y/z`.
- Unity `u0_override_cleared` after `F4`.
- Python `u0_applied` events (mode 3) with corresponding vectors.

Acceptance for this part:
- all three cardinal vectors map to expected ERP marker positions consistently.

## 4) Minimal artifact package for G3 review

- Screenshot or short capture showing overlay transitions during `1/2/3`.
- `g3_mode_validation.jsonl` snippet with request/sent/applied sequence.
- Python `experiment_log.jsonl` snippet with corresponding `mode_switch`.
- For cardinal check, either screenshot proof or log-only proof (`u0_override_*` + `u0_applied`).

## 5) Execution Record (2026-03-03, Windows)

Test context:
- Unity: `2022.3.62f3c1`
- Unity log: `C:/Users/admin/AppData/LocalLow/DefaultCompany/Pano2StereoVR/g3_mode_validation.jsonl`
- Python run log: `D:/WorkSpace/Pano2Stereo/output/streaming/run_014/experiment_log.jsonl`

B-step verification summary:
- Historical issue reproduced before fix: re-pressing current mode caused timeout
  (`2026-03-03T08:00:23Z`, `2026-03-03T08:00:40Z`).
- Fix applied: same-mode key press becomes no-op (`keyboard_already_applied`) and does not enter pending state.
- Post-fix retest session (`2026-03-03T08:06:48Z` -> `2026-03-03T08:07:35Z`):
  - repeated same-mode presses logged as `keyboard_already_applied` for mode `3/1/2`;
  - no new `mode_timeout` in this session;
  - real switches still apply normally:
    - `3 -> 1` latency `163.948 ms`
    - `1 -> 2` latency `56.664 ms`
    - `2 -> 3` latency `138.218 ms`

C-step verification summary (log-only):
- Unity has complete override chain:
  - `u0_override_set preset=F1_center` with `(1,0,0)`
  - `u0_override_set preset=F2_left` with `(0,1,0)`
  - `u0_override_set preset=F3_top` with `(0,0,1)`
  - `u0_override_cleared` after `F4`
- Python has matching `u0_applied` records in mode 3:
  - `[1.0, 0.0, 0.0]`
  - `[0.0, 1.0, 0.0]`
  - `[0.0, 0.0, 1.0]`
  - fallback to `[1.0, 0.0, 0.0]` after clear

Gate decision:
- `G3` is **PASS** under the documented log-based acceptance criteria.
