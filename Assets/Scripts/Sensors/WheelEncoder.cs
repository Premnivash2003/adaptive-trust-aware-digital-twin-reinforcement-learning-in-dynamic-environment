using UnityEngine;
using ATADTRL.Core;

namespace ATADTRL.Sensors
{
    /// <summary>
    /// Simulated differential-drive wheel encoder derived from the robot's
    /// actual linear/angular displacement each tick:
    ///   dsL = r*dThetaL, dsR = r*dThetaR
    ///   ds  = (dsR+dsL)/2,  dTheta = (dsR-dsL)/L
    /// This class inverts that relationship: given the robot's true linear
    /// and angular displacement, it derives per-wheel ground-truth
    /// displacement, then adds configurable noise/quantization.
    /// </summary>
    public class WheelEncoder : MonoBehaviour, ISensor
    {
        public SensorNoiseConfig noiseConfig;
        public RobotConfig robotConfig;
        [Range(0f, 10f)] public float noiseMultiplier = 1f;
        public bool forceDropoutOverride = false;

        public string SensorName => "WheelEncoder";
        public float UpdateRateHz => noiseConfig != null ? noiseConfig.encoderUpdateRateHz : 50f;
        public bool LastReadingDroppedOut { get; private set; }

        public float LeftWheelDistance { get; private set; }
        public float RightWheelDistance { get; private set; }
        public float LeftWheelVelocity { get; private set; }
        public float RightWheelVelocity { get; private set; }
        public float EstimatedLinearDisplacement { get; private set; }
        public float EstimatedAngularDisplacement { get; private set; }
        public float EstimatedLinearVelocity { get; private set; }

        // Dead-reckoning estimate (integrated), separate from ground truth pose.
        public Vector3 EstimatedPosition { get; private set; }
        public float EstimatedYaw { get; private set; }

        private float _timer;
        private Vector3 _lastPosition;
        private float _lastYaw;
        private bool _initialized;

        public void InitializePose(Vector3 pos, float yaw)
        {
            EstimatedPosition = pos;
            EstimatedYaw = yaw;
            _lastPosition = pos;
            _lastYaw = yaw;
            _initialized = true;
        }

        public void TickSensor(float deltaTime)
        {
            if (noiseConfig == null || robotConfig == null) return;
            _timer += deltaTime;
            float interval = 1f / Mathf.Max(0.01f, UpdateRateHz);
            if (_timer < interval) return;
            float dt = _timer;
            _timer = 0f;

            if (!_initialized) InitializePose(transform.position, transform.eulerAngles.y);

            LastReadingDroppedOut = forceDropoutOverride;

            // Ground-truth robot displacement this tick.
            float dsGroundTruth = Vector3.Distance(transform.position, _lastPosition);
            float dThetaGroundTruthDeg = Mathf.DeltaAngle(_lastYaw, transform.eulerAngles.y);
            float dThetaGroundTruthRad = dThetaGroundTruthDeg * Mathf.Deg2Rad;
            _lastPosition = transform.position;
            _lastYaw = transform.eulerAngles.y;

            float L = Mathf.Max(0.01f, robotConfig.wheelBaseDistance);

            // Invert kinematics: ds = (dsR+dsL)/2 ; dTheta = (dsR-dsL)/L
            float dsL = dsGroundTruth - (dThetaGroundTruthRad * L) / 2f;
            float dsR = dsGroundTruth + (dThetaGroundTruthRad * L) / 2f;

            float std = noiseConfig.encoderNoiseStd * Mathf.Max(0.0001f, noiseMultiplier);
            float noisyDsL = LastReadingDroppedOut ? 0f : dsL + Gaussian(std);
            float noisyDsR = LastReadingDroppedOut ? 0f : dsR + Gaussian(std);

            if (noiseConfig.encoderQuantization && noiseConfig.encoderTicksPerMeter > 0f)
            {
                float tickSize = 1f / noiseConfig.encoderTicksPerMeter;
                noisyDsL = Mathf.Round(noisyDsL / tickSize) * tickSize;
                noisyDsR = Mathf.Round(noisyDsR / tickSize) * tickSize;
            }

            LeftWheelDistance = noisyDsL;
            RightWheelDistance = noisyDsR;
            LeftWheelVelocity = dt > 0f ? noisyDsL / dt : 0f;
            RightWheelVelocity = dt > 0f ? noisyDsR / dt : 0f;

            EstimatedLinearDisplacement = (noisyDsR + noisyDsL) / 2f;
            EstimatedAngularDisplacement = (noisyDsR - noisyDsL) / L;
            EstimatedLinearVelocity = dt > 0f ? EstimatedLinearDisplacement / dt : 0f;

            // Dead-reckoning integration (independent of ground truth pose).
            EstimatedYaw += EstimatedAngularDisplacement * Mathf.Rad2Deg;
            Vector3 forward = Quaternion.Euler(0, EstimatedYaw, 0) * Vector3.forward;
            EstimatedPosition += forward * EstimatedLinearDisplacement;
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
