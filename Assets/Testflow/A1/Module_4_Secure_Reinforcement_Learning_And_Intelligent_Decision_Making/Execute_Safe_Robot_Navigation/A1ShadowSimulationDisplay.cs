using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;
using ATADTRL.Environment;
using ATADTRL.Pipeline;
using ATADTRL.Scenarios;

namespace ATADTRL.TestFlow.A1.UI
{
    /// <summary>
    /// Display 3 is the counterfactual A1 execution of the same warehouse task
    /// shown on Display 1. Display 1 keeps the Module-1 collision. This branch
    /// uses the real A1 coordinates and a visual replica of the real warehouse,
    /// but executes the trusted-context wait before the blind corner.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class A1ShadowSimulationDisplay : MonoBehaviour
    {
        public const int ShadowLayer = 29;

        private enum TaskStage
        {
            Waiting, NavigateToSource, ScanBarcode, Pick, NavigateToDestination,
            SecureWait, AlignAtDestination, Place, VerifyRelease, Complete
        }

        private ScenarioManager scenarios;
        private ATADTRLPipelineManager pipeline;
        private Camera camera3;
        private Camera display1Camera;
        private Transform world, staticWarehouse, robot, human, parcel, loadDeck;
        private TMP_Text missionText;
        private TMP_Text applyUpdateLabel;
        private TMP_Text hudStateText, hudEventText, hudStatusText, hudResultText;
        private Image stateIndicator;
        private Image hudProgress;
        private readonly List<Image> hudStageChips = new List<Image>();
        private Button applyUpdateButton;
        private readonly List<Vector3> deliveryRoute = new List<Vector3>();
        private sealed class ActorPose
        {
            public float time;
            public Vector3 position;
            public Quaternion rotation;
        }

        private readonly Dictionary<string, Transform> mirroredActors = new Dictionary<string, Transform>();
        private readonly Dictionary<string, List<ActorPose>> actorTracks = new Dictionary<string, List<ActorPose>>();
        private readonly List<ActorPose> robotTrack = new List<ActorPose>();
        private readonly Dictionary<Camera, int> originalCameraMasks = new Dictionary<Camera, int>();
        private readonly List<Vector3> navCorners = new List<Vector3>();
        private NavMeshPath robotPath;
        private int navCornerIndex;
        private Vector3 navLogicalTarget;
        private bool navPathReady;
        private float nextPathAttempt;
        private int routeIndex;
        private TaskStage taskStage = TaskStage.Waiting;
        private float stageTimer;
        private float robotSpeed = 1.55f;
        private float humanSpeed = 0.8f;
        private bool running, carrying, completed, predictedConflict;
        private Vector3 pickup, destination, blindCorner, humanGoal;
        private Vector3 robotStart, humanStart;
        private Vector3 laneDirection = Vector3.right;
        private ScenarioDefinition preparedScenario;
        private string parcelBarcode = "PKG-A1-P1-D2";
        private string parcelCategory = "Apparel";
        private float baselineCaptureStartedAt, replayStartedAt, nextActorCaptureTime;
        private float recordedPickupDepartureTime, recordedSafetyInterventionTime;
        private bool followingRecordedRobot;

        public string Stage { get; private set; } = "WAITING FOR A1";
        public bool Completed => completed;

        private void Awake()
        {
            scenarios = FindAnyObjectByType<ScenarioManager>();
            pipeline = FindAnyObjectByType<ATADTRLPipelineManager>();
            robotPath = new NavMeshPath();
            EnsureCamera();
            IsolateDisplay3Layer();
            BuildWorldRoot();
            CloneStaticWarehouse();
            BuildRobotReplica();
            if (scenarios != null) scenarios.OnScenarioStarted += OnScenarioStarted;
            if (pipeline != null) pipeline.SafeReplayRequested += StartSafeReplay;
        }

        private void Start()
        {
            // Awake may precede the warehouse's material assignment. Refresh
            // once at Start so the idle Display-3 view already matches
            // Display 1, even before the operator starts A1.
            if (staticWarehouse != null) Destroy(staticWarehouse.gameObject);
            CloneStaticWarehouse();
            ResolveDisplay1Camera();
            UpdateCamera(true);
            if (scenarios != null && scenarios.IsRunning && scenarios.CurrentScenarioIndex >= 0 &&
                scenarios.CurrentScenarioIndex < scenarios.Scenarios.Count &&
                scenarios.Scenarios[scenarios.CurrentScenarioIndex].scenarioCode == "A1")
                OnScenarioStarted(scenarios.Scenarios[scenarios.CurrentScenarioIndex]);
        }

        private void OnDestroy()
        {
            if (scenarios != null) scenarios.OnScenarioStarted -= OnScenarioStarted;
            if (pipeline != null) pipeline.SafeReplayRequested -= StartSafeReplay;
            foreach (KeyValuePair<Camera, int> pair in originalCameraMasks)
                if (pair.Key != null) pair.Key.cullingMask = pair.Value;
            originalCameraMasks.Clear();
        }

        private void EnsureCamera()
        {
            GameObject cameraObject = GameObject.Find("ATADTRL_Display3_Camera");
            if (cameraObject == null) cameraObject = new GameObject("ATADTRL_Display3_Camera", typeof(Camera));
            camera3 = cameraObject.GetComponent<Camera>();
            camera3.targetDisplay = 2;
            camera3.clearFlags = CameraClearFlags.Skybox;
            camera3.backgroundColor = new Color(0.12f, 0.18f, 0.23f);
            camera3.cullingMask = 1 << ShadowLayer;
            camera3.depth = 0f;
            camera3.orthographic = false;
            camera3.fieldOfView = 62f;
            camera3.nearClipPlane = 0.08f;
            camera3.enabled = true;
        }

        private void ResolveDisplay1Camera()
        {
            IsolateDisplay3Layer();
            Camera preferred = Camera.main;
            if (preferred != null && preferred != camera3 && preferred.targetDisplay == 0 && preferred.enabled)
            {
                display1Camera = preferred;
                CopyDisplay1CameraSettings(preferred);
                return;
            }
            foreach (Camera candidate in FindObjectsByType<Camera>(FindObjectsInactive.Exclude))
            {
                if (candidate == null || candidate == camera3 || candidate.targetDisplay != 0 || !candidate.enabled) continue;
                display1Camera = candidate;
                CopyDisplay1CameraSettings(candidate);
                break;
            }
        }

        private void CopyDisplay1CameraSettings(Camera source)
        {
            camera3.CopyFrom(source);
            camera3.targetDisplay = 2;
            camera3.targetTexture = null;
            camera3.rect = new Rect(0f, 0f, 1f, 1f);
            camera3.cullingMask = 1 << ShadowLayer;
            camera3.depth = 0f;
            camera3.enabled = true;
        }

        private void IsolateDisplay3Layer()
        {
            int shadowMask = 1 << ShadowLayer;
            foreach (Camera candidate in FindObjectsByType<Camera>(FindObjectsInactive.Exclude))
            {
                if (candidate == null) continue;
                if (candidate == camera3)
                {
                    candidate.cullingMask = shadowMask;
                    candidate.targetDisplay = 2;
                    continue;
                }

                if (!originalCameraMasks.ContainsKey(candidate))
                    originalCameraMasks[candidate] = candidate.cullingMask;
                candidate.cullingMask &= ~shadowMask;
            }
        }

        private void BuildWorldRoot()
        {
            GameObject existing = GameObject.Find("A1_ATADTRL_Shadow_Warehouse");
            if (existing != null) Destroy(existing);
            world = new GameObject("A1_ATADTRL_Shadow_Warehouse").transform;
            SetLayer(world.gameObject);
        }

        private void CloneStaticWarehouse()
        {
            if (world == null || scenarios == null || scenarios.warehouseManager == null) return;
            staticWarehouse = new GameObject("Display3_RealWarehouseReplica").transform;
            staticWarehouse.SetParent(world, false);
            SetLayer(staticWarehouse.gameObject);
            Transform dynamicRoot = scenarios.warehouseManager.DynamicObjectRoot;
            foreach (MeshRenderer sourceRenderer in scenarios.warehouseManager.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (sourceRenderer == null || !sourceRenderer.enabled) continue;
                if (dynamicRoot != null && sourceRenderer.transform.IsChildOf(dynamicRoot)) continue;
                if (sourceRenderer.GetComponentInParent<Canvas>() != null) continue;
                MeshFilter sourceFilter = sourceRenderer.GetComponent<MeshFilter>();
                if (sourceFilter == null || sourceFilter.sharedMesh == null) continue;
                GameObject copy = new GameObject("D3_" + sourceRenderer.gameObject.name,
                    typeof(MeshFilter), typeof(MeshRenderer));
                copy.transform.SetParent(staticWarehouse, false);
                copy.transform.position = sourceRenderer.transform.position;
                copy.transform.rotation = sourceRenderer.transform.rotation;
                copy.transform.localScale = sourceRenderer.transform.lossyScale;
                copy.GetComponent<MeshFilter>().sharedMesh = sourceFilter.sharedMesh;
                copy.GetComponent<MeshRenderer>().sharedMaterials = sourceRenderer.sharedMaterials;
                copy.layer = ShadowLayer;
            }

            // Rack names, station names and safety boards in Display 1 are
            // real world-space TextMeshPro signs rather than UI overlays.
            // Copy them into the shadow warehouse so the environment itself
            // has the same industrial identification on Display 3.
            foreach (TextMeshPro sourceText in scenarios.warehouseManager.GetComponentsInChildren<TextMeshPro>(true))
            {
                if (sourceText == null || !sourceText.enabled || !sourceText.gameObject.activeInHierarchy) continue;
                if (dynamicRoot != null && sourceText.transform.IsChildOf(dynamicRoot)) continue;
                GameObject copy = new GameObject("D3_" + sourceText.gameObject.name, typeof(TextMeshPro));
                copy.transform.SetParent(staticWarehouse, false);
                copy.transform.position = sourceText.transform.position;
                copy.transform.rotation = sourceText.transform.rotation;
                copy.transform.localScale = sourceText.transform.lossyScale;
                TextMeshPro target = copy.GetComponent<TextMeshPro>();
                target.text = sourceText.text;
                target.font = sourceText.font;
                target.fontSharedMaterial = sourceText.fontSharedMaterial;
                target.fontSize = sourceText.fontSize;
                target.fontStyle = sourceText.fontStyle;
                target.color = sourceText.color;
                target.alignment = sourceText.alignment;
                target.textWrappingMode = sourceText.textWrappingMode;
                target.rectTransform.sizeDelta = sourceText.rectTransform.sizeDelta;
                copy.layer = ShadowLayer;
            }
        }

        private void BuildRobotReplica()
        {
            if (robot != null) Destroy(robot.gameObject);
            robot = new GameObject("ATADTRL_AMR_SAFE_COUNTERPART").transform;
            robot.SetParent(world, false);
            SetLayer(robot.gameObject);
            bool copied = scenarios != null && scenarios.robotController != null &&
                          CloneActorRenderers(scenarios.robotController.transform, robot);
            if (!copied)
            {
                Part(robot, "Chassis", new Vector3(0f, .38f, 0f), new Vector3(1.45f, .55f, 1.8f), new Color(.08f, .37f, .58f));
                Part(robot, "Safety Bumper", new Vector3(0f, .28f, .95f), new Vector3(1.55f, .25f, .12f), new Color(.95f, .65f, .05f));
            }
            loadDeck = new GameObject("Shadow Load Deck").transform;
            loadDeck.SetParent(robot, false);
            // Match the physical AMR rear cargo deck used on Display 1.
            loadDeck.localPosition = new Vector3(0f, .69f, -.48f);
            SetLayer(loadDeck.gameObject);
        }

        private void BuildHumanReplica(Transform source)
        {
            if (human != null) Destroy(human.gameObject);
            human = new GameObject("H1_SAFE_BRANCH_WORKER").transform;
            human.SetParent(world, false);
            SetLayer(human.gameObject);
            bool copied = source != null && CloneActorRenderers(source, human);
            if (!copied)
            {
                Part(human, "Body", new Vector3(0f, .95f, 0f), new Vector3(.55f, 1.35f, .38f), new Color(.95f, .55f, .12f), PrimitiveType.Capsule);
                Part(human, "Head", new Vector3(0f, 1.85f, 0f), new Vector3(.38f, .38f, .38f), new Color(.67f, .45f, .31f), PrimitiveType.Sphere);
                Part(human, "Carried Carton", new Vector3(0f, 1.05f, .42f), new Vector3(.65f, .45f, .5f), new Color(.50f, .30f, .14f));
            }
        }

        private static bool CloneActorRenderers(Transform sourceRoot, Transform targetRoot)
        {
            bool copiedAny = false;
            foreach (MeshRenderer sourceRenderer in sourceRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                // Display-1 actor prefabs retain an intentionally hidden
                // primitive only for collision. Copying it made Display 3
                // show a large coloured block around the detailed model.
                if (!sourceRenderer.enabled || !sourceRenderer.gameObject.activeInHierarchy) continue;
                MeshFilter sourceFilter = sourceRenderer.GetComponent<MeshFilter>();
                if (sourceFilter == null || sourceFilter.sharedMesh == null) continue;
                GameObject copy = new GameObject(sourceRenderer.gameObject.name, typeof(MeshFilter), typeof(MeshRenderer));
                copy.transform.SetParent(targetRoot, false);
                copy.transform.localPosition = sourceRoot.InverseTransformPoint(sourceRenderer.transform.position);
                copy.transform.localRotation = Quaternion.Inverse(sourceRoot.rotation) * sourceRenderer.transform.rotation;
                copy.transform.localScale = sourceRenderer.transform.lossyScale;
                copy.GetComponent<MeshFilter>().sharedMesh = sourceFilter.sharedMesh;
                copy.GetComponent<MeshRenderer>().sharedMaterials = sourceRenderer.sharedMaterials;
                copy.layer = ShadowLayer;
                copiedAny = true;
            }
            return copiedAny;
        }

        private void OnScenarioStarted(ScenarioDefinition scenario)
        {
            if (scenario == null || scenario.scenarioCode != "A1")
            {
                if (world != null) world.gameObject.SetActive(false);
                return;
            }
            if (world != null) world.gameObject.SetActive(true);
            preparedScenario = scenario;
            actorTracks.Clear();
            robotTrack.Clear();
            baselineCaptureStartedAt = Time.time;
            nextActorCaptureTime = Time.time;
            // WarehouseManager normally builds before this component, but an
            // explicit refresh here makes the replica deterministic regardless
            // of GameObject/Awake ordering in the saved scene.
            // The initial Awake clone can run before WarehouseManager has
            // applied its final industrial materials. Rebuild now, after A1
            // setup, so Display 3 receives the same rack/floor/wall materials
            // that are visible on Display 1 rather than default white meshes.
            if (staticWarehouse != null) Destroy(staticWarehouse.gameObject);
            CloneStaticWarehouse();
            Transform physicalH1 = null;
            foreach (DynamicObjectMover actor in FindObjectsByType<DynamicObjectMover>(FindObjectsInactive.Exclude))
            {
                if (actor != null && actor.ObjectId == "H1") { physicalH1 = actor.transform; break; }
            }
            BuildHumanReplica(physicalH1);
            BuildBackgroundActorReplicas();

            pickup = scenario.pickupPositions != null && scenario.pickupPositions.Count > 0 ? scenario.pickupPositions[0] : scenario.pickupPosition;
            destination = scenario.dropOffPositions != null && scenario.dropOffPositions.Count > 0 ? scenario.dropOffPositions[0] : scenario.goalPosition;
            parcelBarcode = scenario.parcelBarcodes != null && scenario.parcelBarcodes.Count > 0
                ? scenario.parcelBarcodes[0] : "PKG-A1-P1-D2";
            parcelCategory = scenario.parcelCategories != null && scenario.parcelCategories.Count > 0
                ? scenario.parcelCategories[0] : "Apparel";
            robotStart = scenario.startPosition;
            robotStart.y = pickup.y = destination.y = .45f;

            deliveryRoute.Clear();
            if (scenario.firstTaskDeliveryWaypoints != null)
                foreach (Vector3 point in scenario.firstTaskDeliveryWaypoints)
                    deliveryRoute.Add(new Vector3(point.x, .45f, point.z));
            deliveryRoute.Add(destination);
            routeIndex = 0;
            blindCorner = deliveryRoute.Count >= 2 ? deliveryRoute[1] : destination;
            Vector3 exitPoint = deliveryRoute.Count >= 3 ? deliveryRoute[2] : destination;
            laneDirection = (exitPoint - blindCorner).normalized;
            if (laneDirection.sqrMagnitude < .01f) laneDirection = Vector3.right;

            humanStart = exitPoint + laneDirection * 3.5f;
            humanGoal = blindCorner - laneDirection * 3.5f;
            foreach (var actor in scenario.dynamicObjects)
            {
                if (actor.actorId != "H1") continue;
                humanStart = actor.startPosition;
                humanGoal = actor.targetPosition;
                humanSpeed = Mathf.Max(.25f, actor.speed);
                break;
            }
            robot.position = robotStart;
            robot.rotation = Quaternion.identity;
            human.position = new Vector3(humanStart.x, 0f, humanStart.z);
            Vector3 humanDirection = humanGoal - human.position;
            if (humanDirection.sqrMagnitude > .01f) human.rotation = Quaternion.LookRotation(humanDirection);

            if (parcel != null) Destroy(parcel.gameObject);
            parcel = CreateParcel(PickupParcelPosition());
            taskStage = TaskStage.Waiting;
            stageTimer = 0f;
            running = false;
            carrying = completed = predictedConflict = false;
            Stage = "OBSERVING DISPLAY 1 — WAITING FOR MAJOR UPDATE";
            ResolveDisplay1Camera();
            UpdateCamera(true);
            UpdateMissionHud();
            Debug.Log("ATADTRL DISPLAY 3: A1 replay prepared and paused; waiting for the major update and operator command.");
        }

        private void StartSafeReplay()
        {
            if (preparedScenario == null || robot == null || human == null || pipeline == null ||
                pipeline.ActiveScenarioCode != "A1" || !pipeline.ReplayRunning)
                return;

            ResetRobotPath();
            routeIndex = 0;
            robot.position = robotStart;
            robot.rotation = Quaternion.identity;
            human.position = new Vector3(humanStart.x, 0f, humanStart.z);
            Vector3 humanDirection = humanGoal - human.position;
            if (humanDirection.sqrMagnitude > .01f) human.rotation = Quaternion.LookRotation(humanDirection);

            if (parcel != null) Destroy(parcel.gameObject);
            parcel = CreateParcel(PickupParcelPosition());
            taskStage = TaskStage.NavigateToSource;
            replayStartedAt = Time.time;
            stageTimer = 0f;
            carrying = completed = predictedConflict = false;
            followingRecordedRobot = PrepareRecordedRobotReplay();
            running = true;
            Stage = followingRecordedRobot
                ? "REPLAYING IDENTICAL A1 BASELINE CONDITIONS"
                : "POLICY UPDATE APPLIED — NAVIGATE TO P1";
            UpdateCamera(true);
            UpdateMissionHud();
        }

        private void Update()
        {
            // The replay is deliberately paused while the Display-1 baseline
            // runs. Keep this HUD alive so the post-collision update package
            // becomes visible and clickable even though no shadow actor is
            // currently moving.
            UpdateMissionHud();
            if (!running)
            {
                CaptureAndMirrorDisplay1Actors();
                return;
            }
            if (completed || robot == null || human == null) return;
            float dt = Time.deltaTime;
            stageTimer += dt;
            float replayElapsed = Time.time - replayStartedAt;
            if (!PlaybackRecordedActorTraffic(replayElapsed, dt)) MoveHuman(dt);

            // Keep the complete A1 run identical to Display 1 until the
            // learned safety action is required. This preserves the real
            // baseline navigation, scan/pick dwell time, route and traffic
            // timing; the only counterfactual change is the pre-collision
            // wait selected by Modules 2-4.
            if (followingRecordedRobot)
            {
                if (replayElapsed < recordedSafetyInterventionTime)
                {
                    ApplyRecordedPose(robot, robotTrack, replayElapsed);
                    if (!carrying && replayElapsed >= recordedPickupDepartureTime)
                    {
                        carrying = true;
                        parcel.SetParent(loadDeck, false);
                        parcel.localPosition = Vector3.zero;
                    }
                    Stage = carrying
                        ? "SAME A1 RUN — TRANSPORT P1 TOWARD BOOKS-RACK CORNER"
                        : "SAME A1 RUN — APPROACH, SCAN AND PICK P1";
                    ReportStage(carrying ? .48f : .20f,
                        carrying ? ATADTRLPipelineManager.SecureAction.Forward : ATADTRLPipelineManager.SecureAction.Wait);
                    UpdateMissionHud();
                    return;
                }

                ApplyRecordedPose(robot, robotTrack, recordedSafetyInterventionTime);
                if (!carrying)
                {
                    carrying = true;
                    parcel.SetParent(loadDeck, false);
                    parcel.localPosition = Vector3.zero;
                }
                followingRecordedRobot = false;
                predictedConflict = true;
                routeIndex = Mathf.Min(2, Mathf.Max(0, deliveryRoute.Count - 1));
                EnterStage(TaskStage.SecureWait);
            }
            switch (taskStage)
            {
                case TaskStage.NavigateToSource:
                    Stage = "NAVIGATE TO P1 PICKUP";
                    ReportStage(.10f, ATADTRLPipelineManager.SecureAction.Forward);
                    if (MoveRobotOnNavMesh(pickup, dt)) EnterStage(TaskStage.ScanBarcode);
                    break;
                case TaskStage.ScanBarcode:
                    Stage = "SCAN BARCODE AT P1";
                    ReportStage(.20f, ATADTRLPipelineManager.SecureAction.Wait);
                    if (stageTimer >= .75f) EnterStage(TaskStage.Pick);
                    break;
                case TaskStage.Pick:
                    Stage = "PICK AND VERIFY GRASP";
                    ReportStage(.30f, ATADTRLPipelineManager.SecureAction.Pick);
                    if (stageTimer >= .8f)
                    {
                        carrying = true;
                        parcel.SetParent(loadDeck, false);
                        parcel.localPosition = Vector3.zero;
                        EnterStage(TaskStage.NavigateToDestination);
                    }
                    break;
                case TaskStage.NavigateToDestination:
                    Stage = predictedConflict ? "CONFLICT CLEARED — RESUME TO D2" : "TRANSPORT P1 PARCEL TO D2";
                    ReportStage(predictedConflict ? .72f : .48f, ATADTRLPipelineManager.SecureAction.Forward);
                    if (ShouldSecureWait()) { predictedConflict = true; EnterStage(TaskStage.SecureWait); break; }
                    if (routeIndex < deliveryRoute.Count && MoveRobotOnNavMesh(deliveryRoute[routeIndex], dt))
                    {
                        routeIndex++;
                        ResetRobotPath();
                        if (routeIndex >= deliveryRoute.Count) EnterStage(TaskStage.AlignAtDestination);
                    }
                    break;
                case TaskStage.SecureWait:
                    Stage = "PIPELINE WAIT — H1 PREDICTED AT BLIND CORNER";
                    ReportStage(.62f, ATADTRLPipelineManager.SecureAction.Wait);
                    if (HasHumanClearedCorner()) EnterStage(TaskStage.NavigateToDestination);
                    break;
                case TaskStage.AlignAtDestination:
                    Stage = "ALIGN WITH D2 DELIVERY BAY";
                    ReportStage(.82f, ATADTRLPipelineManager.SecureAction.SlowDown);
                    if (stageTimer >= .8f) EnterStage(TaskStage.Place);
                    break;
                case TaskStage.Place:
                    Stage = "PLACE PARCEL AT D2";
                    ReportStage(.90f, ATADTRLPipelineManager.SecureAction.Place);
                    if (stageTimer >= .8f)
                    {
                        parcel.SetParent(world, true);
                        parcel.position = new Vector3(destination.x, .72f, destination.z);
                        carrying = false;
                        EnterStage(TaskStage.VerifyRelease);
                    }
                    break;
                case TaskStage.VerifyRelease:
                    Stage = "VERIFY RELEASE";
                    ReportStage(.96f, ATADTRLPipelineManager.SecureAction.Wait);
                    if (stageTimer >= .6f)
                    {
                        taskStage = TaskStage.Complete;
                        completed = true;
                        running = false;
                        Stage = "TASK COMPLETED — P1 DELIVERED TO D2 WITHOUT COLLISION";
                        pipeline?.ReportSafeReplayCompleted();
                        Debug.Log("ATADTRL DISPLAY 3 RESULT: A1 P1-to-D2 task completed without collision after the trusted-context safety wait.");
                    }
                    break;
            }
            UpdateMissionHud();
        }

        private void ReportStage(float progress, ATADTRLPipelineManager.SecureAction action)
        {
            pipeline?.ReportSafeReplayStage(Stage, progress, action);
        }

        private void MoveHuman(float dt)
        {
            Vector3 goal = new Vector3(humanGoal.x, human.position.y, humanGoal.z);
            MoveActor(human, goal, humanSpeed, dt);
        }

        private void BuildBackgroundActorReplicas()
        {
            foreach (Transform copy in mirroredActors.Values)
                if (copy != null) Destroy(copy.gameObject);
            mirroredActors.Clear();

            foreach (DynamicObjectMover actor in FindObjectsByType<DynamicObjectMover>(FindObjectsInactive.Exclude))
            {
                if (actor == null || actor.ObjectId == "H1" || actor.gameObject.name.StartsWith("Ambient_")) continue;
                Transform copy = new GameObject("D3_" + actor.ObjectId + "_MIRROR").transform;
                copy.SetParent(world, false);
                copy.position = actor.transform.position;
                copy.rotation = actor.transform.rotation;
                SetLayer(copy.gameObject);
                CloneActorRenderers(actor.transform, copy);
                mirroredActors[actor.ObjectId] = copy;
            }
        }

        private void CaptureAndMirrorDisplay1Actors()
        {
            if (preparedScenario == null || scenarios == null || !scenarios.IsRunning) return;
            bool capture = Time.time >= nextActorCaptureTime;
            if (capture) nextActorCaptureTime = Time.time + .05f;
            float sampleTime = Mathf.Max(0f, Time.time - baselineCaptureStartedAt);

            foreach (DynamicObjectMover actor in FindObjectsByType<DynamicObjectMover>(FindObjectsInactive.Exclude))
            {
                if (actor == null || string.IsNullOrWhiteSpace(actor.ObjectId)) continue;
                if (actor.gameObject.name.StartsWith("Ambient_")) continue;
                string id = actor.ObjectId;
                if (id != "H1" && id != "H2" && id != "H3" && id != "H4" && id != "H5" &&
                    id != "F1" && id != "F2") continue;

                Transform visual = id == "H1" ? human : EnsureBackgroundReplica(actor);
                if (visual != null)
                {
                    visual.position = actor.transform.position;
                    visual.rotation = actor.transform.rotation;
                }
                if (!capture) continue;
                if (!actorTracks.TryGetValue(id, out List<ActorPose> track))
                {
                    track = new List<ActorPose>();
                    actorTracks[id] = track;
                }
                track.Add(new ActorPose
                {
                    time = sampleTime,
                    position = actor.transform.position,
                    rotation = actor.transform.rotation
                });
            }

            if (capture && scenarios.robotController != null)
            {
                Transform sourceRobot = scenarios.robotController.transform;
                robotTrack.Add(new ActorPose
                {
                    time = sampleTime,
                    position = sourceRobot.position,
                    rotation = sourceRobot.rotation
                });
            }
        }

        private bool PrepareRecordedRobotReplay()
        {
            if (robotTrack.Count < 3) return false;

            bool reachedPickup = false;
            bool departedPickup = false;
            recordedPickupDepartureTime = robotTrack[0].time;
            for (int i = 0; i < robotTrack.Count; i++)
            {
                float distance = PlanarDistance(robotTrack[i].position, pickup);
                if (distance <= .75f) reachedPickup = true;
                else if (reachedPickup && distance >= .95f)
                {
                    recordedPickupDepartureTime = robotTrack[i].time;
                    departedPickup = true;
                    break;
                }
            }

            // Stop at a pose on the real baseline path about two metres
            // before its terminal collision pose. No alternate route or
            // artificial holding location is introduced.
            Vector3 collisionPose = robotTrack[robotTrack.Count - 1].position;
            int interventionIndex = Mathf.Max(0, robotTrack.Count - 2);
            for (int i = robotTrack.Count - 2; i >= 0; i--)
            {
                if (PlanarDistance(robotTrack[i].position, collisionPose) < 2f) continue;
                interventionIndex = i;
                break;
            }
            recordedSafetyInterventionTime = robotTrack[interventionIndex].time;
            return departedPickup && recordedSafetyInterventionTime > recordedPickupDepartureTime;
        }

        private static void ApplyRecordedPose(Transform target, List<ActorPose> track, float playbackTime)
        {
            if (target == null || track == null || track.Count == 0) return;
            if (playbackTime <= track[0].time)
            {
                target.position = track[0].position;
                target.rotation = track[0].rotation;
                return;
            }

            ActorPose a = track[0];
            ActorPose b = track[track.Count - 1];
            for (int i = 1; i < track.Count; i++)
            {
                if (track[i].time < playbackTime) continue;
                a = track[i - 1];
                b = track[i];
                break;
            }
            float span = Mathf.Max(.0001f, b.time - a.time);
            float blend = Mathf.Clamp01((playbackTime - a.time) / span);
            target.position = Vector3.Lerp(a.position, b.position, blend);
            target.rotation = Quaternion.Slerp(a.rotation, b.rotation, blend);
        }

        private Transform EnsureBackgroundReplica(DynamicObjectMover actor)
        {
            if (actor == null) return null;
            if (mirroredActors.TryGetValue(actor.ObjectId, out Transform existing) && existing != null)
                return existing;
            Transform copy = new GameObject("D3_" + actor.ObjectId + "_MIRROR").transform;
            copy.SetParent(world, false);
            copy.position = actor.transform.position;
            copy.rotation = actor.transform.rotation;
            SetLayer(copy.gameObject);
            CloneActorRenderers(actor.transform, copy);
            mirroredActors[actor.ObjectId] = copy;
            return copy;
        }

        private bool PlaybackRecordedActorTraffic(float elapsed, float dt)
        {
            bool h1Played = false;
            foreach (KeyValuePair<string, List<ActorPose>> entry in actorTracks)
            {
                List<ActorPose> track = entry.Value;
                if (track == null || track.Count == 0) continue;
                Transform visual = entry.Key == "H1" ? human :
                    mirroredActors.TryGetValue(entry.Key, out Transform copy) ? copy : null;
                if (visual == null) continue;

                float duration = track[track.Count - 1].time;

                // Replay the Display-1 traffic exactly once. PingPong made
                // workers and forklifts reverse at the baseline collision
                // timestamp, which no longer represented the same A1 run.
                // After that timestamp H1 continues its original assigned
                // route while the updated robot performs the safety wait.
                if (elapsed >= duration)
                {
                    if (entry.Key == "H1")
                    {
                        MoveHuman(dt);
                        h1Played = true;
                    }
                    else
                    {
                        ActorPose last = track[track.Count - 1];
                        visual.rotation = last.rotation;
                        if (track.Count > 1)
                        {
                            ActorPose previous = track[track.Count - 2];
                            float sampleSpan = Mathf.Max(.001f, last.time - previous.time);
                            Vector3 continuingVelocity = (last.position - previous.position) / sampleSpan;
                            // Preserve the actor's direction and speed at the
                            // end of Display 1 instead of freezing the rest of
                            // the A1 warehouse traffic during the safe finish.
                            visual.position += continuingVelocity * dt;
                        }
                        else
                        {
                            visual.position = last.position;
                        }
                    }
                    continue;
                }

                float playbackTime = Mathf.Clamp(elapsed, 0f, duration);
                ActorPose a = track[0], b = track[track.Count - 1];
                for (int i = 1; i < track.Count; i++)
                {
                    if (track[i].time < playbackTime) continue;
                    a = track[i - 1]; b = track[i]; break;
                }
                float span = Mathf.Max(.0001f, b.time - a.time);
                float blend = Mathf.Clamp01((playbackTime - a.time) / span);
                visual.position = Vector3.Lerp(a.position, b.position, blend);
                visual.rotation = Quaternion.Slerp(a.rotation, b.rotation, blend);
                if (entry.Key == "H1") h1Played = true;
            }
            return h1Played;
        }

        private bool ShouldSecureWait()
        {
            float robotCornerDistance = PlanarDistance(robot.position, blindCorner);
            float signedHumanPosition = Vector3.Dot(human.position - blindCorner, laneDirection);
            bool sameConflictWindow = robotCornerDistance < 6f && signedHumanPosition < 7f && signedHumanPosition > -2f;
            bool pipelineWait = pipeline != null && pipeline.SelectedAction == ATADTRLPipelineManager.SecureAction.Wait;
            return sameConflictWindow || (pipelineWait && robotCornerDistance < 8f);
        }

        private bool HasHumanClearedCorner()
        {
            float signedHumanPosition = Vector3.Dot(human.position - blindCorner, laneDirection);
            return signedHumanPosition <= -2f || PlanarDistance(human.position, humanGoal) < .15f;
        }

        private void EnterStage(TaskStage next)
        {
            taskStage = next;
            stageTimer = 0f;
            ResetRobotPath();
        }

        private bool MoveRobotOnNavMesh(Vector3 logicalTarget, float dt)
        {
            if (!navPathReady || PlanarDistance(navLogicalTarget, logicalTarget) > .1f)
            {
                if (Time.unscaledTime < nextPathAttempt) return false;
                nextPathAttempt = Time.unscaledTime + .35f;
                if (!PrepareRobotPath(logicalTarget))
                {
                    Stage = "WAITING FOR VALID NAVMESH AISLE PATH";
                    return false;
                }
            }

            if (navCornerIndex >= navCorners.Count)
            {
                ResetRobotPath();
                return true;
            }

            Vector3 corner = navCorners[navCornerIndex];
            corner.y = robot.position.y;
            if (MoveActor(robot, corner, robotSpeed, dt))
            {
                navCornerIndex++;
                if (navCornerIndex >= navCorners.Count)
                {
                    ResetRobotPath();
                    return true;
                }
            }
            return false;
        }

        private bool PrepareRobotPath(Vector3 logicalTarget)
        {
            ResetRobotPath();
            if (!NavMesh.SamplePosition(robot.position, out NavMeshHit startHit, 3f, NavMesh.AllAreas) ||
                !NavMesh.SamplePosition(logicalTarget, out NavMeshHit targetHit, 4f, NavMesh.AllAreas))
                return false;

            if (!NavMesh.CalculatePath(startHit.position, targetHit.position, NavMesh.AllAreas, robotPath) ||
                robotPath.status == NavMeshPathStatus.PathInvalid || robotPath.corners.Length < 2)
                return false;

            navLogicalTarget = logicalTarget;
            for (int i = 1; i < robotPath.corners.Length; i++)
                navCorners.Add(new Vector3(robotPath.corners[i].x, robot.position.y, robotPath.corners[i].z));
            navCornerIndex = 0;
            navPathReady = navCorners.Count > 0;
            return navPathReady;
        }

        private void ResetRobotPath()
        {
            navCorners.Clear();
            navCornerIndex = 0;
            navPathReady = false;
        }

        private static bool MoveActor(Transform actor, Vector3 target, float speed, float dt)
        {
            target.y = actor.position.y;
            Vector3 direction = target - actor.position;
            direction.y = 0f;
            if (direction.sqrMagnitude > .001f)
                actor.rotation = Quaternion.Slerp(actor.rotation, Quaternion.LookRotation(direction), dt * 7f);
            actor.position = Vector3.MoveTowards(actor.position, target, speed * dt);
            return PlanarDistance(actor.position, target) <= .08f;
        }

        private void LateUpdate()
        {
            // Cameras created later by a dashboard or runtime loader must also
            // be prevented from drawing the Display 3 replica on Displays 1/2.
            IsolateDisplay3Layer();
            // A4/T3 own the Display-3 camera during their safe replay. Do not
            // keep forcing the camera toward the hidden A1 robot.
            if (pipeline == null || string.IsNullOrEmpty(pipeline.ActiveScenarioCode) ||
                pipeline.ActiveScenarioCode == "A1")
                UpdateCamera(false);
        }

        private void UpdateCamera(bool immediate)
        {
            if (camera3 == null || robot == null) return;
            // True onboard AMR camera: the user sees the safe branch from the
            // robot sensor mast, matching the operational view on Display 1.
            Vector3 desiredPosition = robot.TransformPoint(new Vector3(0f, 1.55f, .58f));
            Vector3 lookTarget = robot.TransformPoint(new Vector3(0f, 1.25f, 6f));
            Quaternion desiredRotation = Quaternion.LookRotation(lookTarget - desiredPosition, Vector3.up);
            float blend = immediate ? 1f : 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime);
            camera3.transform.position = Vector3.Lerp(camera3.transform.position, desiredPosition, blend);
            camera3.transform.rotation = Quaternion.Slerp(camera3.transform.rotation, desiredRotation, blend);
        }

        private Transform CreateParcel(Vector3 position)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "D3_Parcel_" + parcelBarcode + "_" + parcelCategory;
            box.transform.SetParent(world, true);
            box.transform.position = position;
            box.transform.localScale = new Vector3(.55f, .45f, .40f);
            box.GetComponent<Renderer>().material.color = new Color(.50f, .33f, .19f);
            Collider shape = box.GetComponent<Collider>();
            if (shape != null) Destroy(shape);
            WarehouseManager.CreatePhysicalSign(box.transform, "Parcel shipping label",
                new Vector3(0f, 0f, -.525f), Quaternion.identity, new Vector2(.80f, .60f),
                parcelBarcode + "\n" + parcelCategory,
                new Color(.94f, .93f, .88f), Color.black);
            SetLayer(box);
            return box.transform;
        }

        private Vector3 PickupParcelPosition()
        {
            return pickup + WarehouseManager.GetStationRearOffset(pickup, .95f) + Vector3.up * 1.05f;
        }

        private void BuildMissionHud()
        {
            GameObject canvasObject = new GameObject("A1_Display3_MissionHUD", typeof(RectTransform),
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            DontDestroyOnLoad(canvasObject);
            canvasObject.hideFlags = HideFlags.HideInHierarchy;
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera3;
            canvas.planeDistance = .8f;
            canvas.targetDisplay = 2;
            canvas.sortingOrder = 950;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            RectTransform panel = CreateHudPanel(canvasObject.transform, "ControlPanelBackdrop",
                new Vector2(18f, -18f), new Vector2(500f, 694f),
                new Color(.025f, .045f, .068f, .975f)).rectTransform;
            CreateHudPanel(panel, "OperationsAccent", Vector2.zero, new Vector2(500f, 4f),
                new Color(.10f, .59f, .90f));
            CreateHudText(panel, "OperationsTitle", "WAREHOUSE OPERATIONS", new Vector2(12f, -10f),
                new Vector2(472f, 32f), 24f, FontStyles.Bold);
            CreateHudText(panel, "OperationsSubtitle", "MODULE 04   /   SECURE POLICY EXECUTION",
                new Vector2(12f, -44f), new Vector2(472f, 21f), 15f);

            Image scenarioBar = CreateHudPanel(panel, "ScenarioTitle", new Vector2(12f, -72f),
                new Vector2(472f, 42f), new Color(.96f, .97f, .98f, 1f));
            TMP_Text scenarioTitle = CreateHudText(scenarioBar.transform, "Label",
                "A1: Books-Rack Blind-Turn Collision", new Vector2(10f, -7f),
                new Vector2(448f, 28f), 18f, FontStyles.Bold);
            scenarioTitle.color = new Color(.08f, .12f, .18f);

            CreateHudText(panel, "MissionBriefLabel", "MISSION BRIEF                         SAME A1 CONDITIONS",
                new Vector2(12f, -125f), new Vector2(472f, 20f), 15f, FontStyles.Bold);
            TMP_Text brief = CreateHudText(panel, "MissionBrief",
                "The P1-to-D2 Books-rack mission replays the recorded Display 1 route, timing, humans and forklifts. The trusted policy changes only the robot response at the predicted blind-corner conflict.",
                new Vector2(12f, -159f), new Vector2(472f, 103f), 17f);
            brief.textWrappingMode = TextWrappingModes.Normal;
            brief.overflowMode = TextOverflowModes.Ellipsis;

            Image telemetry = CreateHudPanel(panel, "TelemetryBackdrop", new Vector2(12f, -282f),
                new Vector2(476f, 132f), new Color(.050f, .090f, .127f));
            stateIndicator = CreateHudPanel(telemetry.transform, "StateIndicator", new Vector2(12f, -11f),
                new Vector2(7f, 16f), new Color(.28f, .68f, .84f));
            hudStateText = CreateHudText(telemetry.transform, "OperationsState", "MONITORING",
                new Vector2(28f, -7f), new Vector2(432f, 24f), 18f, FontStyles.Bold);
            missionText = CreateHudText(telemetry.transform, "MissionText", "",
                new Vector2(12f, -37f), new Vector2(448f, 88f), 16f);
            missionText.textWrappingMode = TextWrappingModes.Normal;

            CreateHudPanel(panel, "EpisodeProgressTrack", new Vector2(12f, -418f),
                new Vector2(476f, 3f), new Color(.10f, .19f, .26f));
            hudProgress = CreateHudPanel(panel, "EpisodeProgress", new Vector2(12f, -418f),
                new Vector2(0f, 3f), new Color(.10f, .68f, .87f));
            hudEventText = CreateHudText(panel, "EventStatus", "",
                new Vector2(12f, -430f), new Vector2(472f, 37f), 16f);
            hudStatusText = CreateHudText(panel, "StatusMessage", "",
                new Vector2(12f, -472f), new Vector2(472f, 39f), 16f);

            GameObject buttonObject = new GameObject("ApplyUpdateAndRun", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(panel, false);
            LayoutHud(buttonObject.GetComponent<RectTransform>(), new Vector2(12f, -519f), new Vector2(476f, 36f));
            buttonObject.GetComponent<Image>().color = new Color(.12f, .18f, .22f, .98f);
            applyUpdateButton = buttonObject.GetComponent<Button>();
            applyUpdateButton.onClick.AddListener(ApplyUpdateFromMissionHud);

            GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(buttonObject.transform, false);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
            applyUpdateLabel = labelObject.GetComponent<TextMeshProUGUI>();
            applyUpdateLabel.fontSize = 16f;
            applyUpdateLabel.fontStyle = FontStyles.Bold;
            applyUpdateLabel.alignment = TextAlignmentOptions.Center;
            applyUpdateLabel.color = Color.white;
            applyUpdateLabel.raycastTarget = false;

            hudStageChips.Clear();
            string[] stages = { "SAME A1 INPUT", "SAFE WAIT", "D2 COMPLETE" };
            for (int i = 0; i < stages.Length; i++)
            {
                Image chip = CreateHudPanel(panel, "Stage_" + i,
                    new Vector2(12f + i * 160f, -563f), new Vector2(152f, 34f),
                    new Color(.15f, .21f, .29f));
                TMP_Text chipText = CreateHudText(chip.transform, "Label", stages[i],
                    new Vector2(4f, -7f), new Vector2(144f, 22f), 13f, FontStyles.Bold);
                chipText.alignment = TextAlignmentOptions.Center;
                hudStageChips.Add(chip);
            }
            hudResultText = CreateHudText(panel, "PipelineResult", "",
                new Vector2(12f, -609f), new Vector2(472f, 64f), 14f);
            hudResultText.textWrappingMode = TextWrappingModes.Normal;
            SetLayer(canvasObject);
            UpdateMissionHud();
        }

        private void ApplyUpdateFromMissionHud()
        {
            if (pipeline == null) return;
            pipeline.ApplyUpdateAndRunSafeReplay();
            UpdateMissionHud();
        }

        private void UpdateMissionHud()
        {
            if (missionText == null) return;
            string state = completed ? "COMPLETED" : running ? "RUNNING" :
                pipeline != null && pipeline.ReplayReady ? "UPDATE READY" : "MONITORING";
            string load = carrying ? "P1 SECURED" : completed ? "RELEASED AT D2" : "EMPTY";
            string visibleStage = !running && pipeline != null ? pipeline.ShadowMissionStage : Stage;
            float progress = pipeline != null ? pipeline.ShadowMissionProgress : completed ? 1f : 0f;
            if (hudStateText != null) hudStateText.text = $"{state}   /   SAFE REPLAY";
            missionText.text =
                $"<color=#86AABD>MISSION</color>  P1 PICKUP  →  D2 DELIVERY\n" +
                $"<color=#86AABD>AMR</color>  {StageVelocity():F2} m/s  ·  Policy {(running ? "ACTIVE" : "READY")}\n" +
                $"<color=#86AABD>LOAD</color>  {load}\n" +
                $"<color=#86AABD>STAGE</color>  {visibleStage}";
            if (hudProgress != null)
                hudProgress.rectTransform.sizeDelta = new Vector2(476f * Mathf.Clamp01(progress), 3f);
            if (hudEventText != null)
                hudEventText.text = predictedConflict
                    ? "<color=#E9BA65>EVENT</color>  Blind-corner conflict predicted — controlled wait applied"
                    : "<color=#E9BA65>EVENT</color>  Same recorded A1 warehouse traffic";
            if (hudStatusText != null)
                hudStatusText.text = completed
                    ? "Safe outcome verified: parcel released at D2 without human collision."
                    : running ? "Executing the updated policy on the same A1 baseline conditions."
                    : pipeline != null && pipeline.ReplayReady
                        ? "Major update received. Apply the policy update to begin."
                        : "Waiting for the Display 1 A1 collision and pipeline update.";
            if (hudResultText != null)
                hudResultText.text = "<color=#86AABD>PIPELINE RESULT</color>\n" +
                    (pipeline != null ? pipeline.DecisionReason : "Waiting for Modules 2–4");
            for (int i = 0; i < hudStageChips.Count; i++)
            {
                bool active = i == 0 ? running || completed : i == 1 ? predictedConflict || completed : completed;
                hudStageChips[i].color = active ? new Color(.06f, .49f, .79f) : new Color(.15f, .21f, .29f);
            }
            if (stateIndicator != null)
                stateIndicator.color = completed ? new Color(.15f, .90f, .60f) :
                    running ? new Color(0f, .85f, 1f) : new Color(.55f, .65f, .72f);

            if (applyUpdateButton != null && applyUpdateLabel != null && pipeline != null)
            {
                bool ready = pipeline.ReplayReady && !pipeline.ReplayRunning;
                applyUpdateButton.interactable = ready;
                Image buttonImage = applyUpdateButton.GetComponent<Image>();
                if (pipeline.ReplayCompleted)
                {
                    applyUpdateLabel.text = "SAFE REPLAY COMPLETED";
                    buttonImage.color = new Color(.10f, .40f, .28f, .98f);
                }
                else if (pipeline.ReplayRunning)
                {
                    applyUpdateLabel.text = "SAFE REPLAY RUNNING...";
                    buttonImage.color = new Color(.04f, .40f, .52f, .98f);
                }
                else if (ready)
                {
                    applyUpdateLabel.text = "APPLY UPDATE & RUN SAFE REPLAY";
                    buttonImage.color = new Color(0f, .62f, .78f, .98f);
                }
                else
                {
                    applyUpdateLabel.text = "WAITING FOR A1 MAJOR UPDATE";
                    buttonImage.color = new Color(.12f, .18f, .22f, .98f);
                }
            }
        }

        private float StageVelocity()
        {
            if (!running || taskStage == TaskStage.SecureWait || taskStage == TaskStage.ScanBarcode ||
                taskStage == TaskStage.Pick || taskStage == TaskStage.Place || taskStage == TaskStage.VerifyRelease)
                return 0f;
            return robotSpeed;
        }

        private static Image CreateHudPanel(Transform parent, string name, Vector2 position, Vector2 size, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            LayoutHud(go.GetComponent<RectTransform>(), position, size);
            Image image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static TMP_Text CreateHudText(Transform parent, string name, string value, Vector2 position,
            Vector2 size, float fontSize, FontStyles style = FontStyles.Normal)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            LayoutHud(go.GetComponent<RectTransform>(), position, size);
            TMP_Text text = go.GetComponent<TMP_Text>();
            text.text = value;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = new Color(.78f, .87f, .92f);
            text.alignment = TextAlignmentOptions.TopLeft;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            return text;
        }

        private static void LayoutHud(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void Part(Transform parent, string name, Vector3 localPosition, Vector3 scale,
            Color color, PrimitiveType primitive = PrimitiveType.Cube)
        {
            GameObject part = GameObject.CreatePrimitive(primitive);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = scale;
            part.GetComponent<Renderer>().material.color = color;
            Collider shape = part.GetComponent<Collider>();
            if (shape != null) Destroy(shape);
            SetLayer(part);
        }

        private static void SetLayer(GameObject target)
        {
            target.layer = ShadowLayer;
            foreach (Transform child in target.transform) SetLayer(child.gameObject);
        }

        private static float PlanarDistance(Vector3 a, Vector3 b)
        {
            a.y = b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
