# A4 complete ATADTRL pipeline

**Baseline:** Display 1 runs scenario 4 unchanged. Rack_4_3 moves after the Books parcel route is committed, and Module 1 places at the stale position (`WRONG_PLACEMENT`).

**Module 1:** `A4ScenarioContract.cs`, shared Unity warehouse/sensors/loggers, and `CSV_Outputs/Scenario_04`.

**Module 2:** EDATS receives the physical rack-relocation event, updates the rack and destination pose, constructs the relocated-rack context, and evaluates `A4_PPO_V1`. The trained artifact and reproducible training log are under `Learn_Optimal_Navigation_Policy_PPO`.

**Module 3:** the shared trust module validates rack/inventory/twin state before the relocated target is accepted.

**Module 4:** `A4T3SafeReplayDisplay` replays the recorded Display 1 robot, human, and forklift traffic. It branches only at the stale destination, replans to the synchronized Rack_4_3 position, places the parcel, verifies the corrected slot, and returns the twin to normal monitoring.

Shared runtime code is located in `Assets/Testflow/Extensions`; it is shared to prevent duplicate Unity types while scenario data, trained policy and outputs remain isolated in this folder.
