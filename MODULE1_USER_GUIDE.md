# ATADTRL Module 1 — Warehouse and Data Acquisition

## Start the module

1. Open `Assets/scene1.unity` and enter Play mode.
2. Use the top-left scenario panel to select and run A1–A5, E1–E5, or T1–T5.
3. Select **INVENTORY & DESIGN** inside the operations card to open the management page.
4. Use its separate **Inventory**, **Facility Layout**, and **Scenario Design** tabs. Use **OPEN CSV FOLDER** to find the live dataset.

The scenario description scrolls independently of the controls. The operations card shows task stage, payload/battery, episode timing and event status. Rack, station and safety labels use mounted signboards rather than floating world text.

E1–E5 and T1–T5 each attempt one complete transfer. Pairings cover P1–P3 and D1–D3 across the ten scenarios. The time limit is 120 seconds except E1 (30 seconds). Agent scenario definitions are retained, but shared robot handling changes require rechecking encounter timing.

## Inventory page

The rack browser shows all 12 addressable segments for each rack. Every row contains the slot state, parcel number, barcode, description, mass, source, and destination.

- **VACANT** means the segment can accept a parcel.
- **RESERVED** means the robot has scanned a parcel and allocated the segment, but placement has not yet completed.
- **OCCUPIED** means the parcel was placed and the inventory transaction was committed.

Interrupted or failed episodes cancel an uncommitted reservation. Every research episode resets to the same deterministic starting inventory so results remain comparable.

## Custom warehouse environment

Enter the warehouse length, width, rack rows, rack columns, and aisle width, then choose **APPLY & REBUILD**. Module 1 rebuilds the geometry, rack inventory, station coordinates, research scenarios, and runtime NavMesh as one operation. **RESTORE BASELINE** returns to the default 60 m × 40 m, 4-row, 6-column layout.

The minimum 4 rows and 6 columns preserves named research addresses such as the Books rack (`Rack_4_3`). Larger layouts are supported when the requested rack grid fits inside the warehouse boundary.

## Custom data-acquisition scenario

Enter a scenario name and choose its source, destination, actor counts, optional obstacle, task mode, and episode limit. **CREATE, SAVE & SELECT** adds it to the normal scenario dropdown. Custom scenarios persist between runs and write the same dataset schema as the research scenarios.

**Run All always executes only the fixed 15 controlled research scenarios.** Custom scenarios run individually with **Start**, so they cannot accidentally alter the A/E/T benchmark.

## CSV and configuration locations

Live files are intentionally not written inside `Assets/StreamingAssets`, because Unity may import a CSV while it is still being appended and report a processed-byte/file-size mismatch.

For the current Player Settings, output is under:

`C:\Users\dprem\AppData\LocalLow\DefaultCompany\ATADTRL_Module1\ATADTRL_Dataset`

The Console prints the exact path at startup, every 100 logged records, and at episode completion. Each scenario folder contains `GroundTruth_Environment.csv`, `Unified_Observation.csv` and `Operational_Events.csv`; the dataset root also contains performance, validation, and episode outcome reports.

Custom layouts and scenarios are stored beside the dataset under `ATADTRL_Configurations`.
## Verification and limitations

See [Module 1 scenario protocol](MODULE1_SCENARIO_PROTOCOL.md) for all 15 scenarios, actor roles, fault timing, measurement fields and the acceptance checklist.

Runtime/Editor compilation and automated scenario/data-contract checks pass. Live testing is not yet verified: Unity batch mode stopped with `No valid Unity Editor license found`. Activate the installed Editor license in Unity Hub, then run the Play Mode checklist in the protocol.

This remains a procedural, kinematic research simulation. It does not yet provide photorealistic assets, physical force-verified grasping, optical barcode decoding, actor stock-ledger transactions or deployment certification. Future Modules 2–4 are not represented as already implemented.
