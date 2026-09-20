using System;
using System.IO;
using UnityEngine;
using ATADTRL.Core;
using ATADTRL.Logging;
using ATADTRL.Pipeline;

namespace ATADTRL.Module2
{
    public class Module2Manager :
        MonoBehaviour
    {
        [Header("EDATS")]
        public EDATSConfig edatsConfig;

        [Header("Logging")]
        public string datasetFolderName =
            "ATADTRL_Dataset";

        private EnvironmentalChangeDetector
            changeDetector;

        private EDATSAlgorithm edats;

        private AdaptiveDigitalTwin
            digitalTwin;

        private TwinWorldStateLogger
            twinLogger;

        private TwinDivergenceEvaluator
            divergenceEvaluator;

        private ObservationRecord
            previousObservation;

        // =============================================================
        // INDUSTRIAL MONITOR TELEMETRY
        // =============================================================

        public ObservationRecord LatestObservation { get; private set; }

        public int CurrentScenarioId { get; private set; } = -1;

        public long ProcessedObservationCount { get; private set; }

        public int SynchronizationCount { get; private set; }

        public float ScenarioRuntime { get; private set; }

        public float LastSyncRuntime { get; private set; }

        private float scenarioStartedAt;

        // =============================================================
        // MONITOR VALUES
        // =============================================================

        public float CurrentChangeScore { get; private set; }

        public bool SynchronizationTriggered { get; private set; }

        public bool CriticalEvent { get; private set; }

        public string SyncReason { get; private set; } = "";

        public string ChangedComponents { get; private set; } = "";

        public bool MajorUpdateLatched { get; private set; }

        public string MajorUpdateTitle { get; private set; } = "";

        public string MajorUpdateFlow { get; private set; } = "";

        public event Action MajorUpdateRegistered;
        private int majorScenarioId = 1;
        private string majorTerminalStage = "COLLISION_HUMAN";

        public float LastObservationRealtime { get; private set; } = -1f;

        public bool IsMonitoring =>
            CurrentScenarioId >= 0 &&
            LatestObservation != null &&
            Time.realtimeSinceStartup - LastObservationRealtime < 0.75f;

        public bool DataLoggingActive { get; private set; }
        public bool IsIdleLiveTwin => CurrentScenarioId == 0 && !DataLoggingActive;

        public TwinWorldStateRecord CurrentTwin =>
            digitalTwin?.Current;

        public TwinDivergenceResult
            CurrentDivergence { get; private set; }

        // =============================================================

        private void Awake()
        {
            if (edatsConfig == null)
            {
                Debug.LogError(
                    "Module2Manager: EDATSConfig is not assigned.");

                enabled = false;
                return;
            }

            changeDetector =
                new EnvironmentalChangeDetector(
                    edatsConfig);

            edats =
                new EDATSAlgorithm(
                    edatsConfig);

            divergenceEvaluator =
                new TwinDivergenceEvaluator(edatsConfig);

            digitalTwin =
                new AdaptiveDigitalTwin();

            string root =
                Path.Combine(
                    Application.persistentDataPath,
                    datasetFolderName);

            twinLogger =
                new TwinWorldStateLogger(root);

            // Keep the scene backward compatible: the end-to-end A1 pilot is
            // attached automatically beside Module 2 when older scenes do not
            // yet contain the component.
            if (GetComponent<ATADTRLPipelineManager>() == null)
                gameObject.AddComponent<ATADTRLPipelineManager>();
        }

        public void BeginScenario(
            int scenarioId)
        {
            previousObservation = null;
            digitalTwin = new AdaptiveDigitalTwin();
            twinLogger.BeginScenario(scenarioId);
            CurrentScenarioId = scenarioId;
            DataLoggingActive = true;

            ProcessedObservationCount = 0;
            SynchronizationCount = 0;

            scenarioStartedAt = Time.time;
            ScenarioRuntime = 0f;
            LastSyncRuntime = 0f;

            LatestObservation = null;
            LastObservationRealtime = -1f;
            MajorUpdateLatched = false;
            MajorUpdateTitle = "";
            MajorUpdateFlow = "";
            majorScenarioId = scenarioId;
            majorTerminalStage = "MAJOR_UPDATE";
        }

        public void BeginIdleMonitoring()
        {
            if (MajorUpdateLatched || (CurrentScenarioId == 0 && LatestObservation != null))
                return;

            previousObservation = null;
            digitalTwin = new AdaptiveDigitalTwin();
            CurrentScenarioId = 0;
            DataLoggingActive = false;
            ProcessedObservationCount = 0;
            SynchronizationCount = 0;
            scenarioStartedAt = Time.time;
            ScenarioRuntime = 0f;
            LastSyncRuntime = 0f;
            CurrentChangeScore = 0f;
            SynchronizationTriggered = false;
            CriticalEvent = false;
            SyncReason = "Live twin connected — memory only; CSV logging starts with an episode";
            ChangedComponents = "Live robot and sensor telemetry";
            Debug.Log("ATADTRL TWIN: idle live monitoring connected; data logging is OFF.");
        }

        public void ProcessObservation(ObservationRecord observation)
        {
            ProcessObservationCore(observation, true);
        }

        public void ProcessLiveObservation(ObservationRecord observation)
        {
            ProcessObservationCore(observation, false);
        }

        public void StopMonitoring()
        {
            if (DataLoggingActive) return;
            CurrentScenarioId = -1;
            DataLoggingActive = false;
            SynchronizationTriggered = false;
            CriticalEvent = false;
            SyncReason = "Warehouse operations stopped by operator";
            ChangedComponents = "None";
            Debug.Log("ATADTRL TWIN: live monitoring stopped by operator.");
        }

        public void ReturnToNormalLiveState()
        {
            twinLogger.EndScenario();
            MajorUpdateLatched = false;
            MajorUpdateTitle = "";
            MajorUpdateFlow = "";
            CurrentScenarioId = 0;
            DataLoggingActive = false;
            CurrentChangeScore = 0f;
            SynchronizationTriggered = false;
            CriticalEvent = false;
            SyncReason = "Safe replay completed — normal live twin monitoring resumed";
            ChangedComponents = "Live robot, H1-H5 and F1-F2 telemetry";
            previousObservation = null;
            LatestObservation = null;
            LastObservationRealtime = -1f;
            digitalTwin = new AdaptiveDigitalTwin();
            scenarioStartedAt = Time.time;
            ScenarioRuntime = 0f;
            LastSyncRuntime = 0f;
            Debug.Log("ATADTRL TWIN: safe replay completed; major event cleared and normal live monitoring resumed.");
        }

        private void ProcessObservationCore(ObservationRecord observation, bool persistRecord)
        {
            if (observation == null)
                return;

            // =============================================================
            // STORE LATEST OBSERVATION / RUNTIME INFORMATION
            // =============================================================

            LatestObservation =
                observation;

            LastObservationRealtime = Time.realtimeSinceStartup;

            ProcessedObservationCount++;

            ScenarioRuntime =
                Time.time - scenarioStartedAt;

            // =============================================================
            // 1. DETECT ENVIRONMENTAL CHANGES
            // =============================================================

            ChangeDetectionResult change =
                changeDetector.Detect(
                    previousObservation,
                    observation);

            // =============================================================
            // 2. CALCULATE CURRENT TWIN AGE
            //    A_t = t_current - t_lastSync
            // =============================================================

            float currentTwinAge = 0f;

            if (digitalTwin.IsInitialized)
            {
                currentTwinAge =
                    Mathf.Max(
                        0f,
                        (float)(
                            observation.timestamp -
                            digitalTwin.LastSyncTimestamp));
            }

            // =============================================================
            // 3. CALCULATE OBSERVATION ↔ TWIN DIVERGENCE
            // =============================================================

            TwinDivergenceResult divergence =
                divergenceEvaluator.Evaluate(
                    observation,
                    digitalTwin.Current);

            CurrentDivergence =
                divergence;

            // =============================================================
            // 4. EDATS SYNCHRONIZATION DECISION
            //
            // Synchronize when:
            // E_t = 1
            // OR C_t > tau_C
            // OR D_t > tau_D
            // OR A_t > tau_A
            // =============================================================

            EDATSDecision decision =
                edats.Evaluate(
                    change,
                    divergence,
                    currentTwinAge);

            // =============================================================
            // 5. RECORD SYNCHRONIZATION INFORMATION
            // =============================================================

            if (decision.synchronize)
            {
                SynchronizationCount++;

                LastSyncRuntime =
                    Time.time - scenarioStartedAt;
            }

            // =============================================================
            // 6. UPDATE ADAPTIVE DIGITAL TWIN
            // =============================================================

            TwinWorldStateRecord twin =
                digitalTwin.Update(
                    observation,
                    decision);

            // =============================================================
            // 7. STORE DIVERGENCE INFORMATION IN TWIN DATASET
            // =============================================================

            if (twin != null &&
                divergence != null)
            {
                twin.robot_position_divergence =
                    divergence.robotPositionError;

                twin.robot_yaw_divergence =
                    divergence.robotYawError;

                twin.robot_velocity_divergence =
                    divergence.robotVelocityError;

                twin.max_actor_position_divergence =
                    divergence.maximumActorPositionError;

                twin.divergence_triggered =
                    decision.divergenceTriggered;

                twin.stale_state_triggered =
                    decision.staleStateTriggered;
            }

            // A final observation can be emitted in the same frame as the
            // collision. Never let that normal update erase the major event.
            if (MajorUpdateLatched)
                ApplyMajorUpdateToTwin(twin);

            // =============================================================
            // 8. LOG TWIN WORLD STATE
            // =============================================================

            if (persistRecord && DataLoggingActive)
                twinLogger.LogRecord(twin);

            // =============================================================
            // 9. UPDATE LIVE MONITOR VALUES
            // =============================================================

            if (!MajorUpdateLatched)
            {
                CurrentChangeScore =
                    decision.changeScore;

                SynchronizationTriggered =
                    decision.synchronize;

                CriticalEvent =
                    decision.criticalEvent;

                SyncReason =
                    decision.reason;

                ChangedComponents =
                    string.Join(
                        ", ",
                        decision.changedComponents);
            }

            // =============================================================
            // 10. STORE CURRENT OBSERVATION FOR NEXT STEP
            // =============================================================

            previousObservation =
                observation;
        }

        /// <summary>
        /// Registers a terminal physical collision as a major digital-twin
        /// update. This is called after Module 1 closes its episode log, so the
        /// final state remains latched on Display 2 for explanation and review.
        /// </summary>
        public void RegisterTerminalOutcome(int scenarioId, string outcome, int collisionCount)
        {
            bool collision = collisionCount > 0 ||
                (!string.IsNullOrWhiteSpace(outcome) &&
                 outcome.StartsWith("COLLISION", StringComparison.OrdinalIgnoreCase));
            if (scenarioId == 1 && collision)
            {
                RegisterMajorScenarioUpdate(1, "MAJOR UPDATE - A1 HUMAN COLLISION",
                    "Physical collision detected at the Books-rack blind corner. EDATS forced an immediate terminal synchronization.",
                    "robot.collision, H1.contact, robot.navigation_status, mission.outcome",
                    "CHANGE DETECTION: collision event confirmed\n" +
                    "EDATS: immediate synchronization triggered\n" +
                    "ADAPTIVE TWIN: collision and mission state updated\n" +
                    "CONTEXT + TAM: blind-corner risk and trusted state evaluated\n" +
                    "SECURE POLICY: controlled wait selected for the shadow robot",
                    "COLLISION_HUMAN");
                return;
            }
            if (scenarioId == 4 && string.Equals(outcome, "WRONG_PLACEMENT", StringComparison.OrdinalIgnoreCase))
                RegisterMajorScenarioUpdate(4, "MAJOR UPDATE - A4 RACK RELOCATION",
                    "Rack_4_3 relocation invalidated the committed destination. EDATS synchronized the physical rack position.",
                    "Rack_4_3.position, destination.approach, destination.storage, mission.outcome",
                    "CHANGE DETECTION: relocated rack confirmed\n" +
                    "EDATS: rack and destination state synchronized\n" +
                    "ADAPTIVE TWIN: relocated storage pose updated\n" +
                    "CONTEXT + TAM: new destination validated\n" +
                    "SECURE POLICY: replan-to-relocated-rack action generated",
                    "WRONG_PLACEMENT");
        }

        public void RegisterMajorScenarioUpdate(int scenarioId, string title, string reason,
            string components, string flow, string terminalStage)
        {
            if (MajorUpdateLatched)
            {
                CurrentScenarioId = scenarioId;
                return;
            }
            ScenarioRuntime = Mathf.Max(ScenarioRuntime, Time.time - scenarioStartedAt);
            CurrentScenarioId = scenarioId;
            CurrentChangeScore = 1f;
            SynchronizationTriggered = true;
            CriticalEvent = true;
            MajorUpdateLatched = true;
            SynchronizationCount++;
            LastSyncRuntime = ScenarioRuntime;
            majorScenarioId = scenarioId;
            majorTerminalStage = terminalStage;
            MajorUpdateTitle = title;
            SyncReason = reason;
            ChangedComponents = components;
            MajorUpdateFlow = flow;

            TwinWorldStateRecord twin = CurrentTwin;
            if (twin != null)
            {
                ApplyMajorUpdateToTwin(twin);

                // The live bridge calls this before the scenario logger
                // closes. Append the event to the open stream; publishing is
                // deliberately deferred until normal episode shutdown so an
                // AssetDatabase refresh cannot interrupt the dashboard frame.
                twinLogger.LogRecord(twin);
            }

            MajorUpdateRegistered?.Invoke();
            Debug.Log($"ATADTRL TWIN: {MajorUpdateTitle}. {SyncReason}");
        }

        private void ApplyMajorUpdateToTwin(TwinWorldStateRecord twin)
        {
            if (twin == null) return;

            twin.scenarioId = majorScenarioId;
            twin.change_score = 1f;
            twin.sync_trigger = true;
            twin.critical_event = true;
            twin.sync_reason = SyncReason;
            twin.changed_components = ChangedComponents.Replace(", ", "|");
            twin.state_age = 0f;
            twin.last_sync_timestamp = twin.timestamp;
            twin.current_task_stage = majorTerminalStage;
        }

        public void EndScenario()
        {
            twinLogger.EndScenario();
            DataLoggingActive = false;

            CurrentScenarioId = MajorUpdateLatched ? majorScenarioId : -1;
            previousObservation = null;
        }

        private void OnDestroy()
        {
            twinLogger?.CloseAll();
        }
    }
}
