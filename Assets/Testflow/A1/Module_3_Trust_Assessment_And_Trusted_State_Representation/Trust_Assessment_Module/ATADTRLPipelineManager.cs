using System;
using System.Globalization;
using UnityEngine;
using ATADTRL.Core;
using ATADTRL.Logging;
using ATADTRL.Module2;
using ATADTRL.Module4;
using ATADTRL.Navigation;
using ATADTRL.Scenarios;
using ATADTRL.TestFlow;
using ATADTRL.TestFlow.A1.Logging;
using ATADTRL.TestFlow.A1.Module1;
using ATADTRL.TestFlow.Extensions;
using ATADTRL.Trust;

namespace ATADTRL.Pipeline
{
    /// <summary>
    /// A1 end-to-end integration pilot for Modules 2-4.  The Module 1
    /// baseline remains available; Full Pipeline adds trust assessment,
    /// context prediction and a safety action gate to the same A1 episode.
    /// Module 2 loads the trained A1 PPO actor; Module 3 supplies its trusted
    /// state and Module 4 safety-verifies the learned action before execution.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ATADTRLPipelineManager : MonoBehaviour
    {
        public enum ExecutionMode { Module1Baseline, FullATADTRL }
        public enum SecureAction { Forward, SlowDown, Wait, Left, Right, Pick, Place, Replan, FusedNavigate }

        [Header("A1 Pipeline Pilot")]
        [SerializeField] private ExecutionMode executionMode = ExecutionMode.FullATADTRL;
        [SerializeField, Range(0.25f, 3f)] private float safeSeparation = 1.25f;
        [SerializeField, Range(1f, 6f)] private float predictionHorizon = 4f;
        [SerializeField, Range(0.1f, 2f)] private float clearConfirmationSeconds = 0.8f;

        private Module2Manager module2;
        private ScenarioManager scenarios;
        private A1PipelineCsvLogger pipelineLogger;
        private PPOPolicyLearning ppoPolicy;
        private PPOPolicyInference latestPPO = PPOPolicyInference.Fallback;
        private ScenarioPPOPolicyLearning a4Policy;
        private ScenarioPPOPolicyLearning t3Policy;
        private ScenarioPPOInference latestScenarioPolicy;
        private int activeScenario = -1;
        private int activeEpisodeId = -1;
        private bool safetyHold;
        private float clearTimer;
        private Vector2 blindCorner;
        private string baselineOutcome = "PENDING";
        private int baselineCollisions;
        private string activeScenarioCode = "";
        private string activeScenarioName = "";
        private bool a4RackRelocated;
        private Vector3 a4OriginalRackPosition;
        private Vector3 a4RelocatedStoragePosition;
        private bool t3DegradationDetected;
        private float previousLidarFront = float.NaN;
        private float lidarVariationEma;

        public ExecutionMode Mode => executionMode;
        public int ActiveScenarioId => activeScenario;
        public string ActiveScenarioCode => activeScenarioCode;
        public string ActiveScenarioName => activeScenarioName;
        public Vector3 A4RelocatedStoragePosition => a4RelocatedStoragePosition;
        public bool FullPipelineEnabled => executionMode == ExecutionMode.FullATADTRL;
        public float LidarTrust { get; private set; }
        public float CameraTrust { get; private set; }
        public float ImuTrust { get; private set; }
        public float EncoderTrust { get; private set; }
        public float CommunicationTrust { get; private set; }
        public float OverallTrust { get; private set; }
        public TrustAwareWorldState CurrentTrustedState { get; private set; }
        public float CollisionRisk { get; private set; }
        public float PredictedMinimumSeparation { get; private set; } = float.PositiveInfinity;
        public float TimeToClosestApproach { get; private set; }
        public string ContextStatus { get; private set; } = "Waiting for A1";
        public SecureAction SelectedAction { get; private set; } = SecureAction.Wait;
        public string DecisionReason { get; private set; } = "No active episode";
        public bool SafetyHoldActive => safetyHold;
        public long TrustedStateCount { get; private set; }
        public long DecisionCount { get; private set; }
        public long ContextSampleCount { get; private set; }
        public string ShadowMissionStage { get; private set; } = "WAITING FOR A1";
        public string ShadowOutcome { get; private set; } = "NOT STARTED";
        public float ShadowMissionProgress { get; private set; }
        public float ForwardProbability { get; private set; }
        public float SlowProbability { get; private set; }
        public float WaitProbability { get; private set; }
        public float LeftProbability { get; private set; }
        public float RightProbability { get; private set; }
        public bool ReplayReady { get; private set; }
        public bool ReplayRunning { get; private set; }
        public bool ReplayCompleted { get; private set; }
        public int PolicyRevision { get; private set; } = 1;
        public string AppliedChanges { get; private set; } = "No update has been applied";
        public bool PPOModelLoaded => ppoPolicy != null && ppoPolicy.IsModelLoaded;
        public string PPOPolicyId => ppoPolicy != null ? ppoPolicy.PolicyId : "PPO_MODEL_NOT_LOADED";
        public int PPOTrainingTimesteps => ppoPolicy != null ? ppoPolicy.TrainingTimesteps : 0;
        public int PPOEvaluationEpisodes => ppoPolicy != null ? ppoPolicy.EvaluationEpisodes : 0;
        public float PPOEvaluationSuccessRate => ppoPolicy != null ? ppoPolicy.EvaluationSuccessRate : 0f;
        public float PPOEvaluationCollisionRate => ppoPolicy != null ? ppoPolicy.EvaluationCollisionRate : 1f;
        public string ActivePolicyId => activeScenarioCode == "A4" ? a4Policy?.PolicyId :
            activeScenarioCode == "T3" ? t3Policy?.PolicyId : PPOPolicyId;
        public RobotNavigationStatus CurrentNavigationStatus { get; private set; }
        public ATADTRLPerformanceEvaluation LatestPerformanceEvaluation { get; private set; }
        public event Action SafeReplayRequested;

        private void Awake()
        {
            module2 = GetComponent<Module2Manager>();
            if (module2 == null) module2 = FindAnyObjectByType<Module2Manager>();
            scenarios = FindAnyObjectByType<ScenarioManager>();
            pipelineLogger = new A1PipelineCsvLogger();
            ppoPolicy = new PPOPolicyLearning();
            a4Policy = new ScenarioPPOPolicyLearning("A4_PPO_Policy");
            t3Policy = new ScenarioPPOPolicyLearning("T3_PPO_Policy");
            if (GetComponent<ATADTRL.UI.ATADTRLOutcomeDashboard>() == null)
                gameObject.AddComponent<ATADTRL.UI.ATADTRLOutcomeDashboard>();
            if (GetComponent<ATADTRL.TestFlow.A1.UI.A1ShadowSimulationDisplay>() == null)
                gameObject.AddComponent<ATADTRL.TestFlow.A1.UI.A1ShadowSimulationDisplay>();
            if (GetComponent<DeployATADTRLInUnityDigitalTwin>() == null)
                gameObject.AddComponent<DeployATADTRLInUnityDigitalTwin>();
            if (GetComponent<UnityWarehouseEnvironmentSimulation>() == null)
                gameObject.AddComponent<UnityWarehouseEnvironmentSimulation>();
            if (GetComponent<ATADTRL.TestFlow.Extensions.A4T3SafeReplayDisplay>() == null)
                gameObject.AddComponent<ATADTRL.TestFlow.Extensions.A4T3SafeReplayDisplay>();
        }

        private void OnEnable()
        {
            ObservationLogger.ScenarioStarted += BeginScenario;
            ObservationLogger.ObservationLogged += ProcessObservation;
            ObservationLogger.ScenarioEnded += EndScenario;
            if (scenarios != null) scenarios.OnScenarioCompleted += HandleScenarioCompleted;
            if (scenarios != null) scenarios.OnRackRelocated += HandleRackRelocated;
        }

        private void OnDisable()
        {
            ObservationLogger.ScenarioStarted -= BeginScenario;
            ObservationLogger.ObservationLogged -= ProcessObservation;
            ObservationLogger.ScenarioEnded -= EndScenario;
            if (scenarios != null) scenarios.OnScenarioCompleted -= HandleScenarioCompleted;
            if (scenarios != null) scenarios.OnRackRelocated -= HandleRackRelocated;
            pipelineLogger?.EndScenario();
        }

        public void ToggleMode()
        {
            SetMode(FullPipelineEnabled ? ExecutionMode.Module1Baseline : ExecutionMode.FullATADTRL);
        }

        public void SetMode(ExecutionMode mode)
        {
            if (scenarios != null && scenarios.IsRunning) return;
            executionMode = mode;
            DecisionReason = FullPipelineEnabled
                ? "Full ATADTRL selected; scenario policy will use trusted twin context"
                : "Module 1 baseline selected; physical scenario behavior is unchanged";
            Debug.Log($"ATADTRL PIPELINE: execution mode = {executionMode}.");
        }

        private void BeginScenario(int scenarioId)
        {
            ScenarioDefinition definition = scenarios?.Scenarios.Find(s => s.scenarioId == scenarioId);
            activeScenarioCode = definition != null ? definition.scenarioCode : $"S{scenarioId}";
            activeScenarioName = definition != null ? definition.scenarioName : "Warehouse scenario";
            if (!IsSupportedScenario(scenarioId))
            {
                activeScenario = -1;
                return;
            }
            activeScenario = scenarioId;
            activeEpisodeId = -1;
            TrustedStateCount = DecisionCount = ContextSampleCount = 0;
            safetyHold = false;
            clearTimer = 0f;
            ReplayReady = false;
            ReplayRunning = false;
            ReplayCompleted = false;
            CollisionRisk = 0f;
            latestPPO = PPOPolicyInference.Fallback;
            a4RackRelocated = false;
            t3DegradationDetected = false;
            previousLidarFront = float.NaN;
            lidarVariationEma = 0f;
            ContextStatus = scenarioId == 1 ? "A1 twin initialization" :
                scenarioId == 4 ? "A4 rack-state initialization" : "T3 multi-sensor trust initialization";
            SelectedAction = SecureAction.Forward;
            ShadowMissionStage = $"OBSERVING DISPLAY 1 {activeScenarioCode} BASELINE";
            ShadowOutcome = "WAITING FOR MAJOR UPDATE";
            ShadowMissionProgress = 0f;
            baselineOutcome = "PENDING";
            baselineCollisions = 0;
            CurrentNavigationStatus = new RobotNavigationStatus(ShadowMissionStage, 0f,
                SelectedAction.ToString(), false, 0f, ShadowOutcome);
            LatestPerformanceEvaluation = default;
            pipelineLogger.BeginScenario(scenarioId);
            ResolveBlindCorner();
            Debug.Log($"ATADTRL PIPELINE: Scenario {scenarioId:D2} started in {executionMode} mode. " +
                      $"{activeScenarioCode} pipeline CSV files will be logged under '{pipelineLogger.CurrentScenarioFolder}'.");
        }

        private static bool IsSupportedScenario(int scenarioId) => scenarioId == 1 || scenarioId == 4 || scenarioId == 13;

        private void ResolveBlindCorner()
        {
            blindCorner = Vector2.zero;
            ScenarioDefinition a1 = scenarios?.Scenarios.Find(s => s.scenarioCode == "A1");
            if (a1 != null && a1.firstTaskDeliveryWaypoints != null && a1.firstTaskDeliveryWaypoints.Count >= 2)
            {
                Vector3 corner = a1.firstTaskDeliveryWaypoints[1];
                blindCorner = new Vector2(corner.x, corner.z);
            }
        }

        private void ProcessObservation(ObservationRecord observation)
        {
            if (observation == null || observation.scenarioId != activeScenario) return;
            activeEpisodeId = observation.episodeId;
            AssessTrust(observation);
            BuildContext(observation);
            if (observation.scenarioId == 4)
                latestScenarioPolicy = a4Policy.Infer(new[]
                {
                    a4RackRelocated ? 1f : 0f, observation.carrying_status ? 1f : 0f,
                    Mathf.Clamp01(PredictedMinimumSeparation / 15f), a4RackRelocated ? 1f : 0f,
                    OverallTrust, observation.carrying_status ? 1f : 0f,
                    observation.current_task_stage != null && observation.current_task_stage.Contains("Destination") ? 1f : 0f,
                    ShadowMissionProgress
                });
            else if (observation.scenarioId == 13)
                latestScenarioPolicy = t3Policy.Infer(new[]
                {
                    LidarTrust, CameraTrust, ImuTrust, EncoderTrust, CommunicationTrust,
                    Mathf.Clamp01(CollisionRisk), t3DegradationDetected ? 1f : 0f, ShadowMissionProgress
                });
            ContextSampleCount++;
            LogTrustedState(observation);
            pipelineLogger.LogContext(observation, ContextSampleCount, CollisionRisk,
                PredictedMinimumSeparation, TimeToClosestApproach, ContextStatus);

            bool blindCornerExposure = observation.scenarioId == 1 && (CollisionRisk > 0f ||
                                       ContextStatus.StartsWith("Blind-corner", StringComparison.OrdinalIgnoreCase));
            latestPPO = observation.scenarioId == 1 && ppoPolicy != null
                ? ppoPolicy.Infer(CollisionRisk, PredictedMinimumSeparation, TimeToClosestApproach,
                    OverallTrust, observation.carrying_status, blindCornerExposure,
                    safetyHold, ShadowMissionProgress)
                : PPOPolicyInference.Fallback;
            SelectedAction = SelectSafeAction(observation);
            UpdateShadowPolicy(observation);
            LogDecision(observation);
        }

        private void AssessTrust(ObservationRecord o)
        {
            TrustAssessmentResult result = TrustAssessmentModule.Assess(o);
            LidarTrust = result.Lidar;
            CameraTrust = result.Camera;
            ImuTrust = result.Imu;
            EncoderTrust = result.Encoder;
            CommunicationTrust = result.Communication;
            OverallTrust = result.Overall;
            if (o.scenarioId == 13)
            {
                if (IsFinite(previousLidarFront) && IsFinite(o.lidar_front_distance))
                {
                    float variation = Mathf.Abs(o.lidar_front_distance - previousLidarFront);
                    lidarVariationEma = Mathf.Lerp(lidarVariationEma, variation, .22f);
                }
                previousLidarFront = o.lidar_front_distance;
                if (o.lidar_valid)
                    LidarTrust = Mathf.Clamp01(1f - Mathf.Max(0f, lidarVariationEma - .30f) / 2.2f);
                OverallTrust = .30f * LidarTrust + .20f * CameraTrust + .20f * ImuTrust +
                               .15f * EncoderTrust + .15f * CommunicationTrust;
                if (LidarTrust < .65f && !t3DegradationDetected)
                {
                    t3DegradationDetected = true;
                    module2?.RegisterMajorScenarioUpdate(13, "MAJOR UPDATE - T3 LIDAR DEGRADATION",
                        "LiDAR measurement variance exceeded the trusted-source threshold. EDATS synchronized the sensor-health state.",
                        "lidar.measurement_variance, lidar.trust, trusted_state.source_weights, navigation.policy_input",
                        "CHANGE DETECTION: LiDAR variance increase confirmed\n" +
                        "EDATS: sensor-health state synchronized\n" +
                        "ADAPTIVE TWIN: degraded LiDAR reliability recorded\n" +
                        "CONTEXT + TAM: LiDAR down-weighted; RGB/IMU/encoder retained\n" +
                        "SECURE POLICY: fused-state navigation selected",
                        "LIDAR_DEGRADED");
                }
            }
        }

        private void BuildContext(ObservationRecord o)
        {
            PredictedMinimumSeparation = float.PositiveInfinity;
            TimeToClosestApproach = 0f;
            CollisionRisk = 0f;
            if (o.scenarioId == 4)
            {
                PredictedMinimumSeparation = a4RackRelocated
                    ? Vector3.Distance(a4OriginalRackPosition, a4RelocatedStoragePosition)
                    : 0f;
                CollisionRisk = a4RackRelocated ? 1f : 0f;
                ContextStatus = a4RackRelocated
                    ? $"Rack_4_3 relocated {PredictedMinimumSeparation:F2} m; committed destination is stale"
                    : "A4 target rack matches the committed destination";
                return;
            }
            if (o.scenarioId == 13)
            {
                PredictedMinimumSeparation = 0f;
                CollisionRisk = 1f - LidarTrust;
                ContextStatus = t3DegradationDetected
                    ? $"LiDAR degraded (trust {LidarTrust:P0}); fused RGB/IMU/encoder state remains trusted"
                    : $"T3 sensor fusion nominal; LiDAR trust {LidarTrust:P0}";
                return;
            }
            if (o.scenarioId != 1 || !TryParseVector(o.h1_position, out Vector3 h1) ||
                !TryParseVector(o.h1_velocity, out Vector3 h1Velocity))
            {
                ContextStatus = "No valid A1 H1 context";
                return;
            }

            Vector2 robot = new Vector2(o.estimated_x, o.estimated_y);
            Vector2 human = new Vector2(h1.x, h1.z);
            float yaw = o.estimated_yaw * Mathf.Deg2Rad;
            Vector2 robotVelocity = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw)) * Mathf.Max(0f, o.estimated_velocity);
            Vector2 humanVelocity = new Vector2(h1Velocity.x, h1Velocity.z);
            Vector2 relativePosition = human - robot;
            Vector2 relativeVelocity = humanVelocity - robotVelocity;
            float speedSquared = relativeVelocity.sqrMagnitude;
            float closestTime = speedSquared > 0.0001f
                ? Mathf.Clamp(-Vector2.Dot(relativePosition, relativeVelocity) / speedSquared, 0f, predictionHorizon)
                : 0f;
            PredictedMinimumSeparation = (relativePosition + relativeVelocity * closestTime).magnitude;
            TimeToClosestApproach = closestTime;

            float robotCornerDistance = Vector2.Distance(robot, blindCorner);
            float humanCornerDistance = Vector2.Distance(human, blindCorner);
            bool blindCornerExposure = robotCornerDistance < 7f && humanCornerDistance < 7f;
            float separationRisk = Mathf.Clamp01((safeSeparation * 1.8f - PredictedMinimumSeparation) /
                                                  Mathf.Max(0.1f, safeSeparation * 1.8f));
            float timeRisk = Mathf.Clamp01(1f - closestTime / predictionHorizon);
            CollisionRisk = blindCornerExposure ? separationRisk * (0.35f + 0.65f * timeRisk) : 0f;
            ContextStatus = blindCornerExposure
                ? $"Blind-corner prediction: H1 closest approach {PredictedMinimumSeparation:F2} m in {closestTime:F1} s"
                : "A1 actors are outside the blind-corner conflict zone";
        }

        private SecureAction SelectSafeAction(ObservationRecord o)
        {
            if (o.scenarioId == 4)
            {
                int learned = latestScenarioPolicy.ActionIndex;
                DecisionReason = a4RackRelocated
                    ? $"{a4Policy.PolicyId} selected {a4Policy.ActionName(learned)} from the synchronized rack state"
                    : $"{a4Policy.PolicyId} retained the committed route while rack state is unchanged";
                if (a4RackRelocated) return learned == 2 ? SecureAction.Wait : SecureAction.Replan;
                return learned == 4 ? SecureAction.Place : SecureAction.Forward;
            }
            if (o.scenarioId == 13)
            {
                int learned = latestScenarioPolicy.ActionIndex;
                DecisionReason = t3DegradationDetected
                    ? $"{t3Policy.PolicyId} selected {t3Policy.ActionName(learned)} after TAM down-weighted LiDAR"
                    : $"{t3Policy.PolicyId} confirms nominal multi-sensor navigation";
                if (t3DegradationDetected) return learned == 3 || learned == 4 ? SecureAction.Wait : SecureAction.FusedNavigate;
                return learned == 2 ? SecureAction.SlowDown : SecureAction.Forward;
            }
            IntelligentDecision decision = GenerateIntelligentDecisions.Select(
                FullPipelineEnabled, o.scenarioId, o.current_task_stage, o.carrying_status,
                CollisionRisk, PredictedMinimumSeparation, safeSeparation, OverallTrust,
                safetyHold, clearTimer, clearConfirmationSeconds, latestPPO);
            DecisionReason = decision.Reason;
            clearTimer = decision.NextClearTimer;
            return decision.Action;
        }

        private void UpdateShadowPolicy(ObservationRecord o)
        {
            // Display 1 is deliberately never modified.  This action is
            // executed by the parallel digital-twin branch shown on Display 3.
            if (o.scenarioId == 4)
            {
                float[] p = latestScenarioPolicy.Probabilities;
                ForwardProbability = p != null ? p[0] : .12f;
                SlowProbability = p != null ? p[1] : .58f;
                WaitProbability = p != null ? p[2] : .10f;
                LeftProbability = p != null ? p[3] : .10f;
                RightProbability = p != null ? p[4] : .10f;
                ShadowMissionStage = a4RackRelocated ? "REPLAN TO RELOCATED RACK_4_3" : "A4 TRANSPORT TO BOOKS RACK";
                ShadowMissionProgress = a4RackRelocated ? .58f : .35f;
                safetyHold = false;
                return;
            }
            if (o.scenarioId == 13)
            {
                float[] p = latestScenarioPolicy.Probabilities;
                ForwardProbability = p != null ? p[0] : .20f;
                SlowProbability = p != null ? p[1] : .62f;
                WaitProbability = p != null ? p[2] : .10f;
                LeftProbability = p != null ? p[3] : .05f;
                RightProbability = p != null ? p[4] : .03f;
                ShadowMissionStage = t3DegradationDetected ? "TRUSTED FUSED-STATE NAVIGATION" : "T3 NOMINAL TRANSPORT";
                ShadowMissionProgress = t3DegradationDetected ? .55f : .30f;
                safetyHold = false;
                return;
            }
            if (o.scenarioId != 1) return;
            SecurePolicyDistribution optimized = OptimizeSecureNavigationPolicy.Optimize(
                SelectedAction, CollisionRisk, o.carrying_status, o.current_task_stage,
                ShadowMissionProgress, latestPPO);
            ForwardProbability = optimized.Forward;
            SlowProbability = optimized.Slow;
            WaitProbability = optimized.Wait;
            LeftProbability = optimized.Left;
            RightProbability = optimized.Right;
            safetyHold = optimized.SafetyHold;
            ShadowMissionStage = optimized.MissionStage;
            ShadowMissionProgress = optimized.MissionProgress;

            // Policy inference runs during the baseline, but Display 3 remains
            // paused until the operator applies the post-collision update.
        }

        private void LogTrustedState(ObservationRecord o)
        {
            TwinWorldStateRecord twin = module2 != null ? module2.CurrentTwin : null;
            CurrentTrustedState = TrustAwareStateRepresentation.Construct(o, twin, LidarTrust,
                CameraTrust, ImuTrust, EncoderTrust, CommunicationTrust, OverallTrust, ContextStatus);
            pipelineLogger.LogTrusted(o, CurrentTrustedState);
            TrustedStateCount++;
        }

        private void LogDecision(ObservationRecord o)
        {
            pipelineLogger.LogDecision(o, executionMode.ToString(), SelectedAction.ToString(), CollisionRisk,
                PredictedMinimumSeparation, TimeToClosestApproach, safetyHold, DecisionReason);
            CurrentNavigationStatus = new RobotNavigationStatus(ShadowMissionStage, ShadowMissionProgress,
                SelectedAction.ToString(), safetyHold, CollisionRisk, ShadowOutcome);
            pipelineLogger.LogNavigationStatus(o, CurrentNavigationStatus);
            DecisionCount++;
        }

        private void EndScenario()
        {
            activeScenario = -1;
        }

        private void HandleRackRelocated(string rackId, Vector3 originalPosition, Vector3 validPlacementPosition)
        {
            if (activeScenarioCode != "A4") return;
            a4RackRelocated = true;
            a4OriginalRackPosition = originalPosition;
            a4RelocatedStoragePosition = validPlacementPosition;
            ContextStatus = $"{rackId} relocation detected; committed destination is stale";
            module2?.RegisterMajorScenarioUpdate(4, "MAJOR UPDATE - A4 RACK RELOCATION",
                $"{rackId} moved after route commitment. EDATS forced synchronization before the corrected replay.",
                $"{rackId}.position, destination.approach, destination.storage, route.goal",
                "CHANGE DETECTION: physical rack relocation confirmed\n" +
                "EDATS: rack and destination state synchronized\n" +
                "ADAPTIVE TWIN: relocated storage pose updated\n" +
                "CONTEXT + TAM: new destination validated\n" +
                "SECURE POLICY: replan-to-relocated-rack action generated",
                "RACK_RELOCATED");
        }

        private void HandleScenarioCompleted(ScenarioDefinition scenario, PerformanceRecord performance)
        {
            if (scenario == null || !IsSupportedScenario(scenario.scenarioId)) return;
            activeScenarioCode = scenario.scenarioCode;
            activeScenarioName = scenario.scenarioName;
            baselineOutcome = performance.completionStatus;
            baselineCollisions = performance.collisionCount;
            module2?.RegisterTerminalOutcome(scenario.scenarioId, baselineOutcome, baselineCollisions);
            pipelineLogger.LogOutcome(scenario.scenarioId, activeEpisodeId, baselineOutcome,
                baselineCollisions, ShadowOutcome, ShadowMissionStage, ShadowMissionProgress,
                ContextSampleCount, TrustedStateCount, DecisionCount);

            bool requiresReplay = scenario.scenarioId == 1 && (baselineCollisions > 0 ||
                (!string.IsNullOrWhiteSpace(baselineOutcome) && baselineOutcome.StartsWith("COLLISION"))) ||
                scenario.scenarioId == 4 && string.Equals(baselineOutcome, "WRONG_PLACEMENT", StringComparison.OrdinalIgnoreCase) ||
                scenario.scenarioId == 13 && t3DegradationDetected;
            if (requiresReplay)
            {
                ReplayReady = true;
                ReplayRunning = false;
                ReplayCompleted = false;
                PolicyRevision++;
                SelectedAction = scenario.scenarioId == 4 ? SecureAction.Replan :
                    scenario.scenarioId == 13 ? SecureAction.FusedNavigate : SecureAction.Wait;
                safetyHold = scenario.scenarioId == 1;
                ShadowOutcome = $"UPDATE READY — {scenario.scenarioCode} SAFE REPLAY AVAILABLE";
                ShadowMissionStage = "PRESS APPLY UPDATE & RUN SAFE REPLAY";
                ShadowMissionProgress = 0f;
                if (scenario.scenarioId == 1)
                    AppliedChanges = "M2: EDATS synchronized the collision and blind-corner context\n" +
                        $"M2 PPO: {PPOPolicyId} trained for {PPOTrainingTimesteps:N0} steps and loaded for inference\n" +
                        "M3: TAM selected the trusted LiDAR/RGB/IMU/encoder state\n" +
                        "M4: PPO action passed through the safety shield before robot execution";
                else if (scenario.scenarioId == 4)
                    AppliedChanges = "M2: EDATS synchronized relocated Rack_4_3 and destination pose\n" +
                        "M2: context world model replaced the stale rack goal\n" +
                        "M3: rack-position and inventory state validated\n" +
                        "M4: secure policy replans and verifies placement at the relocated rack";
                else
                    AppliedChanges = "M2: EDATS synchronized the LiDAR degradation interval\n" +
                        "M2: context model retained RGB/IMU/encoder evidence\n" +
                        "M3: TAM down-weighted LiDAR in the trusted state\n" +
                        "M4: secure policy continues using fused-state navigation";
                DecisionReason = $"{scenario.scenarioCode} revision prepared; operator confirmation required";
                Debug.Log($"ATADTRL PIPELINE: {scenario.scenarioCode} update package ready. Display 3 is waiting for operator replay command.");
            }
        }

        public bool ApplyUpdateAndRunSafeReplay()
        {
            if (!ReplayReady || ReplayRunning) return false;

            ReplayReady = false;
            ReplayRunning = true;
            ReplayCompleted = false;
            ShadowOutcome = $"SAFE REPLAY RUNNING — POLICY REVISION {PolicyRevision}";
            ShadowMissionStage = "APPLYING TRUSTED-CONTEXT SAFETY UPDATE";
            ShadowMissionProgress = 0f;
            RestoreActiveScenarioPolicyDistribution();
            SafeReplayRequested?.Invoke();
            if (activeScenarioCode == "A4" || activeScenarioCode == "T3")
            {
                ATADTRL.TestFlow.Extensions.A4T3SafeReplayDisplay replay =
                    GetComponent<ATADTRL.TestFlow.Extensions.A4T3SafeReplayDisplay>();
                if (replay == null)
                    replay = gameObject.AddComponent<ATADTRL.TestFlow.Extensions.A4T3SafeReplayDisplay>();
                replay.BeginSafeReplay();
            }
            Debug.Log($"ATADTRL PIPELINE: operator applied policy revision {PolicyRevision}; Display 3 safe replay started.");
            return true;
        }

        public void ReportSafeReplayStage(string stage, float progress, SecureAction action)
        {
            if (!ReplayRunning) return;
            ShadowMissionStage = stage;
            ShadowMissionProgress = Mathf.Clamp01(progress);
            SelectedAction = action;
            CurrentNavigationStatus = new RobotNavigationStatus(stage, ShadowMissionProgress,
                action.ToString(), action == SecureAction.Wait, CollisionRisk, ShadowOutcome);
        }

        public void ReportSafeReplayCompleted()
        {
            string outcome = activeScenarioCode == "A4"
                ? "TASK COMPLETED — RELOCATED RACK UPDATED — CORRECT PLACEMENT VERIFIED"
                : activeScenarioCode == "T3"
                    ? "TASK COMPLETED — DEGRADED LIDAR DOWN-WEIGHTED — DELIVERY VERIFIED"
                    : "TASK COMPLETED — COLLISION AVOIDED — PARCEL DELIVERED TO D2";
            string stage = activeScenarioCode == "A4" ? "CORRECT RACK PLACEMENT VERIFIED" :
                activeScenarioCode == "T3" ? "TRUSTED FUSION DELIVERY VERIFIED" : "VERIFY RELEASE COMPLETE";
            ReportSafeReplayCompleted(outcome, stage);
        }

        public void ReportSafeReplayCompleted(string outcome, string stage)
        {
            ReplayRunning = false;
            ReplayCompleted = true;
            ShadowOutcome = outcome;
            ShadowMissionStage = stage;
            ShadowMissionProgress = 1f;
            SelectedAction = SecureAction.Wait;
            // Retain the learned PPO evidence on Display 3 after completion.
            // Clearing these values made the main decision card appear empty.
            RestoreActiveScenarioPolicyDistribution();
            int completedScenarioId = activeScenarioCode == "A4" ? 4 : activeScenarioCode == "T3" ? 13 : 1;
            pipelineLogger.LogOutcome(completedScenarioId, activeEpisodeId, baselineOutcome, baselineCollisions,
                ShadowOutcome, ShadowMissionStage, ShadowMissionProgress, ContextSampleCount,
                TrustedStateCount, DecisionCount);
            module2?.ReturnToNormalLiveState();
            DecisionReason = $"{activeScenarioCode} update verified; digital twin returned to normal live monitoring";
            CurrentNavigationStatus = new RobotNavigationStatus(ShadowMissionStage, 1f,
                SelectedAction.ToString(), false, 0f, ShadowOutcome);
            LatestPerformanceEvaluation = EvaluateATADTRLPerformance.Evaluate(
                baselineOutcome, baselineCollisions, ShadowOutcome, ReplayCompleted);
            Debug.Log($"ATADTRL SHADOW RESULT: operator-triggered {activeScenarioCode} replay completed. {ShadowOutcome}");
        }

        private void RestoreActiveScenarioPolicyDistribution()
        {
            if (activeScenarioCode != "A4" && activeScenarioCode != "T3") return;
            float[] probabilities = latestScenarioPolicy.Probabilities;
            if (probabilities != null && probabilities.Length >= 5)
            {
                ForwardProbability = Mathf.Clamp01(probabilities[0]);
                SlowProbability = Mathf.Clamp01(probabilities[1]);
                WaitProbability = Mathf.Clamp01(probabilities[2]);
                LeftProbability = Mathf.Clamp01(probabilities[3]);
                RightProbability = Mathf.Clamp01(probabilities[4]);
                return;
            }

            // Visible deterministic fallback while a scenario model is still
            // loading; normal execution replaces it with trained inference.
            if (activeScenarioCode == "T3")
            {
                ForwardProbability = .20f;
                SlowProbability = .62f;
                WaitProbability = .10f;
                LeftProbability = .05f;
                RightProbability = .03f;
            }
            else
            {
                ForwardProbability = .12f;
                SlowProbability = .58f;
                WaitProbability = .10f;
                LeftProbability = .10f;
                RightProbability = .10f;
            }
        }

        private void OnDestroy()
        {
            pipelineLogger?.CloseAll();
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool TryParseVector(string text, out Vector3 value)
        {
            value = Vector3.zero;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string[] parts = text.Split(':');
            if (parts.Length < 3) return false;
            return float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out value.x) &&
                   float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out value.y) &&
                   float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out value.z) &&
                   IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }
    }
}
