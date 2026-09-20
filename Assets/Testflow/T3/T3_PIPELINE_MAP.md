# T3 complete ATADTRL pipeline

**Baseline:** Display 1 runs scenario 13 unchanged. LiDAR variance increases for eight seconds near F1 while RGB, IMU and wheel encoders remain operational.

**Module 1:** `T3ScenarioContract.cs`, shared Unity warehouse/sensors/loggers, and `CSV_Outputs/Scenario_13`.

**Module 2:** change detection identifies increased LiDAR variance, EDATS synchronizes sensor health, the adaptive twin records the degraded source, and `T3_PPO_V1` evaluates the trusted sensor state.

**Module 3:** TAM lowers LiDAR trust and constructs the world state from retained RGB, IMU and encoder evidence.

**Module 4:** `A4T3SafeReplayDisplay` replays the same Display 1 route, humans and forklifts. The policy uses fused-state navigation during degradation, completes the delivery, verifies the result, and returns the twin to normal monitoring.

Shared runtime code is located in `Assets/Testflow/Extensions`; it is shared to prevent duplicate Unity types while scenario data, trained policy and outputs remain isolated in this folder.
