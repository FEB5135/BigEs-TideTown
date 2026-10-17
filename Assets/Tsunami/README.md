# Tsunami prototype: setup & testing guide

## Project inventory (as found)
- Unity **6000.6.6f1**, **URP 17.6** (PC_RPAsset/PC_Renderer: Forward+, SSAO, depth + opaque textures; Mobile_RPAsset also present).
- Input: **new Input System only** (`activeInputHandler: 1`), uGUI 2.6.
- Assets: the empty URP template (`SampleScene`, `Settings/`, `TutorialInfo/`). **No water/ocean shaders, terrain, UI or gameplay scripts existed**, so nothing could be reused. `SampleScene` and all `Settings/` assets are untouched.

Because no water shader existed, two small URP HLSL shaders were added (no textures, one pass, no refraction/reflections/screen-space reads):
- `Shaders/StylizedWater.shader`: used by **both** the ocean (`Water_Ocean.mat`) and the flood surface (`Water_Flood.mat`), so the colours match at the seam. Depth tint, backlit crest glow on tall wave faces, wave-face and shoreline foam, ripples, fresnel and specular are all visual only.
- `Shaders/Spray.shader`: soft round particle (no texture) for the spray.
- `Shaders/LowPolyTerrain.shader`: vertex-coloured, flat-shaded island. Includes DepthOnly/DepthNormals passes so SSAO doesn't break.

## One-time setup
1. Focus the Unity editor so it imports `Assets/Tsunami` (check the Console for errors).
2. Menu **Tsunami > Build Demo Scene**. This creates `Assets/Tsunami/Scenes/TsunamiDemo.unity`, the materials in `Assets/Tsunami/Materials` (existing ones are reused, never overwritten), and adds the scene to Build Settings.
3. Press **Play**. Nothing happens until you click **Start Tsunami**.

> After pulling an update that changes the island or grid size, run **Tsunami > Build Demo Scene** again: the scene stores the old resolution and terrain settings.

## Controls
| Input | Action |
|---|---|
| WASD / arrows | Move camera |
| Q / E | Rotate |
| Mouse wheel | Zoom |
| Middle-drag | Pan |
| Hold LMB | Raise terrain (barrier) |
| Hold RMB | Lower terrain (channel) |
| `[` / `]` | Brush size |

## Architecture
| Component | Responsibility |
|---|---|
| `TerrainGrid` | Heightfield (procedural island, or baked from colliders), world/grid conversion, ray-march picking, brush edits, island mesh |
| `ShallowWaterSolver` | Pure C# staggered-grid shallow-water solver (no Unity dependency, so it can be tested headless) |
| `WaterSimulation` | Fixed-timestep driver, Inspector parameters, depth sampling |
| `WaterMeshRenderer` | One dynamic mesh, one vertex per cell; UV0 = (depth, speed) for the shader. Its outer 8 cells fade into the shared open-ocean surface, so the square grid is never visible |
| `WaterSprayEmitter` | One pooled particle system; throws spray off fast, rising wave fronts using the simulated velocity |
| `OceanRing` | Ocean around the grid, morphing into a circle further out. Heights, normals and foam come from `WaterSimulation.OpenOceanHeight`, the same function the flood mesh fades into, so the seam is exact |
| `TsunamiController` | Owns the `Idle/Warning/Running/Stopped` state; `StartTsunami()`, `StopTsunami()`, `ResetSimulation()` |
| `TsunamiUI` | Buttons, status, and countdown; refreshes only on state change |
| `VillageFloodTracker` | Samples depth around each house every 0.25 s; Safe, Flooded or Destroyed (latched) |
| `TerrainBrush` / `GodCameraController` | Player input; both ignore input that starts over UI |

### How the water works
- Each cell stores terrain height and water depth; each face between two cells carries one signed flux. The flux accelerates with the **water-surface** difference across the face, and its cross-section is the water column above the higher bed. So waves travel at about √(g·depth), and raised terrain blocks water until the water tops it.
- A cell's outgoing fluxes are scaled so it never sends more water than it holds: no negative depths, and no water is created inside the grid.
- **Tsunami sequence** (matches the classic diagram): the sea draws back during the warning. Then a train of waves (each smaller than the last) rides on a slow rise in sea level. Over the shallow coast the solver slows the waves, bunches them up and makes them taller (shoaling), they run up onto land, and after the event the water drains back out (backwash) or stays in hollows.
- The incoming signal is replayed along the edges with a travel delay, so the waves sweep in from the source side instead of the whole border rising at once.
- Grid edges are **open ocean**. A 5-cell absorbing band stops waves from bouncing back off the borders.
- Bed friction scales with 1/depth², so waves cross deep water almost loss-free while thin floods on land settle quickly.
- Terrain edits keep the water depth in each cell, so water is displaced rather than created.
- The flood is entirely the solver's response to the terrain; no animated wall is moved. The heightfield can't curl or break like the diagram; foam on fast, steep crests stands in for breaking.
- This is a gameplay approximation, **not** a physically validated tsunami model.

### Key Inspector parameters
- **WaterSimulation**: `edgeFadeCells` (8: width of the band where the rendered water fades into the open ocean; the simulation still runs there), `waveVisibleDistance`, `timestep` (1/60), `maxStepsPerFrame` (4), `waveSpeed` (1 = physical √(g·h)), `bedFriction` (0.3), `landDrag` (0.25), `baseDrag` (0.04), `edgeAbsorption` (3), `seaLevel`. `StabilityNumber` (Courant number) should stay below ~0.5 (default ≈ 0.1).
- **TerrainGrid**: `resolution` (96), `cellSize` (1.33 m, so the island is ~127 m across), `seaFloor` (-12 m), island shape/hills (26 m mountain, ~16 m ridges), `bakeFromColliders` to use a teammate's island mesh instead.
- **TsunamiController**
  - Source: `sourceSide`, `sourceStart/End`, `edgeTaper` (0.2: the wave fades toward both ends of the edge so the grid corners stay calm), `sourceRows`, `sideReach` (only matters when `edgeTaper` is 0), `approachDistance` (how far out at sea the waves are visible before entering).
  - Waves: `numberOfWaves` (1; 0 means until Stop), `waveHeight` (12 m out at sea, the "strength"; it rears up to ~15 m at the shore), `wavePeriod` (9 s), `waveDecay`, `wavePeriod`, `waveInterval`.
  - Surge: `floodLevel` (1 m), `floodRampTime`, `floodHoldTime`, `floodFadeTime`, `sourceFadeTime` (Start/Stop fade, 1 s). `sideReach` 0.35 keeps the waves coming mainly from the source side.
  - Warning and reset: `useWarning`, `warningDuration`, `drawback`, `resetTerrainOnReset`.
- **VillageFloodTracker**: `checkInterval`, `floodDepth` (0.25 m), `destroyDepth` (1.2 m).

## What has been verified (headless, outside Unity)
The solver was compiled and run with `dotnet` (scratch harness, not in the repo):
- **Shoaling:** a 1 m wave on a sea floor rising from 8 m depth grows to 1.6 m at 1.2 m depth (Green's law predicts about 1.6×), reaches about 2 m in the surf zone, and runs up 2.3 m onto the beach.
- Closed-box dam-break for 60 s: volume drift < 0.0001 %, no negative depths, water passes a wall only through its gap. Ocean at rest: zero motion.
- **Mountain waves** (96×96, 12 m waves): about 13–15 m surfaces at the shore, up to 19 m where they hit a wall. Depths stay valid and nothing blows up.
- Village with no wall: all 10 houses destroyed (12–15 m of water). With a 20 m ring wall around the village: 9 safe; the one house outside the ring is destroyed. A straight 22 m beach wall alone only saves 2, because water wraps around its ends.
- With `edgeTaper` 0.2, the south corners peak at 5 m instead of 17 m (the corner pile-up that caused visual spikes). A single wave still destroys 9 of 10 houses with no wall; a ring wall saves 9.
- Grid/ocean seam: replaying the renderer's maths over a full tsunami, heights and normals on both sides of the grid edge match to about 2e-7 (float rounding).
- After the event, currents fall below 2 m/s within 30 s; only thin films keep draining downhill.
- Cost: ~0.12 ms per solver step at 96² (desktop CPU; at most 4 steps per frame). Mesh and spray updates are not measured yet.
- All scripts compile against the project's Unity 6.6 assemblies with `dotnet build`.

**Not yet verified in the Unity editor:** shader compilation, the scene builder, visuals, UI and frame rate on target laptops. Run the checklist below.

## Acceptance checklist (run in Play mode)
1. Scene starts Idle; sea calm; Stop is disabled.
2. Start: the countdown appears, the sea draws back, and Start becomes disabled.
3. After the countdown "THE WAVE IS COMING" appears. Expect a single wall of water well over the height of the houses, with spray flying off its breaking front. No folds or spikes at the ocean corners. A crest approaches from the south across the open ocean, steepens over the shallow beach and floods inland. Waves 2 and 3 follow and are smaller. There is no gap between the ocean and the simulated water.
4. Start, then Stop during the countdown: no water comes in; status reads Stopped.
5. Stop during flooding: the inflow stops at once and the inland water stays and keeps moving.
6. Water drains back to sea; the hollow east of the village stays wet.
7. Spam Start/Stop: only one event at a time (timer-based, no coroutines).
8. Start again after Stop works. Reset clears water and restores houses (terrain only if `resetTerrainOnReset`).
9. Clicking buttons never sculpts terrain or zooms the camera.
10. Raise a ~20 m ring wall around the village (hold LMB; `[`/`]` resize the brush) before the wave: most houses survive.

## Performance (target: 75+ FPS on low-end PCs)
**Graphics:** **Tsunami > Build Demo Scene** offers to switch the active quality level to *fast settings*. **Tsunami > Graphics > Use Fast Settings** does the same at any time. It creates copies of the project's URP asset and renderer in `Assets/Tsunami/Settings` with SSAO, HDR, MSAA, shadows, depth/opaque texture copies and Forward+ turned off, plus VSync off. The original `Assets/Settings` assets are not modified. **Tsunami > Graphics > Use Original Settings** switches back.

**CPU work per frame:**
- Calm water costs almost nothing: the flood mesh, ocean ring and spray skip their updates when no cell's depth changed and no wave is active.
- The open-ocean surface (incoming wave / drawback) is cached once per step, only for the 8-cell edge band, and only while it changes. The ocean ring evaluates it once per vertex instead of five times.
- The terrain brush only ray-marches between the highest and lowest possible terrain.
- Solver: ~0.12 ms per step at 96² on a desktop CPU, with at most 4 steps per frame (fewer at high FPS).

**Measuring in the editor:**
- Turn off the Game view's **VSync** toggle (it caps you at the monitor's refresh rate).
- Set the Game view to **1920×1080**: QHD (2560×1440) is ~1.8x the pixels of the low-end target.
- Read **Stats** or the **Profiler** (CPU: `WaterSimulation.Update`, `WaterMeshRenderer.LateUpdate`).

**If FPS is still below target:**
- `TerrainGrid.resolution` 64 + `cellSize` 2 (same island, ~2.25x fewer cells), or `WaterSimulation.maxStepsPerFrame` 2.
- Spray: lower `maxParticles` (600) / `maxPerFrame` (25).
- Water material: set `_RippleStrength` to 0 for slightly cheaper pixels.
