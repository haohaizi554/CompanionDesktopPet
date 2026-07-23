# Natural Greetings and Blinks Design

**Date:** 2026-07-24
**Status:** Proposed for implementation
**Branch:** `feat/cute-companion-desktop-pet`

## 1. Goal

Add two readable, cute local character actions to the existing offline desktop pet:

1. a natural blink that changes only the eye area and never squashes the whole face;
2. a greeting gesture made from a small body lean, nod, lift, and a short-lived `嗨♡` badge.

The existing click hearts, drag lean, landing spring, dialogue forest, privacy gates, persistence, and single-file release remain intact.

## 2. Selected visual approach

The current character is a single flat 1024×1024 transparent PNG and contains no visible hands or separate body layers. Three approaches were considered:

- drawing eyelids and a fake hand with WPF vectors: smallest bundle, but alignment and skin/eyelash quality are visibly artificial;
- generating a complete blink and waving sequence: visually ambitious, but full-frame regeneration can change the face, clothing, or silhouette between frames;
- **selected:** add one precisely aligned closed-eye overlay for blinking, and implement greeting as a composed whole-body gesture plus a small badge.

The selected approach gives a real eyelid transition without changing the body or identity, and gives an unmistakable greeting without inventing a hand that does not exist in the source image.

## 3. Visual layers

`CharacterStage` keeps its current transform chain. Two non-interactive overlay layers are added inside the same 320×320 viewbox:

- `BlinkOverlay`: a 1024×1024 transparent PNG aligned with `character.png`; only two small eye patches are opaque. The patches cover the open eyes with matching skin, lashes, and naturally closed lids.
- `GreetingBadge`: a rounded cream/blush badge containing `嗨♡`, initially transparent and positioned beside the upper shoulder.

Both overlays use `IsHitTestVisible="False"`. The blink overlay must have the same pixel dimensions as the base character, transparent outer bounds, and visible pixels only in bounded eye regions. It must not alter the face outline, hair, clothing, or background.

## 4. Action state model

Ambient actions use an explicit coordinator instead of independent booleans.

```text
[Idle]
  -> Blinking
  -> Greeting
  -> Dragging
  -> Paused

[Blinking]
  -> Idle
  -> Dragging (cancel and reset)
  -> Paused (cancel and reset)

[Greeting]
  -> Idle
  -> Dragging (cancel and reset)
  -> Paused (cancel and reset)

[Dragging]
  -> Landing
  -> Paused

[Landing]
  -> Idle
  -> Paused (cancel and reset)

[Paused]
  -> Idle
```

Only one ambient/action-state animation owns `ActionScale`, `ActionRotation`, and `ActionOffset` at a time. Click hearts and the existing click reaction retain their separate transforms and remain available while idle animation is paused. Dragging and landing have priority over blink and greeting. Every cancel and completion path restores overlay opacity and action transforms to neutral values.

## 5. Blink motion

One normal blink lasts about 280–340 ms:

1. close over 85–105 ms;
2. hold closed for 45–65 ms;
3. open over 120–160 ms.

The overlay uses opacity easing, not whole-image scale. Blink intervals are randomized between 3.2 and 6.8 seconds. About one in eight eligible blinks becomes a double blink, with a 100–150 ms gap. The scheduler never accumulates missed blinks: if the pet is busy, dragging, closing, or paused, it schedules one fresh future interval.

## 6. Greeting motion

The greeting lasts about 1.1 seconds:

1. lean 2.5–3.5 degrees toward the badge;
2. lift 3–5 DIPs and compress vertically by at most 1.2 percent;
3. make a small nod/return movement;
4. fade and float the `嗨♡` badge upward by 16–24 DIPs;
5. return all transforms to neutral.

Greeting is triggered once shortly after the first rendered startup reply, and can be replayed through a `打个招呼` context-menu item. It is not driven by corpus `AnimationCue` values and does not replace, synthesize, or bypass v2 dialogue. Automatic repeat greetings are intentionally excluded so the desktop pet does not become distracting.

## 7. Scheduling and lifecycle

`MainWindow` owns one ambient-action timer. The timer:

- starts only after the window is loaded and rendered;
- schedules blinks through a deterministic, injectable scheduler;
- stops while animation is paused, while dragging, and when the window closes;
- restarts with a fresh interval after resume or landing;
- never reads keyboard content, clipboard content, file names, window titles, or network state.

The startup greeting is a local UI action scheduled after render. It does not change the startup dialogue trigger or memory entry. Smoke-test mode may suppress long random waits, but must still prove that both action layers are loaded and can enter and leave their states.

## 8. Components

- `Assets/character-blink-closed.png`: aligned closed-eye overlay.
- `MainWindow.xaml`: overlay image, greeting badge, and context-menu command.
- `PetActionCoordinator`: pure state transitions, action eligibility, and cancellation.
- `AmbientActionScheduler`: randomized blink/double-blink timing with injectable randomness.
- `AnimationController`: `PlayBlink`, `PlayGreeting`, cancellation/reset, and completion callbacks.
- `MainWindow.xaml.cs`: lifecycle, timer, drag/pause arbitration, startup greeting, and menu wiring.

No legacy `DialogueService.GetGreeting` method is restored. No corpus-driven ambient gesture API is restored.

## 9. Testing

Implementation follows test-first development. Automated coverage must prove:

- invalid state transitions are rejected and all actions recover to `Idle`;
- drag and pause cancel blink/greeting and reset overlays/transforms;
- blink delay bounds and double-blink probability decisions are deterministic under injected samples;
- `PlayBlink` animates only the eye overlay and completes at opacity zero;
- `PlayGreeting` animates only its action transforms/badge and restores neutral values;
- startup schedules one greeting after rendering, and the menu command can replay it;
- disabled animation prevents ambient actions while preserving click hearts;
- all existing corpus, privacy, persistence, multi-monitor, single-instance, and event-pump tests remain green;
- the blink overlay matches base dimensions and is transparent outside the bounded eye regions;
- the delivered directory still contains one EXE plus instructions and zero DLL files;
- isolated smoke test exits successfully without leaving a process.

## 10. Release acceptance

The feature is complete only when:

1. a recorded visual inspection shows a natural blink with no face squash or visible rectangular patch;
2. the greeting is clearly readable as a greeting and returns to rest without transform residue;
3. pause, drag, landing, click, close, and restart behave correctly around both actions;
4. clean-tree automated tests and the real publish verifier pass;
5. a new self-contained single-file EXE is committed and the existing draft PR is updated.
