using System;
using System.Collections.Generic;
using UnityEngine;
using ATADTRL.Core;

namespace ATADTRL.Scenarios
{
    public interface IScenario
    {
        int ScenarioId { get; }
        string ScenarioName { get; }
        void Apply(ScenarioRuntimeContext context);
    }

    /// <summary>
    /// Passed to a scenario when it is applied, giving it access to the pieces
    /// of the world it is allowed to configure without hard-coding logic into
    /// the robot or navigation controllers.
    /// </summary>
    public class ScenarioRuntimeContext
    {
        public Transform robotStartPoint;
        public Transform robotGoalPoint;
        public Transform dynamicObjectRoot;
        public GameObject humanPrefab;
        public GameObject forkliftPrefab;
        public GameObject obstaclePrefab;
        public Action<Vector3, Vector3> SetStartGoal;
        public List<GameObject> SpawnedObjects = new List<GameObject>();
    }

    [Serializable]
    public class DynamicObjectSpawn
    {
        public string actorId;
        public string jobName;
        public DynamicObjectType type;
        public Vector3 startPosition;
        public Vector3 targetPosition;
        public Vector3 scale = Vector3.one;
        public float speed = 1.0f;
        public MovementPattern pattern = MovementPattern.LinearPatrol;
        public float activationDelay = 0f;
        // A worker needs a finite perception/reaction interval after first
        // seeing an AMR. A1 uses a longer value because H1 is carrying a
        // carton around a blind rack end; no actor is steered into a crash.
        public float robotReactionTime = 0f;
        // Some operational routes terminate at a work position instead of
        // patrolling back immediately (for example, a forklift parking in a
        // bottleneck or at a loading station).
        public bool stopAtTarget = false;
    }

    [Serializable]
    public class ScenarioDefinition
    {
        public int scenarioId;
        public string scenarioCode;
        public string scenarioName;
        public string description;
        public ScenarioCategory category;

        public Vector3 startPosition;
        public Vector3 goalPosition;

        [Header("Material Handling Task")]
        // A scenario can contain several collection and delivery stops.  The
        // original single-point fields are retained for backwards-compatible
        // CSV/metric consumers, and always describe the first/last stop.
        public List<Vector3> pickupPositions = new List<Vector3>();
        public List<Vector3> dropOffPositions = new List<Vector3>();
        public List<bool> pickupFromRackByTask = new List<bool>();
        public List<bool> deliverToRackByTask = new List<bool>();
        public List<string> sourceIds = new List<string>();
        public List<string> destinationIds = new List<string>();
        public List<string> parcelBarcodes = new List<string>();
        public List<string> parcelCategories = new List<string>();
        // Optional fixed aisle waypoints for the first loaded delivery. They
        // represent an ordinary warehouse route plan, not an incident trigger.
        // A1 uses them to route the robot through a real rack-end blind turn.
        public List<Vector3> firstTaskDeliveryWaypoints = new List<Vector3>();
        public Vector3 pickupPosition;
        public Vector3 dropOffPosition;
        public bool deliverToRack;
        public bool rotateTaskOrderEachEpisode = true;
        public int taskPriority = 1;
        public float communicationDelaySeconds;
        public bool simulateGraspDifficulty;
        public bool urgentOrderChange;
        public bool emergencyEvacuation;
        public bool requiresCharging;
        public float parcelMass = 3f;
        public Vector3 disturbanceLocation;
        public string researchMechanism;

        [Header("Rack Relocation Experiment")]
        public string relocatedRackId;
        public Vector3 rackRelocationOffset;
        public float rackRelocationDelaySeconds = 3f;

        [Header("Pickup Docking Tolerance (A5)")]
        // A5 injects a deliberate approach-pose error at the source. The
        // robot must evaluate this geometry against configured tolerances
        // rather than always attempting the same fixed approach.
        public Vector3 dockingInitialOffset = Vector3.zero;
        public float dockingInitialHeadingErrorDeg = 0f;
        public float dockingLateralToleranceM = 0.12f;
        public float dockingLongitudinalToleranceM = 0.15f;
        public float dockingHeadingToleranceDeg = 8f;

        [Header("Station Capacity / Readiness / Verification (E-series)")]
        // E1: destination receiving capacity, represented explicitly rather
        // than as a generic blocked path.
        public int stationMaxCapacity = 1;
        public int stationInitialLoad = 0;
        // E2: seconds the pickup station needs to finish staging the parcel
        // after the robot arrives. ParcelReady = 0 until this elapses.
        public float parcelReadyDelaySeconds = 0f;
        // E3: when true, the physically staged parcel's barcode does not
        // match the assigned barcode for this task.
        public bool simulateBarcodeMismatch = false;
        public string mismatchedBarcodeObserved = "";
        // E4/E5: marks a scenario as a genuine multi-destination mission
        // (rather than the single collapsed transaction most E/T
        // experiments use) so station-availability logic can act per task.
        public bool multiStationSequence = false;

        public List<DynamicObjectSpawn> dynamicObjects = new List<DynamicObjectSpawn>();

        [Header("Aisle Blockage")]
        public bool blockAisle = false;
        public int blockedAisleIndex = -1;
        public float blockageStartTime = 0f;
        public float blockageEndTime = -1f; // -1 = permanent for episode

        [Header("Sensor Overrides")]
        public bool overrideSensorNoise = false;
        public float lidarNoiseMultiplier = 1f;
        public float imuNoiseMultiplier = 1f;
        public float encoderNoiseMultiplier = 1f;
        public bool forceLidarDropout = false;
        public bool forceCameraDropout = false;
        public bool forceEncoderDropout = false;
        public bool conflictingObservations = false;

        [Header("Episode")]
        public float maxEpisodeDuration = 120f;

        public void ApplyTo(ScenarioRuntimeContext ctx)
        {
            ctx.SetStartGoal?.Invoke(startPosition, goalPosition);

            foreach (var spawn in dynamicObjects)
            {
                GameObject prefab = spawn.type switch
                {
                    DynamicObjectType.Human => ctx.humanPrefab,
                    DynamicObjectType.Forklift => ctx.forkliftPrefab,
                    _ => ctx.obstaclePrefab
                };
                if (prefab == null) continue;

                var go = UnityEngine.Object.Instantiate(prefab, spawn.startPosition, Quaternion.identity, ctx.dynamicObjectRoot);
                go.transform.localScale = Vector3.Scale(go.transform.localScale, spawn.scale);
                var mover = go.GetComponent<Environment.DynamicObjectMover>();
                if (mover == null) mover = go.AddComponent<Environment.DynamicObjectMover>();
                mover.Initialize(spawn);
                ctx.SpawnedObjects.Add(go);
            }
        }
    }

    /// <summary>
    /// Static factory that builds the final 15 Module 1 baseline scenarios in a
    /// purely data-driven way. No scenario-specific logic lives in the robot
    /// or navigation controllers.
    /// </summary>
    public static class ScenarioLibrary
    {
        public static List<ScenarioDefinition> BuildAll(WarehouseConfig wh)
        {
            var list = new List<ScenarioDefinition>();
            Vector3 start = new Vector3(-wh.warehouseLength * 0.4f, 0f, -wh.warehouseWidth * 0.35f);
            Vector3 goal = new Vector3(wh.warehouseLength * 0.4f, 0f, wh.warehouseWidth * 0.35f);

            list.Add(new ScenarioDefinition { scenarioId = 1, scenarioName = "Static Warehouse Navigation", category = ScenarioCategory.StaticNavigation, description = "No dynamic obstacles; robot navigates start to goal.", startPosition = start, goalPosition = goal });

            list.Add(new ScenarioDefinition { scenarioId = 2, scenarioName = "Static Obstacle", category = ScenarioCategory.StaticObstacle, description = "One obstacle blocks part of the route.", startPosition = start, goalPosition = goal,
                dynamicObjects = { new DynamicObjectSpawn { type = DynamicObjectType.MovableObstacle, startPosition = Vector3.Lerp(start, goal, 0.5f), pattern = MovementPattern.Static } } });

            list.Add(new ScenarioDefinition { scenarioId = 3, scenarioName = "Multiple Static Obstacles", category = ScenarioCategory.StaticObstacle, description = "Several obstacles create path constraints.", startPosition = start, goalPosition = goal,
                dynamicObjects = {
                    new DynamicObjectSpawn { type = DynamicObjectType.MovableObstacle, startPosition = Vector3.Lerp(start, goal, 0.3f), pattern = MovementPattern.Static },
                    new DynamicObjectSpawn { type = DynamicObjectType.MovableObstacle, startPosition = Vector3.Lerp(start, goal, 0.6f), pattern = MovementPattern.Static },
                    new DynamicObjectSpawn { type = DynamicObjectType.MovableObstacle, startPosition = Vector3.Lerp(start, goal, 0.8f), pattern = MovementPattern.Static }
                } });

            list.Add(new ScenarioDefinition { scenarioId = 4, scenarioName = "Moving Obstacle", category = ScenarioCategory.DynamicObstacle, description = "An obstacle crosses the robot's path.", startPosition = start, goalPosition = goal,
                dynamicObjects = { new DynamicObjectSpawn { type = DynamicObjectType.MovableObstacle, startPosition = new Vector3(0, 0, -10), targetPosition = new Vector3(0, 0, 10), speed = 1.5f, pattern = MovementPattern.LinearPatrol } } });

            list.Add(new ScenarioDefinition { scenarioId = 5, scenarioName = "Human Crossing", category = ScenarioCategory.HumanInteraction, description = "Human crosses the robot's planned path.", startPosition = start, goalPosition = goal,
                dynamicObjects = { new DynamicObjectSpawn { type = DynamicObjectType.Human, startPosition = new Vector3(2, 0, -8), targetPosition = new Vector3(2, 0, 8), speed = 1.2f, pattern = MovementPattern.CrossPath } } });

            list.Add(new ScenarioDefinition { scenarioId = 6, scenarioName = "Forklift Crossing", category = ScenarioCategory.ForkliftInteraction, description = "Forklift crosses an aisle.", startPosition = start, goalPosition = goal,
                dynamicObjects = { new DynamicObjectSpawn { type = DynamicObjectType.Forklift, startPosition = new Vector3(-4, 0, -6), targetPosition = new Vector3(-4, 0, 6), speed = 2.0f, pattern = MovementPattern.CrossPath } } });

            list.Add(new ScenarioDefinition { scenarioId = 7, scenarioName = "Multiple Moving Objects", category = ScenarioCategory.MultiAgentTraffic, description = "Human and forklift operate simultaneously.", startPosition = start, goalPosition = goal,
                dynamicObjects = {
                    new DynamicObjectSpawn { type = DynamicObjectType.Human, startPosition = new Vector3(2, 0, -8), targetPosition = new Vector3(2, 0, 8), speed = 1.2f, pattern = MovementPattern.CrossPath },
                    new DynamicObjectSpawn { type = DynamicObjectType.Forklift, startPosition = new Vector3(-4, 0, -6), targetPosition = new Vector3(-4, 0, 6), speed = 2.0f, pattern = MovementPattern.CrossPath }
                } });

            list.Add(new ScenarioDefinition { scenarioId = 8, scenarioName = "Temporary Aisle Blockage", category = ScenarioCategory.AisleBlockage, description = "An aisle becomes unavailable temporarily.", startPosition = start, goalPosition = goal,
                blockAisle = true, blockedAisleIndex = 1, blockageStartTime = 2f, blockageEndTime = 20f });

            list.Add(new ScenarioDefinition { scenarioId = 9, scenarioName = "Obstacle Appears Dynamically", category = ScenarioCategory.DynamicObstacle, description = "New obstacle introduced after navigation begins.", startPosition = start, goalPosition = goal,
                dynamicObjects = { new DynamicObjectSpawn { type = DynamicObjectType.MovableObstacle, startPosition = Vector3.Lerp(start, goal, 0.5f), pattern = MovementPattern.Static, activationDelay = 5f } } });

            list.Add(new ScenarioDefinition { scenarioId = 10, scenarioName = "Obstacle Disappears", category = ScenarioCategory.DynamicObstacle, description = "Previously blocked route becomes available.", startPosition = start, goalPosition = goal,
                blockAisle = true, blockedAisleIndex = 0, blockageStartTime = 0f, blockageEndTime = 8f });

            list.Add(new ScenarioDefinition { scenarioId = 11, scenarioName = "Dynamic Obstacle Speed Variation", category = ScenarioCategory.DynamicObstacle, description = "Moving obstacle changes velocity.", startPosition = start, goalPosition = goal,
                dynamicObjects = { new DynamicObjectSpawn { type = DynamicObjectType.MovableObstacle, startPosition = new Vector3(0, 0, -10), targetPosition = new Vector3(0, 0, 10), speed = 3.0f, pattern = MovementPattern.LinearPatrol } } });

            list.Add(new ScenarioDefinition { scenarioId = 12, scenarioName = "Sensor Noise Increase", category = ScenarioCategory.SensorDegradation, description = "Increased sensor measurement uncertainty.", startPosition = start, goalPosition = goal,
                overrideSensorNoise = true, lidarNoiseMultiplier = 4f, imuNoiseMultiplier = 4f, encoderNoiseMultiplier = 4f });

            list.Add(new ScenarioDefinition { scenarioId = 13, scenarioName = "Sensor Dropout", category = ScenarioCategory.SensorDegradation, description = "One sensor temporarily stops providing observations.", startPosition = start, goalPosition = goal,
                overrideSensorNoise = true, forceLidarDropout = true });

            list.Add(new ScenarioDefinition { scenarioId = 14, scenarioName = "Multiple Sensor Degradation", category = ScenarioCategory.SensorDegradation, description = "More than one sensor becomes unreliable.", startPosition = start, goalPosition = goal,
                overrideSensorNoise = true, forceLidarDropout = true, forceCameraDropout = true, encoderNoiseMultiplier = 3f });

            list.Add(new ScenarioDefinition { scenarioId = 15, scenarioName = "Conflicting Observations", category = ScenarioCategory.SensorDegradation, description = "Different sensors produce inconsistent observations.", startPosition = start, goalPosition = goal,
                overrideSensorNoise = true, conflictingObservations = true, lidarNoiseMultiplier = 2f });

            list.Add(new ScenarioDefinition { scenarioId = 16, scenarioName = "Dynamic Warehouse Traffic", category = ScenarioCategory.MultiAgentTraffic, description = "Multiple humans/forklifts create traffic.", startPosition = start, goalPosition = goal,
                dynamicObjects = {
                    new DynamicObjectSpawn { type = DynamicObjectType.Human, startPosition = new Vector3(2, 0, -8), targetPosition = new Vector3(2, 0, 8), speed = 1.2f, pattern = MovementPattern.CrossPath },
                    new DynamicObjectSpawn { type = DynamicObjectType.Human, startPosition = new Vector3(-6, 0, 8), targetPosition = new Vector3(-6, 0, -8), speed = 1.0f, pattern = MovementPattern.CrossPath },
                    new DynamicObjectSpawn { type = DynamicObjectType.Forklift, startPosition = new Vector3(-4, 0, -6), targetPosition = new Vector3(-4, 0, 6), speed = 2.0f, pattern = MovementPattern.CrossPath }
                } });

            list.Add(new ScenarioDefinition { scenarioId = 17, scenarioName = "Narrow Aisle Navigation", category = ScenarioCategory.StaticNavigation, description = "Robot navigates through constrained space.", startPosition = start, goalPosition = new Vector3(0, 0, 0) });

            list.Add(new ScenarioDefinition { scenarioId = 18, scenarioName = "Complex Dynamic Environment", category = ScenarioCategory.StressTest, description = "Combination of moving objects, blockage and sensor uncertainty.", startPosition = start, goalPosition = goal,
                blockAisle = true, blockedAisleIndex = 1, blockageStartTime = 3f, blockageEndTime = 15f,
                overrideSensorNoise = true, lidarNoiseMultiplier = 2f,
                dynamicObjects = {
                    new DynamicObjectSpawn { type = DynamicObjectType.Human, startPosition = new Vector3(2, 0, -8), targetPosition = new Vector3(2, 0, 8), speed = 1.2f, pattern = MovementPattern.CrossPath },
                    new DynamicObjectSpawn { type = DynamicObjectType.Forklift, startPosition = new Vector3(-4, 0, -6), targetPosition = new Vector3(-4, 0, 6), speed = 2.0f, pattern = MovementPattern.CrossPath }
                } });

            list.Add(new ScenarioDefinition { scenarioId = 19, scenarioName = "Full Stress Test", category = ScenarioCategory.StressTest, description = "Dynamic obstacles, human, forklift, blockage, sensor noise/dropout, complex navigation.", startPosition = start, goalPosition = goal,
                blockAisle = true, blockedAisleIndex = 2, blockageStartTime = 2f, blockageEndTime = 25f,
                overrideSensorNoise = true, lidarNoiseMultiplier = 3f, imuNoiseMultiplier = 3f, encoderNoiseMultiplier = 3f, forceCameraDropout = true, conflictingObservations = true,
                dynamicObjects = {
                    new DynamicObjectSpawn { type = DynamicObjectType.Human, startPosition = new Vector3(2, 0, -8), targetPosition = new Vector3(2, 0, 8), speed = 1.4f, pattern = MovementPattern.CrossPath },
                    new DynamicObjectSpawn { type = DynamicObjectType.Human, startPosition = new Vector3(-6, 0, 8), targetPosition = new Vector3(-6, 0, -8), speed = 1.0f, pattern = MovementPattern.CrossPath },
                    new DynamicObjectSpawn { type = DynamicObjectType.Forklift, startPosition = new Vector3(-4, 0, -6), targetPosition = new Vector3(-4, 0, 6), speed = 2.2f, pattern = MovementPattern.CrossPath },
                    new DynamicObjectSpawn { type = DynamicObjectType.MovableObstacle, startPosition = Vector3.Lerp(start, goal, 0.5f), pattern = MovementPattern.Static, activationDelay = 4f }
                } });

            BuildFinalWarehouseTaskScenarios(list, start, goal);
            ConfigureDeliveryTasks(list, wh);
            ConfigureOperationalExperiments(list, wh);
            return list;
        }

        private static void ConfigureOperationalExperiments(List<ScenarioDefinition> scenarios, WarehouseConfig wh)
        {
            string[] titles = { "Destination Station Capacity Full", "Assigned Parcel Not Ready at Pickup",
                "Wrong Barcode Parcel at Pickup Station", "Two-Destination Delivery With Temporary Station Unavailability",
                "Three-Destination Dynamic Order Fulfilment",
                "Stale Forklift Position", "Delayed Human-Position Update", "LiDAR Observation Degradation",
                "Temporary RGB Observation Dropout", "Concurrent Multi-State Twin Divergence" };
            string[] details = {
                "The robot picks a barcode parcel and travels to its assigned drop station. Receiving capacity there reaches its configured maximum before the robot arrives (Current_Load >= Maximum_Capacity), so navigation succeeds but placement cannot proceed until capacity frees up. This is not solved by rerouting.",
                "The robot reaches its assigned pickup station, but the required barcode parcel has not yet been staged because the human picking process is delayed (ParcelReady = 0). The robot must hold and wait, never spawn or assume the parcel, and resume once ParcelReady = 1.",
                "The pickup station physically holds a different barcode than the one assigned to this task. Barcode verification compares Barcode_observed against Barcode_assigned and must reject the mismatched parcel, reporting a mis-sort instead of picking it up.",
                "The robot carries two barcode parcels from S1: one assigned to S2, one assigned to S3. S2 becomes temporarily unavailable from legitimate warehouse operations while S3 remains open. Parcel-destination association never changes; only the visiting order could adapt.",
                "The robot carries three barcode-labelled parcels, one for each of S1, S2 and S3. Station availability changes naturally through the mission as humans and a forklift perform legitimate work. Parcel-destination association never changes; only the visiting order could adapt.",
                "F1 moves normally from an old position to a new one while the recorded twin state is delayed, so navigation can use an obsolete obstacle position until EDATS detects the deviation and synchronizes it.",
                "H2 changes position normally while the twin retains the previous position for a controlled delay, producing a genuine ground-truth/twin divergence rather than a teleport, until EDATS determines synchronization is required.",
                "LiDAR range measurements are temporarily degraded with controlled simulated noise while RGB, IMU and encoders remain operational, so the trust-assessment formulation should down-weight the degraded source.",
                "The RGB camera provides no valid observations (R_RGB_avail = 0) for a controlled interval while other sensors continue sampling, so the trusted-state representation should fall back to the remaining valid sources instead of halting.",
                "Over a short but realistic interval, F1 changes aisle, F2 stops for a warehouse task, H1 changes aisle, H2 moves away from a rack and station telemetry falls behind, progressively causing multiple twin components to become stale rather than changing in a single frame." };
            for (int i = 5; i < Mathf.Min(15, scenarios.Count); i++)
            {
                ScenarioDefinition s = scenarios[i];
                s.scenarioName = titles[i - 5]; s.description = details[i - 5];
                // E-series (i=5..9): EDATS-style context recognition feeding
                // a policy response. T1/T2/T5 (i=10,11,14): EDATS state-age
                // driven twin synchronization. T3/T4 (i=12,13): TAM trust
                // formulation over sensor sources.
                s.researchMechanism = i < 10 ? "Module 2: EDATS + context; Module 4: policy response" :
                    i == 12 || i == 13 ? "Module 3: TAM + source validity; Module 4: trusted policy input" :
                    "Module 2: EDATS state age; Module 3: stale-source trust";
                s.blockAisle = false; s.overrideSensorNoise = false; s.urgentOrderChange = false;
                s.communicationDelaySeconds = 0f; s.forceCameraDropout = false;
                s.dynamicObjects.RemoveAll(a => a.type == DynamicObjectType.MovableObstacle);
                Vector3 source = s.pickupPositions[0];
                // Shared handoff lanes lie outside the rack grid. Each route
                // is tied to a real source station, not the robot's live pose.
                Vector3 inward = new Vector3(-Mathf.Sign(source.x), 0f, 0f);
                Vector3 junction = source + inward * 4.2f;
                s.disturbanceLocation = junction;
                s.startPosition = source + Vector3.back * 1.6f;
                s.firstTaskDeliveryWaypoints = new List<Vector3> { junction, junction + inward * 2f };
                s.deliverToRackByTask = new List<bool> { false, false, false };
                // Actors keep the well-separated warehouse-wide start/target
                // positions EnsureActiveWarehouseActors already gave them
                // (the same spread A1-A5 use) instead of being recomputed
                // relative to a single shared point, which previously
                // collapsed every actor's patrol target onto nearly the same
                // coordinate and caused them to pile up on top of each other.
                string[] jobNames = { "H1 rack picking and carton handoff", "H2 cross-aisle carton transfer", "H3 station scan and dispatch verification",
                    "H4 trolley supply transfer", "H5 rack inspection and replenishment", "F1 inbound pallet replenishment", "F2 outbound pallet dispatch" };
                for (int a = 0; a < s.dynamicObjects.Count; a++)
                {
                    var actor = s.dynamicObjects[a];
                    int k = actor.type == DynamicObjectType.Human ? int.Parse(actor.actorId.Substring(1)) - 1 : 5 + int.Parse(actor.actorId.Substring(1)) - 1;
                    if (k < 0 || k >= jobNames.Length) continue;
                    actor.activationDelay = 0f; actor.stopAtTarget = false;
                    actor.jobName = jobNames[k];
                    // T3's trust degradation is proximity-triggered, so F1
                    // needs to pass close to the robot's short local route.
                    if (actor.actorId=="F1" && s.scenarioCode=="T3")
                    { actor.startPosition=junction+Vector3.forward*1.5f;actor.targetPosition=junction+Vector3.back*1.5f; }
                }

                if (s.scenarioCode == "E4")
                {
                    // S1 = shared pickup/loading station; S2, S3 = the two
                    // route-variant drop stations already assigned above.
                    Vector3 d1 = s.dropOffPositions[0], d2 = s.dropOffPositions.Count > 1 ? s.dropOffPositions[1] : d1;
                    s.pickupPositions = new List<Vector3> { source, source };
                    s.dropOffPositions = new List<Vector3> { d1, d2 };
                    s.sourceIds = new List<string> { "S1", "S1" };
                    s.destinationIds = new List<string> { "S2", "S3" };
                    s.parcelBarcodes = new List<string> { "PKG_E401", "PKG_E402" };
                    s.parcelCategories = new List<string> { "General", "General" };
                    s.pickupFromRackByTask = new List<bool> { false, false };
                    s.deliverToRackByTask = new List<bool> { false, false };
                    s.pickupPosition = source; s.dropOffPosition = d2; s.goalPosition = d2;
                    s.disturbanceLocation = d1; // S2: the destination that goes temporarily unavailable
                    s.multiStationSequence = true;
                }
                else if (s.scenarioCode == "E5")
                {
                    Vector3 d1 = s.dropOffPositions[0];
                    Vector3 d2 = s.dropOffPositions.Count > 1 ? s.dropOffPositions[1] : d1;
                    Vector3 d3 = s.dropOffPositions.Count > 2 ? s.dropOffPositions[2] : d2;
                    s.pickupPositions = new List<Vector3> { source, source, source };
                    s.dropOffPositions = new List<Vector3> { d1, d2, d3 };
                    s.sourceIds = new List<string> { "S1", "S1", "S1" };
                    s.destinationIds = new List<string> { "S1", "S2", "S3" };
                    s.parcelBarcodes = new List<string> { "PKG_E501", "PKG_E502", "PKG_E503" };
                    s.parcelCategories = new List<string> { "General", "General", "General" };
                    s.pickupFromRackByTask = new List<bool> { false, false, false };
                    s.deliverToRackByTask = new List<bool> { false, false, false };
                    s.pickupPosition = source; s.dropOffPosition = d3; s.goalPosition = d3;
                    s.disturbanceLocation = d1; // the station that becomes temporarily unavailable first
                    s.multiStationSequence = true;
                }
                else
                {
                    // A two-minute episode evaluates one complete
                    // transaction for every other E/T experiment.
                    s.pickupPositions = s.pickupPositions.GetRange(0,1);
                    s.dropOffPositions = s.dropOffPositions.GetRange(0,1);
                    s.sourceIds = s.sourceIds.GetRange(0,1);
                    s.destinationIds = s.destinationIds.GetRange(0,1);
                    s.parcelBarcodes = new List<string> { $"ATD-{s.scenarioCode}-001" };
                    s.parcelCategories = s.parcelCategories.GetRange(0,1);
                    s.pickupFromRackByTask = new List<bool> { false };
                    s.deliverToRackByTask = s.deliverToRackByTask.GetRange(0,1);
                    s.pickupPosition=s.pickupPositions[0]; s.dropOffPosition=s.dropOffPositions[0]; s.goalPosition=s.dropOffPosition;
                    if (s.scenarioCode == "E3")
                        // The physically staged parcel carries a different
                        // barcode than the one assigned to this task.
                        s.mismatchedBarcodeObserved = "ATD-WRONG-9999";
                }
                s.description += $" One complete transfer: {s.sourceIds[0]} to {s.destinationIds[0]}. " + s.researchMechanism + ".";
            }
        }

        // The final experiment set replaces the early navigation-only list.
        // Each entry shares the same complete three-parcel mission and varies
        // traffic, access, sensing, task context, or emergency conditions.
        private static void BuildFinalWarehouseTaskScenarios(List<ScenarioDefinition> list, Vector3 start, Vector3 goal)
        {
            list.Clear();
            // ConfigureDeliveryTasks assigns each scenario a safe aisle
            // staging point and one of three distinct P/D route plans.
            start = Vector3.zero;
            // Agent level: the conventional non-RL robot can complete normal
            // work, but these conditions expose local-planning limitations.
            AddFinalScenario(list, 1, "A1", "Books-Rack Blind-Turn Collision", ScenarioCategory.HumanInteraction,
                "The robot scans and collects the P1 parcel, then carries it toward D2 through the aisle beside the Books rack. At the north rack end it turns toward D2 while H1 independently carries another carton toward the same blind corner from the opposite direction. The Books rack hides their approach until the turn, leaving insufficient reaction distance and causing COLLISION_HUMAN.", start, goal);
            AddFinalScenario(list, 2, "A2", "Dynamic Path Obstruction After Route Commitment", ScenarioCategory.ForkliftInteraction,
                "The robot scans a P2 parcel for D3 and commits to the planned lower cross-aisle route. F1 then reaches and stops in that forward path while people restrict nearby passages and F2 continues its assigned route. Rerouting is an acceptable and expected recovery; Module 1 instead repeatedly waits or replans around an unusable route.", start, goal);
            AddFinalScenario(list, 3, "A3", "Drop-Off Approach Blocked", ScenarioCategory.ForkliftInteraction,
                "The robot transports a barcode parcel from P1 to D3. H2 and F2 occupy the D3 approach while H1, H3-H5 and F1 continue working, so the baseline reaches the destination region but cannot safely place the parcel.", start, goal);
            AddFinalScenario(list, 4, "A4", "Rack Relocation Causing Wrong Parcel Placement", ScenarioCategory.DynamicObstacle,
                "After the robot scans a Books parcel and commits to the target rack, that rack is relocated. Module 1 retains the original destination and deposits the parcel at the stale rack position, producing a WRONG_PLACEMENT outcome.", start, goal, priority: 2);
            AddFinalScenario(list, 5, "A5", "Pickup Docking Misalignment at Rack Face", ScenarioCategory.DynamicObstacle,
                "The robot arrives at the Books rack pickup face with a lateral, longitudinal and heading offset from the ideal docking pose, while nearby legitimate worker activity narrows the manoeuvring space. Pickup is only permitted once docking geometry satisfies configured tolerances; Module 1 instead repeats the same fixed approach.", start, goal,
                dockingOffset: new Vector3(0.55f, 0f, 0.35f), dockingHeadingErrorDeg: 18f);

            // Environment level: these represent warehouse operational
            // changes (capacity, staging, verification, sequencing) rather
            // than simple blocked-path/rerouting problems.
            AddFinalScenario(list, 6, "E1", "Destination Station Capacity Full", ScenarioCategory.MultiAgentTraffic,
                "The robot picks a barcode parcel and travels to its assigned drop station. During normal operation the station's receiving capacity reaches its configured maximum before the robot arrives, so navigation succeeds (NavigationSuccess = 1) but placement cannot proceed (TaskSuccess = 0) until capacity frees up.", start, goal,
                stationMaxCapacity: 2, stationInitialLoad: 2);
            AddFinalScenario(list, 7, "E2", "Assigned Parcel Not Ready at Pickup", ScenarioCategory.DynamicObstacle,
                "The robot reaches its assigned pickup station, but the required barcode parcel has not yet been staged because the human picking process is delayed (ParcelReady = 0). The robot must hold and wait rather than spawning or assuming the parcel; it resumes once ParcelReady = 1.", start, goal,
                parcelReadyDelay: 9f);
            AddFinalScenario(list, 8, "E3", "Wrong Barcode Parcel at Pickup Station", ScenarioCategory.DynamicObstacle,
                "The pickup station physically holds a different barcode than the one assigned to this task (Observed_Parcel_ID != Expected_Parcel_ID). Barcode verification must reject the mismatched parcel and report a mis-sort instead of picking it up.", start, goal,
                barcodeMismatch: true);
            AddFinalScenario(list, 9, "E4", "Two-Destination Delivery With Temporary Station Unavailability", ScenarioCategory.MultiAgentTraffic,
                "The robot carries two barcode parcels from S1: PKG_E401 assigned to S2, PKG_E402 assigned to S3. The planned S2-then-S3 sequence is attempted, but S2 becomes temporarily unavailable from legitimate warehouse operations while S3 remains open. Parcel-destination association never changes; Module 1 keeps its fixed sequence and waits at S2 instead of visiting S3 first.", start, goal,
                multiStation: true);
            AddFinalScenario(list, 10, "E5", "Three-Destination Dynamic Order Fulfilment", ScenarioCategory.MultiAgentTraffic,
                "The robot carries three barcode-labelled parcels, one for each of S1, S2 and S3. Station availability changes naturally through the mission as humans and a forklift perform legitimate work at each station. Parcel-destination association never changes; Module 1 keeps its fixed S1-S2-S3 sequence and absorbs any waiting rather than reordering the remaining stops.", start, goal,
                multiStation: true);

            // Twin level: raw sensor/state defects are injected without
            // making the baseline artificially useless. These later measure
            // EDATS, adaptive-twin, TAM and trust-aware-state improvements.
            AddFinalScenario(list, 11, "T1", "Stale Forklift Position", ScenarioCategory.DigitalTwin,
                "F1 moves normally from its old position to a new one while the recorded twin state is delayed, so navigation can use an obsolete obstacle position until EDATS detects the deviation and synchronizes it.", start, goal,
                communicationDelay: 1.5f);
            AddFinalScenario(list, 12, "T2", "Delayed Human-Position Update", ScenarioCategory.DigitalTwin,
                "H2 changes position normally while the twin receives/retains the previous position for a controlled delay, so ground truth and twin state genuinely diverge until EDATS determines synchronization is required.", start, goal,
                communicationDelay: 1.0f);
            AddFinalScenario(list, 13, "T3", "LiDAR Observation Degradation", ScenarioCategory.DigitalTwin,
                "LiDAR range measurements are temporarily degraded with controlled simulated noise during S2-to-S1 transport while RGB, IMU and encoders remain operational, so the trust-assessment formulation should down-weight the degraded source rather than the robot simply stopping.", start, goal,
                sensorNoise: true, lidarNoise: 6f);
            AddFinalScenario(list, 14, "T4", "Temporary RGB Observation Dropout", ScenarioCategory.DigitalTwin,
                "The RGB camera provides no valid observations (R_RGB_avail = 0) for a controlled interval during S3-to-S2 transport while other sensors continue sampling, so the trusted-state representation should fall back to the remaining valid sources instead of halting.", start, goal,
                sensorNoise: true, cameraDropout: true);
            AddFinalScenario(list, 15, "T5", "Concurrent Multi-State Twin Divergence", ScenarioCategory.DigitalTwin,
                "Over a short but realistic interval, F1 changes aisle, F2 stops for a warehouse task, H1 changes aisle, H2 moves away from a rack and station telemetry falls behind, progressively causing multiple twin components to become stale rather than changing in a single frame.", start, goal,
                blockAisle: true, blockedAisle: 3, obstacleDelay: 5f, sensorNoise: true, lidarNoise: 2f,
                communicationDelay: 1.2f);
        }

        private static void AddFinalScenario(List<ScenarioDefinition> list, int id, string code, string name, ScenarioCategory category,
            string description, Vector3 start, Vector3 goal, bool blockAisle = false, int blockedAisle = -1,
            float obstacleDelay = -1f, bool sensorNoise = false, float lidarNoise = 1f, float encoderNoise = 1f,
            bool cameraDropout = false, float communicationDelay = 0f, int priority = 1, bool graspDifficulty = false,
            bool emergency = false, bool urgentOrderChange = false, Vector3? dockingOffset = null,
            float dockingHeadingErrorDeg = 0f, int stationMaxCapacity = 1, int stationInitialLoad = 0,
            float parcelReadyDelay = 0f, bool barcodeMismatch = false, bool multiStation = false)
        {
            var scenario = new ScenarioDefinition
            {
                scenarioId = id,
                scenarioCode = code,
                scenarioName = name,
                category = category,
                description = description,
                startPosition = start,
                goalPosition = goal,
                maxEpisodeDuration = 120f,
                blockAisle = blockAisle,
                blockedAisleIndex = blockedAisle,
                blockageStartTime = blockAisle ? 4f : 0f,
                blockageEndTime = blockAisle ? 30f : -1f,
                overrideSensorNoise = sensorNoise,
                lidarNoiseMultiplier = lidarNoise,
                encoderNoiseMultiplier = encoderNoise,
                forceCameraDropout = cameraDropout,
                communicationDelaySeconds = communicationDelay,
                taskPriority = priority,
                simulateGraspDifficulty = graspDifficulty,
                urgentOrderChange = urgentOrderChange,
                emergencyEvacuation = emergency,
                dockingInitialOffset = dockingOffset ?? Vector3.zero,
                dockingInitialHeadingErrorDeg = dockingHeadingErrorDeg,
                stationMaxCapacity = stationMaxCapacity,
                stationInitialLoad = stationInitialLoad,
                parcelReadyDelaySeconds = parcelReadyDelay,
                simulateBarcodeMismatch = barcodeMismatch,
                multiStationSequence = multiStation
            };

            if (obstacleDelay >= 0f)
            {
                scenario.dynamicObjects.Add(new DynamicObjectSpawn
                {
                    actorId = "PalletCollapse",
                    type = DynamicObjectType.MovableObstacle,
                    startPosition = new Vector3(0f, 0f, 4.5f),
                    targetPosition = new Vector3(0f, 0f, 4.5f),
                    pattern = MovementPattern.Static,
                    activationDelay = obstacleDelay
                });
            }
            list.Add(scenario);
        }

        // Common mission for every scenario: Rack R1 -> PD1, PD2 -> Rack R2,
        // Rack R3 -> PD3. All pickup/delivery markers are kept in reachable
        // aisles while the parcel itself sits visibly on its rack shelf.
        private static void ConfigureDeliveryTasks(List<ScenarioDefinition> scenarios, WarehouseConfig warehouse)
        {
            // P1/P2/P3 are incoming parcel shelves on three perimeter
            // corners. D1/D2/D3 are their corresponding category-rack
            // access bays, so every transfer crosses a long warehouse route.
            Vector3 pickupP1 = GetStationPosition(warehouse.pickupStationPositions, 0, new Vector3(-24f, 0f, -15f));
            Vector3 pickupP2 = GetStationPosition(warehouse.pickupStationPositions, 1, new Vector3(24f, 0f, -15f));
            Vector3 pickupP3 = GetStationPosition(warehouse.pickupStationPositions, 2, new Vector3(-24f, 0f, 15f));
            Vector3 dropD1Electronics = GetStationPosition(warehouse.dropStationPositions, 0, new Vector3(-12.3f, 0f, 13.5f));
            Vector3 dropD2Apparel = GetStationPosition(warehouse.dropStationPositions, 1, new Vector3(12.3f, 0f, 13.5f));
            Vector3 dropD3Healthcare = GetStationPosition(warehouse.dropStationPositions, 2, new Vector3(-12.3f, 0f, -13.5f));

            foreach (var scenario in scenarios)
            {
                // All scenarios use the same physical P1-P3 and D1-D3
                // stations, but their execution order and pairings differ.
                // This prevents A1 and A3 (and adjacent experiments) from
                // appearing to perform an identical warehouse mission.
                string taskPlan;
                int routeVariant = (scenario.scenarioId - 1) % 3;
                if (routeVariant == 0)
                {
                    // A1: P1->D2, P2->D1, P3->D3.
                    scenario.pickupPositions = new List<Vector3> { pickupP1, pickupP2, pickupP3 };
                    scenario.dropOffPositions = new List<Vector3> { dropD2Apparel, dropD1Electronics, dropD3Healthcare };
                    scenario.sourceIds = new List<string> { "P1", "P2", "P3" };
                    scenario.destinationIds = new List<string> { "D2-Apparel", "D1-Electronics", "D3-Healthcare" };
                    scenario.parcelBarcodes = new List<string> { "ATD-APL-0001", "ATD-ELC-0002", "ATD-HLT-0003" };
                    scenario.parcelCategories = new List<string> { "Apparel", "Electronics", "Healthcare" };
                    taskPlan = "P1 Apparel to D2, P2 Electronics to D1, P3 Healthcare to D3";
                }
                else if (routeVariant == 1)
                {
                    // A2: P2->D3, P3->D2, P1->D1.
                    scenario.pickupPositions = new List<Vector3> { pickupP2, pickupP3, pickupP1 };
                    scenario.dropOffPositions = new List<Vector3> { dropD3Healthcare, dropD2Apparel, dropD1Electronics };
                    scenario.sourceIds = new List<string> { "P2", "P3", "P1" };
                    scenario.destinationIds = new List<string> { "D3-Healthcare", "D2-Apparel", "D1-Electronics" };
                    scenario.parcelBarcodes = new List<string> { "ATD-HLT-0001", "ATD-APL-0002", "ATD-ELC-0003" };
                    scenario.parcelCategories = new List<string> { "Healthcare", "Apparel", "Electronics" };
                    taskPlan = "P2 Healthcare to D3, P3 Apparel to D2, P1 Electronics to D1";
                }
                else
                {
                    // Default third route variant: P3->D1, P1->D3, P2->D2.
                    // Agent scenarios can replace this below with their exact
                    // controlled source/destination pair.
                    scenario.pickupPositions = new List<Vector3> { pickupP3, pickupP1, pickupP2 };
                    scenario.dropOffPositions = new List<Vector3> { dropD1Electronics, dropD3Healthcare, dropD2Apparel };
                    scenario.sourceIds = new List<string> { "P3", "P1", "P2" };
                    scenario.destinationIds = new List<string> { "D1-Electronics", "D3-Healthcare", "D2-Apparel" };
                    scenario.parcelBarcodes = new List<string> { "ATD-ELC-0001", "ATD-HLT-0002", "ATD-APL-0003" };
                    scenario.parcelCategories = new List<string> { "Electronics", "Healthcare", "Apparel" };
                    taskPlan = "P3 Electronics to D1, P1 Healthcare to D3, P2 Apparel to D2";
                }

                scenario.pickupFromRackByTask = new List<bool> { false, false, false };
                scenario.deliverToRackByTask = new List<bool> { true, true, true };
                scenario.startPosition = GetScenarioStartPosition(scenario.scenarioId);
                if (scenario.scenarioCode == "A1")
                {
                    // Begin near P1 so the incident occurs only after a real
                    // barcode scan, pick and verified grasp.
                    scenario.startPosition = pickupP1 + Vector3.back * 2.2f;

                    // A1 task 1 is a real P1 -> D2 station delivery. Do not
                    // replace D2 with the barcode category's reserved rack.
                    scenario.deliverToRackByTask = new List<bool> { false, true, true };

                    // Rack_4_3 is the dedicated Books rack. Route the loaded
                    // robot up its east-side aisle, around its north end, and
                    // then toward D2. The rack itself provides the blind spot.
                    float rackAreaWidth = warehouse.rackColumns * warehouse.rackWidth +
                        Mathf.Max(0, warehouse.rackColumns - 1) * warehouse.aisleWidth;
                    float rackStartX = -rackAreaWidth * 0.5f + warehouse.rackWidth * 0.5f;
                    // Books is explicitly assigned to Rack_4_3. Keep that
                    // address stable when the designer adds more rows or
                    // columns instead of silently moving A1's blind corner.
                    int booksRackColumn = Mathf.Clamp(4, 0, warehouse.rackColumns - 1);
                    float booksRackX = rackStartX + booksRackColumn *
                        (warehouse.rackWidth + warehouse.aisleWidth);
                    float booksAisleX = booksRackX + warehouse.rackWidth * 0.5f +
                        warehouse.aisleWidth * 0.5f;

                    float rackAreaDepth = warehouse.rackRows * warehouse.rackLength +
                        (warehouse.rackRows + 1) * warehouse.aisleWidth;
                    float firstRackRowZ = -rackAreaDepth * 0.5f + warehouse.aisleWidth +
                        warehouse.rackLength * 0.5f;
                    int booksRackRow = Mathf.Clamp(3, 0, warehouse.rackRows - 1);
                    float booksRackZ = firstRackRowZ + booksRackRow *
                        (warehouse.rackLength + warehouse.aisleWidth);
                    float booksAisleEntryZ = booksRackZ - warehouse.rackLength * 0.5f -
                        warehouse.aisleWidth * 0.5f;
                    float booksBlindTurnZ = booksRackZ + warehouse.rackLength * 0.5f +
                        warehouse.aisleWidth * 0.5f;
                    float d2ApproachX = scenario.dropOffPositions[0].x;

                    scenario.firstTaskDeliveryWaypoints = new List<Vector3>
                    {
                        new Vector3(booksAisleX, 0f, booksAisleEntryZ),
                        new Vector3(booksAisleX, 0f, booksBlindTurnZ),
                        new Vector3(d2ApproachX, 0f, booksBlindTurnZ)
                    };
                }
                else if (scenario.scenarioCode == "A2")
                {
                    // S2 -> S3 is represented by P2 -> D3. These fixed
                    // waypoints commit the baseline to the lower cross-aisle
                    // before F1 reaches and occupies its centre. Rerouting
                    // around the obstruction is an acceptable recovery.
                    scenario.pickupPositions = new List<Vector3> { pickupP2, pickupP1, pickupP3 };
                    scenario.dropOffPositions = new List<Vector3> { dropD3Healthcare, dropD1Electronics, dropD2Apparel };
                    scenario.sourceIds = new List<string> { "P2", "P1", "P3" };
                    scenario.destinationIds = new List<string> { "D3-Healthcare", "D1-Electronics", "D2-Apparel" };
                    scenario.parcelBarcodes = new List<string> { "ATD-HLT-A2-001", "ATD-ELC-A2-002", "ATD-APL-A2-003" };
                    scenario.parcelCategories = new List<string> { "Healthcare", "Electronics", "Apparel" };
                    scenario.deliverToRackByTask = new List<bool> { false, true, true };
                    scenario.startPosition = pickupP2 + Vector3.back * 2.2f;
                    scenario.firstTaskDeliveryWaypoints = new List<Vector3>
                    {
                        new Vector3(8.4f, 0f, -9f),
                        new Vector3(0f, 0f, -9f),
                        new Vector3(-8.4f, 0f, -9f)
                    };
                    taskPlan = "P2 Healthcare to D3 after lower cross-aisle route commitment, followed by P1 to D1 and P3 to D2";
                }
                else if (scenario.scenarioCode == "A3")
                {
                    scenario.pickupPositions = new List<Vector3> { pickupP1, pickupP2, pickupP3 };
                    scenario.dropOffPositions = new List<Vector3> { dropD3Healthcare, dropD2Apparel, dropD1Electronics };
                    scenario.sourceIds = new List<string> { "P1", "P2", "P3" };
                    scenario.destinationIds = new List<string> { "D3-Healthcare", "D2-Apparel", "D1-Electronics" };
                    scenario.parcelBarcodes = new List<string> { "ATD-HLT-A3-001", "ATD-APL-A3-002", "ATD-ELC-A3-003" };
                    scenario.parcelCategories = new List<string> { "Healthcare", "Apparel", "Electronics" };
                    scenario.deliverToRackByTask = new List<bool> { false, true, true };
                    scenario.startPosition = pickupP1 + Vector3.back * 2.2f;
                    taskPlan = "P1 Healthcare to blocked D3 approach, followed by P2 to D2 and P3 to D1";
                }
                else if (scenario.scenarioCode == "A4")
                {
                    // A Books barcode reserves Rack_4_3. The physical rack is
                    // moved later, while Module 1 retains this original target.
                    scenario.pickupPositions = new List<Vector3> { pickupP2, pickupP1, pickupP3 };
                    scenario.dropOffPositions = new List<Vector3> { dropD2Apparel, dropD1Electronics, dropD3Healthcare };
                    scenario.sourceIds = new List<string> { "P2", "P1", "P3" };
                    scenario.destinationIds = new List<string> { "Rack-Books", "D1-Electronics", "D3-Healthcare" };
                    scenario.parcelBarcodes = new List<string> { "ATD-BOK-A4-001", "ATD-ELC-A4-002", "ATD-HLT-A4-003" };
                    scenario.parcelCategories = new List<string> { "Books", "Electronics", "Healthcare" };
                    scenario.deliverToRackByTask = new List<bool> { true, true, true };
                    scenario.startPosition = pickupP2 + Vector3.back * 2.2f;
                    scenario.relocatedRackId = "Rack_4_3";
                    scenario.rackRelocationOffset = new Vector3(9f, 0f, -9f);
                    scenario.rackRelocationDelaySeconds = 3f;
                    taskPlan = "P2 Books parcel to relocated Rack_4_3, followed by P1 to D1 and P3 to D3";
                }
                else if (scenario.scenarioCode == "A5")
                {
                    // The robot must dock at the Books rack pickup face and
                    // take the parcel directly from the rack (not a station
                    // shelf). A deliberate approach-pose offset is injected
                    // at AlignAtSource (see ScenarioManager::EvaluateA5Docking);
                    // nothing here teleports or forces a pickup failure.
                    scenario.pickupPositions = new List<Vector3> { pickupP2, pickupP1, pickupP3 };
                    scenario.dropOffPositions = new List<Vector3> { dropD1Electronics, dropD2Apparel, dropD3Healthcare };
                    scenario.sourceIds = new List<string> { "Rack-Books", "P1", "P3" };
                    scenario.destinationIds = new List<string> { "D1-Electronics", "D2-Apparel", "D3-Healthcare" };
                    scenario.parcelBarcodes = new List<string> { "ATD-BOK-A5-001", "ATD-ELC-A5-002", "ATD-HLT-A5-003" };
                    scenario.parcelCategories = new List<string> { "Books", "Electronics", "Healthcare" };
                    scenario.pickupFromRackByTask = new List<bool> { true, false, false };
                    scenario.deliverToRackByTask = new List<bool> { false, true, true };
                    scenario.startPosition = pickupP2 + Vector3.back * 2.2f;
                    taskPlan = "Books rack-face parcel picked with an offset docking approach, delivered to D1, followed by P1 to D2 and P3 to D3";
                }
                // A benchmark episode must visit every configured source and
                // destination exactly once. Keeping the order fixed prevents
                // the global episode counter from silently changing P/D
                // pairings between scenarios.
                scenario.rotateTaskOrderEachEpisode = false;
                scenario.pickupPosition = scenario.pickupPositions[0];
                scenario.dropOffPosition = scenario.dropOffPositions[scenario.dropOffPositions.Count - 1];
                scenario.goalPosition = scenario.dropOffPosition;
                // E4/E5 are genuine multi-destination missions and need
                // extra time beyond the standard two-minute cap.
                scenario.maxEpisodeDuration = scenario.scenarioCode == "E5" ? 180f :
                    scenario.scenarioCode == "E4" ? 150f : 120f;
                scenario.description += $" Material-handling mission: {taskPlan}. Barcode determines the declared station destination or reserved category rack slot.";
                EnsureActiveWarehouseActors(scenario);
            }
        }

        // Rack columns sit at x = +/-10.5, +/-6.3 and +/-2.1. These points
        // are therefore placed in the open longitudinal/cross-aisle centres,
        // not inside rack geometry. Six positions are reused in a stable
        // sequence across the 15 experiments.
        private static Vector3 GetScenarioStartPosition(int scenarioId)
        {
            switch (Mathf.Max(0, scenarioId - 1) % 6)
            {
                case 0: return new Vector3(-8.4f, 0f, -9f);
                case 1: return new Vector3(4.2f, 0f, -9f);
                case 2: return new Vector3(0f, 0f, 9f);
                case 3: return new Vector3(8.4f, 0f, 0f);
                case 4: return new Vector3(-4.2f, 0f, 9f);
                default: return new Vector3(0f, 0f, -9f);
            }
        }

        private static Vector3 GetStationPosition(List<Vector3> positions, int index, Vector3 fallback)
        {
            return positions != null && index >= 0 && index < positions.Count ? positions[index] : fallback;
        }

        // All named workers and forklifts follow active routes in every
        // experiment. Pallet obstacles remain scenario-specific and are not
        // included in this common traffic population.
        private static void EnsureActiveWarehouseActors(ScenarioDefinition scenario)
        {
            scenario.dynamicObjects.RemoveAll(actor => actor.type == DynamicObjectType.Human || actor.type == DynamicObjectType.Forklift);
            float humanSpeed = scenario.emergencyEvacuation ? 1.8f : 0.8f;
            float forkliftSpeed = scenario.emergencyEvacuation ? 2.4f : 1.3f;
            float h1Speed = humanSpeed;
            float h2Speed = humanSpeed;
            float f1Speed = forkliftSpeed;
            float f2Speed = forkliftSpeed;
            float h1Delay = 0f;
            float f1Delay = 0f;
            bool h2StopAtTarget = false;
            bool f1StopAtTarget = false;
            bool f2StopAtTarget = false;
            string h2Job = "Cross-aisle item transfer";
            string f1Job = "Inbound rack replenishment";
            string f2Job = "Outbound pallet transport";

            // H1 collects an Apparel carton from its dedicated rack and
            // delivers it to P1; F1 brings an Electronics pallet to P2.
            // Their carried visual loads make the upstream hand-off explicit.
            Vector3 h1Start = new Vector3(10.5f, 0f, 13.5f);
            Vector3 h1Target = scenario.pickupPositions[0];
            Vector3 h2Start = new Vector3(-4.2f, 0f, -13.5f);
            Vector3 h2Target = new Vector3(-4.2f, 0f, 13.5f);
            Vector3 h3Start = scenario.pickupPositions[2];
            Vector3 h3Target = new Vector3(-10.5f, 0f, -13.5f);
            Vector3 h4Start = new Vector3(4.2f, 0f, 13.5f);
            Vector3 h4Target = new Vector3(4.2f, 0f, -4.5f);
            Vector3 f1Start = new Vector3(-10.5f, 0f, 13.5f);
            Vector3 f1Target = scenario.pickupPositions[1];
            Vector3 f2Start = new Vector3(20f, 0f, -8f);
            Vector3 f2Target = new Vector3(20f, 0f, 8f);
            Vector3 h5Start = new Vector3(8.4f, 0f, 10f);
            Vector3 h5Target = new Vector3(8.4f, 0f, -4.5f);

            // Occupancy, rack-access and deadlock experiments use the same
            // named jobs but place those jobs at their specific conflict point.
            if (scenario.scenarioCode == "A1")
            {
                // H1 must walk the exact same shared lane the robot turns
                // onto after the blind corner. Deriving H1's line from the
                // robot's own computed waypoints (rather than re-deriving it
                // separately from pickup-station math) guarantees the two
                // paths always coincide, whatever the warehouse dimensions.
                Vector3 corner = scenario.firstTaskDeliveryWaypoints.Count > 1
                    ? scenario.firstTaskDeliveryWaypoints[1]
                    : new Vector3(8.4f, 0f, 18f);
                Vector3 exitPoint = scenario.firstTaskDeliveryWaypoints.Count > 2
                    ? scenario.firstTaskDeliveryWaypoints[2]
                    : corner + Vector3.right * 6f;
                Vector3 laneDir = (exitPoint - corner).sqrMagnitude > 0.0001f
                    ? (exitPoint - corner).normalized
                    : Vector3.right;

                // H1 starts beyond where the robot exits the shared lane and
                // walks head-on toward (and past) the blind corner, so the
                // two paths physically cross on the shared line.
                h1Start = exitPoint + laneDir * 3.5f;
                h1Target = corner - laneDir * 3.5f;
                h1Speed = 0.39f;
                h1Delay = 0f;

                // Keep H5's maintenance route out of H1's rack aisle so the
                // A1 result measures the intended blind-turn interaction.
                h5Start = new Vector3(4.2f, 0f, 10f);
                h5Target = new Vector3(4.2f, 0f, -4.5f);

                // Forklifts default to generic warehouse-wide routes that are
                // NOT guaranteed to avoid the corner. This experiment must
                // isolate the pedestrian incident, so both are explicitly
                // routed along the far perimeter, well clear of the corner
                // and the shared lane H1 and the robot travel.
                f1Start = new Vector3(-20f, 0f, -8f);
                f1Target = new Vector3(-20f, 0f, 8f);
                f2Start = new Vector3(20f, 0f, -8f);
                f2Target = new Vector3(20f, 0f, 8f);
            }
            else if (scenario.scenarioCode == "A2")
            {
                // F1 performs a real inbound transfer down the central aisle.
                // At 1 m/s it reaches the lower hand-off point at about 17.5 s:
                // after the robot has picked the P2 parcel and committed to
                // this route, but before the robot reaches the centre waypoint.
                // The previous 0 -> -9 m route at 0.40 m/s arrived too late
                // (about 22.5 s), so the robot could pass before the obstruction.
                f1Start = new Vector3(0f, 0f, 8.5f);
                f1Target = new Vector3(0f, 0f, -9f);
                f1Speed = 1.00f;
                f1StopAtTarget = true;
                f1Job = "Inbound pallet transfer: lower cross-aisle hand-off";

                // People continue assigned work in the adjacent passages,
                // reducing the practical recovery choices without targeting
                // or colliding with the robot.
                h1Start = new Vector3(-8.4f, 0f, -4.5f); h1Target = new Vector3(-8.4f, 0f, 4.5f);
                h2Start = new Vector3(4.2f, 0f, -4.5f); h2Target = new Vector3(4.2f, 0f, 4.5f);
                h3Start = new Vector3(-4.2f, 0f, 4.5f); h3Target = new Vector3(-4.2f, 0f, -4.5f);
                h4Start = new Vector3(8.4f, 0f, 4.5f); h4Target = new Vector3(8.4f, 0f, 9f);
                h5Start = new Vector3(-8.4f, 0f, 9f); h5Target = new Vector3(-4.2f, 0f, 9f);
            }
            else if (scenario.scenarioCode == "A3")
            {
                Vector3 d3 = scenario.dropOffPositions[0];
                // H2 approaches from the station lane and F2 approaches from
                // the east. Both finish real jobs beside D3 and remain there,
                // occupying the placement envelope without being spawned in.
                h2Start = new Vector3(d3.x - 0.6f, 0f, -6f);
                h2Target = new Vector3(d3.x - 0.6f, 0f, d3.z);
                h2Speed = 0.75f;
                h2StopAtTarget = true;
                h2Job = "D3 receiving inspection";
                f2Start = new Vector3(d3.x, 0f, -18f);
                f2Target = new Vector3(d3.x + 0.8f, 0f, d3.z);
                f2Speed = 0.45f;
                f2StopAtTarget = true;
                f2Job = "D3 outbound pallet staging";

                h1Start = new Vector3(8.4f, 0f, -4.5f); h1Target = new Vector3(8.4f, 0f, 4.5f);
                h3Start = new Vector3(-4.2f, 0f, 0f); h3Target = new Vector3(-4.2f, 0f, 9f);
                h4Start = new Vector3(4.2f, 0f, -4.5f); h4Target = new Vector3(4.2f, 0f, 4.5f);
                h5Start = new Vector3(-8.4f, 0f, 4.5f); h5Target = new Vector3(-8.4f, 0f, 9f);
                f1Start = new Vector3(20f, 0f, -8f);
                f1Target = new Vector3(20f, 0f, 8f);
                f1Job = "Inbound pallet transport in east perimeter lane";
            }
            else if (scenario.scenarioCode == "A5")
            {
                // A worker stages cartons beside the pickup face and a
                // forklift continues an adjacent-aisle job, both narrowing
                // the manoeuvring envelope without approaching the robot.
                Vector3 pf = scenario.pickupPositions[0];
                h2Start = new Vector3(pf.x - 0.8f, 0f, pf.z - 5f);
                h2Target = new Vector3(pf.x - 0.8f, 0f, pf.z + 1f);
                h2Speed = 0.55f;
                h2StopAtTarget = true;
                h2Job = "Rack-face carton staging beside pickup approach";
                f1Start = new Vector3(pf.x + 3.5f, 0f, pf.z - 10f);
                f1Target = new Vector3(pf.x + 1.2f, 0f, pf.z - 1f);
                f1Speed = 0.50f;
                f1StopAtTarget = true;
                f1Job = "Adjacent-aisle replenishment: reduces pickup manoeuvring space";

                h1Start = new Vector3(8.4f, 0f, -4.5f); h1Target = new Vector3(8.4f, 0f, 4.5f);
                h3Start = new Vector3(-4.2f, 0f, 0f); h3Target = new Vector3(-4.2f, 0f, 9f);
                h4Start = new Vector3(4.2f, 0f, -4.5f); h4Target = new Vector3(4.2f, 0f, 4.5f);
                h5Start = new Vector3(-8.4f, 0f, 4.5f); h5Target = new Vector3(-8.4f, 0f, 9f);
                f2Start = new Vector3(20f, 0f, -8f); f2Target = new Vector3(20f, 0f, 8f);
            }
            else if (scenario.scenarioCode == "E5")
            {
                h3Start = scenario.pickupPositions[0]; // S1
                h3Target = scenario.pickupPositions[1];
                h4Start = scenario.pickupPositions[1]; // S2 trolley
                h4Target = scenario.pickupPositions[2];
                f2Start = scenario.pickupPositions[2]; // S3 outbound load
                f2Target = new Vector3(18f, 0f, 4f);
            }
            else if (scenario.scenarioCode == "E2")
            {
                h1Start = new Vector3(-10.5f, 0f, -4.5f); h1Target = new Vector3(10.5f, 0f, -4.5f);
                h2Start = new Vector3(-10.5f, 0f, 0f); h2Target = new Vector3(10.5f, 0f, 0f);
                h3Start = new Vector3(-10.5f, 0f, 4.5f); h3Target = new Vector3(10.5f, 0f, 4.5f);
                h4Start = new Vector3(0f, 0f, -13.5f); h4Target = new Vector3(0f, 0f, 13.5f);
                h5Start = new Vector3(4.2f, 0f, -13.5f); h5Target = new Vector3(4.2f, 0f, 13.5f);
            }
            else if (scenario.scenarioCode == "E3")
            {
                f1Start = new Vector3(-14f, 0f, 0f);
                f1Target = new Vector3(14f, 0f, 0f);
                f2Start = new Vector3(0f, 0f, -12f);
                f2Target = new Vector3(0f, 0f, 12f);
            }
            else if (scenario.scenarioCode == "T5")
            {
                f1Start = new Vector3(-10.5f, 0f, 4.5f);
                f1Target = new Vector3(2.1f, 0f, 4.5f);
                f2Start = new Vector3(4.2f, 0f, 0f);
                f2Target = new Vector3(4.2f, 0f, 0.2f); // deliberate stop position
                h1Start = new Vector3(-6.3f, 0f, -4.5f); h1Target = new Vector3(6.3f, 0f, -4.5f);
                h2Start = new Vector3(-6.3f, 0f, 4.5f); h2Target = new Vector3(6.3f, 0f, 4.5f);
            }
            else if (scenario.emergencyEvacuation)
            {
                h1Target = new Vector3(-27f, 0f, -15f);
                h2Target = new Vector3(-27f, 0f, 15f);
                h3Target = new Vector3(-27f, 0f, 15f);
                h4Target = new Vector3(27f, 0f, 15f);
                h5Target = new Vector3(27f, 0f, -15f);
                f1Target = new Vector3(-20f, 0f, 16f);
                f2Target = new Vector3(20f, 0f, 16f);
            }

            string h1Job = scenario.scenarioCode == "A1"
                ? "Carton transfer across Books rack north service lane"
                : "Rack picker: R1 staging";
            AddActorRoute(scenario, "H1", h1Job, DynamicObjectType.Human, h1Start, h1Target, h1Speed, h1Delay,
                robotReactionTime: scenario.scenarioCode == "A1" ? 0.75f : 0f);
            AddActorRoute(scenario, "H2", h2Job, DynamicObjectType.Human, h2Start, h2Target, h2Speed,
                stopAtTarget: h2StopAtTarget);
            AddActorRoute(scenario, "H3", "PD station operator", DynamicObjectType.Human, h3Start, h3Target, humanSpeed);
            AddActorRoute(scenario, "H4", "Trolley operator: PD2 transfer", DynamicObjectType.Human, h4Start, h4Target, humanSpeed);
            AddActorRoute(scenario, "H5", "Maintenance and replenishment", DynamicObjectType.Human, h5Start, h5Target, humanSpeed);
            AddActorRoute(scenario, "F1", f1Job, DynamicObjectType.Forklift, f1Start, f1Target, f1Speed, f1Delay,
                stopAtTarget: f1StopAtTarget);
            AddActorRoute(scenario, "F2", f2Job, DynamicObjectType.Forklift, f2Start, f2Target, f2Speed,
                stopAtTarget: f2StopAtTarget);
        }

        private static void AddActorRoute(ScenarioDefinition scenario, string actorId, string jobName, DynamicObjectType type,
            Vector3 start, Vector3 target, float speed, float activationDelay = 0f, float robotReactionTime = 0f,
            bool stopAtTarget = false)
        {
            scenario.dynamicObjects.Add(new DynamicObjectSpawn
            {
                actorId = actorId,
                jobName = jobName,
                type = type,
                startPosition = start,
                targetPosition = target,
                speed = speed,
                activationDelay = activationDelay,
                robotReactionTime = robotReactionTime,
                stopAtTarget = stopAtTarget,
                pattern = MovementPattern.LinearPatrol
            });
        }

        private static void AddScenarioBarrier(ScenarioDefinition scenario, string barrierId, Vector3 position, Vector3 scale)
        {
            scenario.dynamicObjects.Add(new DynamicObjectSpawn
            {
                actorId = barrierId,
                jobName = "Single-lane safety barrier",
                type = DynamicObjectType.MovableObstacle,
                startPosition = position,
                targetPosition = position,
                scale = scale,
                speed = 0f,
                pattern = MovementPattern.Static
            });
        }
    }
}
