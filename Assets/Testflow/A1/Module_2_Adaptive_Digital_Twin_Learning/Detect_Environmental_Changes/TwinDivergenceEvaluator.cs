using System.Collections.Generic;
using UnityEngine;
using ATADTRL.Core;

namespace ATADTRL.Module2
{
    public class TwinDivergenceResult
    {
        public bool exceeded;

        public float robotPositionError;
        public float robotYawError;
        public float robotVelocityError;

        public float maximumActorPositionError;

        public readonly List<string> divergentComponents =
            new List<string>();

        public string reason = "";
    }

    public class TwinDivergenceEvaluator
    {
        private readonly EDATSConfig config;

        public TwinDivergenceEvaluator(
            EDATSConfig config)
        {
            this.config = config;
        }

        public TwinDivergenceResult Evaluate(
            ObservationRecord observation,
            TwinWorldStateRecord twin)
        {
            var result =
                new TwinDivergenceResult();

            if (observation == null ||
                twin == null)
            {
                return result;
            }

            // =========================================================
            // ROBOT POSITION DIVERGENCE
            // =========================================================

            float dx =
                observation.estimated_x -
                twin.twin_robot_x;

            float dy =
                observation.estimated_y -
                twin.twin_robot_y;

            result.robotPositionError =
                Mathf.Sqrt(
                    dx * dx +
                    dy * dy);

            if (result.robotPositionError >
                config.robotPositionDivergenceThreshold)
            {
                result.exceeded = true;

                result.divergentComponents.Add(
                    "robot.position");
            }

            // =========================================================
            // ROBOT YAW DIVERGENCE
            // =========================================================

            result.robotYawError =
                Mathf.Abs(
                    Mathf.DeltaAngle(
                        observation.estimated_yaw,
                        twin.twin_robot_yaw));

            if (result.robotYawError >
                config.robotYawDivergenceThreshold)
            {
                result.exceeded = true;

                result.divergentComponents.Add(
                    "robot.yaw");
            }

            // =========================================================
            // ROBOT VELOCITY DIVERGENCE
            // =========================================================

            result.robotVelocityError =
                Mathf.Abs(
                    observation.estimated_velocity -
                    twin.twin_robot_velocity);

            if (result.robotVelocityError >
                config.robotVelocityDivergenceThreshold)
            {
                result.exceeded = true;

                result.divergentComponents.Add(
                    "robot.velocity");
            }

            // =========================================================
            // ACTOR POSITION DIVERGENCE
            // =========================================================

            CompareActor(
                "H1",
                observation.h1_position,
                twin.h1_position,
                result);

            CompareActor(
                "H2",
                observation.h2_position,
                twin.h2_position,
                result);

            CompareActor(
                "H3",
                observation.h3_position,
                twin.h3_position,
                result);

            CompareActor(
                "H4",
                observation.h4_position,
                twin.h4_position,
                result);

            CompareActor(
                "H5",
                observation.h5_position,
                twin.h5_position,
                result);

            CompareActor(
                "F1",
                observation.f1_position,
                twin.f1_position,
                result);

            CompareActor(
                "F2",
                observation.f2_position,
                twin.f2_position,
                result);

            // =========================================================
            // FINAL REASON
            // =========================================================

            if (result.exceeded)
            {
                result.reason =
                    "Twin divergence threshold exceeded";
            }

            return result;
        }

        private void CompareActor(
            string actorId,
            string observationValue,
            string twinValue,
            TwinDivergenceResult result)
        {
            if (!EnvironmentalChangeDetector.TryParseVector(
                    observationValue,
                    out Vector3 observationPosition))
            {
                return;
            }

            if (!EnvironmentalChangeDetector.TryParseVector(
                    twinValue,
                    out Vector3 twinPosition))
            {
                return;
            }

            float error =
                Vector3.Distance(
                    observationPosition,
                    twinPosition);

            result.maximumActorPositionError =
                Mathf.Max(
                    result.maximumActorPositionError,
                    error);

            if (error >
                config.actorPositionDivergenceThreshold)
            {
                result.exceeded = true;

                result.divergentComponents.Add(
                    actorId + ".position");
            }
        }
    }
}