# XR Super Resolution

This project includes an optional XR-focused super-resolution path built on Unity's native URP upscaler. It is disabled by default for baseline VR tests.

- Unity version target: `2022.3.62f3`
- Render pipeline target: `URP 14.0.12`
- XR runtime target: `OpenXR`
- Upscaler: `AMD FidelityFX Super Resolution 1.0 (FSR)` via the URP Asset

## Why this path

- It is Unity-native and supported by the installed URP package.
- It works with the current OpenXR + HMD presentation path without adding third-party native binaries.
- When explicitly enabled, it can improve perceived HMD sharpness and free GPU budget so the eye buffer can stay closer to the panel resolution.

## Project changes

- `Assets/Editor/XrSuperResolutionSetup.cs`
  - Adds a menu item: `Tools/Pano2StereoVR/Setup XR Super Resolution (URP FSR)`
  - Reuses the current URP asset if one already exists.
  - Otherwise creates `Assets/Settings/Rendering/Pano2StereoVR_XR_FSR.asset`
  - Configures native baseline defaults:
    - `Render Scale = 1.00`
    - `Upscaling Filter = Auto`
    - `FSR Sharpness = 0.82` (only used if runtime SR is enabled)
- `Assets/Scripts/XrSuperResolutionController.cs`
  - Applies FSR presets at runtime.
  - Disabled on startup.
  - Preset hotkeys are disabled by default.
  - Exposes runtime presets:
    - `Performance` -> `0.77`
    - `Balanced` -> `0.85`
    - `Quality` -> `0.92`
    - `Native` -> `1.00`
    - `Supersample115` -> `1.15`
    - `Supersample130` -> `1.30`
    - `Supersample150` -> `1.50`
    - `Supersample170` -> `1.70`
    - `Supersample200` -> `2.00`
  - Optional adaptive render-scale loop is available.
- `Assets/Scripts/ExperimentController.cs`
  - Does not auto-create the controller by default.
  - Shows super-resolution state in the existing overlay.

## Runtime hotkeys

- `F9`: lower preset
- `F10`: higher preset
- `F11`: toggle adaptive render scale
- `F12`: toggle super resolution on/off

## Recommended usage

For baseline VR testing, do nothing. SR remains off, render scale remains native `1.00`, and the controller is not auto-created.

For explicit SR A/B testing:

1. In Unity Editor, run `Tools/Pano2StereoVR/Setup XR Super Resolution (URP FSR)`.
2. Add `XrSuperResolutionController` to a scene object, or enable `autoCreateXrSuperResolutionController` on `ExperimentController`.
3. Enable `enableOnStartup` or call `SetSuperResolutionEnabled(true)` at runtime.
4. Optionally enable preset hotkeys for `F9` / `F10` / `F11` / `F12`.

## Notes

- The super-resolution path is intentionally implemented in URP instead of vendor-specific native plugins so the current OpenXR viewer stays portable across supported PC VR runtimes.
- If no URP asset is active, the runtime overlay reports `SR: unavailable` and the setup menu can be run before explicit SR tests.
- The higher supersample presets (`1.50+`) increase GPU cost very quickly and are mainly intended for visual A/B checks.
