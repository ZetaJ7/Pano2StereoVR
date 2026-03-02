# Protocol Contract

This Unity repo follows the communication contract defined in:

- `Pano2Stereo/docs/protocol.md`

Current contract highlights:

- UDP JSON:
  - `{"u0":[x,y,z]}`
  - `{"mode":2}`
  - `{"u0":[x,y,z],"mode":2}`
- Shared memory layout:
  - `seq_begin` at offset `0` (`uint64`, even=stable, odd=writing)
  - `width` at offset `8` (`uint32`)
  - `height` at offset `12` (`uint32`)
  - `mode` at offset `16` (`uint32`)
  - `seq_end` at offset `20` (`uint64`)
  - pixel bytes from offset `28`

If protocol fields change, update both repos in the same batch and keep version notes in commit messages.
