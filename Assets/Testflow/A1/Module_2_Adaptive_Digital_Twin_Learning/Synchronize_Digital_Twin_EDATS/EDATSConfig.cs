using UnityEngine;

namespace ATADTRL.Module2
{
    [CreateAssetMenu(
        fileName = "EDATSConfig",
        menuName = "ATADTRL/Module 2/EDATS Config")]
    public class EDATSConfig : ScriptableObject
    {
        [Header("Overall EDATS")]
        [Range(0f, 1f)]
        public float overallChangeThreshold = 0.15f;

        [Header("Component Change Thresholds")]
        public float robotPositionThreshold = 0.15f;
        public float robotVelocityThreshold = 0.20f;

        public float humanPositionThreshold = 0.25f;
        public float humanVelocityThreshold = 0.25f;

        public float forkliftPositionThreshold = 0.40f;
        public float forkliftVelocityThreshold = 0.30f;

        public float parcelPositionThreshold = 0.15f;

        [Header("Normalization Ranges")]
        public float positionNormalizationRange = 60f;
        public float velocityNormalizationRange = 3f;

        [Header("Twin Divergence Protection")]

        [Tooltip("Maximum allowed planar robot position divergence in metres.")]
        public float robotPositionDivergenceThreshold = 0.50f;

        [Tooltip("Maximum allowed robot yaw divergence in degrees.")]
        public float robotYawDivergenceThreshold = 10.0f;

        [Tooltip("Maximum allowed robot velocity divergence in m/s.")]
        public float robotVelocityDivergenceThreshold = 0.40f;

        [Tooltip("Maximum allowed human/forklift position divergence in metres.")]
        public float actorPositionDivergenceThreshold = 0.75f;

        [Tooltip("Maximum time the Twin may remain without synchronization.")]
        public float maximumTwinAgeSeconds = 2.0f;

        [Header("Component Weights")]
        public float robotWeight = 1f;
        public float humanWeight = 3f;
        public float forkliftWeight = 4f;
        public float parcelWeight = 2f;
        public float stationWeight = 5f;
        public float taskWeight = 4f;
        public float rackWeight = 5f;

        [Header("Critical Events")]
        public bool stationAvailabilityChangeIsCritical = true;
        public bool rackAvailabilityChangeIsCritical = true;
        public bool taskStageChangeIsCritical = false;
        public bool sensorLinkLossIsCritical = false;
    }
}