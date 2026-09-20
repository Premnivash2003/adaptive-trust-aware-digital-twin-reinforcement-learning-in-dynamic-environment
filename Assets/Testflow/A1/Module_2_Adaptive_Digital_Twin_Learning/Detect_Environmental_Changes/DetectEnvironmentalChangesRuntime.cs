using System;
using System.Collections.Generic;
using UnityEngine;
using ATADTRL.Core;

namespace ATADTRL.Module2
{
    public class ChangeDetectionResult
    {
        public float changeScore;
        public bool criticalEvent;

        public readonly List<string> changedComponents =
            new List<string>();

        public readonly Dictionary<string, float> componentChanges =
            new Dictionary<string, float>();

        public string criticalReason = "";
    }

    public class EnvironmentalChangeDetector
    {
        private readonly EDATSConfig config;

        public EnvironmentalChangeDetector(EDATSConfig config)
        {
            this.config = config;
        }

        public ChangeDetectionResult Detect(
            ObservationRecord previous,
            ObservationRecord current)
        {
            var result = new ChangeDetectionResult();

            if (previous == null || current == null)
            {
                result.changeScore = 1f;
                result.criticalEvent = true;
                result.criticalReason = "Initial twin construction";
                return result;
            }

            float weightedSum = 0f;
            float totalWeight = 0f;

            // ---------------------------------------------------------
            // Robot estimated state
            // ---------------------------------------------------------

            Vector3 previousRobot =
                new Vector3(previous.estimated_x, 0f, previous.estimated_y);

            Vector3 currentRobot =
                new Vector3(current.estimated_x, 0f, current.estimated_y);

            AddVectorChange(
                "robot.position",
                previousRobot,
                currentRobot,
                config.robotPositionThreshold,
                config.positionNormalizationRange,
                config.robotWeight,
                result,
                ref weightedSum,
                ref totalWeight);

            AddScalarChange(
                "robot.velocity",
                previous.estimated_velocity,
                current.estimated_velocity,
                config.robotVelocityThreshold,
                config.velocityNormalizationRange,
                config.robotWeight,
                result,
                ref weightedSum,
                ref totalWeight);

            // ---------------------------------------------------------
            // Humans
            // ---------------------------------------------------------

            ProcessActor(
                "H1",
                previous.h1_position,
                current.h1_position,
                previous.h1_velocity,
                current.h1_velocity,
                config.humanPositionThreshold,
                config.humanVelocityThreshold,
                config.humanWeight,
                result,
                ref weightedSum,
                ref totalWeight);

            ProcessActor(
                "H2",
                previous.h2_position,
                current.h2_position,
                previous.h2_velocity,
                current.h2_velocity,
                config.humanPositionThreshold,
                config.humanVelocityThreshold,
                config.humanWeight,
                result,
                ref weightedSum,
                ref totalWeight);

            ProcessActor(
                "H3",
                previous.h3_position,
                current.h3_position,
                previous.h3_velocity,
                current.h3_velocity,
                config.humanPositionThreshold,
                config.humanVelocityThreshold,
                config.humanWeight,
                result,
                ref weightedSum,
                ref totalWeight);

            ProcessActor(
                "H4",
                previous.h4_position,
                current.h4_position,
                previous.h4_velocity,
                current.h4_velocity,
                config.humanPositionThreshold,
                config.humanVelocityThreshold,
                config.humanWeight,
                result,
                ref weightedSum,
                ref totalWeight);

            ProcessActor(
                "H5",
                previous.h5_position,
                current.h5_position,
                previous.h5_velocity,
                current.h5_velocity,
                config.humanPositionThreshold,
                config.humanVelocityThreshold,
                config.humanWeight,
                result,
                ref weightedSum,
                ref totalWeight);

            // ---------------------------------------------------------
            // Forklifts
            // ---------------------------------------------------------

            ProcessActor(
                "F1",
                previous.f1_position,
                current.f1_position,
                previous.f1_velocity,
                current.f1_velocity,
                config.forkliftPositionThreshold,
                config.forkliftVelocityThreshold,
                config.forkliftWeight,
                result,
                ref weightedSum,
                ref totalWeight);

            ProcessActor(
                "F2",
                previous.f2_position,
                current.f2_position,
                previous.f2_velocity,
                current.f2_velocity,
                config.forkliftPositionThreshold,
                config.forkliftVelocityThreshold,
                config.forkliftWeight,
                result,
                ref weightedSum,
                ref totalWeight);

            // ---------------------------------------------------------
            // Parcel position
            // ---------------------------------------------------------

            AddSerializedVectorChange(
                "parcel.position",
                previous.parcel_position,
                current.parcel_position,
                config.parcelPositionThreshold,
                config.positionNormalizationRange,
                config.parcelWeight,
                result,
                ref weightedSum,
                ref totalWeight);

            // ---------------------------------------------------------
            // Station availability
            // ---------------------------------------------------------

            CheckBoolean(
                "P1.available",
                previous.P1_available,
                current.P1_available,
                config.stationWeight,
                config.stationAvailabilityChangeIsCritical,
                result,
                ref weightedSum,
                ref totalWeight);

            CheckBoolean(
                "P2.available",
                previous.P2_available,
                current.P2_available,
                config.stationWeight,
                config.stationAvailabilityChangeIsCritical,
                result,
                ref weightedSum,
                ref totalWeight);

            CheckBoolean(
                "P3.available",
                previous.P3_available,
                current.P3_available,
                config.stationWeight,
                config.stationAvailabilityChangeIsCritical,
                result,
                ref weightedSum,
                ref totalWeight);

            CheckBoolean(
                "D1.available",
                previous.D1_available,
                current.D1_available,
                config.stationWeight,
                config.stationAvailabilityChangeIsCritical,
                result,
                ref weightedSum,
                ref totalWeight);

            CheckBoolean(
                "D2.available",
                previous.D2_available,
                current.D2_available,
                config.stationWeight,
                config.stationAvailabilityChangeIsCritical,
                result,
                ref weightedSum,
                ref totalWeight);

            CheckBoolean(
                "D3.available",
                previous.D3_available,
                current.D3_available,
                config.stationWeight,
                config.stationAvailabilityChangeIsCritical,
                result,
                ref weightedSum,
                ref totalWeight);

            // ---------------------------------------------------------
            // Rack availability
            // ---------------------------------------------------------

            CheckBoolean(
                "rack.available",
                previous.rack_available,
                current.rack_available,
                config.rackWeight,
                config.rackAvailabilityChangeIsCritical,
                result,
                ref weightedSum,
                ref totalWeight);

            // ---------------------------------------------------------
            // Task stage
            // ---------------------------------------------------------

            if (!string.Equals(
                    previous.current_task_stage,
                    current.current_task_stage,
                    StringComparison.Ordinal))
            {
                AddDiscreteChange(
                    "task.stage",
                    config.taskWeight,
                    config.taskStageChangeIsCritical,
                    result,
                    ref weightedSum,
                    ref totalWeight);
            }

            // ---------------------------------------------------------
            // Carry / grasp state
            // ---------------------------------------------------------

            CheckBoolean(
                "task.carrying",
                previous.carrying_status,
                current.carrying_status,
                config.taskWeight,
                false,
                result,
                ref weightedSum,
                ref totalWeight);

            CheckBoolean(
                "task.grasp",
                previous.grasp_status,
                current.grasp_status,
                config.taskWeight,
                false,
                result,
                ref weightedSum,
                ref totalWeight);

            // ---------------------------------------------------------
            // Actor communication link validity
            // ---------------------------------------------------------

            if (!string.Equals(
                    previous.actor_link_validity,
                    current.actor_link_validity,
                    StringComparison.Ordinal))
            {
                AddDiscreteChange(
                    "actor.link.validity",
                    config.taskWeight,
                    config.sensorLinkLossIsCritical,
                    result,
                    ref weightedSum,
                    ref totalWeight);
            }

            result.changeScore =
                totalWeight > 0f
                    ? weightedSum / totalWeight
                    : 0f;

            return result;
        }

        // =============================================================
        // ACTOR PROCESSING
        // =============================================================

        private void ProcessActor(
            string actorId,
            string previousPosition,
            string currentPosition,
            string previousVelocity,
            string currentVelocity,
            float positionThreshold,
            float velocityThreshold,
            float weight,
            ChangeDetectionResult result,
            ref float weightedSum,
            ref float totalWeight)
        {
            AddSerializedVectorChange(
                actorId + ".position",
                previousPosition,
                currentPosition,
                positionThreshold,
                config.positionNormalizationRange,
                weight,
                result,
                ref weightedSum,
                ref totalWeight);

            AddSerializedVectorChange(
                actorId + ".velocity",
                previousVelocity,
                currentVelocity,
                velocityThreshold,
                config.velocityNormalizationRange,
                weight,
                result,
                ref weightedSum,
                ref totalWeight);
        }

        // =============================================================
        // NUMERIC CHANGE
        // =============================================================

        private static void AddVectorChange(
            string name,
            Vector3 previous,
            Vector3 current,
            float threshold,
            float normalizationRange,
            float weight,
            ChangeDetectionResult result,
            ref float weightedSum,
            ref float totalWeight)
        {
            float physicalChange =
                Vector3.Distance(previous, current);

            float normalized =
                physicalChange /
                Mathf.Max(normalizationRange, 0.0001f);

            normalized = Mathf.Clamp01(normalized);

            result.componentChanges[name] = normalized;

            weightedSum += weight * normalized;
            totalWeight += weight;

            if (physicalChange > threshold)
                result.changedComponents.Add(name);
        }

        private static void AddScalarChange(
            string name,
            float previous,
            float current,
            float threshold,
            float normalizationRange,
            float weight,
            ChangeDetectionResult result,
            ref float weightedSum,
            ref float totalWeight)
        {
            float physicalChange =
                Mathf.Abs(current - previous);

            float normalized =
                physicalChange /
                Mathf.Max(normalizationRange, 0.0001f);

            normalized = Mathf.Clamp01(normalized);

            result.componentChanges[name] = normalized;

            weightedSum += weight * normalized;
            totalWeight += weight;

            if (physicalChange > threshold)
                result.changedComponents.Add(name);
        }

        private static void AddSerializedVectorChange(
            string name,
            string previous,
            string current,
            float threshold,
            float normalizationRange,
            float weight,
            ChangeDetectionResult result,
            ref float weightedSum,
            ref float totalWeight)
        {
            bool prevValid = TryParseVector(previous, out Vector3 p0);
            bool currValid = TryParseVector(current, out Vector3 p1);

            if (!prevValid || !currValid)
            {
                if (previous != current)
                {
                    AddDiscreteChange(
                        name,
                        weight,
                        false,
                        result,
                        ref weightedSum,
                        ref totalWeight);
                }

                return;
            }

            AddVectorChange(
                name,
                p0,
                p1,
                threshold,
                normalizationRange,
                weight,
                result,
                ref weightedSum,
                ref totalWeight);
        }

        private static void CheckBoolean(
            string name,
            bool previous,
            bool current,
            float weight,
            bool critical,
            ChangeDetectionResult result,
            ref float weightedSum,
            ref float totalWeight)
        {
            totalWeight += weight;

            if (previous == current)
            {
                result.componentChanges[name] = 0f;
                return;
            }

            result.componentChanges[name] = 1f;
            weightedSum += weight;

            result.changedComponents.Add(name);

            if (critical)
            {
                result.criticalEvent = true;

                if (string.IsNullOrEmpty(result.criticalReason))
                    result.criticalReason = name + " changed";
            }
        }

        private static void AddDiscreteChange(
            string name,
            float weight,
            bool critical,
            ChangeDetectionResult result,
            ref float weightedSum,
            ref float totalWeight)
        {
            result.componentChanges[name] = 1f;

            weightedSum += weight;
            totalWeight += weight;

            result.changedComponents.Add(name);

            if (critical)
            {
                result.criticalEvent = true;

                if (string.IsNullOrEmpty(result.criticalReason))
                    result.criticalReason = name + " changed";
            }
        }

        // =============================================================
        // VECTOR PARSER
        // =============================================================

        public static bool TryParseVector(
            string value,
            out Vector3 vector)
        {
            vector = Vector3.zero;

            if (string.IsNullOrWhiteSpace(value) ||
                value == "UNAVAILABLE")
                return false;

            // Actor strings may be:
            // H1:1.000:0.000:3.000
            // or simply:
            // 1.000:0.000:3.000

            string[] parts = value.Split(':');

            int offset = parts.Length == 4 ? 1 : 0;

            if (parts.Length - offset != 3)
                return false;

            if (!float.TryParse(
                    parts[offset],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out float x))
                return false;

            if (!float.TryParse(
                    parts[offset + 1],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out float y))
                return false;

            if (!float.TryParse(
                    parts[offset + 2],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out float z))
                return false;

            vector = new Vector3(x, y, z);
            return true;
        }
    }
}