using UnityEngine;
using ATADTRL.Core;

namespace ATADTRL.Sensors
{
    /// <summary>
    /// Central coordinator for all onboard sensors. Ticks each sensor at its
    /// own configured rate, applies scenario-driven noise overrides, and
    /// exposes convenience accessors used by NavigationManager and
    /// ObservationLogger. Attach to the robot GameObject.
    /// </summary>
    public class SensorManager : MonoBehaviour
    {
        public SensorNoiseConfig baseNoiseConfig = new SensorNoiseConfig();

        public LidarSensor lidar;
        public RGBSensor camera;
        public IMUSensor imu;
        public WheelEncoder encoder;

        private bool _conflictingObservationsActive;

        public void Initialize(RobotConfig robotConfig)
        {
            if (lidar != null) lidar.noiseConfig = baseNoiseConfig;
            if (camera != null) camera.noiseConfig = baseNoiseConfig;
            if (imu != null) imu.noiseConfig = baseNoiseConfig;
            if (encoder != null) { encoder.noiseConfig = baseNoiseConfig; encoder.robotConfig = robotConfig; }
        }

        public void ApplyScenarioOverrides(ScenarioOverride ov)
        {
            if (lidar != null)
            {
                lidar.noiseMultiplier = ov.lidarNoiseMultiplier;
                lidar.forcedDropoutOverride = ov.forceLidarDropout ? 1f : -1f;
            }
            if (imu != null) imu.noiseMultiplier = ov.imuNoiseMultiplier;
            if (encoder != null)
            {
                encoder.noiseMultiplier = ov.encoderNoiseMultiplier;
                encoder.forceDropoutOverride = ov.forceEncoderDropout;
            }
            if (camera != null) camera.forceDropoutOverride = ov.forceCameraDropout;

            _conflictingObservationsActive = ov.conflictingObservations;
        }

        public void ClearOverrides()
        {
            ApplyScenarioOverrides(ScenarioOverride.Default);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            lidar?.TickSensor(dt);
            camera?.TickSensor(dt);
            imu?.TickSensor(dt);
            encoder?.TickSensor(dt);
        }

        public float GetNearestObstacleDistance()
        {
            if (lidar == null) return float.MaxValue;
            return lidar.GetMinDistance();
        }

        // Navigation should respond only to hazards in the direction of
        // travel; the 270-degree minimum includes safe objects beside or
        // behind the robot and previously caused false emergency stops.
        public float GetForwardObstacleDistance()
        {
            if (lidar == null) return float.MaxValue;
            return lidar.GetMinDistanceInSector(0f, 35f);
        }

        /// <summary>Builds the noisy, sensor-fused ObservationRecord for the current tick.</summary>
        public ObservationRecord BuildObservation(int scenarioId, int episodeId, long stepId, double timestamp)
        {
            var rec = new ObservationRecord
            {
                timestamp = timestamp,
                scenarioId = scenarioId,
                episodeId = episodeId,
                stepId = stepId
            };

            bool missingAny = false;
            bool dropoutAny = false;

            if (lidar != null)
            {
                rec.lidar_min_distance = lidar.GetMinDistance();
                rec.lidar_front_distance = lidar.GetDistanceNearAngle(0f);
                rec.lidar_left_distance = lidar.GetDistanceNearAngle(-90f);
                rec.lidar_right_distance = lidar.GetDistanceNearAngle(90f);
                rec.lidar_measurement_count = lidar.LatestScan.Count;
                dropoutAny |= lidar.LastReadingDroppedOut;
            }
            else missingAny = true;

            if (camera != null)
            {
                rec.camera_detection_count = camera.LatestDetections.Count;
                rec.camera_detected_objects = camera.SerializeDetections();
                dropoutAny |= camera.LastReadingDroppedOut;
            }
            else missingAny = true;

            if (imu != null)
            {
                Vector3 acc = imu.Accelerometer;
                Vector3 gyro = imu.Gyroscope;
                // Simulate conflicting-observations scenario by adding a deliberate offset
                // between IMU-implied heading and encoder-implied heading downstream.
                rec.ax = acc.x; rec.ay = acc.y; rec.az = acc.z;
                rec.gx = gyro.x; rec.gy = gyro.y; rec.gz = gyro.z;
            }
            else missingAny = true;

            if (encoder != null)
            {
                rec.left_wheel_distance = encoder.LeftWheelDistance;
                rec.right_wheel_distance = encoder.RightWheelDistance;
                rec.left_wheel_velocity = encoder.LeftWheelVelocity;
                rec.right_wheel_velocity = encoder.RightWheelVelocity;

                float yaw = encoder.EstimatedYaw;
                if (_conflictingObservationsActive)
                {
                    // Inject an artificial disagreement between encoder-based dead
                    // reckoning and IMU-implied yaw, as specified by Scenario 15/19.
                    yaw += 12f;
                }

                rec.estimated_x = encoder.EstimatedPosition.x;
                rec.estimated_y = encoder.EstimatedPosition.z; // planar y <- world z
                rec.estimated_yaw = yaw;
                rec.estimated_velocity = encoder.EstimatedLinearVelocity;
                dropoutAny |= encoder.LastReadingDroppedOut;
            }
            else missingAny = true;

            rec.missing_sensor_flag = missingAny;
            rec.sensor_dropout_flag = dropoutAny;
            return rec;
        }
    }

    public struct ScenarioOverride
    {
        public float lidarNoiseMultiplier;
        public float imuNoiseMultiplier;
        public float encoderNoiseMultiplier;
        public bool forceLidarDropout;
        public bool forceCameraDropout;
        public bool forceEncoderDropout;
        public bool conflictingObservations;

        public static ScenarioOverride Default => new ScenarioOverride
        {
            lidarNoiseMultiplier = 1f,
            imuNoiseMultiplier = 1f,
            encoderNoiseMultiplier = 1f
        };
    }
}
