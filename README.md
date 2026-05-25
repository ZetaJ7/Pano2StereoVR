# Pano2StereoVR

Unity OpenXR viewer for VR subjective experiments driven by the `Pano2Stereo` Python pipeline.

## Scope

- Read SBS RGB frames from named shared memory.
- Render per-eye ERP panorama in HMD.
- Send `u0`, `mode`, and IPD control packets over UDP.
- Keep protocol aligned with `docs/protocol.md` in the Python repo.

## Recommended Unity Version

- `Unity 2022.3.62f3` (if Hub shows `2022.3.62f3c1`, use that build)

## Repository Layout

```
Pano2StereoVR/
├── Assets/
│   ├── Scenes/
│   ├── Scripts/
│   └── Shaders/
├── Packages/
├── ProjectSettings/
└── docs/
```

## Quick Start

1. Open this folder with Unity Hub.
2. Install dependencies from `Packages/manifest.json`.
3. Create a scene and attach:
   - `SharedMemoryReceiver`
   - `StereoSphereRenderer`
   - `HeadPoseTracker`
   - `UdpGazeSender`
   - `ExperimentController`
4. Use shader `Pano2Stereo/StereoPanorama` on an inverted sphere material.
5. Start Python server with shared memory + UDP enabled:
   - `python src/pano2stereo.py --source Data/test1.mp4 --provider da2 --repair-method hachaj_fast_gpu --downsample 1080 --output-method shm --shm-name pano2stereo --gaze-udp-port 50051 --experiment-mode 3 --shm-fps-cap 0 --shm-fps-cap-fast 0 --experiment-logging`

## Current Runtime Conditions

- Protocol smoke (G2) validated in Python repo.
- This repo provides G3 Unity MVP scaffolding and core script skeletons.
- G3 acceptance execution checklist: `docs/g3_acceptance.md`.
- Performance backlog / TODO: `docs/TODO.md`.
- Paper condition mapping: `Baseline` = mode `4`, `Pose-agnostic` = mode `2`, `Pose-aware` = mode `3`. Mode `1` remains `Mono` for internal/debug use.
- In `Baseline`, the RTSP URL starts empty; enter a stream address at runtime, then click `Apply` or press `Enter` to reconnect.
- If `Baseline` has no URL or the stream cannot be opened, the overlay shows an explicit warning prompt.
- `Baseline` ffmpeg receive path enables low-latency RTSP input options by default (`direct` I/O, reduced probe/analyze delay, zero max-delay, zero reorder queue).
- Runtime resolution presets: `1080` (`2184x1092`), `2K` (`2884x1442`), and `4K` (`5768x2884`). Use overlay buttons or `F5`/`F6` to cycle. In SHM modes, restart Python with matching `--downsample`; in `Baseline`, the RTSP receiver restarts with the selected output size.
