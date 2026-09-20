using System.Globalization;

namespace ATADTRL.Module4
{
    /// <summary>Serializable output of the safe robot-navigation executor.</summary>
    public readonly struct RobotNavigationStatus
    {
        public readonly string Stage;
        public readonly float Progress;
        public readonly string SelectedAction;
        public readonly bool SafetyHold;
        public readonly float CollisionRisk;
        public readonly string Outcome;

        public RobotNavigationStatus(string stage, float progress, string selectedAction,
            bool safetyHold, float collisionRisk, string outcome)
        {
            Stage = stage;
            Progress = progress;
            SelectedAction = selectedAction;
            SafetyHold = safetyHold;
            CollisionRisk = collisionRisk;
            Outcome = outcome;
        }

        public static string CsvHeader =>
            "timestamp,scenario_id,episode_id,step_id,stage,progress,selected_action,safety_hold,collision_risk,outcome";

        public string ToCsvRow(double timestamp, int scenarioId, int episodeId, long stepId)
        {
            return string.Join(",", F(timestamp), scenarioId, episodeId, stepId,
                Q(Stage), F(Progress), Q(SelectedAction), SafetyHold, F(CollisionRisk), Q(Outcome));
        }

        private static string F(double value) => value.ToString("F4", CultureInfo.InvariantCulture);
        private static string Q(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
    }
}
