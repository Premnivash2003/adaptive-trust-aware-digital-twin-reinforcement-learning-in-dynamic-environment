using System.Globalization;
using System.IO;
using UnityEngine;
using ATADTRL.Core;
using ATADTRL.Logging;
using ATADTRL.Trust;
using ATADTRL.Module4;

namespace ATADTRL.TestFlow.A1.Logging
{
    /// <summary>
    /// Owns every CSV produced by the A1 Module 2-4 test flow.  Files are
    /// written live below persistentDataPath and automatically published into
    /// StreamingAssets/Dataset after completed records are flushed.
    /// </summary>
    public sealed class A1PipelineCsvLogger
    {
        private const string TrustedHeader =
            "timestamp,scenario_id,episode_id,step_id,lidar_trust,camera_trust,imu_trust,encoder_trust,communication_trust,overall_trust,twin_robot_x,twin_robot_z,trusted_robot_x,trusted_robot_z,trusted_robot_yaw,trusted_robot_velocity,h1_position,twin_age,state_source,rejected_sources,context_status";
        private const string ContextHeader =
            "timestamp,scenario_id,episode_id,step_id,context_sample,collision_risk,predicted_minimum_separation,time_to_closest_approach,context_status";
        private const string DecisionHeader =
            "timestamp,scenario_id,episode_id,step_id,execution_mode,policy_id,selected_action,collision_risk,predicted_minimum_separation,time_to_closest_approach,shadow_hold,reason";
        private const string OutcomeHeader =
            "timestamp,scenario_id,episode_id,physical_baseline_outcome,physical_collisions,shadow_outcome,shadow_stage,shadow_progress,context_samples,trusted_states,policy_decisions";

        private readonly string root;
        private CSVLogger trustedScenario, contextScenario, decisionScenario, navigationScenario, outcomeScenario;
        private CSVLogger trustedAll, contextAll, decisionAll, navigationAll, outcomeAll;

        public string CurrentScenarioFolder { get; private set; }

        public A1PipelineCsvLogger()
        {
            root = Path.Combine(Application.persistentDataPath, "ATADTRL_Dataset");
        }

        public void BeginScenario(int scenarioId)
        {
            EndScenario();
            EnsureCombinedLoggers();
            CurrentScenarioFolder = Path.Combine(root, $"Scenario_{scenarioId:D2}");
            trustedScenario = Open(Path.Combine(CurrentScenarioFolder, "Trusted_State.csv"), TrustedHeader);
            contextScenario = Open(Path.Combine(CurrentScenarioFolder, "Context_World_State.csv"), ContextHeader);
            decisionScenario = Open(Path.Combine(CurrentScenarioFolder, "RL_Training_Log.csv"), DecisionHeader);
            navigationScenario = Open(Path.Combine(CurrentScenarioFolder, "Robot_Navigation_Status.csv"),
                RobotNavigationStatus.CsvHeader);
            outcomeScenario = Open(Path.Combine(CurrentScenarioFolder, "A1_Pipeline_Outcome.csv"), OutcomeHeader);
        }

        public void LogTrusted(ObservationRecord o, TrustAwareWorldState state)
        {
            if (state == null) return;
            string row = string.Join(",", F(o.timestamp), o.scenarioId, o.episodeId, o.stepId,
                F(state.lidarTrust), F(state.cameraTrust), F(state.imuTrust), F(state.encoderTrust),
                F(state.communicationTrust), F(state.overallTrust), F(state.twinRobotX), F(state.twinRobotZ),
                F(state.trustedRobotX), F(state.trustedRobotZ), F(state.trustedRobotYaw),
                F(state.trustedRobotVelocity), Q(state.h1Position), F(state.twinAge), Q(state.stateSource),
                Q(state.rejectedSources), Q(state.contextStatus));
            Write(trustedScenario, trustedAll, row);
        }

        public void LogContext(ObservationRecord o, long sample, float risk, float separation, float closestTime,
            string status)
        {
            string row = string.Join(",", F(o.timestamp), o.scenarioId, o.episodeId, o.stepId, sample,
                F(risk), F(separation), F(closestTime), Q(status));
            Write(contextScenario, contextAll, row);
        }

        public void LogDecision(ObservationRecord o, string mode, string action, float risk, float separation,
            float closestTime, bool hold, string reason)
        {
            string policyId = o.scenarioId == 4 ? "A4_PPO_V1" :
                o.scenarioId == 13 ? "T3_PPO_V1" : "A1_PPO_V1";
            string row = string.Join(",", F(o.timestamp), o.scenarioId, o.episodeId, o.stepId, Q(mode),
                policyId, Q(action), F(risk), F(separation), F(closestTime), hold, Q(reason));
            Write(decisionScenario, decisionAll, row);
        }

        public void LogNavigationStatus(ObservationRecord observation, RobotNavigationStatus status)
        {
            if (observation == null) return;
            Write(navigationScenario, navigationAll, status.ToCsvRow(observation.timestamp,
                observation.scenarioId, observation.episodeId, observation.stepId));
        }

        public void LogOutcome(int scenarioId, int episodeId, string baselineOutcome, int collisions,
            string shadowOutcome, string shadowStage, float progress, long contextSamples, long trustedStates,
            long policyDecisions)
        {
            string row = string.Join(",", F(Time.realtimeSinceStartupAsDouble), scenarioId, episodeId,
                Q(baselineOutcome), collisions, Q(shadowOutcome), Q(shadowStage), F(progress), contextSamples,
                trustedStates, policyDecisions);
            Write(outcomeScenario, outcomeAll, row);
            DatasetPublisher.PublishCompletedFiles();
            PublishScenarioModuleOutputs(scenarioId);
        }

        public void EndScenario()
        {
            Close(ref trustedScenario); Close(ref contextScenario); Close(ref decisionScenario);
            Close(ref navigationScenario); Close(ref outcomeScenario);
        }

        public void CloseAll()
        {
            EndScenario();
            Close(ref trustedAll); Close(ref contextAll); Close(ref decisionAll);
            Close(ref navigationAll); Close(ref outcomeAll);
        }

        private static void Write(CSVLogger scenario, CSVLogger combined, string row)
        {
            scenario?.WriteRow(row);
            combined?.WriteRow(row);
        }

        private static CSVLogger Open(string path, string header)
        {
            var logger = new CSVLogger();
            logger.Open(path, header);
            return logger;
        }

        private void EnsureCombinedLoggers()
        {
            if (trustedAll == null) trustedAll = Open(Path.Combine(root, "Trusted_State_All.csv"), TrustedHeader);
            if (contextAll == null) contextAll = Open(Path.Combine(root, "Context_World_State_All.csv"), ContextHeader);
            if (decisionAll == null) decisionAll = Open(Path.Combine(root, "RL_Training_Log_All.csv"), DecisionHeader);
            if (navigationAll == null) navigationAll = Open(Path.Combine(root, "Robot_Navigation_Status_All.csv"),
                RobotNavigationStatus.CsvHeader);
            if (outcomeAll == null) outcomeAll = Open(Path.Combine(root, "A1_Pipeline_Outcome_All.csv"), OutcomeHeader);
        }

        private static void Close(ref CSVLogger logger)
        {
            logger?.Close();
            logger = null;
        }

        /// <summary>
        /// Mirrors completed A1 outputs beside Module 2 so they are visible in
        /// Unity's Project window. StreamingAssets remains the canonical dataset.
        /// </summary>
        private static void PublishScenarioModuleOutputs(int scenarioId)
        {
#if UNITY_EDITOR
            string scenarioCode = scenarioId == 4 ? "A4" : scenarioId == 13 ? "T3" : "A1";
            string scenarioFolder = $"Scenario_{scenarioId:D2}";
            string moduleRoot = Path.Combine(Application.dataPath, "Testflow", scenarioCode,
                "Module_2_Adaptive_Digital_Twin_Learning", "CSV_Outputs");
            string scenarioRoot = Path.Combine(moduleRoot, scenarioFolder);
            string trustRoot = Path.Combine(Application.dataPath, "Testflow", scenarioCode,
                "Module_3_Trust_Assessment_And_Trusted_State_Representation", "CSV_Outputs");
            string trustScenarioRoot = Path.Combine(trustRoot, scenarioFolder);
            string decisionRoot = Path.Combine(Application.dataPath, "Testflow", scenarioCode,
                "Module_4_Secure_Reinforcement_Learning_And_Intelligent_Decision_Making", "CSV_Outputs");
            string decisionScenarioRoot = Path.Combine(decisionRoot, scenarioFolder);
            Directory.CreateDirectory(scenarioRoot);
            Directory.CreateDirectory(trustScenarioRoot);
            Directory.CreateDirectory(decisionScenarioRoot);

            CopyOutput("Twin_World_State_All.csv", Path.Combine(moduleRoot, "Twin_World_State_All.csv"));
            CopyOutput("Context_World_State_All.csv", Path.Combine(moduleRoot, "Context_World_State_All.csv"));
            CopyOutput(Path.Combine(scenarioFolder, "Twin_World_State.csv"),
                Path.Combine(scenarioRoot, "Twin_World_State.csv"));
            CopyOutput(Path.Combine(scenarioFolder, "Context_World_State.csv"),
                Path.Combine(scenarioRoot, "Context_World_State.csv"));

            CopyOutput("Trusted_State_All.csv", Path.Combine(trustRoot, "Trusted_State_All.csv"));
            CopyOutput(Path.Combine(scenarioFolder, "Trusted_State.csv"),
                Path.Combine(trustScenarioRoot, "Trusted_State.csv"));

            CopyOutput("RL_Training_Log_All.csv", Path.Combine(decisionRoot, "RL_Training_Log_All.csv"));
            CopyOutput("Robot_Navigation_Status_All.csv", Path.Combine(decisionRoot, "Robot_Navigation_Status_All.csv"));
            CopyOutput("A1_Pipeline_Outcome_All.csv", Path.Combine(decisionRoot, "A1_Pipeline_Outcome_All.csv"));
            CopyOutput(Path.Combine(scenarioFolder, "RL_Training_Log.csv"),
                Path.Combine(decisionScenarioRoot, "RL_Training_Log.csv"));
            CopyOutput(Path.Combine(scenarioFolder, "Robot_Navigation_Status.csv"),
                Path.Combine(decisionScenarioRoot, "Robot_Navigation_Status.csv"));
            CopyOutput(Path.Combine(scenarioFolder, "A1_Pipeline_Outcome.csv"),
                Path.Combine(decisionScenarioRoot, $"{scenarioCode}_Pipeline_Outcome.csv"));

            UnityEditor.AssetDatabase.Refresh();
            Debug.Log($"ATADTRL {scenarioCode}: scenario pipeline CSV outputs refreshed in '{moduleRoot}'.");
#endif
        }

#if UNITY_EDITOR
        private static void CopyOutput(string relativeSource, string destination)
        {
            string source = Path.Combine(DatasetPublisher.RuntimeRoot, relativeSource);
            if (!File.Exists(source)) source = Path.Combine(DatasetPublisher.PublishedRoot, relativeSource);
            if (!File.Exists(source)) return;

            string directory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            try
            {
                File.Copy(source, destination, true);
            }
            catch (IOException exception)
            {
                // These copies are convenience mirrors inside Assets. Unity's
                // importer can briefly lock them during play; never allow that
                // to abort the episode-completed event chain or dashboard.
                Debug.LogWarning($"ATADTRL A1: deferred CSV mirror '{Path.GetFileName(destination)}' because it is locked. Canonical runtime CSV is safe. {exception.Message}");
            }
            catch (System.UnauthorizedAccessException exception)
            {
                Debug.LogWarning($"ATADTRL A1: could not refresh CSV mirror '{Path.GetFileName(destination)}'. Canonical runtime CSV is safe. {exception.Message}");
            }
        }
#endif

        private static string F(double value) => value.ToString("F4", CultureInfo.InvariantCulture);
        private static string Q(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
    }
}
