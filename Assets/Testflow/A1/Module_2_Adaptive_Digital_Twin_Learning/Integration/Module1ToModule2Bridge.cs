using UnityEngine;
using ATADTRL.Core;
using ATADTRL.Logging;
using ATADTRL.Scenarios;
using ATADTRL.Sensors;

namespace ATADTRL.Module2
{
    /// <summary>
    /// Runtime bridge connecting Module 1 Unified Observation generation
    /// to Module 2 Adaptive Digital Twin processing.
    /// </summary>
    public class Module1ToModule2Bridge :
        MonoBehaviour
    {
        [Header("Module 2")]
        [SerializeField]
        private Module2Manager module2Manager;

        [Header("Debug")]
        [SerializeField]
        private bool showRuntimeMessages = false;

        private long receivedObservationCount = 0;
        private ScenarioManager scenarioManager;
        private float nextIdleSampleTime;
        private long idleStep;
        private bool monitoringStopSent;

        public long ReceivedObservationCount =>
            receivedObservationCount;

        // ============================================================
        // SUBSCRIBE
        // ============================================================

        private void OnEnable()
        {
            ObservationLogger.ObservationLogged +=
                HandleObservation;

            ObservationLogger.ScenarioStarted +=
                HandleScenarioStarted;

            ObservationLogger.ScenarioEnded +=
                HandleScenarioEnded;

            AttachScenarioManager();
        }

        // ============================================================
        // UNSUBSCRIBE
        // ============================================================

        private void OnDisable()
        {
            ObservationLogger.ObservationLogged -=
                HandleObservation;

            ObservationLogger.ScenarioStarted -=
                HandleScenarioStarted;

            ObservationLogger.ScenarioEnded -=
                HandleScenarioEnded;

            DetachScenarioManager();
        }

        // ============================================================
        // START
        // ============================================================

        private void Start()
        {
            if (module2Manager == null)
            {
                module2Manager =
                    GetComponent<Module2Manager>();
            }

            if (module2Manager == null)
            {
                module2Manager =
                    FindAnyObjectByType<
                        Module2Manager>();
            }

            if (module2Manager == null)
            {
                Debug.LogError(
                    "[M1→M2] Module2Manager not found.");
            }
            else
            {
                Debug.Log(
                    "[M1→M2] Bridge connected.");
            }

            AttachScenarioManager();
        }

        private void Update()
        {
            if (module2Manager == null)
                module2Manager = GetComponent<Module2Manager>() ?? FindAnyObjectByType<Module2Manager>();
            if (scenarioManager == null)
                AttachScenarioManager();

            if (module2Manager == null || scenarioManager == null)
                return;

            if (!scenarioManager.WarehouseOperationsActive)
            {
                if (!monitoringStopSent)
                {
                    module2Manager.StopMonitoring();
                    monitoringStopSent = true;
                }
                return;
            }

            monitoringStopSent = false;
            if (scenarioManager.IsRunning || Time.unscaledTime < nextIdleSampleTime)
                return;

            SensorManager sensors = scenarioManager.sensorManager;
            if (sensors == null) return;

            nextIdleSampleTime = Time.unscaledTime + 0.2f;
            module2Manager.BeginIdleMonitoring();
            ObservationRecord live = sensors.BuildObservation(
                0, 0, ++idleStep, Time.realtimeSinceStartupAsDouble);
            live.current_task_stage = "IDLE_LIVE_MONITORING";
            live.sensor_status = "LIVE_NO_LOGGING";
            live.camera_valid = sensors.camera != null && !live.sensor_dropout_flag;
            live.lidar_valid = sensors.lidar != null && !live.sensor_dropout_flag;
            module2Manager.ProcessLiveObservation(live);
        }

        private void AttachScenarioManager()
        {
            ScenarioManager found =
                FindAnyObjectByType<ScenarioManager>();

            if (found == scenarioManager)
                return;

            DetachScenarioManager();
            scenarioManager = found;

            if (scenarioManager != null)
                scenarioManager.OnStatusMessage += HandleScenarioStatus;
        }

        private void DetachScenarioManager()
        {
            if (scenarioManager != null)
                scenarioManager.OnStatusMessage -= HandleScenarioStatus;

            scenarioManager = null;
        }

        /// <summary>
        /// ScenarioManager publishes this message at the exact frame where
        /// physical penetration is confirmed.  Forward it immediately to the
        /// twin instead of waiting for CSV closure and episode completion.
        /// </summary>
        private void HandleScenarioStatus(string message)
        {
            if (module2Manager == null ||
                module2Manager.CurrentScenarioId != 1 ||
                string.IsNullOrWhiteSpace(message) ||
                message.IndexOf("COLLISION_HUMAN", System.StringComparison.OrdinalIgnoreCase) < 0)
                return;

            module2Manager.RegisterTerminalOutcome(
                1,
                "COLLISION_HUMAN",
                1);
        }

        // ============================================================
        // SCENARIO START
        // ============================================================

        private void HandleScenarioStarted(
            int scenarioId)
        {
            if (module2Manager == null)
                return;

            receivedObservationCount = 0;

            module2Manager.BeginScenario(
                scenarioId);

            Debug.Log(
                $"[M1→M2] Scenario {scenarioId:D2} started.");
        }

        // ============================================================
        // OBSERVATION
        // ============================================================

        private void HandleObservation(
            ObservationRecord record)
        {
            if (module2Manager == null ||
                record == null)
                return;

            receivedObservationCount++;

            module2Manager.ProcessObservation(
                record);

            if (showRuntimeMessages &&
                receivedObservationCount % 50 == 0)
            {
                Debug.Log(
                    $"[M1→M2] Observations received: " +
                    receivedObservationCount);
            }
        }

        // ============================================================
        // SCENARIO END
        // ============================================================

        private void HandleScenarioEnded()
        {
            if (module2Manager == null)
                return;

            module2Manager.EndScenario();

            Debug.Log(
                $"[M1→M2] Scenario ended. " +
                $"Records processed: " +
                receivedObservationCount);
        }
    }
}
