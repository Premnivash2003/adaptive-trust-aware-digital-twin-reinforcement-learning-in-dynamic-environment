using UnityEngine;
using ATADTRL.Core;

namespace ATADTRL.Trust
{
    public readonly struct TrustAssessmentResult
    {
        public readonly float Lidar;
        public readonly float Camera;
        public readonly float Imu;
        public readonly float Encoder;
        public readonly float Communication;
        public readonly float Overall;

        public TrustAssessmentResult(float lidar, float camera, float imu, float encoder,
            float communication, float overall)
        {
            Lidar = lidar;
            Camera = camera;
            Imu = imu;
            Encoder = encoder;
            Communication = communication;
            Overall = overall;
        }
    }

    /// <summary>
    /// Module 3 trust-assessment submodule. Produces source-specific and
    /// fused trust scores from each unified observation.
    /// </summary>
    public static class TrustAssessmentModule
    {
        public static TrustAssessmentResult Assess(ObservationRecord observation)
        {
            if (observation == null) return default;
            float lidar = observation.lidar_valid && observation.lidar_measurement_count > 0 &&
                          IsFinite(observation.lidar_front_distance) ? 1f : 0f;
            float camera = observation.camera_valid && !observation.sensor_dropout_flag ? 1f : 0f;
            float imu = IsFinite(observation.ax) && IsFinite(observation.ay) && IsFinite(observation.az) &&
                        IsFinite(observation.gx) && IsFinite(observation.gy) && IsFinite(observation.gz) ? 1f : 0f;
            float wheelDifference = Mathf.Abs(observation.left_wheel_velocity - observation.right_wheel_velocity);
            float encoder = IsFinite(observation.estimated_x) && IsFinite(observation.estimated_y) &&
                            wheelDifference < 4f ? 1f : .2f;
            float communication = Mathf.Clamp01(1f - observation.communication_delay / 1.5f);
            float overall = observation.missing_sensor_flag ? .35f :
                .30f * lidar + .20f * camera + .20f * imu + .15f * encoder + .15f * communication;
            return new TrustAssessmentResult(lidar, camera, imu, encoder, communication, overall);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
