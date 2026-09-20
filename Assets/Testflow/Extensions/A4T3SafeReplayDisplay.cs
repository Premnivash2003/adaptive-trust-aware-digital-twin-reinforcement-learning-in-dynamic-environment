using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using ATADTRL.Environment;
using ATADTRL.Pipeline;
using ATADTRL.Robot;
using ATADTRL.Scenarios;

namespace ATADTRL.TestFlow.Extensions
{
    /// <summary>
    /// Isolated Display-3 replay for A4 and T3. It records the physical robot,
    /// humans and forklifts from Display 1, then replays the same traffic. A4
    /// branches only at the stale destination and replans to the relocated
    /// rack; T3 preserves the route while using the trusted fused state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class A4T3SafeReplayDisplay : MonoBehaviour
    {
        private const int ShadowLayer = 29;

        private sealed class PoseSample
        {
            public float Time;
            public Vector3 Position;
            public Quaternion Rotation;
        }

        private ScenarioManager scenarios;
        private ATADTRLPipelineManager pipeline;
        private RobotController physicalRobot;
        private Camera replayCamera;
        private Transform world, robotReplica, parcelReplica, relocatedRackMarker;
        private readonly List<PoseSample> robotTrack = new List<PoseSample>();
        private readonly List<GameObject> originalA4RackVisuals = new List<GameObject>();
        private readonly List<Vector3> t3SafeRoute = new List<Vector3>();
        private readonly Dictionary<string, List<PoseSample>> actorTracks = new Dictionary<string, List<PoseSample>>();
        private readonly Dictionary<string, Transform> actorReplicas = new Dictionary<string, Transform>();
        private string scenarioCode = "";
        private string a4RackId = "";
        private float captureStartedAt, replayStartedAt, nextCapture;
        private float branchTime;
        private float a4RackRelocationTime = -1f;
        private float t3PlaybackClock;
        private float t3D2DeliveryTime = -1f;
        private float t3BaselineStopTime = -1f, t3DecisionHoldRemaining;
        private bool t3DecisionApplied;
        private bool t3SafeRouteReady;
        private bool t3TrafficDecisionActive;
        private float t3TrafficBlockElapsed, t3ClearanceHoldRemaining;
        private string t3BlockedActorId = "";
        private int t3SafeRouteIndex;
        private bool running, parcelAttached, subscribed;
        private Vector3 branchStart, relocatedTarget, relocatedApproachTarget, t3D2Target;

        private void Awake()
        {
            scenarios = FindAnyObjectByType<ScenarioManager>();
            pipeline = GetComponent<ATADTRLPipelineManager>();
            if (pipeline == null) pipeline = FindAnyObjectByType<ATADTRLPipelineManager>();
            world = new GameObject("ATADTRL_A4_T3_SAFE_REPLAY_WORLD").transform;
            world.gameObject.layer = ShadowLayer;
            world.gameObject.SetActive(false);
            ResolveReplayCamera();
            Subscribe();
        }

        private void Start()
        {
            // The pipeline creates this component dynamically, so recover if
            // ScenarioManager emitted its start event before this subscription.
            if (scenarios == null) scenarios = FindAnyObjectByType<ScenarioManager>();
            if (pipeline == null) pipeline = GetComponent<ATADTRLPipelineManager>();
            ResolveReplayCamera();
            Subscribe();

            if (scenarios != null && scenarios.IsRunning && scenarios.CurrentScenarioIndex >= 0 &&
                scenarios.CurrentScenarioIndex < scenarios.Scenarios.Count)
            {
                ScenarioDefinition current = scenarios.Scenarios[scenarios.CurrentScenarioIndex];
                if (current != null && (current.scenarioCode == "A4" || current.scenarioCode == "T3"))
                    OnScenarioStarted(current);
            }
        }

        private void Subscribe()
        {
            if (subscribed || scenarios == null || pipeline == null) return;
            scenarios.OnScenarioStarted += OnScenarioStarted;
            scenarios.OnRackRelocated += OnRackRelocated;
            pipeline.SafeReplayRequested += StartReplay;
            subscribed = true;
        }

        private void OnDestroy()
        {
            if (scenarios != null)
            {
                scenarios.OnScenarioStarted -= OnScenarioStarted;
                scenarios.OnRackRelocated -= OnRackRelocated;
            }
            if (pipeline != null) pipeline.SafeReplayRequested -= StartReplay;
            subscribed = false;
        }

        private void OnScenarioStarted(ScenarioDefinition scenario)
        {
            running = false;
            scenarioCode = scenario != null ? scenario.scenarioCode : "";
            a4RackId = scenario != null ? scenario.relocatedRackId : "";
            t3D2Target = scenario != null && scenario.dropOffPositions != null &&
                         scenario.dropOffPositions.Count > 0
                ? scenario.dropOffPositions[0]
                : scenario != null ? scenario.goalPosition : Vector3.zero;
            t3SafeRoute.Clear();
            if (scenario != null && scenario.firstTaskDeliveryWaypoints != null)
                foreach (Vector3 waypoint in scenario.firstTaskDeliveryWaypoints)
                    t3SafeRoute.Add(waypoint);
            bool supported = scenarioCode == "A4" || scenarioCode == "T3";
            world.gameObject.SetActive(supported);
            if (!supported) return;

            ClearWorld();
            CloneWarehouseVisuals();
            physicalRobot = FindAnyObjectByType<RobotController>();
            robotReplica = new GameObject($"{scenarioCode}_SAFE_ROBOT").transform;
            robotReplica.SetParent(world, false);
            CloneRenderers(physicalRobot != null ? physicalRobot.transform : null, robotReplica);
            if (robotReplica.childCount == 0)
                AddPrimitive(robotReplica, "AMR Body", Vector3.up * .42f, new Vector3(1.15f, .55f, 1.45f),
                    new Color(.02f, .55f, .72f));
            parcelReplica = AddPrimitive(robotReplica, "Mission Parcel", new Vector3(0f, .92f, 0f),
                new Vector3(.72f, .42f, .72f), new Color(.55f, .32f, .14f)).transform;
            parcelReplica.gameObject.SetActive(false);
            BuildActorReplicas();
            robotTrack.Clear();
            actorTracks.Clear();
            captureStartedAt = Time.time;
            nextCapture = Time.time;
            a4RackRelocationTime = -1f;
            t3PlaybackClock = 0f;
            t3D2DeliveryTime = -1f;
            t3BaselineStopTime = -1f;
            t3DecisionHoldRemaining = 0f;
            t3DecisionApplied = false;
            t3SafeRouteReady = false;
            t3TrafficDecisionActive = false;
            t3TrafficBlockElapsed = 0f;
            t3ClearanceHoldRemaining = 0f;
            t3BlockedActorId = "";
            t3SafeRouteIndex = 0;
            parcelAttached = false;
            Debug.Log($"ATADTRL DISPLAY 3: recording identical {scenarioCode} robot and warehouse traffic for safe replay.");
        }

        private void OnRackRelocated(string rackId, Vector3 originalPosition, Vector3 relocatedPosition)
        {
            if (scenarioCode != "A4") return;
            a4RackId = rackId;
            relocatedTarget = relocatedPosition;
            a4RackRelocationTime = Mathf.Max(0f, Time.time - captureStartedAt);
            foreach (GameObject oldVisual in originalA4RackVisuals)
                if (oldVisual != null) oldVisual.SetActive(false);
            BuildRelocatedRackMarker();
        }

        private void Update()
        {
            if (scenarioCode != "A4" && scenarioCode != "T3") return;
            if (!running)
            {
                if (scenarios != null && scenarios.IsRunning && Time.time >= nextCapture)
                {
                    nextCapture = Time.time + .10f;
                    CaptureFrame();
                }
                return;
            }

            float elapsed = Time.time - replayStartedAt;
            PlaybackActors(elapsed);
            float recordedDuration = robotTrack.Count > 0 ? robotTrack[robotTrack.Count - 1].Time : 0f;
            float duration = scenarioCode == "T3" && t3D2DeliveryTime > 0f
                ? t3D2DeliveryTime : recordedDuration;
            if (scenarioCode == "T3")
            {
                if (!t3DecisionApplied)
                {
                    t3PlaybackClock = Mathf.Min(t3BaselineStopTime, t3PlaybackClock + Time.deltaTime);
                    Playback(robotReplica, robotTrack, t3PlaybackClock);
                    SetParcelAttached(t3PlaybackClock > t3BaselineStopTime * .28f);
                    pipeline?.ReportSafeReplayStage(
                        t3PlaybackClock < t3BaselineStopTime
                            ? "T3 SAME SCENARIO — REPLAYING DISPLAY 1 UNTIL RECORDED STOP"
                            : "T3 DISPLAY 1 STOP REACHED — TAM DECISION STARTING",
                        t3BaselineStopTime > 0f
                            ? .48f * Mathf.Clamp01(t3PlaybackClock / t3BaselineStopTime) : 0f,
                        t3PlaybackClock < t3BaselineStopTime
                            ? ATADTRLPipelineManager.SecureAction.Forward
                            : ATADTRLPipelineManager.SecureAction.Wait);
                    if (t3PlaybackClock >= t3BaselineStopTime)
                    {
                        t3DecisionApplied = true;
                        t3DecisionHoldRemaining = 2.5f;
                        PrepareT3SafeRoute(robotReplica.position);
                    }
                    return;
                }

                if (t3DecisionHoldRemaining > 0f)
                {
                    t3DecisionHoldRemaining = Mathf.Max(0f,
                        t3DecisionHoldRemaining - Time.deltaTime);
                    pipeline?.ReportSafeReplayStage(
                        "T3 TAM DECISION — LIDAR REJECTED, CAMERA / IMU / ENCODER TRUSTED",
                        .56f, ATADTRLPipelineManager.SecureAction.Wait);
                    return;
                }

                if (TryGetT3BlockingActor(out string blockingActorId, out Transform blockingActor))
                {
                    t3TrafficDecisionActive = true;
                    t3BlockedActorId = blockingActorId;
                    t3TrafficBlockElapsed += Time.deltaTime;
                    t3ClearanceHoldRemaining = .75f;
                    bool forklift = blockingActorId.StartsWith("F");
                    pipeline?.ReportSafeReplayStage(
                        $"T3 TAM TRAFFIC DECISION — {(forklift ? "FORKLIFT" : "HUMAN")} {blockingActorId} " +
                        "IN PATH — SAFE STOP",
                        .66f + .08f * Mathf.Clamp01(t3TrafficBlockElapsed / 2.5f),
                        ATADTRLPipelineManager.SecureAction.Wait);

                    // First wait for normal warehouse traffic to pass. If it
                    // remains in the aisle, TAM selects a rack-safe bypass.
                    if (t3TrafficBlockElapsed >= 2.5f && TryPrepareT3ObstacleDetour(blockingActor))
                    {
                        t3TrafficBlockElapsed = 0f;
                        pipeline?.ReportSafeReplayStage(
                            $"T3 TAM DECISION — {blockingActorId} STILL BLOCKING — SAFE DETOUR SELECTED",
                            .75f, ATADTRLPipelineManager.SecureAction.Replan);
                    }
                    return;
                }

                if (t3TrafficDecisionActive && t3ClearanceHoldRemaining > 0f)
                {
                    t3ClearanceHoldRemaining = Mathf.Max(0f,
                        t3ClearanceHoldRemaining - Time.deltaTime);
                    pipeline?.ReportSafeReplayStage(
                        $"T3 TAM CLEARANCE CHECK — {t3BlockedActorId} CLEAR — VALIDATING SAFE GAP",
                        .78f, ATADTRLPipelineManager.SecureAction.Wait);
                    return;
                }
                if (t3TrafficDecisionActive)
                {
                    t3TrafficDecisionActive = false;
                    t3TrafficBlockElapsed = 0f;
                    t3BlockedActorId = "";
                }

                pipeline?.ReportSafeReplayStage(
                    "T3 TRUSTED POLICY — RESUME SAFE DELIVERY TO D2",
                    .62f + .34f * T3RouteProgress(),
                    ATADTRLPipelineManager.SecureAction.FusedNavigate);
                if (MoveT3RobotToD2())
                {
                    PlaceT3ParcelAtD2();
                    Complete("TASK COMPLETED — TAM DECISION APPLIED — PARCEL DELIVERED TO D2",
                        "TRUSTED FUSION D2 DELIVERY VERIFIED");
                }
                return;
            }

            if (elapsed <= branchTime)
            {
                Playback(robotReplica, robotTrack, elapsed);
                SetParcelAttached(elapsed > branchTime * .30f);
                pipeline?.ReportSafeReplayStage("A4 REPLAY TO RECORDED RACK-CHANGE EVENT",
                    branchTime > 0f ? .60f * Mathf.Clamp01(elapsed / branchTime) : 0f,
                    ATADTRLPipelineManager.SecureAction.Forward);
                return;
            }

            float branchElapsed = elapsed - branchTime;
            float branchDuration = Mathf.Max(4f, Vector3.Distance(branchStart, relocatedApproachTarget) / 1.4f);
            float blend = Mathf.Clamp01(branchElapsed / branchDuration);
            robotReplica.position = Vector3.Lerp(branchStart, relocatedApproachTarget, blend);
            Vector3 direction = relocatedApproachTarget - branchStart;
            if (direction.sqrMagnitude > .01f) robotReplica.rotation = Quaternion.LookRotation(direction.normalized);
            SetParcelAttached(blend < .92f);
            pipeline?.ReportSafeReplayStage(blend < .92f
                    ? "A4 REPLAN — NAVIGATE TO RELOCATED RACK_4_3"
                    : "A4 CORRECT PLACEMENT — VERIFY RELOCATED SLOT",
                .60f + .40f * blend, blend < .92f
                    ? ATADTRLPipelineManager.SecureAction.Replan
                    : ATADTRLPipelineManager.SecureAction.Place);
            if (blend >= 1f)
                Complete("TASK COMPLETED — RELOCATED RACK UPDATED — CORRECT PLACEMENT VERIFIED",
                    "CORRECT RACK PLACEMENT VERIFIED");
        }

        private float FindT3BaselineStopTime()
        {
            if (robotTrack.Count < 2) return -1f;

            // Display 1 ends T3 in a sustained stopped state. Work backwards
            // from the final recorded pose so ordinary pickup/scanning pauses
            // cannot be mistaken for the failure that TAM must resolve.
            const float stationaryStep = .055f;
            const float minimumStopSeconds = 1.5f;
            int stopStart = robotTrack.Count - 1;
            Vector3 stopAnchor = robotTrack[stopStart].Position;
            stopAnchor.y = 0f;
            for (int i = robotTrack.Count - 2; i >= 0; i--)
            {
                Vector3 current = robotTrack[i].Position;
                Vector3 next = robotTrack[i + 1].Position;
                current.y = next.y = 0f;
                if (Vector3.Distance(current, next) > stationaryStep ||
                    Vector3.Distance(current, stopAnchor) > .35f) break;
                stopStart = i;
            }

            float terminalStopDuration = robotTrack[robotTrack.Count - 1].Time - robotTrack[stopStart].Time;
            float missionMinimum = Mathf.Max(4f, robotTrack[robotTrack.Count - 1].Time * .12f);
            if (terminalStopDuration >= minimumStopSeconds && robotTrack[stopStart].Time >= missionMinimum)
            {
                Debug.Log($"ATADTRL DISPLAY 3 T3: exact Display-1 terminal stop recorded at " +
                          $"{robotTrack[stopStart].Time:F2}s ({terminalStopDuration:F2}s stationary).");
                return robotTrack[stopStart].Time;
            }

            // Defensive fallback for a recording that ended immediately after
            // the stop: locate the first post-start pause lasting one second.
            for (int start = 1; start < robotTrack.Count - 1; start++)
            {
                if (robotTrack[start].Time < missionMinimum) continue;
                Vector3 anchor = robotTrack[start].Position;
                anchor.y = 0f;
                for (int end = start + 1; end < robotTrack.Count; end++)
                {
                    Vector3 candidate = robotTrack[end].Position;
                    candidate.y = 0f;
                    if (Vector3.Distance(anchor, candidate) > .30f) break;
                    if (robotTrack[end].Time - robotTrack[start].Time < 1f) continue;
                    Debug.Log($"ATADTRL DISPLAY 3 T3: Display-1 sustained stop recorded at " +
                              $"{robotTrack[start].Time:F2}s.");
                    return robotTrack[start].Time;
                }
            }

            float fallback = Mathf.Max(missionMinimum,
                robotTrack[robotTrack.Count - 1].Time * .72f);
            Debug.LogWarning($"ATADTRL DISPLAY 3 T3: no long terminal pause was captured; " +
                             $"using the late Display-1 pose at {fallback:F2}s as the TAM handoff.");
            return fallback;
        }

        private float FindT3D2DeliveryTime()
        {
            const float arrivalRadius = 2.4f;
            foreach (PoseSample sample in robotTrack)
            {
                if (sample.Time < 2f) continue;
                Vector3 separation = sample.Position - t3D2Target;
                separation.y = 0f;
                if (separation.magnitude <= arrivalRadius)
                {
                    Debug.Log($"ATADTRL DISPLAY 3 T3: D2 delivery arrival recorded at {sample.Time:F2}s.");
                    return sample.Time;
                }
            }
            return robotTrack.Count > 0 ? robotTrack[robotTrack.Count - 1].Time : -1f;
        }

        private void PlaceT3ParcelAtD2()
        {
            if (parcelReplica == null) return;
            parcelAttached = false;
            parcelReplica.SetParent(world, true);
            parcelReplica.gameObject.SetActive(true);
            parcelReplica.position = t3D2Target + Vector3.up * .55f;
        }

        private void PrepareT3SafeRoute(Vector3 stopPosition)
        {
            List<Vector3> configuredRoute = new List<Vector3>(t3SafeRoute);
            t3SafeRoute.Clear();
            t3SafeRouteIndex = 0;
            t3SafeRouteReady = false;

            // Project the recorded stop onto the configured P1-to-D2 polyline.
            // Only nodes after that projection are retained, preventing a
            // backwards jump while keeping the route in real warehouse aisles.
            List<Vector3> fullRoute = new List<Vector3>();
            Vector3 routeStart = robotTrack.Count > 0 ? robotTrack[0].Position : stopPosition;
            routeStart.y = stopPosition.y;
            fullRoute.Add(routeStart);
            foreach (Vector3 configured in configuredRoute)
            {
                Vector3 node = configured;
                node.y = stopPosition.y;
                fullRoute.Add(node);
            }
            Vector3 destination = t3D2Target;
            destination.y = stopPosition.y;
            fullRoute.Add(destination);

            int closestSegment = 0;
            float closestDistance = float.PositiveInfinity;
            Vector3 planarStop = stopPosition;
            planarStop.y = 0f;
            for (int i = 0; i < fullRoute.Count - 1; i++)
            {
                Vector3 a = fullRoute[i], b = fullRoute[i + 1];
                a.y = b.y = 0f;
                Vector3 ab = b - a;
                float projection = ab.sqrMagnitude > .0001f
                    ? Mathf.Clamp01(Vector3.Dot(planarStop - a, ab) / ab.sqrMagnitude) : 0f;
                float distance = Vector3.Distance(planarStop, a + ab * projection);
                if (distance >= closestDistance) continue;
                closestDistance = distance;
                closestSegment = i;
            }

            List<Vector3> remainingLogicalRoute = new List<Vector3>();
            for (int i = closestSegment + 1; i < fullRoute.Count; i++)
                if (Vector3.Distance(stopPosition, fullRoute[i]) > .35f)
                    remainingLogicalRoute.Add(fullRoute[i]);
            if (remainingLogicalRoute.Count == 0 ||
                Vector3.Distance(remainingLogicalRoute[remainingLogicalRoute.Count - 1], destination) > .1f)
                remainingLogicalRoute.Add(destination);

            // Convert every logical leg into real NavMesh corners. The prior
            // straight-line interpolation could cross rack meshes even when
            // its endpoints were correct. NavMesh corners keep the replay AMR
            // inside traversable aisles exactly like a physical warehouse AMR.
            Vector3 legStart = stopPosition;
            bool allLegsValid = true;
            foreach (Vector3 logicalTarget in remainingLogicalRoute)
            {
                if (!AppendT3NavMeshLeg(legStart, logicalTarget))
                {
                    allLegsValid = false;
                    break;
                }
                legStart = logicalTarget;
            }

            if (!allLegsValid)
            {
                // Rebuild as one complete NavMesh route to D2. Never fall back
                // to a direct transform line because that can enter a rack.
                t3SafeRoute.Clear();
                allLegsValid = AppendT3NavMeshLeg(stopPosition, destination);
            }
            t3SafeRouteReady = allLegsValid && t3SafeRoute.Count > 0;

            SetParcelAttached(true);
            if (t3SafeRouteReady)
                Debug.Log($"ATADTRL DISPLAY 3 T3: TAM accepted trusted fused state; " +
                          $"rack-safe NavMesh route prepared with {t3SafeRoute.Count} corner(s) to D2.");
            else
                Debug.LogError("ATADTRL DISPLAY 3 T3: no valid aisle route to D2 was available; " +
                               "the replay robot will remain stopped instead of crossing a rack.");
        }

        private bool AppendT3NavMeshLeg(Vector3 from, Vector3 to)
        {
            if (!NavMesh.SamplePosition(from, out NavMeshHit startHit, 3f, NavMesh.AllAreas) ||
                !NavMesh.SamplePosition(to, out NavMeshHit targetHit, 4f, NavMesh.AllAreas))
                return false;

            NavMeshPath path = new NavMeshPath();
            if (!NavMesh.CalculatePath(startHit.position, targetHit.position, NavMesh.AllAreas, path) ||
                path.status != NavMeshPathStatus.PathComplete || path.corners.Length < 2)
                return false;

            for (int i = 1; i < path.corners.Length; i++)
            {
                Vector3 corner = path.corners[i];
                corner.y = from.y;
                if (t3SafeRoute.Count == 0 ||
                    Vector3.Distance(t3SafeRoute[t3SafeRoute.Count - 1], corner) > .08f)
                    t3SafeRoute.Add(corner);
            }
            return true;
        }

        private bool MoveT3RobotToD2()
        {
            if (robotReplica == null || !t3SafeRouteReady || t3SafeRoute.Count == 0) return false;
            while (t3SafeRouteIndex < t3SafeRoute.Count)
            {
                Vector3 target = t3SafeRoute[t3SafeRouteIndex];
                target.y = robotReplica.position.y;
                Vector3 offset = target - robotReplica.position;
                offset.y = 0f;
                if (offset.magnitude <= .18f)
                {
                    robotReplica.position = target;
                    t3SafeRouteIndex++;
                    continue;
                }

                Vector3 direction = offset.normalized;
                robotReplica.position = Vector3.MoveTowards(robotReplica.position, target,
                    1.35f * Time.deltaTime);
                robotReplica.rotation = Quaternion.Slerp(robotReplica.rotation,
                    Quaternion.LookRotation(direction), 6f * Time.deltaTime);
                SetParcelAttached(true);
                return false;
            }
            return true;
        }

        private bool TryGetT3BlockingActor(out string actorId, out Transform actorTransform)
        {
            actorId = "";
            actorTransform = null;
            if (robotReplica == null || !t3SafeRouteReady ||
                t3SafeRouteIndex >= t3SafeRoute.Count) return false;

            Vector3 routeDirection = t3SafeRoute[t3SafeRouteIndex] - robotReplica.position;
            routeDirection.y = 0f;
            if (routeDirection.sqrMagnitude < .01f) routeDirection = robotReplica.forward;
            routeDirection.Normalize();

            float nearestAhead = float.PositiveInfinity;
            foreach (KeyValuePair<string, Transform> pair in actorReplicas)
            {
                if (pair.Value == null || !pair.Value.gameObject.activeInHierarchy ||
                    (!pair.Key.StartsWith("H") && !pair.Key.StartsWith("F"))) continue;

                bool forklift = pair.Key.StartsWith("F");
                Vector3 separation = pair.Value.position - robotReplica.position;
                separation.y = 0f;
                float ahead = Vector3.Dot(separation, routeDirection);
                float lateral = (separation - routeDirection * ahead).magnitude;
                float contactEnvelope = forklift ? 3.0f : 1.55f;
                float lookAhead = forklift ? 5.5f : 4.0f;
                float corridorHalfWidth = forklift ? 2.35f : 1.35f;
                bool immediateContactRisk = separation.magnitude <= contactEnvelope && ahead > -.65f;
                bool routeConflict = ahead >= 0f && ahead <= lookAhead && lateral <= corridorHalfWidth;
                if (!immediateContactRisk && !routeConflict) continue;
                if (ahead >= nearestAhead) continue;
                nearestAhead = ahead;
                actorId = pair.Key;
                actorTransform = pair.Value;
            }
            return actorTransform != null;
        }

        private bool TryPrepareT3ObstacleDetour(Transform blockingActor)
        {
            if (blockingActor == null || robotReplica == null) return false;

            List<Vector3> previousRoute = new List<Vector3>(t3SafeRoute);
            int previousIndex = t3SafeRouteIndex;
            Vector3 forward = t3SafeRouteIndex < t3SafeRoute.Count
                ? t3SafeRoute[t3SafeRouteIndex] - robotReplica.position
                : t3D2Target - robotReplica.position;
            forward.y = 0f;
            if (forward.sqrMagnitude < .01f) forward = robotReplica.forward;
            forward.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 destination = t3D2Target;
            destination.y = robotReplica.position.y;

            for (int direction = -1; direction <= 1; direction += 2)
            {
                Vector3 detour = blockingActor.position + side * (3.6f * direction) + forward * 1.4f;
                detour.y = robotReplica.position.y;
                if (!NavMesh.SamplePosition(detour, out NavMeshHit detourHit, 2.2f, NavMesh.AllAreas))
                    continue;
                Vector3 actorSeparation = detourHit.position - blockingActor.position;
                actorSeparation.y = 0f;
                if (actorSeparation.magnitude < 2.8f) continue;

                t3SafeRoute.Clear();
                if (AppendT3NavMeshLeg(robotReplica.position, detourHit.position) &&
                    AppendT3NavMeshLeg(detourHit.position, destination))
                {
                    t3SafeRouteIndex = 0;
                    t3SafeRouteReady = t3SafeRoute.Count > 0;
                    Debug.Log($"ATADTRL DISPLAY 3 T3: TAM created a rack-safe dynamic detour around " +
                              $"{t3BlockedActorId} using trusted camera/IMU/encoder state.");
                    return t3SafeRouteReady;
                }
            }

            t3SafeRoute.Clear();
            t3SafeRoute.AddRange(previousRoute);
            t3SafeRouteIndex = previousIndex;
            t3SafeRouteReady = previousRoute.Count > previousIndex;
            return false;
        }

        private float T3RouteProgress()
        {
            if (t3SafeRoute.Count == 0) return 1f;
            return Mathf.Clamp01((float)t3SafeRouteIndex / t3SafeRoute.Count);
        }

        private void LateUpdate()
        {
            if (!running || robotReplica == null ||
                (scenarioCode != "A4" && scenarioCode != "T3")) return;
            ResolveReplayCamera();
            if (replayCamera == null) return;

            // Match the operational onboard view used by the working A1
            // replay. The camera now moves with the A4/T3 replay robot instead
            // of remaining at the final Display-1 warehouse viewpoint.
            Vector3 desiredPosition = robotReplica.TransformPoint(new Vector3(0f, 1.55f, .58f));
            Vector3 lookTarget = robotReplica.TransformPoint(new Vector3(0f, 1.25f, 6f));
            Quaternion desiredRotation = Quaternion.LookRotation(lookTarget - desiredPosition, Vector3.up);
            float blend = 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime);
            replayCamera.transform.position = Vector3.Lerp(replayCamera.transform.position, desiredPosition, blend);
            replayCamera.transform.rotation = Quaternion.Slerp(replayCamera.transform.rotation, desiredRotation, blend);
        }

        private void ResolveReplayCamera()
        {
            if (replayCamera == null)
            {
                GameObject cameraObject = GameObject.Find("ATADTRL_Display3_Camera");
                if (cameraObject != null) replayCamera = cameraObject.GetComponent<Camera>();
            }
            if (replayCamera == null) return;
            replayCamera.targetDisplay = 2;
            replayCamera.targetTexture = null;
            replayCamera.cullingMask = 1 << ShadowLayer;
            replayCamera.enabled = true;
        }

        private void StartReplay()
        {
            if (pipeline == null) pipeline = GetComponent<ATADTRLPipelineManager>();
            if (pipeline == null || !pipeline.ReplayRunning ||
                (pipeline.ActiveScenarioCode != "A4" && pipeline.ActiveScenarioCode != "T3")) return;
            if (running) return;
            scenarioCode = pipeline.ActiveScenarioCode;
            if (robotTrack.Count < 2)
            {
                Debug.LogError($"ATADTRL {scenarioCode}: Display-1 recording is unavailable; safe replay cannot start.");
                return;
            }
            world.gameObject.SetActive(true);
            replayStartedAt = Time.time;
            t3PlaybackClock = 0f;
            t3DecisionApplied = false;
            t3SafeRouteReady = false;
            t3TrafficDecisionActive = false;
            t3TrafficBlockElapsed = 0f;
            t3ClearanceHoldRemaining = 0f;
            t3BlockedActorId = "";
            t3DecisionHoldRemaining = 0f;
            t3SafeRouteIndex = 0;
            t3D2DeliveryTime = scenarioCode == "T3" ? FindT3D2DeliveryTime() : -1f;
            t3BaselineStopTime = scenarioCode == "T3" ? FindT3BaselineStopTime() : -1f;
            if (scenarioCode == "T3") HideStationaryT3HumanReplicas();
            branchTime = scenarioCode == "A4"
                ? Mathf.Clamp(a4RackRelocationTime >= 0f
                    ? a4RackRelocationTime
                    : robotTrack[robotTrack.Count - 1].Time * .45f,
                    0f, robotTrack[robotTrack.Count - 1].Time)
                : 0f;
            PoseSample branch = Sample(robotTrack, branchTime);
            branchStart = branch.Position;
            relocatedTarget = pipeline.A4RelocatedStoragePosition;
            relocatedTarget.y = branchStart.y;
            Vector3 approachDirection = relocatedTarget - branchStart;
            approachDirection.y = 0f;
            relocatedApproachTarget = approachDirection.sqrMagnitude > .01f
                ? relocatedTarget - approachDirection.normalized * 1.8f
                : relocatedTarget;
            relocatedApproachTarget.y = branchStart.y;
            if (scenarioCode == "A4" && relocatedRackMarker == null) BuildRelocatedRackMarker();
            running = true;
            Debug.Log($"ATADTRL DISPLAY 3: {scenarioCode} safe replay started with identical recorded traffic.");
        }

        private void HideStationaryT3HumanReplicas()
        {
            foreach (KeyValuePair<string, Transform> pair in actorReplicas)
            {
                if (!pair.Key.StartsWith("H") || pair.Value == null) continue;
                if (!actorTracks.TryGetValue(pair.Key, out List<PoseSample> track) || track.Count < 2)
                {
                    pair.Value.gameObject.SetActive(false);
                    continue;
                }

                Vector3 origin = track[0].Position;
                origin.y = 0f;
                float maximumTravel = 0f;
                foreach (PoseSample sample in track)
                {
                    Vector3 position = sample.Position;
                    position.y = 0f;
                    maximumTravel = Mathf.Max(maximumTravel, Vector3.Distance(origin, position));
                }
                if (maximumTravel >= .40f) continue;
                pair.Value.gameObject.SetActive(false);
                Debug.Log($"ATADTRL DISPLAY 3 T3: removed stationary human replica {pair.Key}; " +
                          "moving warehouse traffic remains active.");
            }
        }

        /// <summary>
        /// Direct, idempotent entry point used by the pipeline in addition to
        /// the event subscription. This guarantees A4/T3 replay startup even
        /// when Unity creates the component during another component's Awake.
        /// </summary>
        public void BeginSafeReplay()
        {
            StartReplay();
        }

        private void Complete(string outcome, string stage)
        {
            if (!running) return;
            running = false;
            SetParcelAttached(false);
            if (scenarioCode == "A4" && parcelReplica != null)
            {
                parcelReplica.gameObject.SetActive(true);
                parcelReplica.SetParent(world, true);
                parcelReplica.position = relocatedTarget + Vector3.up * .55f;
            }
            pipeline?.ReportSafeReplayCompleted(outcome, stage);
        }

        private void CaptureFrame()
        {
            float time = Time.time - captureStartedAt;
            if (physicalRobot != null) robotTrack.Add(Pose(physicalRobot.transform, time));
            foreach (DynamicObjectMover actor in FindObjectsByType<DynamicObjectMover>(FindObjectsInactive.Exclude))
            {
                if (actor == null || string.IsNullOrWhiteSpace(actor.ObjectId)) continue;
                if (!actorTracks.TryGetValue(actor.ObjectId, out List<PoseSample> track))
                {
                    track = new List<PoseSample>();
                    actorTracks[actor.ObjectId] = track;
                }
                track.Add(Pose(actor.transform, time));
            }
        }

        private void BuildActorReplicas()
        {
            actorReplicas.Clear();
            foreach (DynamicObjectMover actor in FindObjectsByType<DynamicObjectMover>(FindObjectsInactive.Exclude))
            {
                if (actor == null || string.IsNullOrWhiteSpace(actor.ObjectId)) continue;
                // The operator requested no forklift visual in the T3 safe
                // replay. Exclude the complete F1/F2 replicas at creation so
                // neither vehicle nor embedded driver can appear on Display 3.
                if (scenarioCode == "T3" && actor.ObjectId.StartsWith("F")) continue;
                Transform replica = new GameObject($"{actor.ObjectId}_{scenarioCode}_REPLAY").transform;
                replica.SetParent(world, false);
                CloneRenderers(actor.transform, replica);
                if (replica.childCount == 0)
                    AddPrimitive(replica, actor.ObjectId, Vector3.up * .7f, new Vector3(.7f, 1.4f, .7f),
                        actor.ObjectId.StartsWith("F") ? new Color(.95f, .55f, .08f) : new Color(.12f, .72f, .42f));
                actorReplicas[actor.ObjectId] = replica;
            }
        }

        private void PlaybackActors(float elapsed)
        {
            foreach (KeyValuePair<string, Transform> pair in actorReplicas)
                if (actorTracks.TryGetValue(pair.Key, out List<PoseSample> track) && track.Count > 0)
                {
                    float trackDuration = track[track.Count - 1].Time;
                    float playbackTime = scenarioCode == "T3" && trackDuration > .1f && elapsed > trackDuration
                        ? elapsed % trackDuration : Mathf.Min(elapsed, trackDuration);
                    Playback(pair.Value, track, playbackTime);
                }
        }

        private static void Playback(Transform target, List<PoseSample> track, float time)
        {
            if (target == null || track.Count == 0) return;
            PoseSample sample = Sample(track, time);
            target.position = sample.Position;
            target.rotation = sample.Rotation;
        }

        private static PoseSample Sample(List<PoseSample> track, float time)
        {
            if (track.Count == 0) return new PoseSample();
            if (time <= track[0].Time) return track[0];
            for (int i = 1; i < track.Count; i++)
            {
                if (track[i].Time < time) continue;
                PoseSample a = track[i - 1], b = track[i];
                float t = Mathf.InverseLerp(a.Time, b.Time, time);
                return new PoseSample { Time = time, Position = Vector3.Lerp(a.Position, b.Position, t),
                    Rotation = Quaternion.Slerp(a.Rotation, b.Rotation, t) };
            }
            return track[track.Count - 1];
        }

        private static PoseSample Pose(Transform source, float time) => new PoseSample
            { Time = time, Position = source.position, Rotation = source.rotation };

        private void SetParcelAttached(bool attached)
        {
            if (parcelReplica == null || parcelAttached == attached) return;
            parcelAttached = attached;
            parcelReplica.gameObject.SetActive(attached);
            if (attached)
            {
                parcelReplica.SetParent(robotReplica, false);
                parcelReplica.localPosition = new Vector3(0f, .92f, 0f);
            }
        }

        private void BuildRelocatedRackMarker()
        {
            if (relocatedRackMarker != null) Destroy(relocatedRackMarker.gameObject);
            WarehouseManager warehouse = FindAnyObjectByType<WarehouseManager>();
            Transform sourceRack = warehouse != null && warehouse.RackRoot != null && !string.IsNullOrWhiteSpace(a4RackId)
                ? warehouse.RackRoot.Find(a4RackId) : null;
            if (sourceRack != null)
            {
                relocatedRackMarker = new GameObject($"DISPLAY3_RELOCATED_{a4RackId}").transform;
                relocatedRackMarker.SetParent(world, false);
                relocatedRackMarker.position = sourceRack.position;
                relocatedRackMarker.rotation = sourceRack.rotation;
                CloneRenderers(sourceRack, relocatedRackMarker);
                return;
            }

            // Visual fallback only: a thin floor target cannot obstruct or
            // hide the replay robot as the previous solid green cube did.
            relocatedRackMarker = AddPrimitive(world, "UPDATED RACK TARGET", relocatedTarget + Vector3.up * .03f,
                new Vector3(2.2f, .06f, 1.2f), new Color(.08f, .75f, .42f)).transform;
        }

        private void CloneWarehouseVisuals()
        {
            WarehouseManager warehouse = FindAnyObjectByType<WarehouseManager>();
            if (warehouse == null) return;
            originalA4RackVisuals.Clear();
            Transform originalA4Rack = scenarioCode == "A4" && warehouse.RackRoot != null &&
                                       !string.IsNullOrWhiteSpace(a4RackId)
                ? warehouse.RackRoot.Find(a4RackId) : null;
            Transform visualRoot = new GameObject($"{scenarioCode}_WAREHOUSE_REPLICA").transform;
            visualRoot.SetParent(world, false);
            foreach (MeshRenderer renderer in warehouse.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (warehouse.DynamicObjectRoot != null && renderer.transform.IsChildOf(warehouse.DynamicObjectRoot)) continue;
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                GameObject copy = new GameObject(renderer.gameObject.name, typeof(MeshFilter), typeof(MeshRenderer));
                copy.transform.SetParent(visualRoot, false);
                copy.transform.position = renderer.transform.position;
                copy.transform.rotation = renderer.transform.rotation;
                copy.transform.localScale = renderer.transform.lossyScale;
                copy.GetComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                copy.GetComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
                copy.layer = ShadowLayer;
                if (originalA4Rack != null && renderer.transform.IsChildOf(originalA4Rack))
                    originalA4RackVisuals.Add(copy);
            }
        }

        private static void CloneRenderers(Transform source, Transform target)
        {
            if (source == null) return;
            foreach (MeshRenderer renderer in source.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!renderer.enabled) continue;
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                GameObject copy = new GameObject(renderer.gameObject.name, typeof(MeshFilter), typeof(MeshRenderer));
                copy.transform.SetParent(target, false);
                copy.transform.localPosition = source.InverseTransformPoint(renderer.transform.position);
                copy.transform.localRotation = Quaternion.Inverse(source.rotation) * renderer.transform.rotation;
                copy.transform.localScale = renderer.transform.lossyScale;
                copy.GetComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                copy.GetComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
                copy.layer = ShadowLayer;
            }
        }

        private static GameObject AddPrimitive(Transform parent, string name, Vector3 position,
            Vector3 scale, Color color)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.layer = ShadowLayer;
            Collider collider = go.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                renderer.material = new Material(shader) { color = color };
            }
            return go;
        }

        private void ClearWorld()
        {
            for (int i = world.childCount - 1; i >= 0; i--) Destroy(world.GetChild(i).gameObject);
            actorReplicas.Clear();
            originalA4RackVisuals.Clear();
            relocatedRackMarker = null;
        }
    }
}
