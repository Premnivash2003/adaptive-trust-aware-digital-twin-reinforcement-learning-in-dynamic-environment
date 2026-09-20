using System;
using UnityEngine;
using ATADTRL.Core;

namespace ATADTRL.Trust
{
    /// <summary>
    /// Formal Module 3 output consumed by the secure decision module.
    /// The Module 2 twin is the primary world state; live sensor evidence is
    /// used to score confidence and provide a fallback when the twin is stale.
    /// </summary>
    [Serializable]
    public sealed class TrustAwareWorldState
    {
        public double timestamp;
        public int scenarioId;
        public int episodeId;
        public long stepId;

        public float lidarTrust;
        public float cameraTrust;
        public float imuTrust;
        public float encoderTrust;
        public float communicationTrust;
        public float overallTrust;

        public float twinRobotX;
        public float twinRobotZ;
        public float trustedRobotX;
        public float trustedRobotZ;
        public float trustedRobotYaw;
        public float trustedRobotVelocity;
        public string h1Position;
        public float twinAge;
        public string stateSource;
        public string rejectedSources;
        public string contextStatus;
    }

    public static class TrustAwareStateRepresentation
    {
        private const float FreshTwinAgeSeconds = 1.0f;

        public static TrustAwareWorldState Construct(
            ObservationRecord observation,
            TwinWorldStateRecord twin,
            float lidarTrust,
            float cameraTrust,
            float imuTrust,
            float encoderTrust,
            float communicationTrust,
            float overallTrust,
            string contextStatus)
        {
            bool twinAvailable = twin != null;
            bool twinFresh = twinAvailable && twin.state_age <= FreshTwinAgeSeconds;
            float twinWeight = twinFresh ? Mathf.Clamp01(0.55f + 0.45f * overallTrust) : 0f;

            float observedX = observation != null ? observation.estimated_x : 0f;
            float observedZ = observation != null ? observation.estimated_y : 0f;
            float observedYaw = observation != null ? observation.estimated_yaw : 0f;
            float observedVelocity = observation != null ? observation.estimated_velocity : 0f;

            var state = new TrustAwareWorldState
            {
                timestamp = observation != null ? observation.timestamp : 0d,
                scenarioId = observation != null ? observation.scenarioId : -1,
                episodeId = observation != null ? observation.episodeId : -1,
                stepId = observation != null ? observation.stepId : 0,
                lidarTrust = lidarTrust,
                cameraTrust = cameraTrust,
                imuTrust = imuTrust,
                encoderTrust = encoderTrust,
                communicationTrust = communicationTrust,
                overallTrust = overallTrust,
                twinRobotX = twinAvailable ? twin.twin_robot_x : observedX,
                twinRobotZ = twinAvailable ? twin.twin_robot_y : observedZ,
                trustedRobotX = twinFresh ? Mathf.Lerp(observedX, twin.twin_robot_x, twinWeight) : observedX,
                trustedRobotZ = twinFresh ? Mathf.Lerp(observedZ, twin.twin_robot_y, twinWeight) : observedZ,
                trustedRobotYaw = twinFresh ? Mathf.LerpAngle(observedYaw, twin.twin_robot_yaw, twinWeight) : observedYaw,
                trustedRobotVelocity = twinFresh
                    ? Mathf.Lerp(observedVelocity, twin.twin_robot_velocity, twinWeight)
                    : observedVelocity,
                h1Position = twinAvailable && !string.IsNullOrWhiteSpace(twin.h1_position)
                    ? twin.h1_position
                    : observation?.h1_position,
                twinAge = twinAvailable ? twin.state_age : float.PositiveInfinity,
                stateSource = twinFresh ? "TWIN_WORLD_STATE_FUSED" : "LIVE_OBSERVATION_FALLBACK",
                rejectedSources = RejectedSources(lidarTrust, cameraTrust, imuTrust, encoderTrust,
                    communicationTrust, twinFresh),
                contextStatus = contextStatus ?? string.Empty
            };
            return state;
        }

        private static string RejectedSources(float lidar, float camera, float imu, float encoder,
            float communication, bool twinFresh)
        {
            string rejected = twinFresh ? string.Empty : "STALE_OR_MISSING_TWIN";
            AppendRejected(ref rejected, "LIDAR", lidar);
            AppendRejected(ref rejected, "CAMERA", camera);
            AppendRejected(ref rejected, "IMU", imu);
            AppendRejected(ref rejected, "ENCODER", encoder);
            AppendRejected(ref rejected, "COMMUNICATION", communication);
            return string.IsNullOrEmpty(rejected) ? "NONE" : rejected;
        }

        private static void AppendRejected(ref string rejected, string source, float trust)
        {
            if (trust >= 0.5f) return;
            if (!string.IsNullOrEmpty(rejected)) rejected += "|";
            rejected += source;
        }
    }
}
