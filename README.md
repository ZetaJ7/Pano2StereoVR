# Pano2StereoVR

Unity OpenXR viewer for VR subjective experiments driven by the `Pano2Stereo` Python pipeline.

## Scope

- Read SBS RGB frames from named shared memory.
- Render per-eye ERP panorama in HMD.
- Send `u0` + `mode` control packets over UDP.
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
   - `python src/pano2stereo.py --source Data/test1.mp4 --output-method shm --shm-name pano2stereo --gaze-udp-port 50051 --experiment-logging`

## Current Baseline

- Protocol smoke (G2) validated in Python repo.
- This repo provides G3 Unity MVP scaffolding and core script skeletons.
- G3 acceptance execution checklist: `docs/g3_acceptance.md`.
- Performance backlog / TODO: `docs/TODO.md`.
- In `Mode4`, the RTSP URL starts empty; enter a stream address at runtime, then click `Apply` or press `Enter` to reconnect.
- If `Mode4` has no URL or the stream cannot be opened, the overlay shows an explicit warning prompt.
- `Mode4` ffmpeg receive path now enables low-latency RTSP input options by default (`direct` I/O, reduced probe/analyze delay, zero max-delay, zero reorder queue).
