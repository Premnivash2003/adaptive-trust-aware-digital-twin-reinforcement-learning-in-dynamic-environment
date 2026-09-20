using System.Collections.Generic;
using System.Text;
using UnityEngine;
using ATADTRL.Core;

namespace ATADTRL.Sensors
{
    public struct DetectedObject
    {
        public string objectClass;
        public Vector3 approxPosition;
    }

    /// <summary>
    /// Simulated RGB camera. Module 1 does not run a real CV model — instead
    /// it uses a frustum overlap test against tagged dynamic objects to
    /// produce a plausible, extensible "detection" observation (count +
    /// approximate positions) with configurable dropout/uncertainty, keeping
    /// the architecture ready for a real perception model in later modules.
    /// </summary>
    public class RGBSensor : MonoBehaviour, ISensor
    {
        public SensorNoiseConfig noiseConfig;
        public Camera sourceCamera;
        public bool forceDropoutOverride = false;

        public string SensorName => "RGBCamera";
        public float UpdateRateHz => noiseConfig != null ? noiseConfig.cameraFrameRate : 15f;
        public bool LastReadingDroppedOut { get; private set; }

        public List<DetectedObject> LatestDetections { get; private set; } = new List<DetectedObject>();

        private float _timer;

        private void Awake()
        {
            if (sourceCamera == null)
            {
                sourceCamera = GetComponent<Camera>();
                if (sourceCamera == null) sourceCamera = gameObject.AddComponent<Camera>();
            }
        }

        public void TickSensor(float deltaTime)
        {
            if (noiseConfig == null) return;
            _timer += deltaTime;
            float interval = 1f / Mathf.Max(0.01f, UpdateRateHz);
            if (_timer < interval) return;
            _timer = 0f;

            sourceCamera.fieldOfView = noiseConfig.cameraFov;
            sourceCamera.nearClipPlane = noiseConfig.cameraNear;
            sourceCamera.farClipPlane = noiseConfig.cameraFar;

            LastReadingDroppedOut = forceDropoutOverride || Random.value < noiseConfig.cameraDetectionDropout;

            LatestDetections.Clear();
            if (LastReadingDroppedOut) return;

            var planes = GeometryUtility.CalculateFrustumPlanes(sourceCamera);
#if UNITY_2023_1_OR_NEWER
#pragma warning disable 0618
            var candidates = UnityEngine.Object.FindObjectsByType<EnvironmentCollisionTag>(FindObjectsSortMode.None);
#pragma warning restore 0618
#else
            var candidates = UnityEngine.Object.FindObjectsOfType<EnvironmentCollisionTag>();
#endif

            foreach (var marker in candidates)
            {
                if (marker.kind != EnvironmentCollisionTag.Kind.DynamicObstacle) continue;

                var go = marker.gameObject;
                var renderer = go.GetComponent<Renderer>();
                Bounds bounds = renderer != null ? renderer.bounds : new Bounds(go.transform.position, Vector3.one * 0.5f);

                if (GeometryUtility.TestPlanesAABB(planes, bounds))
                {
                    Vector3 noisyPos = go.transform.position + Random.insideUnitSphere * noiseConfig.cameraDetectionUncertainty;
                    LatestDetections.Add(new DetectedObject { objectClass = go.name, approxPosition = noisyPos });
                }
            }
        }

        public string SerializeDetections()
        {
            if (LatestDetections.Count == 0) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < LatestDetections.Count; i++)
            {
                var d = LatestDetections[i];
                sb.Append(d.objectClass).Append(':')
                  .Append(d.approxPosition.x.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)).Append(':')
                  .Append(d.approxPosition.y.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)).Append(':')
                  .Append(d.approxPosition.z.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
                if (i < LatestDetections.Count - 1) sb.Append('|');
            }
            return sb.ToString();
        }
    }
}
