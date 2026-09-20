using System;
using UnityEngine;

namespace ATADTRL.Core
{
    /// <summary>
    /// Centralized, Inspector-editable noise/rate configuration for every
    /// onboard sensor. A single instance is shared (by reference) across
    /// LidarSensor, RGBSensor, IMUSensor and WheelEncoder so noise
    /// characteristics stay consistent unless a scenario explicitly
    /// overrides them via SensorManager.ApplyScenarioOverrides.
    /// </summary>
    [Serializable]
    public class SensorNoiseConfig
    {
        [Header("LiDAR")]
        public int lidarNumRays = 180;
        public float lidarHFovDegrees = 270f;
        public float lidarMaxRange = 10f;
        public float lidarMinRange = 0.1f;
        public float lidarUpdateRateHz = 10f;
        public float lidarNoiseStd = 0.02f;
        [Range(0f, 1f)] public float lidarDropoutProbability = 0.0f;

        [Header("RGB Camera")]
        public int cameraWidth = 320;
        public int cameraHeight = 240;
        public float cameraFov = 60f;
        public float cameraNear = 0.1f;
        public float cameraFar = 20f;
        public float cameraFrameRate = 15f;
        [Range(0f, 1f)] public float cameraDetectionDropout = 0.0f;
        public float cameraDetectionUncertainty = 0.05f;

        [Header("IMU")]
        public float imuUpdateRateHz = 50f;
        public float accelNoiseStd = 0.05f;
        public float gyroNoiseStd = 0.02f;
        public Vector3 accelBias = Vector3.zero;
        public Vector3 gyroBias = Vector3.zero;

        [Header("Wheel Encoder")]
        public float encoderUpdateRateHz = 50f;
        public float encoderNoiseStd = 0.01f;
        public bool encoderQuantization = false;
        public float encoderTicksPerMeter = 500f;
    }
}
