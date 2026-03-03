# TODO

## Performance Follow-up (Post G3/G4)

- Keep current IPC path as-is for now: Python writes SBS frames to CPU shared memory,
  Unity reads from shared memory and uploads to GPU texture.
- Evaluate and prototype a GPU-first path after G3/G4 are fully passed:
  - Windows D3D11 shared texture between Python process and Unity process
  - Native Unity plugin (C++) for external texture import and synchronization
  - Replace per-frame GPU→CPU→GPU transfer with GPU-native transfer
- Goal: reduce additional transfer latency and frame-time jitter introduced by CPU round-trip.
- Priority: **P2 (after paper validation milestones)**.
