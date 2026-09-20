# Module 1 operational benchmark

Implementation revision: 10 September 2026. This is a simulation baseline, not a deployed or safety-certified warehouse system.

## Scope and experiment rules

Module 1 implements environment generation, non-RL navigation, parcel handling and raw data acquisition. The attached architecture places EDATS/adaptive synchronization/context learning in Module 2, TAM/trusted-state construction in Module 3, and secure policy execution/evaluation in Module 4. Those mechanisms are future integrations, not implemented claims in this revision.

- A1–A5 retain their accepted scenario definitions and route assignments. Shared handling and stopping improvements can change encounter timing; retest these scenarios before comparing old results.
- Each E/T episode now attempts ONE complete parcel transfer, as agreed. The route variants cover P1–P3 and D1–D3 across the scenario set. They do not rotate within a single one-order episode.
- All episodes have a 120-second simulation-time maximum except E1, which has 30 seconds. Earlier verified completion, collision or handling failure ends an episode.
- Five workers H1–H5 and two operated forklifts F1–F2 receive work routes. Normal work includes loading/inspection, loaded travel, service at destination and return travel. Finite service pauses are intentional, not idle background roaming. Existing A2/A4 obstruction assignments deliberately park involved actors.
- Run All executes the 15 controlled research scenarios once, then stops. Custom scenarios run individually and do not alter this list.
- Default fixture seed is 1701 plus scenario ID. Record the seed, layout, configuration and simulation timing for every comparison. NavMesh/physics execution is not guaranteed bitwise deterministic.
- A disturbance creates a condition, not a required collision or failure. A baseline success is a valid outcome. A run with no relevant event exposure is not evidence of fault robustness.

## The 15 scenarios

Rack-bound destinations below refer to a reserved category rack slot, not simply the station floor marker. The UI/CSV records the actual rack and slot ID.

| ID | Work and disturbance | What to measure | Intended later mechanism |
|---|---|---|---|
| A1 | Existing P1 → D2 delivery via the Books rack blind turn; H1 independently carries a carton toward that corner. | Actual contact, clearance and stopping; do not assume a collision occurred. | Context and secure action selection |
| A2 | Existing central-aisle transfer; F1 stops ahead and F2 constrains the entrance behind. | Deadlock duration, stops, completion/timeout. | Context-based recovery and policy selection |
| A3 | Existing P2 → D3 route commitment followed by F1's cross-aisle handoff obstruction. | Waiting, replanning and path overhead. | Updated context and adaptive decisions |
| A4 | Existing P1 → D3 delivery with H2/F2 occupying the placement approach. | Arrival versus actual verified delivery; blocked placement time. | Station context and safe approach selection |
| A5 | Existing Books-rack relocation after destination commitment. | Valid placement versus stale-coordinate wrong placement. | Change detection, EDATS and destination synchronization |
| E1 | P3 → Electronics rack/D1 assignment. F1 approaches the shared service lane and lowers an inbound pallet after AMR pickup. Pallet remains for receiving. Episode cap: 30 s. | Actual pallet appearance in truth, blocked travel and exposure before the cap. | EDATS physical-change synchronization |
| E2 | P1 → Apparel rack/D2 assignment. H1/H2 carton work and H4 trolley work are dispatched across the shared service lane. Other assigned jobs continue. | Crossing occupancy, stop–go time, route efficiency. | Dynamic context and secure gap selection |
| E3 | P2 → Healthcare rack/D3 assignment. F1's replenishment transfer crosses the loaded AMR's service route, then resumes normal work. | Relative motion, yielding and clearance. | Change detection and policy response |
| E4 | P3 → Electronics rack/D1 assignment. H5 reaches the work bay; an existing hinged gate closes for inspection and reopens. | Closed-route response and recovery after reopening. | Work-zone context and EDATS |
| E5 | P1 → D2 station. H3 inspects the dispatch manifest while F2 services the bay; finite occupancy delays the approach, then clears. | True/observed bay availability, queue delay and verified release. | Point-availability context |
| T1 | P2 → Healthcare rack/D3 assignment. During 12 s of loaded travel, F1 telemetry is 2.5 s late; F1 continues its job. | F1 actual pose versus historical pose and message age. | Selective synchronization and stale-source trust |
| T2 | P3 → Electronics rack/D1 assignment. H2's tag link loses updates for 8 s while H2 continues working. | Frozen last pose, MISSING link, increasing age and recovery. | EDATS plus observation availability/trust |
| T3 | P1 → Apparel rack/D2 assignment. Proximity to F1's wrapped-load transfer starts an 8 s increased-LiDAR-noise window. | Raw range disagreement, sensor validity and baseline stopping. | TAM and multi-source trusted state |
| T4 | P2 → Healthcare rack/D3 assignment. Camera stream is unavailable for 7 s of loaded travel; other sensors remain available. | Dropout and recovery, task robustness without camera observations. | TAM availability reliability |
| T5 | P3 → D1 station. A dispatch wave changes transport/worker assignments; affected actors are 1.5 s stale and station messages 3 s stale for 10 s. | Concurrent state disagreement, timestamps and recovery. | EDATS prioritized updates and trusted context |

All E/T fixtures dispatch only after verified pickup and transition to loaded travel. E1/E4/E5/T3 also require actor arrival/proximity before their event is marked ACTIVE. E2/E3 mark the dispatched work window as active; the event marker alone does not prove the robot encountered congestion. Inspect positions, proximity and outcomes together. Gates and pallets have physical colliders and are included in ground truth.

T1/T2/T5 fault the conventional observation cache, not the true actor motion. The present local LiDAR/NavMesh baseline does not navigate from that cache; therefore a timing defect may create a stale dataset state without causing navigation failure. T4 may similarly succeed using LiDAR. Do not claim navigation improvements attributable to future modules until their decision inputs are connected and evaluated.

## Work and parcel handling

H1: rack picking/carton handoff. H2: cross-aisle carton transfer. H3: station scan/dispatch service. H4: trolley supply transfer. H5: inspection/replenishment. F1: inbound replenishment. F2: outbound dispatch. Forklifts include seated operators.

The normal worker controller follows valid NavMesh routes, yields to detected obstructions, limits acceleration and turns toward movement instead of sliding sideways. No valid path means waiting rather than walking through a rack. Cargo and work-stage visuals follow the service cycle. These worker cargo cycles are currently modeled loads, not individual stock-ledger transactions.

AMR sequence:

Navigate to source → align → validate barcode metadata/reserve slot → move gripper → verify grasp → retract onto rear deck → loaded navigation → retrieve rear load → extend to station/rack → release → verify destination and inventory.

The simulated manipulator uses a lift column, rotating carriage, telescopic beam and parallel gripper. It has a 2.5 m reach, 8 kg payload limit and bounded lift/extension/slew rates. Navigation pauses during handling. Payload reduces travel speed/acceleration/turn rate. An unreachable or unfinished transfer fails after a bounded handling timeout; it is not credited as delivered. Rack stock removal occurs only after grasp verification, and slot occupancy commits only on placement. Interrupted pickup restores the original stock visual; interrupted placement cancels reservations.

This is a kinematic handling model, not a six-axis industrial arm with force/torque sensing, collision-free arm planning, dynamic grasp simulation or certified load stability. Barcode validation reads parcel metadata; optical decoding is not implemented. Collision classification uses actual shape penetration/contact, not just a proximity radius.

The approach/tool/exit structure is informed by industrial workflows, not a claim of hardware equivalence: [UR pick/place documentation](https://www.universal-robots.com/manuals/EN/HTML/SW10_12_1/Content/prod-usr-man/software/PolyScopeX/polyx-program/polyx-Pick_Place.htm) and [UR palletizing documentation](https://www.universal-robots.com/manuals/EN/HTML/SW5_21/Content/prod-usr-man/software/PolyScope/content/Template/new_pallet_en.htm).

## Dataset contract

Use OPEN CSV FOLDER in the operations card or management page. Live data is outside Assets/StreamingAssets so Unity cannot import a file while it is being appended.

Per `Scenario_XX`:

- `GroundTruth_Environment.csv`: current physical state, current station availability and current actor work states, including physical disturbance props.
- `Unified_Observation.csv`: raw sensors/task context plus timestamped actor and station observations. T1/T2/T5 return actual historical samples, not current positions with a fake delay label.
- `Operational_Events.csv`: episode seed/setup, task stages, fixture dispatch/activation/recovery and exposure summary.

Root files include combined observation/truth CSVs, `Performance_Baseline.csv`, `Episode_Outcome_Report.csv` and `Dataset_Validation_Report.csv`. Existing schemas are preserved as `.previous_schema*.csv` before a fresh schema is opened. Episode IDs continue across Play sessions using `Episode_Sequence.txt`; do not manually reset that counter when appending to the same dataset.

New observation fields include P1–P3/D1–D3 availability, actor sample timestamps, message ages, link validity, work stages, station sample time and camera/LiDAR validity. Legacy PD1–PD3 aliases refer to P1–P3. Missing history is UNAVAILABLE/NA rather than substituted ground truth. VALID means a message exists; its age may still be stale. `camera_valid`/`lidar_valid` denote sample availability, not a TAM reliability score.

Join truth and observation by scenario/episode/step identifiers. Event labels and simulator truth are evaluation targets; do not leak them into the policy as privileged observations. A schema/structural PASS does not establish realism, sufficient fault exposure, or successful tasks. Log actual success, contact, path length, stops, replans, elapsed time, completed transfers and task-stage outcomes. E/T success means one transfer; do not compare its raw completion count with the three-transfer Agent missions without normalizing.

## Verification status and remaining acceptance test

Runtime code and Editor utilities compile against the installed Unity 6000.5.8f1 references. Pure C# checks pass for 15 unique scenarios, actor counts, time caps, one-order E/T missions, all P/D coverage, historical delays, link recovery, missing-history handling and CSV schema/decimal-locale consistency.

Live batch-mode verification was attempted but Unity returned `No valid Unity Editor license found. Please activate your license.` (`Logs/module1-verification-retry.log`, return code 198). No new scene screenshots, runtime performance results or visual acceptance results were obtained. Procedural assets, sensor simplifications and uncalibrated timing remain; this revision is not photorealistic or ready for real robot deployment.

After activating the Unity Editor license:

1. Open `Assets/scene1.unity`, allow compilation, then enter Play mode at normal time scale.
2. Verify the operations card, readable dropdown, scrollable description, inventory page and mounted signs at normal monitor resolutions.
3. Run a custom no-disturbance station-to-station transfer, then a station-to-rack transfer; verify pickup, rear load, actual release and stock state.
4. Run E1–E5 and T1–T5 individually. Confirm ACTIVE events, relevant physical exposure, recovery windows and actor job progress in CSVs; do not accept a timeout before pickup as a useful fault test.
5. Retest A1–A5 because slower physical handling changes arrival times even with unchanged routes.
6. Run All; confirm 15 terminal outcome rows, E1 ≤30 s, others ≤120 s and no continuing robot motion after completion.
7. Check CSV timestamps/row counts and outcome reports. Calibrate speeds, geometry, sensing and arm clearance from those live results before freezing an experimental baseline.

`ATADTRLModule1Verification.Run` is an Editor batch-mode smoke-test entry point. It uses a separate temporary dataset (or ATADTRL_VALIDATION_OUTPUT), accelerates simulation and exits the Editor. It is not intended to generate research results or be called inside an unsaved interactive Editor session.
