# ATADTRL — Module 1
Warehouse Environment & Data Acquisition (Non-RL Baseline)

This package implements **only Module 1** of the ATADTRL pipeline: a data-driven
Unity warehouse simulation with a NavMesh-based (non-RL) autonomous robot,
four simulated sensors, 19 predefined scenarios, one-click scenario execution,
dual CSV dataset logging, and dataset validation.

Target: **Unity 6 (6000.x LTS)**, Built-in or URP render pipeline (either works —
no render-pipeline-specific APIs are used). TextMeshPro package required for UI.

---

## 0. Fastest path: one-click scene setup

Instead of manually creating every GameObject in section 2 below, use the
included Editor tool:

1. Open (or create) an empty scene.
2. Menu bar ▸ **ATADTRL ▸ Setup Module 1 Scene (One-Click)**
3. This creates and fully wires: `WarehouseManager`, `Robot` (with all four
   sensors), three dynamic-object prefabs (`Assets/Prefabs/Human.prefab`,
   `Forklift.prefab`, `MovableObstacle.prefab`), `ScenarioManager` +
   `GroundTruthManager`, `GameManager`, the **Scenario Control Panel**
   canvas, and a **Demo Mode** canvas (created inactive — enable it in the
   Hierarchy if you want that screen instead of the Control Panel).
4. Save the scene (Ctrl+S), then press **Play**. `GameManager` builds the
   warehouse and bakes the NavMesh automatically on `Start()`.
5. If the robot doesn't move, use **ATADTRL ▸ Bake NavMesh Now** to re-bake
   after any manual geometry changes.

The tool is safe to re-run — it finds existing top-level objects by name
instead of duplicating them. Sections 2–9 below describe what the tool
builds, for anyone who wants to build or customize the scene by hand.

Note: collision and camera-detection logic no longer relies on Unity's
project Tag Manager — walls, racks, and dynamic objects are marked with an
`EnvironmentCollisionTag` component instead, attached automatically by
`WarehouseManager` and the dynamic-object prefabs, so there's no tag setup
step required.

---

## 1. Folder Structure

```
Assets/
  Scripts/
    Core/
      DataStructures.cs        - shared configs, records, CSV row structs
      SensorNoiseConfig.cs     - centralized sensor noise/rate config
      GameManager.cs           - bootstraps warehouse + scenarios on scene start
    Environment/
      WarehouseManager.cs      - procedural warehouse builder
      DynamicObjectMover.cs    - human/forklift/obstacle motion
      AisleBlockage.cs         - timed NavMeshObstacle for aisle blockage scenarios
      GroundTruthManager.cs    - assembles the authoritative per-step ground truth
    Robot/
      RobotController.cs       - robot physical config + kinematic state
    Navigation/
      INavigationController.cs         - interface (future PPO plugs in here)
      BaselineNavigationController.cs  - NavMeshAgent-based baseline (Module 1)
      NavigationManager.cs             - wires robot + sensors + nav controller
    Sensors/
      ISensor.cs
      LidarSensor.cs
      RGBSensor.cs
      IMUSensor.cs
      WheelEncoder.cs
      SensorManager.cs         - coordinates all sensors, applies scenario overrides
    Scenarios/
      ScenarioDefinition.cs    - IScenario, ScenarioDefinition, ScenarioLibrary (19 scenarios)
      ScenarioManager.cs       - episode lifecycle orchestrator (the "engine")
    Logging/
      CSVLogger.cs             - IDataLogger + append-safe CSV writer
      GroundTruthLogger.cs
      ObservationLogger.cs
      PerformanceLogger.cs
    Validation/
      DatasetValidator.cs
    UI/
      ScenarioUIManager.cs     - "Scenario Control Panel" (dropdown + buttons)
      DemoController.cs        - "Demo Mode" main screen (per-scenario buttons + Run All)
```

Every configuration value (warehouse dimensions, robot kinematics, sensor
noise, scenario parameters) is Inspector-editable — nothing is hard-coded in
the robot or navigation controller.

---

## 2. Unity Scene Setup Procedure

Create one scene, e.g. `Assets/Scenes/Module1_Main.unity`.

### 2.1 GameManager
- Empty GameObject `GameManager`
- Add `GameManager.cs`
- Drag the `ScenarioManager` GameObject (below) into its `Scenario Manager` field

### 2.2 WarehouseManager
- Empty GameObject `WarehouseManager`
- Add `WarehouseManager.cs`
- Set `Config` values (or leave defaults): warehouse 60×40×8m, 4 rows × 6 columns
  of racks, 3m aisles
- Leave geometry empty at edit time — it is generated at runtime by `BuildWarehouse()`

### 2.3 Robot
- Empty GameObject `Robot`
- Add components in this order:
  1. `Rigidbody` (kinematic, no gravity — RobotController configures this in `Awake`)
  2. `BoxCollider` (auto-added by RobotController if missing)
  3. `NavMeshAgent`
  4. `RobotController.cs` — set `Config` (dimensions, speeds, safety distances)
  5. `BaselineNavigationController.cs`
  6. `NavigationManager.cs` — drag `RobotController` and `SensorManager` (below) into fields
  7. `SensorManager.cs` — set `Base Noise Config`
- Child GameObjects for sensor mounts:
  - `Robot/LidarMount` → add `LidarSensor.cs`, set `Obstacle Mask` to a layer
    containing walls/racks/dynamic obstacles
  - `Robot/CameraMount` → add a `Camera` component (disable "audio listener" if
    duplicated in scene) + `RGBSensor.cs`
  - `Robot/IMUMount` (can be the robot root) → add `IMUSensor.cs`
  - `Robot/EncoderMount` (can be the robot root) → add `WheelEncoder.cs`
- On `SensorManager`, drag the four sensor components into `Lidar`, `Camera`,
  `Imu`, `Encoder` fields.
- No manual tag setup is required — `WarehouseManager` attaches
  `EnvironmentCollisionTag` to walls/racks automatically, and the dynamic
  object prefabs (human/forklift/obstacle) should carry the same component
  set to `DynamicObstacle` (done automatically if built via the human/
  forklift/obstacle prefabs the one-click tool generates).

### 2.4 ScenarioManager
- Empty GameObject `ScenarioManager`
- Add `ScenarioManager.cs`
- Drag references: `WarehouseManager`, `GroundTruthManager` (add this
  component to the same or another manager object), `RobotController`,
  `NavigationManager`, `SensorManager`
- Assign `Human Prefab`, `Forklift Prefab`, `Obstacle Prefab` — simple
  capsule/cube prefabs tagged **DynamicObstacle**, each needs a
  `Rigidbody` (kinematic) + collider so LiDAR/camera/collision detection
  work. `DynamicObjectMover.cs` is added automatically at spawn time if
  missing.
- `Aisle Blockage Prefab` can be left empty — `AisleBlockage.cs` is added
  at runtime to a generated empty GameObject if no prefab is assigned.

### 2.5 GroundTruthManager
- Can live on the `ScenarioManager` GameObject or its own.
- Add `GroundTruthManager.cs`, assign `Warehouse Manager` and
  `Navigation Manager`.

### 2.6 UI — Scenario Control Panel
- Create a `Canvas` named `ATADTRL Module 1 Scenario Control Panel`
- Add: `TMP_Dropdown` (scenario list), `TMP_Text` (description), `TMP_Text`
  (status), and Buttons: **Start**, **Stop**, **Reset**, **Next**,
  **Previous**, **Run All**, **Return to Main Menu**
- Add `ScenarioUIManager.cs` to the Canvas (or a child controller object),
  wire every field to the corresponding UI widget and to `ScenarioManager`

### 2.7 UI — Demo Mode
- Create a second `Canvas` (or a panel within the same canvas) with a
  vertical/grid `Layout Group` as `Scenario Button Container`
- Create a `Button` prefab with a `TMP_Text` child → assign as
  `Scenario Button Prefab`
- Add a **Run All** button, a title `TMP_Text`, and a completion banner
  (a `GameObject` containing a `TMP_Text`, initially inactive)
- Add `DemoController.cs`, wire all fields and `ScenarioManager`

---

## 3. NavMesh Setup

1. Window ▸ AI ▸ Navigation (or the Unity 6 AI Navigation package — install
   via Package Manager if not present: `com.unity.ai.navigation`)
2. Since the warehouse is generated at runtime, baking must happen after
   `WarehouseManager.BuildWarehouse()` runs:
   - **Editor testing**: `GameManager.autoBakeNavMeshInEditor = true` calls
     `UnityEditor.AI.NavMeshBuilder.BuildNavMesh()` automatically after the
     warehouse is built in `Start()`.
   - **Standalone builds**: runtime NavMesh building requires the
     `AI Navigation` package's `NavMeshSurface` component (its runtime API
     differs from the Editor-only `NavMeshBuilder`). Add a `NavMeshSurface`
     component to the `WarehouseManager` GameObject, set `Collect Objects`
     to `All`, and call `surface.BuildNavMesh()` from `GameManager.Start()`
     instead of (or in addition to) the Editor-only path when
     `Application.isEditor` is false.
3. Ensure rack/wall primitives generated by `WarehouseManager` are marked
   **Navigation Static** — this is done automatically in the Editor via
   `WarehouseManager.GameObjectUtility_MarkStatic`.

---

## 4. Dataset Output

Written to `Application.persistentDataPath/Dataset/`:

```
Dataset/
  Scenario_01/GroundTruth_Environment.csv
  Scenario_01/Unified_Observation.csv
  Scenario_02/ ...
  ...
  Scenario_19/ ...
  GroundTruth_Environment_All.csv
  Unified_Observation_All.csv
  Performance_Baseline.csv
  Dataset_Validation_Report.csv
```

- Per-scenario files are opened fresh at the start of each scenario run and
  closed at the end — re-running a scenario does not corrupt other
  scenarios' data.
- Combined `_All.csv` files are opened once in **append** mode for the
  session, so previous runs are preserved unless you delete the `Dataset`
  folder manually.
- `Path.Combine(Application.persistentDataPath, "Dataset")` resolves to:
  - Windows: `%userprofile%\AppData\LocalLow\<Company>\<Product>\Dataset`
  - macOS: `~/Library/Application Support/<Company>/<Product>/Dataset`
  - Linux: `~/.config/unity3d/<Company>/<Product>/Dataset`

### Dataset schemas
See `GroundTruthRecord`, `ObservationRecord`, `PerformanceRecord`, and
`ValidationRecord` in `Core/DataStructures.cs` for the authoritative column
list — each class's `CsvHeader()` method is the single source of truth so
header and row order can never drift apart.

---

## 5. One-Click Scenario Execution

1. Press Play.
2. In the **Scenario Control Panel**, pick a scenario from the dropdown
   (its description updates automatically).
3. Press **START**. `ScenarioManager.StartScenario(id)` runs:
   reset world → apply scenario config → spawn dynamic objects → set
   sensor overrides → set NavMesh goal → begin logging both CSVs at 10 Hz
   → run until goal reached or timeout → write performance record →
   validate output → clean up.
4. Use **STOP** to cancel mid-run, **RESET** to clear the scene between
   attempts, **NEXT/PREVIOUS** to change the dropdown selection without
   starting, and **RETURN TO MAIN MENU** to leave the panel.

No code or Inspector edits are required to change scenarios — everything
scenario-specific lives in `ScenarioLibrary.BuildAll()`.

---

## 6. Run-All-19-Scenarios Procedure

1. Press **RUN ALL** in either UI (`ScenarioManager.StartRunAll()`).
2. Scenarios 01→19 execute sequentially: reset → run → log → validate →
   next, exactly as with a single scenario.
3. On completion, `OnAllScenariosCompleted` fires; the Demo Mode screen
   shows **ALL SCENARIOS COMPLETED**, and the same message appears in the
   Control Panel's status text.

---

## 7. Dataset Validation

`DatasetValidator` runs automatically after every scenario, checking each
freshly written CSV for missing identifier fields, NaN/Infinity values,
duplicate rows, and non-monotonic timestamps, and appends a row to
`Dataset_Validation_Report.csv` with a `PASS` / `FAIL_*` status.

---

## 8. Troubleshooting

| Symptom | Likely Cause | Fix |
|---|---|---|
| Robot doesn't move | NavMesh not baked, or robot not on NavMesh | Bake NavMesh after warehouse build; verify `NavMeshAgent.isOnNavMesh` |
| Compile error on `linearVelocity` | Older Unity version | Unity 6 renamed `Rigidbody.velocity` → `Rigidbody.linearVelocity`. On Unity 2022/2021, replace with `.velocity`. |
| Robot walks through racks | Racks not marked Navigation Static before bake | Confirm `WarehouseManager` calls `SetStaticEditorFlags`, then re-bake |
| LiDAR always reports max range | `Obstacle Mask` set to `Nothing`, or raycasts start inside own collider | Set the mask to include warehouse/obstacle layers; offset `LidarMount` slightly forward |
| Camera detections always zero | Dynamic object prefabs missing `Renderer` or `DynamicObstacle` tag | Ensure human/forklift/obstacle prefabs are tagged `DynamicObstacle` and have a visible mesh |
| CSV files empty/missing | `persistentDataPath` folder not yet created, or scenario stopped before `EndScenario()` | Let a scenario run to completion at least once; check `ScenarioManager.DatasetRootPath` in a debug log |
| Ground truth == sensor observation exactly | Noise config `std = 0` or scenario didn't set `overrideSensorNoise` | Check `SensorNoiseConfig` values > 0; confirm scenario definitions 12–19 apply overrides |
| Aisle blockage never activates | `blockedAisleIndex` out of range for current rack layout | `WarehouseManager.AisleBounds.Count == rackColumns - 1`; keep index within that range |
| UI dropdown empty | `ScenarioUIManager.Start()` ran before `ScenarioManager.BuildWorldAndScenarios()` | Ensure `GameManager` executes first (Script Execution Order), or call `PopulateDropdown()` after scenarios are built |

---

## 9. Design Rule for Future Modules

Module 1 never imports or references Modules 2–5. The only contract handed
forward is the pair of CSV schemas (`GroundTruthRecord`, `ObservationRecord`)
and the `INavigationController` interface, which Module 4's PPO controller
will implement without any changes to `RobotController`, `SensorManager`, or
`ScenarioManager`.
