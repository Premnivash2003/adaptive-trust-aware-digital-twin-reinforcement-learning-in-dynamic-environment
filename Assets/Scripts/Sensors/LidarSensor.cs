using System.Collections.Generic;
using UnityEngine;
using ATADTRL.Core;

namespace ATADTRL.Sensors
{
    public struct LidarPoint
    {
        public float angle;
        public float groundTruthDistance;
        public float noisyDistance;
        public bool detected;
    }

    /// <summary>
    /// Raycast-based 2D LiDAR. Attach to the robot at sensor mount height.
    /// Ground truth distances come from Physics.Raycast; the noisy distances
    /// (with Gaussian noise + dropout) are what feeds Unified_Observation.csv.
    /// </summary>
    public class LidarSensor : MonoBehaviour, ISensor
    {
        public SensorNoiseConfig noiseConfig;
        public LayerMask obstacleMask = ~0;
        [Range(0f, 10f)] public float noiseMultiplier = 1f;
        [Range(0f, 1f)] public float forcedDropoutOverride = -1f; // -1 = use config value

        public string SensorName => "LiDAR";
        public float UpdateRateHz => noiseConfig != null ? noiseConfig.lidarUpdateRateHz : 10f;
        public bool LastReadingDroppedOut { get; private set; }

        public List<LidarPoint> LatestScan { get; private set; } = new List<LidarPoint>();

        private float _timer;
        // The LiDAR is mounted beneath the robot root.  A single Raycast can
        // immediately hit the robot's own collider (particularly for the
        // rear/side rays), so retain several hits and discard that collider.
        private readonly RaycastHit[] _raycastHits = new RaycastHit[16];

        public void TickSensor(float deltaTime)
        {
            if (noiseConfig == null) return;
            _timer += deltaTime;
            float interval = 1f / Mathf.Max(0.01f, UpdateRateHz);
            if (_timer < interval) return;
            _timer = 0f;

            float dropoutP = forcedDropoutOverride >= 0f ? forcedDropoutOverride : noiseConfig.lidarDropoutProbability;
            LastReadingDroppedOut = Random.value < dropoutP;

            LatestScan.Clear();
            int rays = Mathf.Max(1, noiseConfig.lidarNumRays);
            float fov = noiseConfig.lidarHFovDegrees;
            float startAngle = -fov / 2f;
            float angleStep = rays > 1 ? fov / (rays - 1) : 0f;

            for (int i = 0; i < rays; i++)
            {
                float angle = startAngle + i * angleStep;
                Vector3 dir = Quaternion.Euler(0, angle, 0) * transform.forward;

                float groundTruth = noiseConfig.lidarMaxRange;
                bool hit = false;

                if (!LastReadingDroppedOut)
                {
                    int hitCount = Physics.RaycastNonAlloc(
                        transform.position,
                        dir,
                        _raycastHits,
                        noiseConfig.lidarMaxRange,
                        obstacleMask,
                        QueryTriggerInteraction.Ignore);

                    float nearestDistance = noiseConfig.lidarMaxRange;
                    for (int hitIndex = 0; hitIndex < hitCount; hitIndex++)
                    {
                        RaycastHit hitInfo = _raycastHits[hitIndex];
                        if (hitInfo.collider == null || hitInfo.collider.transform.IsChildOf(transform.root))
                        {
                            continue;
                        }

                        if (hitInfo.distance < nearestDistance)
                        {
                            nearestDistance = hitInfo.distance;
                            hit = true;
                        }
                    }

                    if (hit)
                    {
                        groundTruth = Mathf.Clamp(nearestDistance, noiseConfig.lidarMinRange, noiseConfig.lidarMaxRange);
                    }
                }

                float noise = SampleGaussian(0f, noiseConfig.lidarNoiseStd * Mathf.Max(0.0001f, noiseMultiplier));
                float noisy = LastReadingDroppedOut ? noiseConfig.lidarMaxRange : Mathf.Clamp(groundTruth + noise, noiseConfig.lidarMinRange, noiseConfig.lidarMaxRange);

                LatestScan.Add(new LidarPoint
                {
                    angle = angle,
                    groundTruthDistance = groundTruth,
                    noisyDistance = noisy,
                    detected = hit && !LastReadingDroppedOut
                });
            }
        }

        public float GetMinDistance()
        {
            if (LatestScan.Count == 0) return noiseConfig != null ? noiseConfig.lidarMaxRange : 999f;
            float min = float.MaxValue;
            foreach (var p in LatestScan) if (p.noisyDistance < min) min = p.noisyDistance;
            return min;
        }

        public float GetDistanceNearAngle(float targetAngle, float tolerance = 10f)
        {
            float best = noiseConfig != null ? noiseConfig.lidarMaxRange : 999f;
            float bestDiff = float.MaxValue;
            foreach (var p in LatestScan)
            {
                float diff = Mathf.Abs(Mathf.DeltaAngle(p.angle, targetAngle));
                if (diff < tolerance && diff < bestDiff)
                {
                    bestDiff = diff;
                    best = p.noisyDistance;
                }
            }
            return best;
        }

        /// <summary>Returns the closest reading inside a forward-facing angular sector.</summary>
        public float GetMinDistanceInSector(float centerAngle, float halfAngle)
        {
            if (LatestScan.Count == 0) return noiseConfig != null ? noiseConfig.lidarMaxRange : 999f;

            float closest = noiseConfig != null ? noiseConfig.lidarMaxRange : 999f;
            foreach (var point in LatestScan)
            {
                if (Mathf.Abs(Mathf.DeltaAngle(point.angle, centerAngle)) <= halfAngle)
                {
                    closest = Mathf.Min(closest, point.noisyDistance);
                }
            }
            return closest;
        }

        private static float SampleGaussian(float mean, float std)
        {
            float u1 = 1f - Random.value;
            float u2 = 1f - Random.value;
            float z = Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Sin(2f * Mathf.PI * u2);
            return mean + std * z;
        }
    }
}
