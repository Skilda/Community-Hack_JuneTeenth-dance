# Community Hack — Juneteenth Dance

A **VR music‑visualizer experience** built in Unity for the Community Hackathon. The player stands inside a reactive, audio‑driven world — soundbars pulse to the beat, a central particle system bursts with the rhythm, everything mirrors across a water surface, and the player can **physically drum** an interactive object with their hands or controllers to fire colored particle bursts.

---

## ✨ Features

- **Audio‑reactive visualizer** — 100 soundbars arranged in a ring scale and color‑shift in real time from an FFT of the playing track (separate bass / treble bands), built on *Rhythm Visualizator Pro*.
- **Beat‑driven central particle system** — emits firework‑style bursts on detected beats (bass energy crossing a threshold).
- **Animated color gradients** — the whole scene cycles through a configurable bright color palette.
- **Water planar reflection** — the scene reflects in a water plane (`FXWaterPro`), with a **custom multi‑pass patch** (`Water.cs`) that feeds correct per‑eye view/projection matrices so the reflection renders correctly in stereo VR.
- **In‑VR Drum interaction (custom)** — a drum the player hits with a hand or controller:
  - **Squash‑and‑stretch** deformation with an animation curve, springing back to rest.
  - **Color cycling** through its own bright gradient over time.
  - **Beat‑scaled particle bursts** — the number of particles a hit emits scales with how *on‑beat* the strike lands (≈120 off‑beat, spiking toward 300 on a strong beat).
- **Music player** — simple song‑list UI for choosing tracks.

---

## 🛠 Tech Stack & Requirements

| | |
|---|---|
| **Engine** | Unity **6000.4.2f1** (Unity 6) |
| **Render pipeline** | Universal Render Pipeline (URP) |
| **XR** | OpenXR + **Meta XR SDK** (`OVRCameraRig`), Unity XR Hands |
| **Target** | Meta Quest (standalone) and PCVR |
| **Input** | VR controllers and hand tracking |

> The water reflection's stereo patch assumes **Multi‑Pass** rendering. Set it under
> *Project Settings → XR Plug‑in Management → OpenXR → Render Mode → Multi‑pass* if reflections look wrong in the headset.

---

## 🚀 Getting Started

1. Install **Unity 6000.4.2f1** (via Unity Hub).
2. Clone this repository and open the project folder in Unity Hub.
3. Open the main scene: **`Assets/Scenes/CommunityHack.unity`**.
4. **Play in‑headset:** connect a Quest (Link/Air Link) or PCVR headset and press Play, or build to the device.
5. **Add your own music:** in the *Music Player Canvas → Song List*, duplicate the example song entry and assign your audio clips.

---

## 🥁 The Drum Interaction

The custom interactive drum lives on the **`Drum`** GameObject and is driven by **`Assets/Scripts/DrumHitInteraction.cs`**.

### How it works

- **Hit detection** — each frame the script measures the distance from the VR hand/controller anchors (`LeftHandAnchor` / `RightHandAnchor`, etc.) to the drum's surface. Crossing from *outside* to within `contactRadius` registers one hit.
  - Detection is evaluated against the drum's **rest‑scale geometry** (not the live, squashing collider), so the deformation can't move the trigger surface and cause self‑retriggering.
  - A **global cooldown** (`hitCooldown`) prevents overlapping anchors (e.g. `HandAnchor` + `HandAnchorDetached`) from double‑firing a single strike.
- **Squash deformation** — on hit, the drum's scale animates (wider + shorter) and springs back via a tunable `AnimationCurve`.
- **Color** — the drum cycles through `gradientColors` over `gradientCycleDuration`, writing `_BaseColor` / `_EmissionColor` on its material.
- **Particles** — each hit fires the **Central Particle System** once, tinted with the drum's current color. The count scales with beat energy read from `RhythmVisualizatorPro.RhythmAverage`.

### Key Inspector settings (on `DrumHitInteraction`)

| Field | Purpose |
|---|---|
| `contactRadius` | How close (m) a hand must get to the drum surface to count as a hit. |
| `hitCooldown` | Minimum seconds between registered hits (global, across all anchors). |
| `squashDuration` / `squashWiden` / `squashFlatten` / `squashCurve` | Shape and timing of the squash‑and‑stretch. |
| `gradientColors` / `gradientCycleDuration` / `emissionIntensity` | Drum color cycle and glow. |
| `minParticlesPerHit` / `maxParticlesPerHit` | Particle count range for off‑beat vs. on‑beat hits. |
| `beatLow` / `beatHigh` / `beatSharpness` | Map bass energy → particle count. Enable `logHitEnergy` to calibrate `beatHigh` to your track. |

> **Calibration tip:** turn on `logHitEnergy`, play a song, and watch the Console for `beat energy X → N particles`. Adjust `beatHigh` until off‑beat hits sit near the minimum and strong beats spike to the maximum.

---

## 📁 Project Structure

```
Assets/
├── Scenes/
│   └── CommunityHack.unity              # Main scene
├── Scripts/
│   └── DrumHitInteraction.cs            # Custom VR drum interaction
└── Rhythm Visualizator Pro/            # Audio visualizer asset (soundbars, particles, music player)
    ├── Scripts/RhythmVisualizatorPro.cs # Core visualizer (exposes RhythmAverage for the drum)
    ├── Music Player/                    # Song list & playback UI
    └── Standard Assets/Environment/Water # FXWaterPro reflection (+ VR multi-pass patch in Water.cs)
```

---

## 🙏 Credits

- **Rhythm Visualizator Pro** — audio visualization framework by *Carlos Arturo Rodriguez Silva (Legend)*.
- **Meta XR SDK / OVR**, **Unity OpenXR**, **URP** — Unity Technologies & Meta.
- VR drum interaction, the visualizer's stereo water patch, and beat‑reactive integration — built for the Community Hackathon (Juneteenth Dance).
