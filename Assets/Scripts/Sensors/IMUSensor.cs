using UnityEngine;
using ATADTRL.Core;

namespace ATADTRL.Sensors
{
    /// <summary>
    /// Simulated IMU derived from the robot's actual simulated motion
    /// (ground truth linear/angular velocity), with configurable bias and
    /// Gaussian noise applied to produce the final noisy measurement.
    /// </summary>
    public class IMUSensor : MonoBehaviour, ISensor
    {
        public SensorNoiseConfig noiseConfig;
        [Range(0f, 10f)] public float noiseMultiplier = 1f;

        public string SensorName => "IMU";
        public float UpdateRateHz => noiseConfig != null ? noiseConfig.imuUpdateRateHz : 50f;
        public bool LastReadingDroppedOut { get; private set; }

        public Vector3 Accelerometer { get; private set; }
        public Vector3 Gyroscope { get; private set; }

        private float _timer;
        private Vector3 _lastVelocity;
        private Vector3 _lastEuler;
        private bool _hasPreviousSample;

        /// <summary>Resets the IMU reference after the robot is warped to a new scenario start.</summary>
        public void InitializePose(Vector3 position, float yawDegrees)
        {
            _lastPositionCache = position;
            _lastVelocity = Vector3.zero;
            _lastEuler = new Vector3(0f, yawDegrees, 0f);
            _hasPreviousSample = true;
            Accelerometer = Vector3.zero;
            Gyroscope = Vector3.zero;
        }

        public void TickSensor(float deltaTime)
        {
            if (noiseConfig == null) return;
            _timer += deltaTime;
            float interval = 1f / Mathf.Max(0.01f, UpdateRateHz);
            if (_timer < interval) return;

            float dt = _timer;
            _timer = 0f;
            LastReadingDroppedOut = false;

            // NavMeshAgent drives this kinematic robot via its Transform, so
            // Rigidbody.linearVelocity remains zero. Derive the IMU motion
            // directly from the simulated pose instead.
            Vector3 currentVelocity = (transform.position - _lastPositionCache) / Mathf.Max(dt, 0.0001f);
            if (!_hasPreviousSample)
            {
                _lastPositionCache = transform.position;
                _lastVelocity = Vector3.zero;
                _lastEuler = transform.eulerAngles;
                _hasPreviousSample = true;
                Accelerometer = noiseConfig.accelBias + RandomGaussianVector(noiseConfig.accelNoiseStd * noiseMultiplier);
                Gyroscope = noiseConfig.gyroBias + RandomGaussianVector(noiseConfig.gyroNoiseStd * noiseMultiplier);
                return;
            }

            Vector3 groundTruthAccel = (currentVelocity - _lastVelocity) / Mathf.Max(dt, 0.0001f);
            _lastVelocity = currentVelocity;

            Vector3 eulerNow = transform.eulerAngles;
            Vector3 angularDelta = new Vector3(
                Mathf.DeltaAngle(_lastEuler.x, eulerNow.x),
                Mathf.DeltaAngle(_lastEuler.y, eulerNow.y),
                Mathf.DeltaAngle(_lastEuler.z, eulerNow.z));
            Vector3 groundTruthGyro = angularDelta / Mathf.Max(dt, 0.0001f) * Mathf.Deg2Rad;
            _lastEuler = eulerNow;
            _lastPositionCache = transform.position;

            float accStd = noiseConfig.accelNoiseStd * Mathf.Max(0.0001f, noiseMultiplier);
            float gyroStd = noiseConfig.gyroNoiseStd * Mathf.Max(0.0001f, noiseMultiplier);

            Accelerometer = groundTruthAccel + noiseConfig.accelBias + RandomGaussianVector(accStd);
            Gyroscope = groundTruthGyro + noiseConfig.gyroBias + RandomGaussianVector(gyroStd);
        }

        private Vector3 _lastPositionCache;

        private static Vector3 RandomGaussianVector(float std)
        {
            return new Vector3(Gaussian(std), Gaussian(std), Gaussian(std));
        }

        private static float Gaussian(float std)
        {
            float u1 = 1f - Random.value;
            float u2 = 1f - Random.value;
            float z = Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Sin(2f * Mathf.PI * u2);
            return std * z;
        }
    }
}
