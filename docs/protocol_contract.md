# Protocol Contract

This Unity repo follows the communication contract defined in:

- `Pano2Stereo/docs/protocol.md`

Current contract highlights:

- Paper condition mapping:
  - `Baseline`: Unity mode `4`, RTSP ingest, no SHM/UDP dependency
  - `Pose-agnostic`: protocol mode `2`, stereo generation with gaze correction disabled
  - `Pose-aware`: protocol mode `3`, stereo generation with HMD/gaze correction enabled
  - `Mono`: protocol mode `1`, retained as an internal/debug condition
- UDP JSON:
  - `{"u0":[x,y,z]}`
  - `{"mode":2}`
  - `{"u0":[x,y,z],"mode":2}`
  - `{"ipd":0.065}`
  - `{"u0":[x,y,z],"ipd":0.065}`
  - `{"u0":[x,y,z],"mode":2,"ipd":0.065}`
- Shared memory layout:
  - `seq_begin` at offset `0` (`uint64`, even=stable, odd=writing)
  - `width` at offset `8` (`uint32`)
  - `height` at offset `12` (`uint32`)
  - `mode` at offset `16` (`uint32`)
  - `seq_end` at offset `20` (`uint64`)
  - pixel bytes from offset `28`
- SHM pacing is controlled on the Python side:
  - `--shm-fps-cap` applies to mode `3`
  - `--shm-fps-cap-fast` applies to modes `1` and `2`
- Runtime resolution presets:
  - `1080`: `2184x1092`, Python `--downsample 1080`
  - `2K`: `2884x1442`, Python `--downsample 2k`
  - `4K`: `5768x2884`, Python `--downsample 4k`
- In SHM modes, resolution is producer controlled by Python and Unity only displays/validates the received header size. In `Baseline` mode (`4`), Unity applies the preset to RTSP decode output and restarts ffmpeg when needed.

If protocol fields change, update both repos in the same batch and keep version notes in commit messages.
