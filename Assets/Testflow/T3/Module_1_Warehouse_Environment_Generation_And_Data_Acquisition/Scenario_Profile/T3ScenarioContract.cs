namespace ATADTRL.TestFlow.T3
{
    public static class T3ScenarioContract
    {
        public const int ScenarioId = 13;
        public const string ScenarioCode = "T3";
        public const string EnvironmentalChange = "LIDAR_DEGRADED";
        public const string TrustResponse = "LIDAR_DOWN_WEIGHTED_RGB_IMU_ENCODER_RETAINED";
        public const string LearnedPolicy = "T3_PPO_V1";
        public const string SafeOutcome = "TRUSTED_FUSION_DELIVERY_VERIFIED";
    }
}
