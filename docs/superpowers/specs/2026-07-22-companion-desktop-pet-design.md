# Companion Desktop Pet — Design Specification

**Date:** 2026-07-22  
**Status:** Approved design, pending written-spec review  
**Target:** Windows 10/11 x64, single-file executable

## 1. Goal

Build a lightweight Windows desktop pet using the user-provided character image. The pet must launch by double-clicking a single `.exe`, display the character as a transparent cutout, remain interactive without a console window, and provide simple local companionship features.

The application is deliberately offline. It does not call an API, collect telemetry, or require an account.

## 2. Chosen Scope

The chosen product tier is the companionship version:

- transparent real-person cutout based on the supplied image;
- frameless, transparent, always-on-top desktop window;
- idle breathing, gentle sway, and small vertical floating animations;
- drag-to-move interaction;
- click reaction with a short bounce and speech bubble;
- time-aware greeting on launch;
- random local speech bubbles every 5–10 minutes;
- context menu for common controls;
- persisted position, size, animation, and always-on-top preferences;
- self-contained, single-file Windows executable.

Not included in this version:

- AI or network chat;
- microphone, camera, or audio recording;
- automatic startup with Windows;
- Live2D skeletal animation or facial reenactment;
- cloud synchronization or telemetry.

## 3. Technology Decision

Use **C# with WPF on .NET 9**.

Reasons:

- WPF supports alpha-transparent, frameless windows and smooth transforms natively.
- The target computer already has the .NET 9 SDK and Windows Desktop runtime.
- A self-contained `win-x64` publish produces a double-clickable executable with no Python or Node installation requirement.
- Compared with PySide6/PyInstaller and Electron, WPF gives faster startup and lower packaging/runtime overhead for this Windows-only application.

## 4. Window and Visual Design

The main pet window uses:

- `WindowStyle=None` and `AllowsTransparency=True`;
- transparent background;
- no taskbar button;
- topmost enabled by default;
- DPI-aware sizing in WPF device-independent pixels;
- a tight visual footprint around the character so invisible window area does not obstruct normal desktop clicks.

Initial behavior:

- target character height: approximately 360 DIP;
- initial location: lower-right of the primary work area with a 24 DIP margin;
- minimum/maximum size presets: 75%, 100%, and 125%;
- saved position takes precedence when it remains visible on a current monitor;
- an invalid or off-screen saved position is clamped back into the nearest work area.

The speech bubble appears above or beside the character depending on available screen space. It uses a rounded, high-contrast light surface, dark text, subtle shadow, and a small pointer. The bubble automatically disappears after approximately five seconds.

## 5. Character Asset Processing

The supplied source image is converted into a transparent PNG asset:

- remove only the scene background;
- preserve the character's face, hairstyle, clothing, pose, and photographic appearance;
- keep edges around hair and clothing softly feathered;
- crop excessive transparent margins;
- do not sexualize, change age appearance, or materially redesign the person;
- derive a simple application icon from the processed character asset.

The processed PNG and icon are embedded into the executable as WPF resources so the delivered application does not depend on loose asset files.

## 6. Interaction Model

### Dragging

Pressing and dragging the visible character moves the window. A small movement threshold distinguishes a drag from a click. Releasing after a drag saves the new position.

### Clicking

A normal left click:

1. plays a short squash-and-bounce response;
2. chooses a local phrase that avoids immediate repetition;
3. displays the phrase in the speech bubble.

### Right-click menu

The character context menu contains:

- Say something;
- Pause/Resume animation;
- Size: Small / Normal / Large;
- Always on top (checked toggle);
- Restore default position;
- Exit.

The menu uses native WPF commands and keyboard-accessible labels.

## 7. Animation

Idle animation combines three low-amplitude transforms:

- breathing scale: roughly 0.985–1.015 over four seconds;
- sway rotation: roughly -1.2° to +1.2° over six seconds;
- vertical float: roughly -3 to +3 DIP over five seconds.

Durations differ so the motion does not look mechanically repetitive. Transforms use eased, repeating WPF storyboards and remain on the UI thread without blocking work. Pausing freezes the idle animation cleanly; the click response remains available.

## 8. Companion Dialogue

Dialogue is stored locally in categorized phrase lists:

- morning, afternoon, evening, and late-night greetings;
- encouragement;
- break and hydration reminders;
- neutral friendly observations.

On startup, the pet selects a greeting based on local time. After that, a timer schedules the next bubble with randomized intervals between five and ten minutes. The timer resets after a manual click so automatic and manual bubbles do not overlap.

Only one speech bubble is visible at a time. A newer phrase replaces and restarts the dismissal timer for the current one.

## 9. State and Reliability

Settings are stored at:

`%LOCALAPPDATA%\CompanionDesktopPet\settings.json`

Saved fields:

- window position;
- scale preset;
- animation paused state;
- always-on-top state.

Writes use a temporary file followed by replacement so an interrupted write does not leave half-written JSON. Missing, malformed, or incompatible settings fall back to defaults without preventing startup.

A process-wide named mutex prevents accidental duplicate instances. Closing through the context menu stops timers and animations, saves settings, releases resources, and terminates the process.

## 10. Packaging

Publish with a Release, self-contained, single-file `win-x64` configuration:

- no console subsystem/window;
- application icon embedded;
- WPF assemblies and character resources bundled;
- ReadyToRun disabled unless measurement proves it improves the final experience;
- single-file compression enabled to reduce delivery size.

Primary delivery artifact:

`outputs/CompanionDesktopPet/角色桌宠.exe`

A short `使用说明.txt` may accompany the executable for controls and troubleshooting, but the executable itself must not require adjacent assets or configuration files.

## 11. Error Handling

- Asset loading failure: show a concise native error dialog and exit instead of displaying an invisible process.
- Settings failure: continue with defaults and overwrite settings only after a successful interaction or clean exit.
- Monitor layout change: clamp the window into a visible work area.
- Unexpected UI exception: show a short error dialog, attempt a settings save, then exit cleanly.

## 12. Verification and Acceptance

Completion requires evidence for every item below:

1. `dotnet test` passes tests for greeting selection, non-repeating phrase selection, settings fallback, settings round-trip, and screen-bound clamping.
2. Release publish completes and emits the requested single `.exe` artifact.
3. The executable launches by double-click without a console window.
4. The rendered window has a transparent background and shows the processed character correctly.
5. Idle animation visibly runs and can be paused/resumed.
6. Dragging moves the pet; a click triggers the bounce and one speech bubble.
7. Every context-menu command performs its documented action.
8. Position and preferences survive a clean restart.
9. A deliberately off-screen saved position is recovered to a visible monitor.
10. Exit removes the process completely, and a second launch cannot create a duplicate pet.

Verification will include automated tests, build/publish output inspection, process/window checks, and a real GUI smoke test with screenshots.

