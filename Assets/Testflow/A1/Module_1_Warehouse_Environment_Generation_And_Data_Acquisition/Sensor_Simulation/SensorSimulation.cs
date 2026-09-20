using UnityEngine;
using ATADTRL.Core;
using ATADTRL.Sensors;

namespace ATADTRL.TestFlow.A1.Module1
{
    /// <summary>Architecture adapter for the robot's live sensor simulation.</summary>
    public static class SensorSimulation
    {
        public static SensorManager ResolveSensorSuite()
        {
            return Object.FindAnyObjectByType<SensorManager>();
        }

        public static bool IsSuiteReady(SensorManager sensors)
        {
            return sensors != null && sensors.lidar != null && sensors.camera != null &&
                   sensors.imu != null && sensors.encoder != null;
        }

        public static ObservationRecord Capture(SensorManager sensors, int scenarioId,
            int episodeId, long stepId, double timestamp)
        {
            if (!IsSuiteReady(sensors)) return null;
            return sensors.BuildObservation(scenarioId, episodeId, stepId, timestamp);
        }
    }
}
