using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using ATADTRL.Core;
using ATADTRL.Environment;
using ATADTRL.Robot;
using ATADTRL.Navigation;
using ATADTRL.Sensors;
using ATADTRL.Logging;
using ATADTRL.Validation;

namespace ATADTRL.Scenarios
{
    public enum RunMode { None, Single, RunAll }

    /// <summary>
    /// Central orchestrator: loads a ScenarioDefinition, resets the world,
    /// runs the episode, logs both datasets every fixed step, records
    /// performance metrics, validates output, and reports completion.
    /// This is the single entry point the UI calls for one-click execution.
    /// </summary>
    public class ScenarioManager : MonoBehaviour
    {
        private const float MaximumEpisodeDurationSeconds = 120f;
        private const int DefaultResearchScenarioCount = 15;
        private const string ConfigurationFolderName = "ATADTRL_Configurations";

        [Serializable]
        private sealed class WarehouseLayoutRecord
        {
            public float length = 60f;
            public float width = 40f;
            public int rows = 4;
            public int columns = 6;
            public float aisleWidth = 3f;
        }

        [Serializable]
        private sealed class CustomScenarioRecord
        {
            public int serial;
            public string name;
            public string sourceId;
            public string destinationId;
            public int humanCount;
            public int forkliftCount;
            public bool obstacleEnabled;
            public string deliveryMode;
            public float durationSeconds;
        }

        [Serializable]
        private sealed class CustomScenarioCollection
        {
            public List<CustomScenarioRecord> scenarios = new List<CustomScenarioRecord>();
        }

        private enum DeliveryStage
        {
            NavigateToSource,
            NavigateToCharge,
            Charging,
            AlignAtSource,
            ScanBarcode,
            Pick,
            VerifyGrasp,
            SecureForTransport,
            NavigateToDestination,
            Place,
            VerifyRelease,
            EmergencyToSafety,
            Complete
        }
        [Header("Scene References")]
        public WarehouseManager warehouseManager;
        public GroundTruthManager groundTruthManager;
        public RobotController robotController;
        public NavigationManager navigationManager;
        public SensorManager sensorManager;
        public GameObject humanPrefab;
        public GameObject forkliftPrefab;
        public GameObject obstaclePrefab;
        public GameObject aisleBlockagePrefab; // simple empty prefab; AisleBlockage added at runtime if missing

        [Header("Logging Fixed Timestep")]
        public float loggingIntervalSeconds = 0.1f; // 10 Hz dataset logging
        public int experimentSeed = 1701;

        public List<ScenarioDefinition> Scenarios { get; private set; } = new List<ScenarioDefinition>();

        public int CurrentScenarioIndex { get; private set; } = 0;
        public bool IsInitialized { get; private set; }
        public bool IsRunning { get; private set; }
        public RunMode CurrentRunMode { get; private set; } = RunMode.None;

        public event Action<ScenarioDefinition> OnScenarioStarted;
        public event Action<ScenarioDefinition, PerformanceRecord> OnScenarioCompleted;
        public event Action OnAllScenariosCompleted;
        public event Action OnInitialized;
        public event Action OnScenarioCatalogChanged;
        public event Action<string> OnStatusMessage;
        public event Action<string, Vector3, Vector3> OnRackRelocated;
        public bool A4RackRelocated => _a4RackRelocated;
        public Vector3 A4RackOriginalPosition => _a4RackOriginalPosition;
        public Vector3 A4ValidPlacementPosition => _a4ValidPlacementPosition;

        private string _datasetRoot;
        private string _configurationRoot;
        private string _layoutConfigurationPath;
        private string _customScenarioPath;
        private GroundTruthLogger _gtLogger;
        private ObservationLogger _obsLogger;
        private PerformanceLogger _perfLogger;
        private EpisodeOutcomeLogger _outcomeLogger;
        private DatasetValidator _validator;

        private int _episodeCounter = 0;
        private long _stepCounter = 0;
        private float _episodeTime = 0f;
        private float _episodeStartRealTime = 0f;
        private float _sumVelocity = 0f;
        private int _velocitySamples = 0;
        private float _maxVelocity = 0f;
        private int _collisionCount = 0;
        private Coroutine _episodeCoroutine;
        private readonly List<DynamicObjectMover> _activeDynamicObjects = new List<DynamicObjectMover>();
        private readonly List<DynamicObjectMover> _ambientDynamicObjects = new List<DynamicObjectMover>();
        private readonly List<AisleBlockage> _activeBlockages = new List<AisleBlockage>();
        private DeliveryStage _deliveryStage;
        private int _deliveryTaskIndex;
        private readonly List<GameObject> _pickupMarkers = new List<GameObject>();
        private readonly List<GameObject> _dropOffMarkers = new List<GameObject>();
        private readonly List<GameObject> _deliveredParcels = new List<GameObject>();
        private GameObject _parcel;
        private float _taskStageTimer;
        private bool _carryingParcel;
        private bool _graspVerified;
        private bool _graspRetryPerformed;
        private int _deliveryRouteWaypointIndex;
        private float _batteryLevel;
        private RobotTaskAction _currentAction = RobotTaskAction.Wait;
        private string _terminalOutcome;
        private bool _emergencyTriggered;
        private bool _urgentOrderChanged;
        private int _activeBenchmarkEpisode;
        private int _activeBenchmarkEpisodeTotal;
        private RackSlot _reservedRackSlot;
        private RackSlot _sourceRackSlot;
        private ParcelInventoryRecord _currentParcelRecord;
        private string _currentBarcode;
        private string _currentCategory;
        private float _scenarioDiagnosticTimer;
        private bool _a1HumanAnnounced;
        private bool _a2ObstructionAnnounced;
        private bool _a2BlockerParkedAnnounced;
        private bool _a2RerouteAttempted;
        private bool _a3DropBlockedAnnounced;
        private bool _a4RackRelocated;
        private float _a4LoadedTravelTime;
        private Transform _a4RelocatedRack;
        private Vector3 _a4RackOriginalPosition;
        private Vector3 _a4ValidPlacementPosition;
        private int _a5DockingAttempts;
        private float _e2ArrivalTime = -1f;
        private float _a1FirstDetectionDistance = -1f;
        private float _a1MinHumanDistance = float.MaxValue;
        private bool _a1NearCollisionFlag;
        private bool _a1LosWasClear;
        private Vector3? _a1PrevRobotPos;
        private bool _a1CollisionSummaryLogged;
        private int _researchScenarioCount;
        private ScenarioDisturbanceRuntime _disturbance;
        private MobileManipulator _manipulator;
        private Vector3 _releasePosition;
        private bool _parcelOnTool;
        private readonly OperationalObservationChannel _observationChannel = new OperationalObservationChannel();
        private CSVLogger _eventLogger;
        public int CurrentEpisodeNumber => _activeBenchmarkEpisode;
        public int CurrentEpisodeTotal => _activeBenchmarkEpisodeTotal;
        public float EpisodeElapsedSeconds => _episodeTime;
        public float EpisodeDurationLimitSeconds => Scenarios.Count > CurrentScenarioIndex ? Scenarios[CurrentScenarioIndex].maxEpisodeDuration : 120f;
        public float BatteryLevelPercent => _batteryLevel;
        public string CurrentTaskStage => _deliveryStage.ToString();
        public string CurrentParcelBarcode => _currentBarcode;
        public bool IsCarryingParcel => _carryingParcel;
        public int CurrentTaskNumber => Mathf.Min(_deliveryTaskIndex + 1, CurrentTaskCount);
        public int CurrentTaskCount => Scenarios.Count > CurrentScenarioIndex ? GetDeliveryTaskCount(Scenarios[CurrentScenarioIndex]) : 0;
        public string DisturbanceStatus => _disturbance?.Status ?? "Normal warehouse operations";
        public bool WarehouseOperationsActive { get; private set; }
        private Coroutine _warehouseRebuildCoroutine;

        private void Awake()
        {
            // CSV files must not be modified beneath Assets while the Unity
            // Asset Database is importing them.  A live append changes the
            // file size mid-import, producing the "processed bytes does not
            // match file size" error.  persistentDataPath is outside Assets,
            // so it is safe for per-frame logging in both the Editor and a
            // standalone build.
            _datasetRoot = Path.Combine(Application.persistentDataPath, "ATADTRL_Dataset");
#if UNITY_EDITOR
            string verificationOutput = System.Environment.GetEnvironmentVariable("ATADTRL_VALIDATION_OUTPUT");
            if (!string.IsNullOrWhiteSpace(verificationOutput)) _datasetRoot = verificationOutput;
#endif
            if (!Directory.Exists(_datasetRoot)) Directory.CreateDirectory(_datasetRoot);
            _episodeCounter = LoadEpisodeSequence();

            _configurationRoot = Path.Combine(Application.persistentDataPath, ConfigurationFolderName);
            if (!Directory.Exists(_configurationRoot)) Directory.CreateDirectory(_configurationRoot);
            _layoutConfigurationPath = Path.Combine(_configurationRoot, "Warehouse_Layout.json");
            _customScenarioPath = Path.Combine(_configurationRoot, "Custom_Scenarios.json");

            _gtLogger = new GroundTruthLogger(_datasetRoot);
            _obsLogger = new ObservationLogger(_datasetRoot);
            _perfLogger = new PerformanceLogger(_datasetRoot);
            _outcomeLogger = new EpisodeOutcomeLogger(_datasetRoot);
            _validator = new DatasetValidator(_datasetRoot);
            Debug.Log($"ATADTRL: Runtime CSV output folder (safe from Asset Database imports): {_datasetRoot}");
        }

        public void BuildWorldAndScenarios()
        {
            if (IsInitialized) return;

            LoadSavedWarehouseLayout();
            warehouseManager.BuildWarehouse();
            RebuildNavigationSurface();
            RebuildScenarioCatalog();
            navigationManager.Initialize();
            sensorManager.Initialize(robotController.config);
            IsInitialized = true;
            WarehouseOperationsActive = true;
            StartAmbientWarehouseOperations();
            OnInitialized?.Invoke();
        }

        public string DatasetRootPath => DatasetPublisher.PublishedRoot;
        public string RuntimeDatasetRootPath => _datasetRoot;
        private int LoadEpisodeSequence()
        {
            string sequence = Path.Combine(_datasetRoot, "Episode_Sequence.txt");
            if (File.Exists(sequence) && int.TryParse(File.ReadAllText(sequence), out int saved)) return Mathf.Max(0, saved);
            // Bootstrap from existing recordings, including interrupted episodes.
            int maximum = 0;
            string observations = Path.Combine(_datasetRoot, "Unified_Observation_All.csv");
            if (File.Exists(observations))
                foreach (string line in File.ReadLines(observations))
                {
                    string[] identifiers = line.Split(new[] { ',' }, 4);
                    if (identifiers.Length >= 3 && int.TryParse(identifiers[2], out int episode)) maximum = Mathf.Max(maximum, episode);
                }
            return maximum;
        }
        public string ConfigurationRootPath => _configurationRoot;
        public int ResearchScenarioCount => _researchScenarioCount;

        // ---------------- Public control API (used by UI) ----------------

        /// <summary>
        /// Validates, saves and rebuilds a user-authored warehouse layout.
        /// The minimum 4x6 rack grid is intentional: the fixed A/E/T research
        /// scenarios address named racks such as Rack_4_3 and Rack_5_3.
        /// </summary>
        public bool TryApplyWarehouseLayout(float length, float width, int rows, int columns,
            float aisleWidth, out string message)
        {
            if (!ValidateWarehouseLayout(length, width, rows, columns, aisleWidth, out message))
                return false;
            if (_warehouseRebuildCoroutine != null)
            {
                message = "A warehouse rebuild is already in progress.";
                return false;
            }

            ApplyWarehouseLayout(length, width, rows, columns, aisleWidth);
            SaveWarehouseLayout();

            if (Application.isPlaying && IsInitialized)
            {
                _warehouseRebuildCoroutine = StartCoroutine(RebuildWarehouseCoroutine());
                message = $"Rebuilding {length:F0} m x {width:F0} m warehouse with {rows} rows, " +
                          $"{columns} columns and {aisleWidth:F1} m aisles...";
            }
            else
            {
                message = "Warehouse layout saved. Enter Play mode to build and navigate it.";
            }
            return true;
        }

        public bool TryRestoreBaselineWarehouseLayout(out string message)
        {
            if (_warehouseRebuildCoroutine != null)
            {
                message = "A warehouse rebuild is already in progress.";
                return false;
            }

            ApplyWarehouseLayout(60f, 40f, 4, 6, 3f);
            SaveWarehouseLayout();
            if (Application.isPlaying && IsInitialized)
            {
                _warehouseRebuildCoroutine = StartCoroutine(RebuildWarehouseCoroutine());
                message = "Restoring the validated 60 m x 40 m Module 1 baseline warehouse...";
            }
            else
            {
                message = "Baseline layout saved. Enter Play mode to build it.";
            }
            return true;
        }

        /// <summary>
        /// Adds a persistent, user-authored material-handling experiment. It
        /// remains selectable from the dropdown but is deliberately excluded
        /// from Run All, which must retain the controlled 15-scenario study.
        /// </summary>
        public bool TryCreateCustomScenario(string name, string sourceId, string destinationId,
            int humanCount, int forkliftCount, bool obstacleEnabled, string deliveryMode,
            float durationSeconds, out int scenarioIndex, out string message)
        {
            scenarioIndex = -1;
            name = (name ?? string.Empty).Trim();
            sourceId = (sourceId ?? string.Empty).Trim().ToUpperInvariant();
            destinationId = (destinationId ?? string.Empty).Trim().ToUpperInvariant();
            deliveryMode = (deliveryMode ?? string.Empty).Trim();

            if (!IsInitialized)
            {
                message = "Module 1 is still initializing.";
                return false;
            }
            if (name.Length < 3 || name.Length > 80)
            {
                message = "Use a scenario name between 3 and 80 characters.";
                return false;
            }
            if (!IsStationId(sourceId, 'P') || !IsStationId(destinationId, 'D'))
            {
                message = "Source must be P1-P3 and destination must be D1-D3.";
                return false;
            }
            if (humanCount < 1 || humanCount > 5 || forkliftCount < 0 || forkliftCount > 2)
            {
                message = "Custom scenarios support 1-5 active humans and 0-2 active forklifts.";
                return false;
            }
            if (!IsSupportedDeliveryMode(deliveryMode))
            {
                message = "Choose Station to station, Station to rack, Rack to station, or Rack to rack.";
                return false;
            }

            CustomScenarioCollection collection = LoadCustomScenarioCollection();
            int serial = collection.scenarios.Count == 0
                ? 1
                : collection.scenarios.Max(item => item != null ? item.serial : 0) + 1;
            var record = new CustomScenarioRecord
            {
                serial = serial,
                name = name,
                sourceId = sourceId,
                destinationId = destinationId,
                humanCount = humanCount,
                forkliftCount = forkliftCount,
                obstacleEnabled = obstacleEnabled,
                deliveryMode = deliveryMode,
                durationSeconds = Mathf.Clamp(durationSeconds, 30f, MaximumEpisodeDurationSeconds)
            };
            collection.scenarios.Add(record);

            try
            {
                File.WriteAllText(_customScenarioPath, JsonUtility.ToJson(collection, true));
            }
            catch (Exception ex)
            {
                message = $"Could not save the custom scenario: {ex.Message}";
                return false;
            }

            ScenarioDefinition definition = BuildCustomScenario(record);
            Scenarios.Add(definition);
            scenarioIndex = Scenarios.Count - 1;
            CurrentScenarioIndex = scenarioIndex;
            OnScenarioCatalogChanged?.Invoke();
            message = $"Created {definition.scenarioCode}: {definition.scenarioName}. It is selected for Start; " +
                      "Run All remains the fixed A1-A5, E1-E5 and T1-T5 benchmark.";
            Debug.Log($"ATADTRL DESIGNER: {message} Configuration='{_customScenarioPath}'.");
            return true;
        }

        public void StartScenario(int scenarioId)
        {
            if (!IsInitialized)
            {
                OnStatusMessage?.Invoke("Scenarios are still loading. Please try again in a moment.");
                return;
            }

            Debug.Log($"ATADTRL: StartScenario({scenarioId}) called. Total scenarios loaded: {Scenarios.Count}");
            int idx = Scenarios.FindIndex(s => s.scenarioId == scenarioId);
            if (idx < 0)
            {
                OnStatusMessage?.Invoke($"Scenario {scenarioId} not found.");
                return;
            }
            CurrentScenarioIndex = idx;
            CurrentRunMode = RunMode.Single;
            WarehouseOperationsActive = true;
            StopAmbientWarehouseOperations();
            InterruptRunningEpisode();
            ResumeSimulationForExplicitStart();
            _episodeCoroutine = StartCoroutine(RunEpisodeGuarded(Scenarios[idx], isPartOfRunAll: false, benchmarkEpisode: 1, benchmarkTotal: 1));
        }

        public void StartRunAll(bool randomOrder = false)
        {
            if (!IsInitialized || _researchScenarioCount == 0)
            {
                OnStatusMessage?.Invoke("No scenarios are available to run.");
                return;
            }

            CurrentRunMode = RunMode.RunAll;
            WarehouseOperationsActive = true;
            StopAmbientWarehouseOperations();
            InterruptRunningEpisode();
            ResumeSimulationForExplicitStart();
            Debug.Log($"ATADTRL: RUN ALL started. {_researchScenarioCount} controlled research scenarios will execute " +
                      $"{(randomOrder ? "in random order" : "sequentially from A1 through T5")}. " +
                      $"{Mathf.Max(0, Scenarios.Count - _researchScenarioCount)} custom scenario(s) remain selectable individually.");
            _episodeCoroutine = StartCoroutine(RunAllCoroutine(randomOrder));
        }

        /// <summary>Runs one randomly selected scenario from the populated list.</summary>
        public void StartRandomScenario()
        {
            if (!IsInitialized || Scenarios.Count == 0)
            {
                OnStatusMessage?.Invoke("No scenarios are available to run.");
                return;
            }

            StartScenario(Scenarios[UnityEngine.Random.Range(0, Scenarios.Count)].scenarioId);
        }

        public void StopScenario()
        {
            InterruptRunningEpisode();
            WarehouseOperationsActive = false;
            StopAmbientWarehouseOperations();
            navigationManager.ResetNavigation();
            CurrentRunMode = RunMode.None;
            DatasetPublisher.PublishCompletedFiles();
            OnStatusMessage?.Invoke("Scenario stopped.");
        }

        /// <summary>
        /// Cancels any in-flight episode coroutine and force-closes its CSV
        /// file handles. Required because StopCoroutine() abandons execution
        /// at the current yield point — any cleanup code written after the
        /// coroutine's main loop (including CSVLogger.Close calls) never
        /// runs, which would otherwise leave the file locked for the next
        /// Open() attempt (IOException: Sharing violation).
        /// </summary>
        private void InterruptRunningEpisode()
        {
            if (_episodeCoroutine != null)
            {
                StopCoroutine(_episodeCoroutine);
                _episodeCoroutine = null;
            }
            IsRunning = false;
            CleanupEpisodeObjects();
            _gtLogger.EndScenario();
            _obsLogger.EndScenario();
        }

        public void ResetScenario()
        {
            StopScenario();
            OnStatusMessage?.Invoke("Scenario reset.");
        }

        public void NextScenario()
        {
            if (Scenarios.Count == 0) return;
            CurrentScenarioIndex = (CurrentScenarioIndex + 1) % Scenarios.Count;
            OnStatusMessage?.Invoke($"Selected {Scenarios[CurrentScenarioIndex].scenarioName}");
        }

        public void PreviousScenario()
        {
            if (Scenarios.Count == 0) return;
            CurrentScenarioIndex = (CurrentScenarioIndex - 1 + Scenarios.Count) % Scenarios.Count;
            OnStatusMessage?.Invoke($"Selected {Scenarios[CurrentScenarioIndex].scenarioName}");
        }

        /// <summary>Selects a scenario in the UI without starting its episode.</summary>
        public bool SelectScenario(int index)
        {
            if (!IsInitialized || index < 0 || index >= Scenarios.Count) return false;
            CurrentScenarioIndex = index;
            OnStatusMessage?.Invoke($"Selected {Scenarios[CurrentScenarioIndex].scenarioName}");
            return true;
        }

        private bool ValidateWarehouseLayout(float length, float width, int rows, int columns,
            float aisleWidth, out string message)
        {
            if (float.IsNaN(length) || float.IsInfinity(length) ||
                float.IsNaN(width) || float.IsInfinity(width) ||
                float.IsNaN(aisleWidth) || float.IsInfinity(aisleWidth))
            {
                message = "Warehouse measurements must be finite numbers.";
                return false;
            }
            if (length < 48f || length > 120f || width < 40f || width > 100f)
            {
                message = "Use a warehouse length of 48-120 m and width of 40-100 m.";
                return false;
            }
            if (rows < 4 || rows > 10 || columns < 6 || columns > 14)
            {
                message = "Use 4-10 rack rows and 6-14 rack columns. The 4x6 minimum preserves all named research racks.";
                return false;
            }
            if (aisleWidth < 2.4f || aisleWidth > 6f)
            {
                message = "Aisle width must be between 2.4 m and 6.0 m for safe robot/forklift operation.";
                return false;
            }

            float rackGridWidth = columns * warehouseManager.config.rackWidth +
                                  Mathf.Max(0, columns - 1) * aisleWidth;
            float rackGridDepth = rows * warehouseManager.config.rackLength + (rows + 1) * aisleWidth;
            if (rackGridWidth > length - 5f)
            {
                message = $"The rack columns need at least {rackGridWidth + 5f:F1} m warehouse length, including perimeter lanes.";
                return false;
            }
            if (rackGridDepth > width - 0.4f)
            {
                message = $"The rack rows need at least {rackGridDepth + 0.4f:F1} m warehouse width. Increase width or reduce rows/aisle size.";
                return false;
            }

            message = string.Empty;
            return true;
        }

        private void ApplyWarehouseLayout(float length, float width, int rows, int columns, float aisleWidth)
        {
            WarehouseConfig config = warehouseManager.config;
            config.warehouseLength = length;
            config.warehouseWidth = width;
            config.rackRows = rows;
            config.rackColumns = columns;
            config.aisleWidth = aisleWidth;

            float xEdge = length * 0.40f;
            float zEdge = width * 0.375f;
            float rackGridWidth = columns * config.rackWidth + Mathf.Max(0, columns - 1) * aisleWidth;
            float rackGridDepth = rows * config.rackLength + (rows + 1) * aisleWidth;
            float firstRackX = -rackGridWidth * 0.5f + config.rackWidth * 0.5f;
            float sixthRackX = firstRackX + Mathf.Min(5, columns - 1) * (config.rackWidth + aisleWidth);
            float firstRackZ = -rackGridDepth * 0.5f + aisleWidth + config.rackLength * 0.5f;
            float fourthRackZ = firstRackZ + Mathf.Min(3, rows - 1) * (config.rackLength + aisleWidth);
            float leftRackApproachX = firstRackX - config.rackWidth * 0.5f - 1.05f;
            float rightRackApproachX = sixthRackX + config.rackWidth * 0.5f + 1.05f;
            config.pickupStationPositions = new List<Vector3>
            {
                new Vector3(-xEdge, 0f, -zEdge),
                new Vector3(xEdge, 0f, -zEdge),
                new Vector3(-xEdge, 0f, zEdge)
            };
            config.dropStationPositions = new List<Vector3>
            {
                new Vector3(leftRackApproachX, 0f, fourthRackZ),
                new Vector3(rightRackApproachX, 0f, fourthRackZ),
                new Vector3(leftRackApproachX, 0f, firstRackZ)
            };
            config.robotStartArea = config.pickupStationPositions[0] + new Vector3(0f, 0f, -2.2f);
            config.chargingStationPosition = new Vector3(xEdge, 0f, -width * 0.30f);
            config.safetyZonePosition = new Vector3(xEdge, 0f, zEdge);
            config.goalAreas = new List<Vector3>(config.dropStationPositions);
        }

        private void SaveWarehouseLayout()
        {
            if (string.IsNullOrEmpty(_layoutConfigurationPath)) return;
            try
            {
                var record = new WarehouseLayoutRecord
                {
                    length = warehouseManager.config.warehouseLength,
                    width = warehouseManager.config.warehouseWidth,
                    rows = warehouseManager.config.rackRows,
                    columns = warehouseManager.config.rackColumns,
                    aisleWidth = warehouseManager.config.aisleWidth
                };
                File.WriteAllText(_layoutConfigurationPath, JsonUtility.ToJson(record, true));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"ATADTRL DESIGNER: Could not persist warehouse layout: {ex.Message}");
            }
        }

        private void LoadSavedWarehouseLayout()
        {
            if (string.IsNullOrEmpty(_layoutConfigurationPath) || !File.Exists(_layoutConfigurationPath)) return;
            try
            {
                WarehouseLayoutRecord record = JsonUtility.FromJson<WarehouseLayoutRecord>(
                    File.ReadAllText(_layoutConfigurationPath));
                string validationMessage = "The saved record is empty.";
                if (record != null && ValidateWarehouseLayout(record.length, record.width, record.rows,
                        record.columns, record.aisleWidth, out validationMessage))
                {
                    ApplyWarehouseLayout(record.length, record.width, record.rows, record.columns, record.aisleWidth);
                    Debug.Log($"ATADTRL DESIGNER: Restored warehouse layout from '{_layoutConfigurationPath}'.");
                }
                else if (record != null)
                {
                    Debug.LogWarning($"ATADTRL DESIGNER: Saved warehouse layout was ignored: {validationMessage}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"ATADTRL DESIGNER: Saved warehouse layout could not be read: {ex.Message}");
            }
        }

        private IEnumerator RebuildWarehouseCoroutine()
        {
            InterruptRunningEpisode();
            CurrentRunMode = RunMode.None;
            OnStatusMessage?.Invoke("Rebuilding warehouse geometry and navigation surface...");

            warehouseManager.BuildWarehouse();
            // Runtime Destroy is deferred. One frame ensures old colliders are
            // gone before the navigation package collects the new geometry.
            yield return null;
            RebuildNavigationSurface();
            navigationManager.Initialize();

            Vector3 start = warehouseManager.config.robotStartArea;
            if (NavMesh.SamplePosition(start, out NavMeshHit hit, 6f, NavMesh.AllAreas)) start = hit.position;
            navigationManager.WarpRobot(start, Quaternion.identity);
            robotController.ResetRobot(start, Quaternion.identity);
            sensorManager.encoder?.InitializePose(start, 0f);
            sensorManager.imu?.InitializePose(start, 0f);

            RebuildScenarioCatalog();
            CurrentScenarioIndex = 0;
            OnScenarioCatalogChanged?.Invoke();
            OnStatusMessage?.Invoke($"Warehouse rebuilt: {warehouseManager.Inventory.TotalCapacity} rack slots, " +
                                    $"{Scenarios.Count} selectable scenarios. Run All still executes {_researchScenarioCount} research scenarios.");
            Debug.Log($"ATADTRL DESIGNER: Rebuild complete. Configuration='{_layoutConfigurationPath}'.");
            _warehouseRebuildCoroutine = null;
        }

        private void RebuildNavigationSurface()
        {
            if (warehouseManager == null) return;
            NavMesh.RemoveAllNavMeshData();
            NavMeshSurface surface = warehouseManager.GetComponent<NavMeshSurface>();
            if (surface == null) surface = warehouseManager.gameObject.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = ~0;
            surface.ignoreNavMeshAgent = true;
            surface.ignoreNavMeshObstacle = true;
            surface.BuildNavMesh();

            NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();
            if (triangulation.vertices.Length == 0)
                Debug.LogError("ATADTRL: Runtime NavMesh build produced no walkable surface.");
            else
                Debug.Log($"ATADTRL: Runtime NavMesh ready — {triangulation.vertices.Length} vertices, " +
                          $"{triangulation.indices.Length / 3} triangles.");
        }

        private void RebuildScenarioCatalog()
        {
            Scenarios = ScenarioLibrary.BuildAll(warehouseManager.config);
            _researchScenarioCount = Mathf.Min(DefaultResearchScenarioCount, Scenarios.Count);
            CustomScenarioCollection customScenarios = LoadCustomScenarioCollection();
            foreach (CustomScenarioRecord record in customScenarios.scenarios)
            {
                if (record != null) Scenarios.Add(BuildCustomScenario(record));
            }
        }

        private CustomScenarioCollection LoadCustomScenarioCollection()
        {
            if (string.IsNullOrEmpty(_customScenarioPath) || !File.Exists(_customScenarioPath))
                return new CustomScenarioCollection();
            try
            {
                CustomScenarioCollection result = JsonUtility.FromJson<CustomScenarioCollection>(
                    File.ReadAllText(_customScenarioPath));
                return result ?? new CustomScenarioCollection();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"ATADTRL DESIGNER: Custom scenario catalog could not be read: {ex.Message}");
                return new CustomScenarioCollection();
            }
        }

        private ScenarioDefinition BuildCustomScenario(CustomScenarioRecord record)
        {
            int sourceIndex = Mathf.Clamp(ParseStationIndex(record.sourceId), 0, 2);
            int destinationIndex = Mathf.Clamp(ParseStationIndex(record.destinationId), 0, 2);
            bool sourceIsRack = record.deliveryMode.StartsWith("Rack", StringComparison.OrdinalIgnoreCase);
            bool destinationIsRack = record.deliveryMode.EndsWith("rack", StringComparison.OrdinalIgnoreCase);
            string[] categories = { "Electronics", "Apparel", "Healthcare" };
            string sourceCategory = categories[sourceIndex];
            string destinationCategory = categories[destinationIndex];
            string parcelCategory = destinationIsRack ? destinationCategory : sourceCategory;

            Vector3 sourcePosition = sourceIsRack
                ? GetRackApproachPosition(sourceCategory, GetConfiguredStation(true, sourceIndex))
                : GetConfiguredStation(true, sourceIndex);
            Vector3 destinationPosition = destinationIsRack
                ? GetRackApproachPosition(destinationCategory, GetConfiguredStation(false, destinationIndex))
                : GetConfiguredStation(false, destinationIndex);
            Vector3 towardCentre = -new Vector3(sourcePosition.x, 0f, sourcePosition.z).normalized;
            if (towardCentre.sqrMagnitude < 0.01f) towardCentre = Vector3.forward;

            var scenario = new ScenarioDefinition
            {
                scenarioId = 1000 + Mathf.Max(1, record.serial),
                scenarioCode = $"C{Mathf.Max(1, record.serial):D2}",
                scenarioName = record.name,
                category = record.obstacleEnabled ? ScenarioCategory.DynamicObstacle : ScenarioCategory.MultiAgentTraffic,
                description = $"User-authored {record.deliveryMode} task from {record.sourceId} to {record.destinationId}. " +
                              $"{record.humanCount} assigned workers and {record.forkliftCount} assigned forklifts remain active. " +
                              $"The episode records the same ground-truth and unified-observation schema as the research scenarios.",
                startPosition = sourcePosition + towardCentre * 2.4f,
                goalPosition = destinationPosition,
                pickupPosition = sourcePosition,
                dropOffPosition = destinationPosition,
                pickupPositions = new List<Vector3> { sourcePosition },
                dropOffPositions = new List<Vector3> { destinationPosition },
                pickupFromRackByTask = new List<bool> { sourceIsRack },
                deliverToRackByTask = new List<bool> { destinationIsRack },
                sourceIds = new List<string> { sourceIsRack ? $"Rack-{sourceCategory}" : record.sourceId },
                destinationIds = new List<string> { destinationIsRack ? $"Rack-{destinationCategory}" : record.destinationId },
                parcelBarcodes = new List<string> { $"CUS-{record.serial:D3}-{sourceIndex + 1}{destinationIndex + 1}" },
                parcelCategories = new List<string> { parcelCategory },
                deliverToRack = destinationIsRack,
                rotateTaskOrderEachEpisode = false,
                taskPriority = 1,
                parcelMass = 2.5f + destinationIndex,
                maxEpisodeDuration = Mathf.Clamp(record.durationSeconds, 30f, MaximumEpisodeDurationSeconds)
            };

            AddCustomActors(scenario, record.humanCount, record.forkliftCount);
            if (record.obstacleEnabled)
            {
                Vector3 obstaclePosition = Vector3.Lerp(sourcePosition, destinationPosition, 0.55f);
                obstaclePosition.y = 0f;
                scenario.dynamicObjects.Add(new DynamicObjectSpawn
                {
                    actorId = "C-PALLET",
                    jobName = "Scenario-specific pallet obstruction",
                    type = DynamicObjectType.MovableObstacle,
                    startPosition = obstaclePosition,
                    targetPosition = obstaclePosition,
                    scale = new Vector3(1.2f, 1f, 1f),
                    speed = 0f,
                    pattern = MovementPattern.Static,
                    activationDelay = 3f
                });
            }
            return scenario;
        }

        private void AddCustomActors(ScenarioDefinition scenario, int humanCount, int forkliftCount)
        {
            float length = warehouseManager.config.warehouseLength;
            float width = warehouseManager.config.warehouseWidth;
            Vector3[] humanStarts =
            {
                new Vector3(-length * .14f, 0f, -width * .30f), new Vector3(length * .07f, 0f, width * .30f),
                new Vector3(-length * .28f, 0f, width * .20f), new Vector3(length * .28f, 0f, -width * .15f),
                new Vector3(-length * .08f, 0f, -width * .05f)
            };
            Vector3[] humanTargets =
            {
                new Vector3(-length * .14f, 0f, width * .30f), new Vector3(length * .07f, 0f, -width * .25f),
                new Vector3(length * .28f, 0f, width * .20f), new Vector3(-length * .28f, 0f, -width * .15f),
                new Vector3(length * .20f, 0f, -width * .05f)
            };
            string[] jobs =
            {
                "Rack picking and parcel hand-off", "Cross-aisle stock transfer", "Station quality check",
                "Trolley replenishment route", "Maintenance and inventory inspection"
            };
            for (int i = 0; i < Mathf.Clamp(humanCount, 1, 5); i++)
            {
                scenario.dynamicObjects.Add(new DynamicObjectSpawn
                {
                    actorId = $"H{i + 1}", jobName = jobs[i], type = DynamicObjectType.Human,
                    startPosition = humanStarts[i], targetPosition = humanTargets[i], speed = 0.75f + i * 0.05f,
                    pattern = MovementPattern.LinearPatrol
                });
            }

            Vector3[] forkliftStarts =
            {
                new Vector3(-length * .36f, 0f, width * .22f),
                new Vector3(length * .36f, 0f, -width * .22f)
            };
            Vector3[] forkliftTargets =
            {
                new Vector3(-length * .36f, 0f, -width * .22f),
                new Vector3(length * .36f, 0f, width * .22f)
            };
            string[] forkliftJobs = { "Inbound pallet replenishment", "Outbound pallet transport" };
            for (int i = 0; i < Mathf.Clamp(forkliftCount, 0, 2); i++)
            {
                scenario.dynamicObjects.Add(new DynamicObjectSpawn
                {
                    actorId = $"F{i + 1}", jobName = forkliftJobs[i], type = DynamicObjectType.Forklift,
                    startPosition = forkliftStarts[i], targetPosition = forkliftTargets[i], speed = 1.1f + i * .1f,
                    pattern = MovementPattern.LinearPatrol
                });
            }
        }

        private Vector3 GetConfiguredStation(bool pickup, int index)
        {
            List<Vector3> stations = pickup
                ? warehouseManager.config.pickupStationPositions
                : warehouseManager.config.dropStationPositions;
            return stations != null && index >= 0 && index < stations.Count ? stations[index] : Vector3.zero;
        }

        private Vector3 GetRackApproachPosition(string category, Vector3 fallback)
        {
            WarehouseInventory inventory = warehouseManager.Inventory;
            if (inventory == null) return fallback;
            RackSlot slot = inventory.Slots.FirstOrDefault(candidate =>
                string.Equals(candidate.category, category, StringComparison.OrdinalIgnoreCase));
            return slot != null ? slot.approachPosition : fallback;
        }

        private static bool IsStationId(string value, char prefix) => value != null && value.Length == 2 &&
            char.ToUpperInvariant(value[0]) == prefix && value[1] >= '1' && value[1] <= '3';

        private static int ParseStationIndex(string value) =>
            value != null && value.Length > 1 && char.IsDigit(value[1]) ? value[1] - '1' : 0;

        private static bool IsSupportedDeliveryMode(string value) =>
            string.Equals(value, "Station to station", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "Station to rack", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "Rack to station", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "Rack to rack", StringComparison.OrdinalIgnoreCase);

        // ---------------- Episode execution ----------------

        private IEnumerator RunAllCoroutine(bool randomOrder)
        {
            int count = Mathf.Min(_researchScenarioCount, Scenarios.Count);
            var runList = Scenarios.GetRange(0, count);
            if (randomOrder)
            {
                for (int i = runList.Count - 1; i > 0; i--)
                {
                    int swapIndex = UnityEngine.Random.Range(0, i + 1);
                    (runList[i], runList[swapIndex]) = (runList[swapIndex], runList[i]);
                }
            }

            for (int i = 0; i < runList.Count; i++)
            {
                CurrentScenarioIndex = Scenarios.IndexOf(runList[i]);
                OnStatusMessage?.Invoke($"Run All: {i + 1}/{runList.Count} — Scenario {runList[i].scenarioId:D2}: {runList[i].scenarioName}");
                Debug.Log($"ATADTRL: RUN ALL progress {i + 1}/{runList.Count}; starting Scenario {runList[i].scenarioId:D2}.");
                yield return RunEpisodeGuarded(runList[i], isPartOfRunAll: true, benchmarkEpisode: i + 1, benchmarkTotal: runList.Count);
                if (_episodeFaulted) { _episodeCoroutine = null; yield break; }
            }
            OnStatusMessage?.Invoke("ALL SCENARIOS COMPLETED");
            Debug.Log($"ATADTRL: RUN ALL complete. Episode outcome report: {Path.Combine(_datasetRoot, "Episode_Outcome_Report.csv")}");
            OnAllScenariosCompleted?.Invoke();
            CurrentRunMode = RunMode.None;
            _episodeCoroutine = null;
        }

        private bool _episodeFaulted;

        private void ResumeSimulationForExplicitStart()
        {
            _episodeFaulted = false;
            if (Time.timeScale <= 0f) Time.timeScale = 1f;
#if UNITY_EDITOR
            // Start/Run All explicitly resume a previously paused Editor.
            // Do not keep unpausing while running: new errors remain visible.
            if (UnityEditor.EditorApplication.isPaused) UnityEditor.EditorApplication.isPaused = false;
#endif
        }

        private IEnumerator RunEpisodeGuarded(ScenarioDefinition scenario, bool isPartOfRunAll, int benchmarkEpisode, int benchmarkTotal)
        {
            IEnumerator episode = RunEpisodeCoroutine(scenario, isPartOfRunAll, benchmarkEpisode, benchmarkTotal);
            while (true)
            {
                bool next = false;
                object yielded = null;
                try { next = episode.MoveNext(); if (next) yielded = episode.Current; }
                catch (Exception error)
                {
                    _episodeFaulted = true;
                    IsRunning = false;
                    CurrentRunMode = RunMode.None;
                    navigationManager?.ResetNavigation();
                    _gtLogger?.EndScenario(); _obsLogger?.EndScenario();
                    CleanupEpisodeObjects();
                    OnStatusMessage?.Invoke($"RUN ERROR: {error.GetType().Name}: {error.Message}");
                    Debug.LogError($"ATADTRL {scenario.scenarioCode} stopped because of a runtime error. Run All cancelled; see the exception below.");
                    Debug.LogException(error);
                }
                if (!next || _episodeFaulted) yield break;
                yield return yielded;
            }
        }

        private IEnumerator RunEpisodeCoroutine(ScenarioDefinition scenario, bool isPartOfRunAll, int benchmarkEpisode, int benchmarkTotal)
        {
            IsRunning = true;
            _activeBenchmarkEpisode = benchmarkEpisode;
            _activeBenchmarkEpisodeTotal = benchmarkTotal;
            OnStatusMessage?.Invoke($"Episode {benchmarkEpisode:D2}/{benchmarkTotal:D2}: loading Scenario {scenario.scenarioId:D2}: {scenario.scenarioName}");

            ResetWorldForEpisode();
            SetupScenario(scenario);

            _episodeCounter++;
            File.WriteAllText(Path.Combine(_datasetRoot, "Episode_Sequence.txt"), _episodeCounter.ToString());
            _stepCounter = 0;
            _episodeTime = 0f;
            _episodeStartRealTime = Time.time;
            _sumVelocity = 0f;
            _velocitySamples = 0;
            _maxVelocity = 0f;
            _collisionCount = 0;
            EnsureA5SourceStock(scenario);
            BeginDeliveryTask(scenario);

            _gtLogger.BeginScenario(scenario.scenarioId);
            _obsLogger.BeginScenario(scenario.scenarioId);
            _eventLogger = new CSVLogger();
            _eventLogger.Open(Path.Combine(_datasetRoot, $"Scenario_{scenario.scenarioId:D2}", "Operational_Events.csv"),
                "timestamp,episode_id,scenario_code,phase,detail");
            RecordOperationalEvent("EPISODE_SETUP", $"Seed={experimentSeed+scenario.scenarioId}; orders={GetDeliveryTaskCount(scenario)}; Module1 conventional baseline");
            if (!_obsLogger.IsScenarioOpen || !_gtLogger.IsScenarioOpen)
            {
                Debug.LogError($"ATADTRL CSV: Scenario {scenario.scenarioId:D2} could not open one or more dataset files. Close them in Excel, then run again.");
            }
            else
            {
                Debug.Log($"ATADTRL CSV: Scenario {scenario.scenarioId:D2} writing observations to '{_obsLogger.CurrentScenarioFilePath}' and ground truth to '{_gtLogger.CurrentScenarioFilePath}'.");
            }

            Vector3 initialGoal = _deliveryStage == DeliveryStage.NavigateToCharge
                ? warehouseManager.config.chargingStationPosition
                : GetPickupPoint(scenario, 0);
            navigationManager.SetGoal(initialGoal);
            OnScenarioStarted?.Invoke(scenario);
            OnStatusMessage?.Invoke(_deliveryStage == DeliveryStage.NavigateToCharge
                ? $"Episode {benchmarkEpisode:D2}/{benchmarkTotal:D2}: Scenario {scenario.scenarioId:D2} low-battery protocol; moving to charging station."
                : $"Episode {benchmarkEpisode:D2}/{benchmarkTotal:D2}: Scenario {scenario.scenarioId:D2} moving to pickup 1 of {GetDeliveryTaskCount(scenario)}.");

            Debug.Log($"ATADTRL: Episode started — scenario {scenario.scenarioId}, robot at " +
                      $"{robotController.transform.position}, first task goal {initialGoal}, " +
                      $"nav status = {navigationManager.Status}, timeScale={Time.timeScale:F2}");

            float loggingTimer = 0f;
            float diagnosticTimer = 0f;
            bool prevCollision = false;
            // Enforce the global benchmark cap even if an old scene or a
            // hand-authored ScenarioDefinition contains a larger value.
            float episodeTimeLimit = Mathf.Min(
                Mathf.Max(1f, scenario.maxEpisodeDuration),
                MaximumEpisodeDurationSeconds);

            while (_episodeTime < episodeTimeLimit && _deliveryStage != DeliveryStage.Complete && string.IsNullOrEmpty(_terminalOutcome))
            {
                float dt = Time.deltaTime;
                _episodeTime += dt;
                loggingTimer += dt;
                diagnosticTimer += dt;

                if (diagnosticTimer >= 1f)
                {
                    diagnosticTimer = 0f;
                    var agent = robotController.GetComponent<NavMeshAgent>();
                    Debug.Log($"ATADTRL: t={_episodeTime:F1}s status={navigationManager.Status} " +
                              $"robotPos={robotController.transform.position} " +
                              $"linVel={robotController.LinearVelocity:F3} " +
                              $"agent.enabled={(agent != null ? agent.enabled.ToString() : "null")} " +
                              $"agent.isOnNavMesh={(agent != null ? agent.isOnNavMesh.ToString() : "null")} " +
                              $"agent.velocity={(agent != null ? agent.velocity.magnitude.ToString("F3") : "null")} " +
                              $"agent.remainingDistance={(agent != null && agent.hasPath ? agent.remainingDistance.ToString("F2") : "no path")} " +
                              $"agent.pathStatus={(agent != null ? agent.pathStatus.ToString() : "null")} " +
                              $"agent.isStopped={(agent != null ? agent.isStopped.ToString() : "null")}");
                }

                if (robotController.IsColliding && !prevCollision) _collisionCount++;
                prevCollision = robotController.IsColliding;

                float v = robotController.LinearVelocity;
                _sumVelocity += v;
                _velocitySamples++;
                if (v > _maxVelocity) _maxVelocity = v;

                if (loggingTimer >= loggingIntervalSeconds)
                {
                    loggingTimer = 0f;
                    LogStep(scenario);
                }

                if (navigationManager.Status == NavigationStatus.Collided && scenario.category != ScenarioCategory.StressTest)
                {
                    // Non-fatal: keep running until timeout or goal; collision is recorded, not episode-ending,
                    // matching the requirement that baseline navigation reacts and can continue.
                }

                _batteryLevel = Mathf.Max(0f, _batteryLevel - dt * (_carryingParcel ? 0.045f : 0.02f));
                _disturbance?.Tick(_episodeTime, _carryingParcel, _deliveryStage.ToString(),
                    GetDestinationId(scenario, Mathf.Min(_deliveryTaskIndex, GetDeliveryTaskCount(scenario) - 1)),
                    GetDropOffPoint(scenario, Mathf.Min(_deliveryTaskIndex, GetDeliveryTaskCount(scenario) - 1)));
                DetectTerminalCollision();
                UpdateDeliveryTask(scenario, dt);
                UpdateScenarioDiagnostics(scenario, dt);

                yield return null;
            }

            if (_deliveryStage != DeliveryStage.Complete &&
                string.IsNullOrEmpty(_terminalOutcome) &&
                _episodeTime >= episodeTimeLimit)
            {
                _episodeTime = episodeTimeLimit;
                _terminalOutcome = "TIMEOUT";
                Debug.Log($"ATADTRL: Scenario {scenario.scenarioId:D2} reached the {episodeTimeLimit:F0}-second episode limit.");
            }

            // Final log sample at episode end.
            LogStep(scenario);
            RecordOperationalEvent(_disturbance != null && _disturbance.Triggered ? "EXPOSURE_RECORDED" : "NO_DISTURBANCE_EXPOSURE",
                "Episode ended; use exposure flag when comparing policies. " + (_terminalOutcome ?? _deliveryStage.ToString()));

            // Complete means the state machine has ended; it does not itself
            // mean the mission succeeded.  Collision and emergency paths end
            // in Complete too, but must remain visible as distinct outcomes.
            bool success = _deliveryStage == DeliveryStage.Complete && string.IsNullOrEmpty(_terminalOutcome);
            var perf = new PerformanceRecord
            {
                scenarioId = scenario.scenarioId,
                episodeId = _episodeCounter,
                episodeDuration = _episodeTime,
                pathLength = navigationManager.PathLength,
                navigationTime = _episodeTime,
                collisionCount = _collisionCount,
                goalSuccess = success,
                finalDistanceToGoal = Vector3.Distance(robotController.transform.position,
                    GetDropOffPoint(scenario, GetDeliveryTaskCount(scenario) - 1)),
                averageVelocity = _velocitySamples > 0 ? _sumVelocity / _velocitySamples : 0f,
                maxVelocity = _maxVelocity,
                numberOfStops = navigationManager.StopCount,
                replanningEvents = navigationManager.ReplanningEvents,
                completionStatus = success ? "SUCCESS" : (_terminalOutcome ?? "TIMEOUT")
            };
            _perfLogger.LogEpisode(perf);
            _outcomeLogger.Log(new EpisodeOutcomeRecord
            {
                benchmarkEpisodeNumber = benchmarkEpisode,
                benchmarkEpisodeTotal = benchmarkTotal,
                globalEpisodeId = _episodeCounter,
                scenarioId = scenario.scenarioId,
                scenarioCode = scenario.scenarioCode,
                scenarioName = scenario.scenarioName,
                outcome = perf.completionStatus,
                durationSeconds = _episodeTime,
                missionSuccess = perf.goalSuccess,
                collisions = _collisionCount,
                completedParcelTasks = _deliveryTaskIndex,
                totalParcelTasks = GetDeliveryTaskCount(scenario),
                batteryLevel = _batteryLevel,
                finalTaskStage = _deliveryStage.ToString()
            });

            string folder = Path.Combine(_datasetRoot, $"Scenario_{scenario.scenarioId:D2}");
            _gtLogger.EndScenario();
            _obsLogger.EndScenario();
            LogCsvFileStatus(scenario, folder);

            // Validate this scenario's freshly written files.
            _validator.ValidateScenarioFile(scenario.scenarioId, "GroundTruth_Environment",
                Path.Combine(folder, "GroundTruth_Environment.csv"));
            _validator.ValidateScenarioFile(scenario.scenarioId, "Unified_Observation",
                Path.Combine(folder, "Unified_Observation.csv"));
            _validator.WriteReport();

            // Publish only flushed snapshots. Direct per-frame writes beneath
            // Assets previously caused Unity's "processed bytes" import error.
            DatasetPublisher.PublishCompletedFiles();

            // Every terminal outcome—including timeout—leaves the robot in a
            // stopped navigation state before the next episode can begin.
            navigationManager.ResetNavigation();
            CleanupEpisodeObjects();
            IsRunning = false;

            // A single recorded episode ends, but the simulated warehouse
            // itself remains alive. Restore the normal assigned workforce so
            // Display 1 and the live twin continue until the operator presses
            // Stop. Run All owns the transition to its next episode instead.
            if (!isPartOfRunAll && WarehouseOperationsActive)
                StartAmbientWarehouseOperations();

            OnStatusMessage?.Invoke($"Episode {benchmarkEpisode:D2}/{benchmarkTotal:D2} ended: {perf.completionStatus}. " +
                "Robot stopped; normal warehouse operations and live twin monitoring continue until Stop is pressed.");
            OnScenarioCompleted?.Invoke(scenario, perf);
            if (!isPartOfRunAll)
            {
                CurrentRunMode = RunMode.None;
                _episodeCoroutine = null;
            }
        }

        private void LogStep(ScenarioDefinition scenario)
        {
            double timestamp = _episodeTime;
            _stepCounter++;

            var gtRecord = groundTruthManager.BuildRecord(
                scenario.scenarioId, _episodeCounter, _stepCounter, timestamp,
                scenario.category.ToString(), _activeDynamicObjects);
            _disturbance?.AppendPhysicalGroundTruth(gtRecord, _episodeTime);
            var obsRecord = sensorManager.BuildObservation(scenario.scenarioId, _episodeCounter, _stepCounter, timestamp);
            PopulateTaskObservation(obsRecord, scenario);
            bool[] stations = new bool[6];
            for (int i=0;i<3;i++)
            {
                Vector3 p=warehouseManager.config.pickupStationPositions[i], d=warehouseManager.config.dropStationPositions[i];
                stations[i]=!IsTrafficNear(p,1.5f) && !(_disturbance?.IsStationBusy(p) ?? false);
                stations[i+3]=!IsTrafficNear(d,1.5f) && !(_disturbance?.IsStationBusy(d) ?? false);
            }
            _observationChannel.Capture(_episodeTime, _activeDynamicObjects, stations);
            gtRecord.station_availability = string.Join("|", Enumerable.Range(0, 6).Select(i =>
                $"{(i < 3 ? "P" : "D")}{i % 3 + 1}:{stations[i]}"));
            gtRecord.actor_work_states = string.Join("|", _activeDynamicObjects.Where(a => a != null && a.IsActive)
                .Select(a => $"{a.ObjectId}:{a.JobName}:{a.CurrentWorkStage}:load={a.IsCarryingCargo}:handoffs={a.CompletedWorkTrips}"));
            _gtLogger.LogRecord(gtRecord);
            _observationChannel.Apply(obsRecord, _episodeTime, _disturbance);
            obsRecord.camera_valid=sensorManager.camera != null && !sensorManager.camera.LastReadingDroppedOut;
            obsRecord.lidar_valid=sensorManager.lidar != null && !sensorManager.lidar.LastReadingDroppedOut;
            _obsLogger.LogRecord(obsRecord);

            if (_stepCounter % 100 == 0)
            {
                string status = _obsLogger.IsScenarioOpen && _gtLogger.IsScenarioOpen ? "updated" : "NOT OPEN";
                Debug.Log($"ATADTRL CSV: Scenario {scenario.scenarioId:D2} {status} ({_stepCounter} records) -> {_obsLogger.CurrentScenarioFilePath}");
            }
        }

        private void LogCsvFileStatus(ScenarioDefinition scenario, string folder)
        {
            string observationPath = Path.Combine(folder, "Unified_Observation.csv");
            string groundTruthPath = Path.Combine(folder, "GroundTruth_Environment.csv");
            long observationBytes = File.Exists(observationPath) ? new FileInfo(observationPath).Length : 0L;
            long groundTruthBytes = File.Exists(groundTruthPath) ? new FileInfo(groundTruthPath).Length : 0L;
            Debug.Log($"ATADTRL CSV: Scenario {scenario.scenarioId:D2} completed with {_stepCounter} records. " +
                      $"Observation={observationBytes} bytes; GroundTruth={groundTruthBytes} bytes; folder='{folder}'.");
        }

        // ---------------- World setup/reset ----------------

        private void ResetWorldForEpisode()
        {
            CleanupEpisodeObjects();
            warehouseManager.Inventory?.ResetToInitialLayout();
            navigationManager.SetEncoderLocalizationMode(false);
            navigationManager.WarpRobot(warehouseManager.config.robotStartArea, Quaternion.identity);
            robotController.ResetRobot(warehouseManager.config.robotStartArea, Quaternion.identity);
            navigationManager.ResetNavigation();
            sensorManager.ClearOverrides();

            if (sensorManager.encoder != null)
            {
                sensorManager.encoder.InitializePose(robotController.transform.position, robotController.transform.eulerAngles.y);
            }
            sensorManager.imu?.InitializePose(robotController.transform.position, robotController.transform.eulerAngles.y);
        }

        private void SetupScenario(ScenarioDefinition scenario)
        {
            UnityEngine.Random.InitState(experimentSeed + scenario.scenarioId);
            robotController.GetComponent<BaselineNavigationController>()?.SetPayload(0f);
            var ctx = new ScenarioRuntimeContext
            {
                robotStartPoint = robotController.transform,
                dynamicObjectRoot = warehouseManager.DynamicObjectRoot,
                humanPrefab = humanPrefab,
                forkliftPrefab = forkliftPrefab,
                obstaclePrefab = obstaclePrefab,
                SetStartGoal = (s, g) =>
                {
                    navigationManager.WarpRobot(s, Quaternion.identity);
                    robotController.ResetRobot(s, Quaternion.identity);
                }
            };

            scenario.ApplyTo(ctx);
            // Rebase both dead-reckoning sensors after the scenario-specific
            // warp. Otherwise a changed start position itself looks like an
            // encoder fault before the robot has moved.
            sensorManager.encoder?.InitializePose(robotController.transform.position, robotController.transform.eulerAngles.y);
            sensorManager.imu?.InitializePose(robotController.transform.position, robotController.transform.eulerAngles.y);
            navigationManager.SetEncoderLocalizationMode(false);
            _scenarioDiagnosticTimer = 0f;
            _a1HumanAnnounced = false;
            _a2ObstructionAnnounced = false;
            _a2BlockerParkedAnnounced = false;
            _a2RerouteAttempted = false;
            _a3DropBlockedAnnounced = false;
            _a4RackRelocated = false;
            _a4LoadedTravelTime = 0f;
            _a4ValidPlacementPosition = Vector3.zero;
            _a5DockingAttempts = 0;
            _e2ArrivalTime = -1f;
            _a1FirstDetectionDistance = -1f;
            _a1MinHumanDistance = float.MaxValue;
            _a1NearCollisionFlag = false;
            _a1LosWasClear = false;
            _a1PrevRobotPos = null;
            _a1CollisionSummaryLogged = false;

            foreach (var go in ctx.SpawnedObjects)
            {
                var mover = go.GetComponent<DynamicObjectMover>();
                if (mover != null) _activeDynamicObjects.Add(mover);
            }

            if (scenario.overrideSensorNoise)
            {
                sensorManager.ApplyScenarioOverrides(new ScenarioOverride
                {
                    lidarNoiseMultiplier = scenario.lidarNoiseMultiplier,
                    imuNoiseMultiplier = scenario.imuNoiseMultiplier,
                    encoderNoiseMultiplier = scenario.encoderNoiseMultiplier,
                    forceLidarDropout = scenario.forceLidarDropout,
                    forceCameraDropout = scenario.forceCameraDropout,
                    forceEncoderDropout = scenario.forceEncoderDropout,
                    conflictingObservations = scenario.conflictingObservations
                });
            }

            if (scenario.blockAisle && scenario.blockedAisleIndex >= 0)
            {
                Bounds b = warehouseManager.GetAisleBounds(scenario.blockedAisleIndex);
                GameObject blockGO = aisleBlockagePrefab != null
                    ? Instantiate(aisleBlockagePrefab)
                    : new GameObject("AisleBlockage");
                blockGO.transform.SetParent(warehouseManager.DynamicObjectRoot, false);

                var blockage = blockGO.GetComponent<AisleBlockage>();
                if (blockage == null) blockage = blockGO.AddComponent<AisleBlockage>();
                blockage.Setup(b);
                blockage.Schedule(Time.time, scenario.blockageStartTime, scenario.blockageEndTime);
                _activeBlockages.Add(blockage);
            }
            _observationChannel.Clear();
            _disturbance = new ScenarioDisturbanceRuntime();
            _disturbance.Initialize(scenario, robotController.transform, warehouseManager, sensorManager, _activeDynamicObjects);
            _disturbance.EventChanged += RecordOperationalEvent;
        }

        private void RecordOperationalEvent(string phase, string detail)
        {
            string code = Scenarios.Count > CurrentScenarioIndex ? Scenarios[CurrentScenarioIndex].scenarioCode : "";
            string quote = "\"";
            _eventLogger?.WriteRow(string.Join(",", _episodeTime.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
                _episodeCounter, code, phase, quote + (detail ?? "").Replace(quote, quote + quote) + quote));
        }

        private void CleanupEpisodeObjects()
        {
            _disturbance?.Restore();
            _disturbance = null;
            _eventLogger?.Close();
            _eventLogger = null;
            RestoreA4RackPosition();
            CleanupDeliveryVisuals();
            foreach (var obj in _activeDynamicObjects)
            {
                if (obj != null) Destroy(obj.gameObject);
            }
            _activeDynamicObjects.Clear();

            foreach (var b in _activeBlockages)
            {
                if (b != null) Destroy(b.gameObject);
            }
            _activeBlockages.Clear();

        }

        private void StartAmbientWarehouseOperations()
        {
            if (!WarehouseOperationsActive || !IsInitialized || _ambientDynamicObjects.Count > 0)
                return;

            // These are ordinary, non-fault work assignments. They make the
            // warehouse visibly operational while no experiment is recording.
            // A1 replaces them with its controlled actor routes for the
            // episode, then this population is restored after completion.
            SpawnAmbientActor("H1", "Rack picker: Books replenishment", DynamicObjectType.Human,
                new Vector3(-8.4f, 0f, -9f), new Vector3(-8.4f, 0f, 9f), .72f);
            SpawnAmbientActor("H2", "Cross-aisle carton transfer", DynamicObjectType.Human,
                new Vector3(-4.2f, 0f, -13.5f), new Vector3(-4.2f, 0f, 13.5f), .78f);
            SpawnAmbientActor("H3", "Dispatch station order verification", DynamicObjectType.Human,
                new Vector3(0f, 0f, -9f), new Vector3(0f, 0f, 9f), .68f);
            SpawnAmbientActor("H4", "Trolley transfer between PD stations", DynamicObjectType.Human,
                new Vector3(4.2f, 0f, -9f), new Vector3(4.2f, 0f, 9f), .74f);
            SpawnAmbientActor("H5", "Rack maintenance and cycle counting", DynamicObjectType.Human,
                new Vector3(8.4f, 0f, -9f), new Vector3(8.4f, 0f, 9f), .62f);
            SpawnAmbientActor("F1", "Inbound pallet replenishment", DynamicObjectType.Forklift,
                new Vector3(-20f, 0f, -8f), new Vector3(-20f, 0f, 8f), 1.05f);
            SpawnAmbientActor("F2", "Outbound pallet transport", DynamicObjectType.Forklift,
                new Vector3(20f, 0f, 8f), new Vector3(20f, 0f, -8f), 1.05f);

            OnStatusMessage?.Invoke("Warehouse live: normal H1–H5 and F1–F2 work assignments are running. CSV logging is off.");
            Debug.Log("ATADTRL WAREHOUSE: ambient operations running; H1-H5 and F1-F2 are active, CSV logging OFF.");
        }

        private void SpawnAmbientActor(string actorId, string jobName, DynamicObjectType type,
            Vector3 start, Vector3 target, float speed)
        {
            GameObject prefab = type == DynamicObjectType.Human ? humanPrefab : forkliftPrefab;
            if (prefab == null) return;
            GameObject actor = Instantiate(prefab, start, Quaternion.identity, warehouseManager.DynamicObjectRoot);
            actor.name = "Ambient_" + actorId;
            DynamicObjectMover mover = actor.GetComponent<DynamicObjectMover>();
            if (mover == null) mover = actor.AddComponent<DynamicObjectMover>();
            mover.Initialize(new DynamicObjectSpawn
            {
                actorId = actorId,
                jobName = jobName,
                type = type,
                startPosition = start,
                targetPosition = target,
                speed = speed,
                pattern = MovementPattern.LinearPatrol
            });
            _ambientDynamicObjects.Add(mover);
        }

        private void StopAmbientWarehouseOperations()
        {
            foreach (DynamicObjectMover actor in _ambientDynamicObjects)
                if (actor != null) Destroy(actor.gameObject);
            _ambientDynamicObjects.Clear();
        }

        private void BeginDeliveryTask(ScenarioDefinition scenario)
        {
            _manipulator=robotController.GetComponent<MobileManipulator>();
            if (_manipulator==null) _manipulator=robotController.gameObject.AddComponent<MobileManipulator>();
            _manipulator.Initialize(); _parcelOnTool=false;
            _deliveryStage = scenario.requiresCharging ? DeliveryStage.NavigateToCharge : DeliveryStage.NavigateToSource;
            _deliveryTaskIndex = 0;
            _taskStageTimer = 0f;
            _carryingParcel = false;
            _graspVerified = false;
            _graspRetryPerformed = false;
            _deliveryRouteWaypointIndex = 0;
            _batteryLevel = scenario.requiresCharging ? 18f : 100f;
            _currentAction = RobotTaskAction.Forward;
            _terminalOutcome = null;
            _emergencyTriggered = false;
            _urgentOrderChanged = false;
            _reservedRackSlot = null;
            _currentParcelRecord = null;
            _currentBarcode = null;
            _currentCategory = null;
            int taskCount = GetDeliveryTaskCount(scenario);

            for (int i = 0; i < taskCount; i++)
            {
                bool pickupFromRack = IsPickupFromRack(scenario, i);
                bool dropAtRack = IsDeliveryToRack(scenario, i);
                _pickupMarkers.Add(CreateTaskMarker(
                    pickupFromRack ? $"RackPickupPoint_{i + 1}" : $"StationPickupPoint_{i + 1}",
                    GetPickupPoint(scenario, i), pickupFromRack ? new Color(0.62f, 0.28f, 0.92f) : new Color(0.10f, 0.60f, 1f)));
                _dropOffMarkers.Add(CreateTaskMarker(
                    dropAtRack ? $"RackDropPoint_{i + 1}" : $"DispatchDropPoint_{i + 1}",
                    GetDropOffPoint(scenario, i), dropAtRack ? new Color(0.20f, 0.95f, 0.35f) : new Color(1f, 0.65f, 0.05f)));
            }

            CreateParcelAtPickup(scenario, _deliveryTaskIndex);
        }

        // The Module 1 baseline follows a fixed task sequence, but every
        // material-handling state is explicit and recorded in the dataset.
        private void UpdateDeliveryTask(ScenarioDefinition scenario, float deltaTime)
        {
            // Scenario 06 changes the operational priority while the robot is
            // in motion. Module 1 logs the new context but deliberately
            // continues its fixed route, exposing the baseline limitation.
            if (scenario.urgentOrderChange && !_urgentOrderChanged && _episodeTime >= 5f)
            {
                _urgentOrderChanged = true;
                OnStatusMessage?.Invoke("Urgent order update: Parcel A is redirected to PD3; Module 1 retains its predefined route.");
            }

            if (scenario.emergencyEvacuation && !_emergencyTriggered && _episodeTime >= 5f)
            {
                _emergencyTriggered = true;
                if (_parcel != null && _carryingParcel)
                {
                    _parcel.transform.SetParent(null, true);
                    _parcel.transform.position = robotController.transform.position + Vector3.up * 0.28f;
                    var collider = _parcel.GetComponent<Collider>();
                    if (collider != null) collider.enabled = true;
                }
                _carryingParcel = false;
                navigationManager.SetGoal(warehouseManager.config.safetyZonePosition);
                SetTaskStage(DeliveryStage.EmergencyToSafety, RobotTaskAction.Wait, "Emergency alarm: abandoning task and moving to the safety zone.");
                return;
            }

            if (_deliveryStage == DeliveryStage.NavigateToCharge)
            {
                if (ReachedTaskPoint(warehouseManager.config.chargingStationPosition))
                    SetTaskStage(DeliveryStage.Charging, RobotTaskAction.Wait, "Robot docked at charging station; charging battery.");
                return;
            }

            if (_deliveryStage == DeliveryStage.EmergencyToSafety)
            {
                if (ReachedTaskPoint(warehouseManager.config.safetyZonePosition))
                {
                    _terminalOutcome = "EMERGENCY_SAFE";
                    SetTaskStage(DeliveryStage.Complete, RobotTaskAction.Wait, "Emergency safety zone reached; scenario ended safely.");
                }
                return;
            }

            if (_deliveryStage == DeliveryStage.NavigateToSource)
            {
                if (ReachedTaskPoint(GetPickupPoint(scenario, _deliveryTaskIndex)))
                    SetTaskStage(DeliveryStage.AlignAtSource, RobotTaskAction.Wait, $"Aligned at {GetSourceId(scenario, _deliveryTaskIndex)}; preparing pick.");
                return;
            }

            if (_deliveryStage == DeliveryStage.NavigateToDestination)
            {
                if (FollowFirstTaskDeliveryRoute(scenario)) return;
                Vector3 destination = GetDropOffPoint(scenario, _deliveryTaskIndex);
                if (ReachedTaskPoint(destination) &&
                    !IsTrafficNear(destination, 1.25f) && !(_disturbance?.IsStationBusy(destination) ?? false))
                    SetTaskStage(DeliveryStage.Place, RobotTaskAction.Place, $"At {GetDestinationId(scenario, _deliveryTaskIndex)}; placing parcel.");
                return;
            }

            _taskStageTimer += deltaTime;
            switch (_deliveryStage)
            {
                case DeliveryStage.Charging:
                    if (_taskStageTimer >= 2f)
                    {
                        _batteryLevel = 100f;
                        navigationManager.SetGoal(GetPickupPoint(scenario, _deliveryTaskIndex));
                        SetTaskStage(DeliveryStage.NavigateToSource, RobotTaskAction.Forward, "Charging complete: moving to first parcel source.");
                    }
                    break;
                case DeliveryStage.AlignAtSource:
                    AlignRobotToCurrentSource(scenario);
                    if (scenario.scenarioCode == "E2" && _e2ArrivalTime < 0f) _e2ArrivalTime = _episodeTime;
                    if (_taskStageTimer >= 0.45f)
                    {
                        if (scenario.scenarioCode == "E2" && _episodeTime - _e2ArrivalTime < scenario.parcelReadyDelaySeconds)
                        {
                            if (_taskStageTimer >= 0.45f && Mathf.Repeat(_taskStageTimer, 2f) < deltaTime)
                                OnStatusMessage?.Invoke($"E2 WAITING: ParcelReady = 0 at {GetSourceId(scenario, _deliveryTaskIndex)}; holding for human picking process.");
                            break;
                        }
                        if (scenario.scenarioCode == "A5" && !EvaluateA5Docking(scenario))
                        {
                            _taskStageTimer = 0f;
                            break;
                        }
                        string scanStatus = IsDeliveryToRack(scenario, _deliveryTaskIndex)
                            ? "Reading parcel barcode and checking the category rack for an empty slot."
                            : $"Reading parcel barcode and validating station destination {GetDestinationId(scenario, _deliveryTaskIndex)}.";
                        SetTaskStage(DeliveryStage.ScanBarcode, RobotTaskAction.Wait, scanStatus);
                    }
                    break;
                case DeliveryStage.ScanBarcode:
                    if (_taskStageTimer < 0.30f) break;
                    if (scenario.scenarioCode == "E3" && scenario.simulateBarcodeMismatch)
                    {
                        _terminalOutcome = "BARCODE_MISMATCH";
                        navigationManager.ResetNavigation();
                        SetTaskStage(DeliveryStage.Complete, RobotTaskAction.Wait,
                            $"BARCODE MISMATCH \u2014 ASSIGNED PARCEL NOT PRESENT (expected {scenario.parcelBarcodes[0]}, observed {scenario.mismatchedBarcodeObserved}).");
                        break;
                    }
                    bool rackDelivery = IsDeliveryToRack(scenario, _deliveryTaskIndex);
                    if (rackDelivery && _reservedRackSlot == null)
                    {
                        _terminalOutcome = "NO_EMPTY_CATEGORY_SLOT";
                        navigationManager.ResetNavigation();
                        SetTaskStage(DeliveryStage.Complete, RobotTaskAction.Wait, $"Barcode {_currentBarcode} read, but no empty {_currentCategory} rack segment is available.");
                        break;
                    }
                    string verifiedDestination = rackDelivery
                        ? _reservedRackSlot.slotId
                        : GetDestinationId(scenario, _deliveryTaskIndex);
                    SetTaskStage(DeliveryStage.Pick, RobotTaskAction.Pick,
                        $"Barcode {_currentBarcode} verified: {_currentCategory} -> {verifiedDestination}. Pick action started.");
                    break;
                case DeliveryStage.Pick:
                    if (_parcel != null && scenario.parcelMass > _manipulator.MaximumPayloadKg)
                    { _terminalOutcome="PAYLOAD_EXCEEDS_LIMIT"; break; }
                    if (_parcel != null && _manipulator.MoveTool(_parcel.transform.position + Vector3.up * 0.25f, true, deltaTime))
                    {
                        AttachCurrentParcelToRobot();
                        SetTaskStage(DeliveryStage.VerifyGrasp, RobotTaskAction.Wait, "Verifying parcel grasp.");
                    }
                    break;
                case DeliveryStage.VerifyGrasp:
                    if (_taskStageTimer < 0.45f) break;
                    if (_parcel == null || _parcel.transform.parent != _manipulator.Tool || !_manipulator.GripperClosed)
                    { _terminalOutcome="GRASP_NOT_CONFIRMED"; break; }
                    if (scenario.simulateGraspDifficulty && !_graspRetryPerformed)
                    {
                        _graspRetryPerformed = true;
                        DetachParcelAtSource(scenario);
                        SetTaskStage(DeliveryStage.AlignAtSource, RobotTaskAction.Wait, "Grasp verification failed; realigning for one baseline retry.");
                        break;
                    }
                    _graspVerified = true;
                    _carryingParcel = true;
                    if (_sourceRackSlot!=null && _sourceRackSlot.HasParcel &&
                        !warehouseManager.Inventory.TryRemoveParcel(_sourceRackSlot.slotId,out _))
                    { _terminalOutcome="SOURCE_INVENTORY_MISMATCH"; break; }
                    SetTaskStage(DeliveryStage.SecureForTransport, RobotTaskAction.Wait, "Grasp verified; retracting parcel to rear transport deck.");
                    break;
                case DeliveryStage.SecureForTransport:
                    if (_manipulator.MoveTool(_manipulator.StowPoint, true, deltaTime))
                    {
                        StowCurrentParcelOnRearDeck();
                        robotController.GetComponent<BaselineNavigationController>()?.SetPayload(scenario.parcelMass);
                        _manipulator.OpenGripper();
                        BeginDestinationNavigation(scenario);
                        SetTaskStage(DeliveryStage.NavigateToDestination, RobotTaskAction.Forward, $"Load secured: transporting to {GetDestinationId(scenario, _deliveryTaskIndex)}.");
                    }
                    break;
                case DeliveryStage.Place:
                    if (!_parcelOnTool)
                    {
                        if (_parcel != null && _manipulator.MoveTool(_parcel.transform.position + Vector3.up * 0.25f, true, deltaTime))
                            AttachCurrentParcelToRobot();
                        break;
                    }
                    Vector3 placement=GetPlacementPosition(scenario);
                    if (_manipulator.MoveTool(placement + Vector3.up * 0.25f, true, deltaTime))
                    {
                        PlaceCurrentParcel(scenario);
                        if (_terminalOutcome == "WRONG_PLACEMENT")
                            SetTaskStage(DeliveryStage.Complete, RobotTaskAction.Wait, "Wrong Placement: parcel was deposited at the stale rack position; scenario ended.");
                        else
                            SetTaskStage(DeliveryStage.VerifyRelease, RobotTaskAction.Wait, "Verifying parcel release.");
                    }
                    break;
                case DeliveryStage.VerifyRelease:
                    if (_taskStageTimer < 0.45f) break;
                    if (_manipulator.GripperClosed) { _terminalOutcome="RELEASE_NOT_CONFIRMED"; break; }
                    bool rackRelease = IsDeliveryToRack(scenario, _deliveryTaskIndex);
                    bool deliveryPresent = rackRelease
                        ? _reservedRackSlot != null && _reservedRackSlot.HasParcel &&
                          _reservedRackSlot.parcel?.barcode == _currentBarcode
                        : _deliveredParcels.Any(p => p != null && p.transform.parent == null &&
                          Vector3.Distance(p.transform.position, _releasePosition) < 0.05f);
                    if (!deliveryPresent || Vector3.Distance(_releasePosition, GetPlacementPosition(scenario)) > 0.05f)
                    { _terminalOutcome="RELEASE_OUTSIDE_DESTINATION"; break; }
                    _carryingParcel = false;
                    _deliveryTaskIndex++;
                    if (_deliveryTaskIndex >= GetDeliveryTaskCount(scenario))
                    {
                        SetTaskStage(DeliveryStage.Complete, RobotTaskAction.Wait, "Mission complete: all parcels have been picked, verified, placed, and released.");
                        break;
                    }
                    _graspVerified = false;
                    _deliveryRouteWaypointIndex = 0;
                    _reservedRackSlot = null;
                    CreateParcelAtPickup(scenario, _deliveryTaskIndex);
                    navigationManager.SetGoal(GetPickupPoint(scenario, _deliveryTaskIndex));
                    SetTaskStage(DeliveryStage.NavigateToSource, RobotTaskAction.Forward, $"Moving to {GetSourceId(scenario, _deliveryTaskIndex)} for task {_deliveryTaskIndex + 1} of {GetDeliveryTaskCount(scenario)}.");
                    break;
            }
            if (_taskStageTimer > 20f && (_deliveryStage == DeliveryStage.Pick || _deliveryStage == DeliveryStage.Place || _deliveryStage == DeliveryStage.SecureForTransport))
            {
                _terminalOutcome="MANIPULATOR_REACH_OR_TRANSFER_FAILED";
                navigationManager.HoldAtTaskPoint();
                RecordOperationalEvent("TRANSFER_FAILED", "Tool could not complete the bounded transfer; parcel was not credited as delivered.");
            }
        }

        private void SetTaskStage(DeliveryStage stage, RobotTaskAction action, string status)
        {
            RecordOperationalEvent(stage.ToString(), status);
            _deliveryStage = stage;
            _currentAction = action;
            _taskStageTimer = 0f;
            if (stage != DeliveryStage.NavigateToSource && stage != DeliveryStage.NavigateToDestination && stage != DeliveryStage.NavigateToCharge && stage != DeliveryStage.EmergencyToSafety)
                navigationManager.HoldAtTaskPoint();
            if (stage == DeliveryStage.Pick || stage == DeliveryStage.Place) SetArmExtension(0.55f);
            else if (stage != DeliveryStage.VerifyGrasp && stage != DeliveryStage.VerifyRelease) SetArmExtension(0.08f);
            OnStatusMessage?.Invoke($"Episode {_activeBenchmarkEpisode:D2}/{_activeBenchmarkEpisodeTotal:D2}: {status}");
        }

        private void UpdateScenarioDiagnostics(ScenarioDefinition scenario, float deltaTime)
        {
            if (scenario.scenarioCode == "E2" && _parcel != null)
            {
                // The parcel object is created at mission start like every
                // other scenario, but E2 requires it not be treated as
                // present/available until human staging finishes. Keeping
                // it physically hidden/uncollidable until then makes
                // ParcelReady = 0 visually real, not just a logic flag.
                bool parcelReady = _e2ArrivalTime >= 0f &&
                    _episodeTime - _e2ArrivalTime >= scenario.parcelReadyDelaySeconds;
                Renderer parcelRenderer = _parcel.GetComponent<Renderer>();
                Collider parcelCollider = _parcel.GetComponent<Collider>();
                if (parcelRenderer != null) parcelRenderer.enabled = parcelReady;
                if (parcelCollider != null) parcelCollider.enabled = parcelReady;
            }

            if (scenario.scenarioCode == "A1")
            {
                UpdateA1DetectionAndStopping(scenario, deltaTime);

                // Observation only: A1 never moves, teleports, redirects or
                // forces H1 here. Robot and H1 follow the independent work
                // routes declared in ScenarioDefinition from episode start.
                if (!_a1HumanAnnounced &&
                    _carryingParcel && _deliveryStage == DeliveryStage.NavigateToDestination)
                {
                    _a1HumanAnnounced = true;
                    string message = "A1: P1 parcel picked for D2. Robot is carrying it through the aisle beside the Books rack while H1 carries another carton toward the north rack-end corner.";
                    OnStatusMessage?.Invoke(message);
                    Debug.Log($"ATADTRL {message}");
                }
            }

            if (scenario.scenarioCode == "A2" && !_a2ObstructionAnnounced &&
                _carryingParcel && _deliveryStage == DeliveryStage.NavigateToDestination)
            {
                _a2ObstructionAnnounced = true;
                string message = "A2 ROUTE COMMITTED: P2 parcel is bound for D3 through the lower cross-aisle; F1 is moving to its stopping position on that path. Rerouting remains an acceptable recovery.";
                OnStatusMessage?.Invoke(message);
                Debug.Log($"ATADTRL {message}");
            }

            if (scenario.scenarioCode == "A2" && !_a2BlockerParkedAnnounced &&
                _carryingParcel && _deliveryStage == DeliveryStage.NavigateToDestination)
            {
                DynamicObjectMover f1 = FindActiveActor("F1");
                if (f1 != null)
                {
                    DynamicObjectState state = f1.GetState();
                    Vector3 blockingPoint = new Vector3(0f, 0f, -9f);
                    if (Vector3.Distance(state.position, blockingPoint) <= 0.20f &&
                        state.velocity.sqrMagnitude <= 0.01f)
                    {
                        _a2BlockerParkedAnnounced = true;
                        string message = "A2 OBSTRUCTION ACTIVE: F1 completed its inbound pallet hand-off and is now stationary on the robot's committed lower cross-aisle route.";
                        OnStatusMessage?.Invoke(message);
                        Debug.Log($"ATADTRL {message}");
                    }
                }
            }

            if (scenario.scenarioCode == "A3" && !_a3DropBlockedAnnounced &&
                _carryingParcel && _deliveryStage == DeliveryStage.NavigateToDestination &&
                IsTrafficNear(GetDropOffPoint(scenario, _deliveryTaskIndex), 1.25f))
            {
                _a3DropBlockedAnnounced = true;
                string message = "A3 DROP-OFF BLOCKED: H2 and F2 occupy the D3 placement envelope; Module 1 must not release the parcel.";
                OnStatusMessage?.Invoke(message);
                Debug.Log($"ATADTRL {message}");
            }

            if (scenario.scenarioCode == "A4" && !_a4RackRelocated &&
                _carryingParcel && _deliveryStage == DeliveryStage.NavigateToDestination)
            {
                _a4LoadedTravelTime += deltaTime;
                if (_a4LoadedTravelTime >= scenario.rackRelocationDelaySeconds)
                    RelocateA4TargetRack(scenario);
            }

            _scenarioDiagnosticTimer += deltaTime;
            if (_scenarioDiagnosticTimer < 2f) return;
            _scenarioDiagnosticTimer = 0f;

            if (scenario.scenarioCode == "A2" && navigationManager.Status == NavigationStatus.Stopped)
            {
                OnStatusMessage?.Invoke("A2 PATH OBSTRUCTED: F1 occupies the committed cross-aisle route. Module 1 is waiting/replanning while adjacent traffic limits alternatives.");
            }
            // Late in the episode, if the mission still hasn't completed,
            // make one visible, logged reroute attempt that fails — Module 1
            // checks the adjacent aisle, finds it also constrained by
            // ongoing worker traffic, and falls back to waiting on the
            // committed route. This does not change navigation behaviour;
            // it only surfaces the attempt-and-failure as a status/CSV
            // event, and it fires on elapsed time + task state rather than
            // a specific NavigationStatus reading so it isn't missed if the
            // baseline is crawling/replanning rather than fully halted.
            if (scenario.scenarioCode == "A2" && !_a2RerouteAttempted &&
                _episodeTime >= scenario.maxEpisodeDuration - 25f &&
                _deliveryStage != DeliveryStage.Complete)
            {
                _a2RerouteAttempted = true;
                string message = "A2 REROUTE ATTEMPT: Module 1 evaluates the adjacent cross-aisle as an alternate path. REROUTE FAILED \u2014 alternate aisle also constrained by ongoing worker traffic; falling back to waiting on the committed route.";
                OnStatusMessage?.Invoke(message);
                Debug.Log($"ATADTRL {message}");
                RecordOperationalEvent("A2_REROUTE_FAILED", "Adjacent aisle evaluated and rejected; robot remains on the committed, obstructed route.");
            }
            else if (scenario.scenarioCode == "A3" && _a3DropBlockedAnnounced)
            {
                OnStatusMessage?.Invoke("A3 WAITING AT D3: placement remains unsafe because H2 and F2 occupy the approach area.");
            }
            else if (scenario.scenarioCode == "A5" && _a5DockingAttempts > 0 && _deliveryStage == DeliveryStage.AlignAtSource)
            {
                OnStatusMessage?.Invoke($"A5 DOCKING RETRY {_a5DockingAttempts}: back-off / realign / re-approach in progress at the pickup face.");
            }
        }

        // Evaluates whether the robot's current approach pose at the source
        // satisfies A5's configured docking tolerances. Each failed check
        // performs one BACK-OFF -> REALIGN -> RE-APPROACH cycle: the
        // injected error is reduced (a real realignment manoeuvre would do
        // the same) and verification is retried, rather than forcing a
        // pickup failure or silently skipping the check.
        private bool EvaluateA5Docking(ScenarioDefinition scenario)
        {
            float decay = Mathf.Pow(0.30f, _a5DockingAttempts);
            float lateralError = Mathf.Abs(scenario.dockingInitialOffset.x) * decay;
            float longitudinalError = Mathf.Abs(scenario.dockingInitialOffset.z) * decay;
            float headingError = Mathf.Abs(scenario.dockingInitialHeadingErrorDeg) * decay;
            bool withinTolerance = lateralError <= scenario.dockingLateralToleranceM &&
                longitudinalError <= scenario.dockingLongitudinalToleranceM &&
                headingError <= scenario.dockingHeadingToleranceDeg;
            if (withinTolerance)
            {
                string ok = $"A5 DOCKING VERIFIED: lateral {lateralError:F2} m, longitudinal {longitudinalError:F2} m, heading {headingError:F1} deg after {_a5DockingAttempts} retry(ies).";
                OnStatusMessage?.Invoke(ok);
                Debug.Log($"ATADTRL {ok}");
                RecordOperationalEvent("A5_DOCKING_VERIFIED",
                    $"attempts={_a5DockingAttempts + 1},lateral={lateralError:F3},longitudinal={longitudinalError:F3},headingDeg={headingError:F2}");
                return true;
            }
            _a5DockingAttempts++;
            string retry = $"A5 BACK-OFF -> REALIGN -> RE-APPROACH: docking outside tolerance (lateral {lateralError:F2} m, longitudinal {longitudinalError:F2} m, heading {headingError:F1} deg). Attempt {_a5DockingAttempts}.";
            OnStatusMessage?.Invoke(retry);
            Debug.Log($"ATADTRL {retry}");
            RecordOperationalEvent("A5_DOCKING_RETRY",
                $"attempt={_a5DockingAttempts},lateral={lateralError:F3},longitudinal={longitudinalError:F3},headingDeg={headingError:F2}");
            return false;
        }

        // Implements A1's required instrumentation: LOS(Robot,H1), first
        // detection distance, robot velocity and stopping distance at the
        // moment of detection (d_stop = v*t_r + v^2/(2*a_b)), minimum
        // human distance, and a near-collision flag for close calls that
        // never became a physical collision. This never moves, redirects
        // or brakes either actor; it only observes and logs.
        private void UpdateA1DetectionAndStopping(ScenarioDefinition scenario, float deltaTime)
        {
            if (!string.IsNullOrEmpty(_terminalOutcome) && !_a1CollisionSummaryLogged)
            {
                _a1CollisionSummaryLogged = true;
                bool collided = _terminalOutcome.StartsWith("COLLISION");
                float minDistance = _a1MinHumanDistance == float.MaxValue ? -1f : _a1MinHumanDistance;
                RecordOperationalEvent("A1_EPISODE_OUTCOME",
                    $"collisionFlag={(collided ? 1 : 0)},nearCollisionFlag={(_a1NearCollisionFlag ? 1 : 0)},firstDetectionDistance={_a1FirstDetectionDistance:F3},minHumanDistance={minDistance:F3},navigationTime={_episodeTime:F2}");
            }

            DynamicObjectMover h1 = FindActiveActor("H1");
            if (h1 == null || robotController == null) return;

            Vector3 robotPos = robotController.transform.position;
            Vector3 h1Pos = h1.transform.position;
            float distance = Vector3.Distance(robotPos, h1Pos);

            // Self-computed velocity avoids depending on an assumed
            // RobotController speed property.
            float v = 0f;
            if (_a1PrevRobotPos.HasValue && deltaTime > 0f)
                v = Vector3.Distance(robotPos, _a1PrevRobotPos.Value) / deltaTime;
            _a1PrevRobotPos = robotPos;

            // LOS is blocked while the Books rack sits between the two
            // actors; it becomes true once the linecast either hits nothing
            // or reaches H1 directly (no rack face in between).
            bool hitSomething = Physics.Linecast(robotPos + Vector3.up * 0.5f, h1Pos + Vector3.up * 0.5f, out RaycastHit hit);
            bool losClear = !hitSomething || hit.transform == h1.transform || hit.transform.IsChildOf(h1.transform);

            if (losClear && !_a1LosWasClear && _a1FirstDetectionDistance < 0f)
            {
                _a1FirstDetectionDistance = distance;
                const float reactionTime = 0.75f;      // matches H1's configured robotReactionTime
                const float brakingDeceleration = 1.2f; // m/s^2, conservative loaded-AMR estimate
                float stoppingDistance = v * reactionTime + (v * v) / (2f * brakingDeceleration);
                string message = $"A1 DETECTION: LOS(Robot,H1) became true at {distance:F2} m. Robot velocity {v:F2} m/s, computed stopping distance {stoppingDistance:F2} m (available separation {distance:F2} m).";
                OnStatusMessage?.Invoke(message);
                Debug.Log($"ATADTRL {message}");
                RecordOperationalEvent("A1_DETECTION",
                    $"detectionDistance={distance:F3},velocity={v:F3},stoppingDistance={stoppingDistance:F3},sufficientToStop={(distance >= stoppingDistance)}");
            }
            _a1LosWasClear = losClear;

            if (distance < _a1MinHumanDistance) _a1MinHumanDistance = distance;
            if (!_a1NearCollisionFlag && string.IsNullOrEmpty(_terminalOutcome) && distance <= 0.60f)
            {
                _a1NearCollisionFlag = true;
                string message = $"A1 NEAR-COLLISION: minimum separation reached {distance:F2} m without contact.";
                OnStatusMessage?.Invoke(message);
                Debug.Log($"ATADTRL {message}");
                RecordOperationalEvent("A1_NEAR_COLLISION", $"minDistance={distance:F3}");
            }
        }


        private void RelocateA4TargetRack(ScenarioDefinition scenario)
        {
            if (_reservedRackSlot == null || warehouseManager.RackRoot == null ||
                string.IsNullOrWhiteSpace(scenario.relocatedRackId))
            {
                Debug.LogError("ATADTRL A5: rack relocation could not start because the reserved slot or rack identifier is missing.");
                return;
            }

            Transform rack = warehouseManager.RackRoot.Find(scenario.relocatedRackId);
            if (rack == null)
            {
                Debug.LogError($"ATADTRL A5: target rack '{scenario.relocatedRackId}' was not found.");
                return;
            }

            _a4RelocatedRack = rack;
            _a4RackOriginalPosition = rack.position;
            _a4ValidPlacementPosition = _reservedRackSlot.storagePosition + scenario.rackRelocationOffset;
            rack.position += scenario.rackRelocationOffset;
            _a4RackRelocated = true;
            OnRackRelocated?.Invoke(scenario.relocatedRackId, _a4RackOriginalPosition,
                _a4ValidPlacementPosition);

            string message = $"A4 RACK RELOCATED: {scenario.relocatedRackId} moved from {_a4RackOriginalPosition} to {rack.position}. Module 1 retains stale destination {_reservedRackSlot.approachPosition}.";
            OnStatusMessage?.Invoke(message);
            Debug.Log($"ATADTRL {message}");
        }

        private void RestoreA4RackPosition()
        {
            if (_a4RelocatedRack != null)
                _a4RelocatedRack.position = _a4RackOriginalPosition;
            _a4RelocatedRack = null;
            _a4RackRelocated = false;
            _a4LoadedTravelTime = 0f;
            _a4ValidPlacementPosition = Vector3.zero;
        }

        private void BeginDestinationNavigation(ScenarioDefinition scenario)
        {
            _deliveryRouteWaypointIndex = 0;
            bool hasFirstTaskRoute = _deliveryTaskIndex == 0 &&
                scenario.firstTaskDeliveryWaypoints != null &&
                scenario.firstTaskDeliveryWaypoints.Count > 0;
            navigationManager.SetGoal(hasFirstTaskRoute
                ? scenario.firstTaskDeliveryWaypoints[0]
                : GetDropOffPoint(scenario, _deliveryTaskIndex));
        }

        // Returns true while a configured first-delivery route is active.
        // Waypoints are ordinary route-planner constraints. They are selected
        // before the episode starts and never follow or target a human.
        private bool FollowFirstTaskDeliveryRoute(ScenarioDefinition scenario)
        {
            if (_deliveryTaskIndex != 0 || scenario.firstTaskDeliveryWaypoints == null ||
                _deliveryRouteWaypointIndex >= scenario.firstTaskDeliveryWaypoints.Count)
                return false;

            Vector3 waypoint = scenario.firstTaskDeliveryWaypoints[_deliveryRouteWaypointIndex];
            if (!ReachedRouteWaypoint(waypoint)) return true;

            _deliveryRouteWaypointIndex++;
            bool routeComplete = _deliveryRouteWaypointIndex >= scenario.firstTaskDeliveryWaypoints.Count;
            Vector3 nextGoal = routeComplete
                ? GetDropOffPoint(scenario, _deliveryTaskIndex)
                : scenario.firstTaskDeliveryWaypoints[_deliveryRouteWaypointIndex];
            navigationManager.SetGoal(nextGoal);

            string routeMessage;
            if (scenario.scenarioCode == "A1")
            {
                if (routeComplete)
                    routeMessage = $"A1 route: Books-rack corner cleared; continuing to {GetDestinationId(scenario, _deliveryTaskIndex)}.";
                else if (_deliveryRouteWaypointIndex == 1)
                    routeMessage = "A1 route: entered the aisle beside the Books rack; carrying the parcel north toward D2.";
                else
                    routeMessage = "A1 route: reached the north end of the Books rack; making the blind turn toward D2.";
            }
            else if (scenario.scenarioCode == "A2")
            {
                routeMessage = routeComplete
                    ? "A2 route: bottleneck interior reached; attempting to continue to D2."
                    : "A2 route: central aisle entrance reached; loaded robot is entering the bottleneck.";
            }
            else if (scenario.scenarioCode == "A3")
            {
                routeMessage = routeComplete
                    ? "A3 route: committed cross-aisle cleared; continuing to D3."
                    : $"A3 route commitment waypoint {_deliveryRouteWaypointIndex}/{scenario.firstTaskDeliveryWaypoints.Count} reached.";
            }
            else if (routeComplete)
                routeMessage = $"{scenario.scenarioCode} fixed route complete; continuing to {GetDestinationId(scenario, _deliveryTaskIndex)}.";
            else
                routeMessage = $"{scenario.scenarioCode} fixed route waypoint {_deliveryRouteWaypointIndex}/{scenario.firstTaskDeliveryWaypoints.Count} reached.";
            OnStatusMessage?.Invoke($"Episode {_activeBenchmarkEpisode:D2}/{_activeBenchmarkEpisodeTotal:D2}: {routeMessage}");
            Debug.Log($"ATADTRL {routeMessage}");
            return true;
        }

        private bool ReachedRouteWaypoint(Vector3 waypoint)
        {
            if (navigationManager.GoalReached) return true;
            Vector3 planar = robotController.transform.position - waypoint;
            planar.y = 0f;
            return planar.sqrMagnitude <= 0.45f * 0.45f;
        }

        // A waiting target parcel remains physical so warehouse actors do not
        // walk through it. It is staged behind the approach pad, and the AMR
        // can scan/grasp it from this safe arm-reach distance.
        private bool ReachedTaskPoint(Vector3 point)
        {
            // Encoder-localized experiments also require physical arm range;
            // a biased commanded goal must not count as a valid task pose.
            if (navigationManager.GoalReached && !navigationManager.UsesEncoderLocalization) return true;
            if (robotController == null) return false;
            Vector3 planar = robotController.transform.position - point;
            planar.y = 0f;
            const float taskArmReach = 0.45f;
            return planar.sqrMagnitude <= taskArmReach * taskArmReach;
        }

        private void AttachCurrentParcelToRobot()
        {
            if (_deliveryTaskIndex < _pickupMarkers.Count && _pickupMarkers[_deliveryTaskIndex] != null)
                _pickupMarkers[_deliveryTaskIndex].SetActive(false);
            if (_parcel != null)
            {
                var collider = _parcel.GetComponent<Collider>();
                if (collider != null) collider.enabled = false;
                Transform gripper = _manipulator.Tool;
                _parcel.transform.SetParent(gripper != null ? gripper : robotController.transform, true);
                _parcel.transform.localPosition = new Vector3(0f, -0.25f, 0f);
                _parcelOnTool=true;
            }
        }

        private void StowCurrentParcelOnRearDeck()
        {
            if (_parcel == null || robotController == null) return;
            _parcelOnTool=false;

            Transform rearDeck = robotController.transform.Find("Detailed visual/Rear Cargo Deck");
            _parcel.transform.SetParent(rearDeck != null ? rearDeck : robotController.transform, true);
            _parcel.transform.localRotation = Quaternion.identity;
            _parcel.transform.localPosition = rearDeck != null
                ? new Vector3(0f, 0.28f, 0f)
                : new Vector3(0f, 0.65f, -0.48f);

            // The carried load belongs to the robot hierarchy and must not
            // become a false obstacle for its own proximity sensors.
            var collider = _parcel.GetComponent<Collider>();
            if (collider != null) collider.enabled = false;
        }

        private void DetachParcelAtSource(ScenarioDefinition scenario)
        {
            _parcelOnTool=false;
            _manipulator.OpenGripper();
            if (_parcel != null)
            {
                _parcel.transform.SetParent(null, true);
                _parcel.transform.position = _sourceRackSlot != null ? _sourceRackSlot.storagePosition :
                    GetParcelStoragePosition(GetPickupPoint(scenario, _deliveryTaskIndex), IsPickupFromRack(scenario, _deliveryTaskIndex));
                var collider = _parcel.GetComponent<Collider>();
                if (collider != null) collider.enabled = true;
            }
            if (_deliveryTaskIndex < _pickupMarkers.Count && _pickupMarkers[_deliveryTaskIndex] != null)
                _pickupMarkers[_deliveryTaskIndex].SetActive(true);
        }

        private void PlaceCurrentParcel(ScenarioDefinition scenario)
        {
            if (_parcel != null)
            {
                bool rackPlacement = IsDeliveryToRack(scenario, _deliveryTaskIndex) && _reservedRackSlot != null;
                _parcel.transform.SetParent(null, true);
                _parcel.transform.position = GetPlacementPosition(scenario);
                _releasePosition=_parcel.transform.position;
                _manipulator.OpenGripper(); _parcelOnTool=false;
                robotController.GetComponent<BaselineNavigationController>()?.SetPayload(0f);
                Vector3 placedPosition = _parcel.transform.position;
                var collider = _parcel.GetComponent<Collider>();
                if (collider != null) collider.enabled = true;

                bool wrongPlacement = scenario.scenarioCode == "A4" && _a4RackRelocated &&
                                      Vector3.Distance(placedPosition, _a4ValidPlacementPosition) > 0.75f;
                if (wrongPlacement)
                {
                    _terminalOutcome = "WRONG_PLACEMENT";
                    string message = $"A4 WRONG PLACEMENT: parcel placed at stale position {placedPosition}; relocated rack requires {_a4ValidPlacementPosition}.";
                    OnStatusMessage?.Invoke(message);
                    Debug.LogWarning($"ATADTRL {message}");
                    _deliveredParcels.Add(_parcel);
                }
                else if (rackPlacement)
                {
                    // Reservation and physical handling are separate states:
                    // only a completed Place action turns the slot Occupied.
                    if (warehouseManager.Inventory != null && warehouseManager.Inventory.CommitPlacement(_reservedRackSlot, _currentParcelRecord))
                        Destroy(_parcel);
                    else { _terminalOutcome="INVENTORY_COMMIT_FAILED"; _deliveredParcels.Add(_parcel); }
                }
                else
                {
                    _deliveredParcels.Add(_parcel);
                }
                _parcel = null;
            }
            SetArmExtension(0.08f);
            if (_deliveryTaskIndex < _dropOffMarkers.Count && _dropOffMarkers[_deliveryTaskIndex] != null)
                _dropOffMarkers[_deliveryTaskIndex].SetActive(false);
        }

        private void AlignRobotToCurrentSource(ScenarioDefinition scenario)
        {
            Vector3 target = _parcel != null ? _parcel.transform.position : GetPickupPoint(scenario, _deliveryTaskIndex);
            Vector3 direction = target - robotController.transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.01f) return;
            Quaternion desired = Quaternion.LookRotation(direction.normalized, Vector3.up);
            robotController.transform.rotation = Quaternion.Slerp(robotController.transform.rotation, desired, Time.deltaTime * 8f);
        }

        private Vector3 GetPlacementPosition(ScenarioDefinition scenario) =>
            IsDeliveryToRack(scenario,_deliveryTaskIndex) && _reservedRackSlot!=null ? _reservedRackSlot.storagePosition :
            GetParcelStoragePosition(GetDropOffPoint(scenario,_deliveryTaskIndex),false);

        private void SetArmExtension(float extension)
        {
            Transform arm = robotController != null
                ? robotController.transform.Find("Detailed visual/AMR Arm/Arm Extension")
                : null;
            if (arm != null) arm.localPosition = new Vector3(0f, 0f, 0.18f + extension);
        }

        private void DetectTerminalCollision()
        {
            if (!string.IsNullOrEmpty(_terminalOutcome) || robotController == null) return;
            foreach (var actor in _activeDynamicObjects)
            {
                if (actor == null || !actor.IsActive) continue;
                Collider robotShape=robotController.GetComponent<Collider>();
                Collider actorShape=actor.GetComponent<Collider>();
                if (robotShape==null || actorShape==null || !actorShape.enabled ||
                    !Physics.ComputePenetration(robotShape, robotShape.transform.position, robotShape.transform.rotation,
                        actorShape, actorShape.transform.position, actorShape.transform.rotation, out _, out float penetration) || penetration<=0.005f) continue;

                // Count proximity-detected impacts when Unity physics has not
                // already raised the robot collision flag this frame.
                if (!robotController.IsColliding) _collisionCount++;
                _terminalOutcome = actor.Type == DynamicObjectType.Human ? "COLLISION_HUMAN" :
                    actor.Type == DynamicObjectType.Forklift ? "COLLISION_FORKLIFT" : "COLLISION_OBSTACLE";
                navigationManager.HoldAtTaskPoint();
                _deliveryStage = DeliveryStage.Complete;
                _currentAction = RobotTaskAction.Wait;
                OnStatusMessage?.Invoke($"Safety failure: {_terminalOutcome}. Scenario ended.");
                return;
            }

            if (robotController.IsColliding)
            {
                _terminalOutcome = robotController.LastCollisionActorType == DynamicObjectType.Human ? "COLLISION_HUMAN" :
                    robotController.LastCollisionActorType == DynamicObjectType.Forklift ? "COLLISION_FORKLIFT" : "COLLISION_OBSTACLE";
                navigationManager.HoldAtTaskPoint();
                _deliveryStage = DeliveryStage.Complete;
                _currentAction = RobotTaskAction.Wait;
                OnStatusMessage?.Invoke($"Safety failure: {_terminalOutcome}. Scenario ended.");
            }
        }

        private int GetDeliveryTaskCount(ScenarioDefinition scenario)
        {
            if (scenario.pickupPositions == null || scenario.dropOffPositions == null) return 1;
            int count = Mathf.Min(scenario.pickupPositions.Count, scenario.dropOffPositions.Count);
            return Mathf.Max(1, count);
        }

        private Vector3 GetPickupPoint(ScenarioDefinition scenario, int index)
        {
            if (_sourceRackSlot!=null && index==_deliveryTaskIndex) return _sourceRackSlot.approachPosition;
            int slot = GetTaskSlot(scenario, index);
            return scenario.pickupPositions != null && slot >= 0 && slot < scenario.pickupPositions.Count
                ? scenario.pickupPositions[slot]
                : scenario.pickupPosition;
        }

        private Vector3 GetDropOffPoint(ScenarioDefinition scenario, int index)
        {
            if (IsDeliveryToRack(scenario, index) && _reservedRackSlot != null && index == _deliveryTaskIndex)
                return _reservedRackSlot.approachPosition;
            int slot = GetTaskSlot(scenario, index);
            return scenario.dropOffPositions != null && slot >= 0 && slot < scenario.dropOffPositions.Count
                ? scenario.dropOffPositions[slot]
                : scenario.dropOffPosition;
        }

        private bool IsPickupFromRack(ScenarioDefinition scenario, int index)
        {
            int slot = GetTaskSlot(scenario, index);
            return scenario.pickupFromRackByTask != null && slot >= 0 && slot < scenario.pickupFromRackByTask.Count
                ? scenario.pickupFromRackByTask[slot]
                : false;
        }

        private bool IsDeliveryToRack(ScenarioDefinition scenario, int index)
        {
            int slot = GetTaskSlot(scenario, index);
            return scenario.deliverToRackByTask != null && slot >= 0 && slot < scenario.deliverToRackByTask.Count
                ? scenario.deliverToRackByTask[slot]
                : scenario.deliverToRack;
        }

        private int GetTaskSlot(ScenarioDefinition scenario, int executionIndex)
        {
            int taskCount = GetDeliveryTaskCount(scenario);
            // Final benchmark scenarios run the complete, declared mission:
            // P1→D2, P2→D1, P3→D3. This makes every pickup/drop point
            // auditable once per episode instead of rotating by a previous
            // scenario's global episode number.
            if (!scenario.rotateTaskOrderEachEpisode || taskCount <= 1) return executionIndex;
            return (executionIndex + Mathf.Max(0, _episodeCounter - 1)) % taskCount;
        }

        private string GetSourceId(ScenarioDefinition scenario, int index)
        {
            int slot = GetTaskSlot(scenario, index);
            return scenario.sourceIds != null && slot >= 0 && slot < scenario.sourceIds.Count
                ? scenario.sourceIds[slot] : $"Source_{index + 1}";
        }

        private string GetDestinationId(ScenarioDefinition scenario, int index)
        {
            int slot = GetTaskSlot(scenario, index);
            return scenario.destinationIds != null && slot >= 0 && slot < scenario.destinationIds.Count
                ? scenario.destinationIds[slot] : $"Destination_{index + 1}";
        }

        private string GetParcelBarcode(ScenarioDefinition scenario, int index)
        {
            int slot = GetTaskSlot(scenario, index);
            return scenario.parcelBarcodes != null && slot >= 0 && slot < scenario.parcelBarcodes.Count
                ? scenario.parcelBarcodes[slot] : $"ATD-BOX-{index + 1:D4}";
        }

        private string GetParcelCategory(ScenarioDefinition scenario, int index)
        {
            int slot = GetTaskSlot(scenario, index);
            return scenario.parcelCategories != null && slot >= 0 && slot < scenario.parcelCategories.Count
                ? scenario.parcelCategories[slot] : "General";
        }

        private void PopulateTaskObservation(ObservationRecord record, ScenarioDefinition scenario)
        {
            int taskIndex = Mathf.Clamp(_deliveryTaskIndex, 0, GetDeliveryTaskCount(scenario) - 1);
            record.parcel_id = $"Parcel_{taskIndex + 1}";
            Vector3 parcelPosition = _parcel != null ? _parcel.transform.position : GetDropOffPoint(scenario, taskIndex);
            record.parcel_position = SerializeVector(parcelPosition);
            record.source_id = GetSourceId(scenario, taskIndex);
            record.destination_id = GetDestinationId(scenario, taskIndex);
            record.barcode_id = _currentBarcode ?? GetParcelBarcode(scenario, taskIndex);
            record.parcel_category = _currentCategory ?? GetParcelCategory(scenario, taskIndex);
            record.destination_rack_id = _reservedRackSlot != null ? _reservedRackSlot.rackId : "UNASSIGNED";
            record.destination_slot_id = _reservedRackSlot != null ? _reservedRackSlot.slotId : "UNASSIGNED";
            record.parcel_mass = scenario.parcelMass;
            record.empty_slot_count = warehouseManager.Inventory != null
                ? warehouseManager.Inventory.EmptySlotCount(record.parcel_category) : 0;
            record.carrying_status = _carryingParcel;
            record.grasp_status = _graspVerified;
            record.rack_available = _reservedRackSlot != null && !IsTrafficNear(GetDropOffPoint(scenario, taskIndex), 1.1f);
            record.PD1_available = !IsTrafficNear(warehouseManager.config.pickupStationPositions[0], 1.1f);
            record.PD2_available = !IsTrafficNear(warehouseManager.config.pickupStationPositions[1], 1.1f);
            record.PD3_available = !IsTrafficNear(warehouseManager.config.pickupStationPositions[2], 1.1f);
            record.human_positions = SerializeActors(DynamicObjectType.Human, false);
            record.human_velocities = SerializeActors(DynamicObjectType.Human, true);
            record.forklift_positions = SerializeActors(DynamicObjectType.Forklift, false);
            record.forklift_velocities = SerializeActors(DynamicObjectType.Forklift, true);
            PopulateNamedActorObservations(record);
            record.battery_level = _batteryLevel;
            record.sensor_status = record.missing_sensor_flag ? "Missing" : record.sensor_dropout_flag ? "Degraded" : "Nominal";
            record.communication_delay = scenario.communicationDelaySeconds;
            record.task_priority = _urgentOrderChanged ? 3 : scenario.taskPriority;
            RobotTaskAction recordedAction = _currentAction;
            bool navigationStage = _deliveryStage == DeliveryStage.NavigateToSource ||
                _deliveryStage == DeliveryStage.NavigateToDestination ||
                _deliveryStage == DeliveryStage.NavigateToCharge ||
                _deliveryStage == DeliveryStage.EmergencyToSafety;
            if (navigationStage && navigationManager.Status == NavigationStatus.Stopped)
                recordedAction = RobotTaskAction.Wait;
            record.current_task_stage = $"{_deliveryStage}:{recordedAction}";
        }

        private DynamicObjectMover FindActiveActor(string actorId)
        {
            foreach (var actor in _activeDynamicObjects)
            {
                if (actor != null && actor.IsActive && actor.ObjectId == actorId)
                    return actor;
            }
            return null;
        }

        private bool IsTrafficNear(Vector3 point, float radius)
        {
            foreach (var actor in _activeDynamicObjects)
            {
                if (actor != null && actor.IsActive && Vector3.Distance(actor.transform.position, point) <= radius)
                    return true;
            }
            return false;
        }

        private string SerializeActors(DynamicObjectType type, bool velocity)
        {
            var values = new List<string>();
            foreach (var actor in _activeDynamicObjects)
            {
                if (actor == null || !actor.IsActive || actor.Type != type) continue;
                var state = actor.GetState();
                Vector3 value = velocity ? state.velocity : state.position;
                values.Add($"{state.objectId}:{SerializeVector(value)}");
            }
            return string.Join("|", values);
        }

        private void PopulateNamedActorObservations(ObservationRecord record)
        {
            foreach (var actor in _activeDynamicObjects)
            {
                if (actor == null || !actor.IsActive) continue;
                var state = actor.GetState();
                string position = SerializeVector(state.position);
                string velocity = SerializeVector(state.velocity);
                switch (state.objectId)
                {
                    case "H1": record.h1_position = position; record.h1_velocity = velocity; break;
                    case "H2": record.h2_position = position; record.h2_velocity = velocity; break;
                    case "H3": record.h3_position = position; record.h3_velocity = velocity; break;
                    case "H4": record.h4_position = position; record.h4_velocity = velocity; break;
                    case "H5": record.h5_position = position; record.h5_velocity = velocity; break;
                    case "F1": record.f1_position = position; record.f1_velocity = velocity; break;
                    case "F2": record.f2_position = position; record.f2_velocity = velocity; break;
                }
            }
        }

        private static string SerializeVector(Vector3 vector) => System.FormattableString.Invariant($"{vector.x:F3}:{vector.y:F3}:{vector.z:F3}");

        // A5 picks up directly from the Books rack face rather than a
        // station shelf. CreateParcelAtPickup requires an existing occupied
        // slot of the right category to pick from (it never spawns stock
        // out of nowhere), so this stages one Books parcel into an empty
        // rack slot before the episode's first task begins. Reuses the same
        // Reserve -> Commit pair CreateParcelAtPickup/PlaceCurrentParcel
        // already use for deliveries, just applied before the mission
        // narrative starts rather than at the end of it.
        private void EnsureA5SourceStock(ScenarioDefinition scenario)
        {
            if (scenario.scenarioCode != "A5" || warehouseManager.Inventory == null) return;
            bool alreadyStocked = warehouseManager.Inventory.Slots.Any(s => s.HasParcel && s.category == "Books");
            if (alreadyStocked) return;

            var stagedRecord = new ParcelInventoryRecord
            {
                parcelId = $"EP{_episodeCounter:D4}-A5SRC",
                parcelNumber = "PARCEL-A5-SOURCE",
                barcode = scenario.parcelBarcodes != null && scenario.parcelBarcodes.Count > 0 ? scenario.parcelBarcodes[0] : "ATD-BOK-A5-001",
                details = "Books parcel pre-staged at the rack face for A5 pickup",
                category = "Books",
                massKg = scenario.parcelMass,
                sourceId = "Rack-Books",
                destinationId = scenario.destinationIds != null && scenario.destinationIds.Count > 0 ? scenario.destinationIds[0] : "D1-Electronics"
            };
            var slot = warehouseManager.Inventory.ReserveFirstAvailableSlot("Books", stagedRecord);
            if (slot == null)
            {
                Debug.LogWarning("ATADTRL A5: no empty Books rack slot available to pre-stock for rack-face pickup. Falling back: CreateParcelAtPickup will report NO_ACCESSIBLE_SOURCE_STOCK if this is not resolved.");
                return;
            }
            warehouseManager.Inventory.CommitPlacement(slot, stagedRecord);
        }

        private void CreateParcelAtPickup(ScenarioDefinition scenario, int taskIndex)
        {
            _sourceRackSlot=null;
            _currentBarcode = GetParcelBarcode(scenario, taskIndex);
            _currentCategory = GetParcelCategory(scenario, taskIndex);
            if (IsPickupFromRack(scenario,taskIndex) && warehouseManager.Inventory!=null)
            {
                _sourceRackSlot=warehouseManager.Inventory.Slots.FirstOrDefault(s=>s.HasParcel &&
                    s.category==_currentCategory && s.storagePosition.y<1.8f);
                if (_sourceRackSlot==null) { _terminalOutcome="NO_ACCESSIBLE_SOURCE_STOCK"; return; }
                _currentBarcode=_sourceRackSlot.parcel.barcode;
                if(_sourceRackSlot.visual!=null) _sourceRackSlot.visual.SetActive(false);
            }
            _currentParcelRecord = new ParcelInventoryRecord
            {
                parcelId = $"EP{_episodeCounter:D4}-P{taskIndex + 1:D2}",
                parcelNumber = $"PARCEL-{scenario.scenarioCode}-{taskIndex + 1:D2}",
                barcode = _currentBarcode,
                details = $"{_currentCategory} material-handling parcel for {GetDestinationId(scenario, taskIndex)}",
                category = _currentCategory,
                massKg = scenario.parcelMass,
                sourceId = GetSourceId(scenario, taskIndex),
                destinationId = GetDestinationId(scenario, taskIndex)
            };
            // The physical barcode must identify, at minimum, the parcel,
            // its destination and the task it belongs to. _currentBarcode up
            // to this point is the short assigned/observed identifier (used
            // as-is for E3's mismatch comparison); the scanned/committed
            // barcode composes all three required fields together.
            string taskId = $"{scenario.scenarioCode}-T{taskIndex + 1:D2}";
            _currentBarcode = $"{_currentParcelRecord.parcelId}|DEST:{_currentParcelRecord.destinationId}|TASK:{taskId}";
            _currentParcelRecord.barcode = _currentBarcode;
            _reservedRackSlot = IsDeliveryToRack(scenario, taskIndex) && warehouseManager.Inventory != null
                ? warehouseManager.Inventory.ReserveFirstAvailableSlot(_currentCategory, _currentParcelRecord) : null;
            _parcel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _parcel.name = $"Parcel_{_currentBarcode}_{_currentCategory}";
            _parcel.transform.position = _sourceRackSlot!=null ? _sourceRackSlot.storagePosition : GetParcelStoragePosition(
                GetPickupPoint(scenario, taskIndex), IsPickupFromRack(scenario, taskIndex));
            _parcel.transform.localScale = new Vector3(0.55f, 0.45f, 0.40f);
            var parcelCollider = _parcel.GetComponent<Collider>();
            if (parcelCollider != null) parcelCollider.enabled = true;
            EnvironmentCollisionTag.Attach(_parcel, EnvironmentCollisionTag.Kind.DynamicObstacle);
            _parcel.GetComponent<Renderer>().material.color = new Color(0.50f, 0.33f, 0.19f);
            CreateBarcodeLabel(_parcel.transform, _currentBarcode, _currentCategory);
        }

        private static void CreateBarcodeLabel(Transform parcel, string barcode, string category)
        {
            WarehouseManager.CreatePhysicalSign(parcel,"Parcel shipping label",new Vector3(0f,0f,-0.525f),
                Quaternion.identity,new Vector2(0.80f,0.60f),$"{barcode}\n{category}",new Color(0.94f,0.93f,0.88f),Color.black);
        }

        // Markers sit in the aisle so the NavMeshAgent can reach them. Rack
        // parcels are offset onto the adjacent rack shelf; inbound parcels
        // wait behind the pickup approach pad rather than in front of LiDAR.
        private static Vector3 GetParcelStoragePosition(Vector3 taskPoint, bool onRack)
        {
            return onRack
                ? taskPoint + Vector3.left * 1.75f + Vector3.up * 1.15f
                : taskPoint + WarehouseManager.GetStationRearOffset(taskPoint, 0.95f) + Vector3.up * 1.05f;
        }

        private GameObject CreateTaskMarker(string markerName, Vector3 position, Color color)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = markerName;
            marker.transform.position = position + Vector3.up * 0.02f;
            marker.transform.localScale = new Vector3(0.65f, 0.012f, 0.045f);
            var collider = marker.GetComponent<Collider>();
            if (collider != null) collider.enabled = false;
            marker.GetComponent<Renderer>().material.color = new Color(0.88f,0.74f,0.32f);
            return marker;
        }

        private void CleanupDeliveryVisuals()
        {
            // Failed or interrupted episodes must not leave ghost inventory
            // reservations. A successfully placed slot is Occupied, so this
            // cancellation is intentionally a no-op for completed putaway.
            warehouseManager?.Inventory?.CancelReservation(_reservedRackSlot);
            if (_sourceRackSlot != null && _sourceRackSlot.HasParcel && _sourceRackSlot.visual != null)
                _sourceRackSlot.visual.SetActive(true);
            foreach (var marker in _pickupMarkers) if (marker != null) Destroy(marker);
            foreach (var marker in _dropOffMarkers) if (marker != null) Destroy(marker);
            foreach (var parcel in _deliveredParcels) if (parcel != null) Destroy(parcel);
            if (_parcel != null) Destroy(_parcel);
            _pickupMarkers.Clear();
            _dropOffMarkers.Clear();
            _deliveredParcels.Clear();
            _parcel = null;
            _sourceRackSlot = null;
            _parcelOnTool = false;
            _carryingParcel = false;
            _graspVerified = false;
            _reservedRackSlot = null;
            _currentParcelRecord = null;
            _deliveryTaskIndex = 0;
            _deliveryStage = DeliveryStage.Complete;
        }

        private void OnDestroy()
        {
            _disturbance?.Restore();
            _eventLogger?.Close();
            _gtLogger?.CloseAll();
            _obsLogger?.CloseAll();
            _perfLogger?.Close();
            _outcomeLogger?.Close();
        }
    }
}
