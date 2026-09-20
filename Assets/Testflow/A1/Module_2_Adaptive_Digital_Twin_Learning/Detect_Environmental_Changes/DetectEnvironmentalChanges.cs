using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using ATADTRL.Core;

namespace ATADTRL.TestFlow
{
    // ================================================================
    // CHANGE-DETECTION OUTPUT
    // ================================================================

    [Serializable]
    public class EnvironmentalChange
    {
        public float changeScore;

        public bool significantChange;
        public bool criticalEvent;

        public float robotPositionChange;
        public float robotYawChange;
        public float robotVelocityChange;

        public float humanH1PositionChange;

        public float lidarFrontChange;
        public float lidarLeftChange;
        public float lidarRightChange;

        public List<string> changedComponents =
            new List<string>();

        public string reason = "Initial state";
    }


    // ================================================================
    // MODULE 2.1 — DETECT ENVIRONMENTAL CHANGES
    // ================================================================

    public class EnvironmentChangeDetector
    {
        private const float EPSILON = 0.0001f;

        // Initial experimental threshold.
        // Later moved to Config/tuned experimentally.
        private readonly float changeThreshold = 0.15f;

        public EnvironmentalChange Detect(
            ObservationRecord previous,
            ObservationRecord current)
        {
            EnvironmentalChange result =
                new EnvironmentalChange();

            if (current == null)
            {
                result.reason =
                    "Observation unavailable";

                return result;
            }

            if (previous == null)
            {
                result.reason =
                    "Initial observation";

                return result;
            }

            // ========================================================
            // ROBOT POSITION
            // ========================================================

            float dx =
                current.estimated_x -
                previous.estimated_x;

            float dy =
                current.estimated_y -
                previous.estimated_y;

            float displacement =
                Mathf.Sqrt(
                    dx * dx +
                    dy * dy);

            result.robotPositionChange =
                Normalize(displacement, 2f);


            // ========================================================
            // ROBOT ORIENTATION
            // ========================================================

            float yawDifference =
                Mathf.Abs(
                    Mathf.DeltaAngle(
                        previous.estimated_yaw,
                        current.estimated_yaw));

            result.robotYawChange =
                Normalize(
                    yawDifference,
                    180f);


            // ========================================================
            // ROBOT VELOCITY
            // ========================================================

            float velocityDifference =
                Mathf.Abs(
                    current.estimated_velocity -
                    previous.estimated_velocity);

            result.robotVelocityChange =
                Normalize(
                    velocityDifference,
                    3f);


            // ========================================================
            // H1 POSITION
            //
            // H1 is the critical human for A1.
            // ========================================================

            if (TryParseVector(
                    previous.h1_position,
                    out Vector3 previousH1)
                &&
                TryParseVector(
                    current.h1_position,
                    out Vector3 currentH1))
            {
                result.humanH1PositionChange =
                    Normalize(
                        Vector3.Distance(
                            previousH1,
                            currentH1),
                        2f);
            }


            // ========================================================
            // LIDAR
            // ========================================================

            result.lidarFrontChange =
                Normalize(
                    Mathf.Abs(
                        current.lidar_front_distance -
                        previous.lidar_front_distance),
                    20f);

            result.lidarLeftChange =
                Normalize(
                    Mathf.Abs(
                        current.lidar_left_distance -
                        previous.lidar_left_distance),
                    20f);

            result.lidarRightChange =
                Normalize(
                    Mathf.Abs(
                        current.lidar_right_distance -
                        previous.lidar_right_distance),
                    20f);


            // ========================================================
            // WEIGHTED ENVIRONMENTAL CHANGE SCORE
            //
            //             Σ wi di(t)
            // C_t = -----------------------
            //                Σ wi
            // ========================================================

            float numerator =
                  1.00f * result.robotPositionChange
                + 0.50f * result.robotYawChange
                + 0.75f * result.robotVelocityChange
                + 1.25f * result.humanH1PositionChange
                + 1.25f * result.lidarFrontChange
                + 0.75f * result.lidarLeftChange
                + 0.75f * result.lidarRightChange;

            float denominator =
                  1.00f
                + 0.50f
                + 0.75f
                + 1.25f
                + 1.25f
                + 0.75f
                + 0.75f;

            result.changeScore =
                numerator /
                (denominator + EPSILON);


            // ========================================================
            // COMPONENT IDENTIFICATION
            // ========================================================

            AddChanged(
                result,
                "RobotPosition",
                result.robotPositionChange);

            AddChanged(
                result,
                "RobotYaw",
                result.robotYawChange);

            AddChanged(
                result,
                "RobotVelocity",
                result.robotVelocityChange);

            AddChanged(
                result,
                "H1Position",
                result.humanH1PositionChange);

            AddChanged(
                result,
                "LiDARFront",
                result.lidarFrontChange);

            AddChanged(
                result,
                "LiDARLeft",
                result.lidarLeftChange);

            AddChanged(
                result,
                "LiDARRight",
                result.lidarRightChange);


            // ========================================================
            // SIGNIFICANT CHANGE
            // ========================================================

            result.significantChange =
                result.changeScore >=
                changeThreshold;


            // ========================================================
            // CRITICAL IMMEDIATE PROXIMITY
            //
            // This is NOT yet the complete A1 collision prediction.
            // TTC/context is calculated later in the Context World Model.
            // ========================================================

            result.criticalEvent =
                current.lidar_front_distance > 0f &&
                current.lidar_front_distance <= 1.0f;


            // ========================================================
            // DIAGNOSTIC REASON
            // ========================================================

            if (result.criticalEvent)
            {
                result.reason =
                    "Critical obstacle proximity";
            }
            else if (result.significantChange)
            {
                result.reason =
                    "Environmental change detected";
            }
            else
            {
                result.reason =
                    "Environment maintained";
            }

            return result;
        }


        // ============================================================
        // NORMALIZATION
        // ============================================================

        private float Normalize(
            float difference,
            float range)
        {
            return Mathf.Clamp01(
                Mathf.Abs(difference) /
                (range + EPSILON));
        }


        // ============================================================
        // CHANGED COMPONENT IDENTIFICATION
        // ============================================================

        private void AddChanged(
            EnvironmentalChange result,
            string name,
            float change)
        {
            if (change >= 0.05f)
            {
                result.changedComponents.Add(
                    name);
            }
        }


        // ============================================================
        // EXISTING UNIFIED OBSERVATION VECTOR PARSER
        //
        // Format:
        // x:y:z
        // ============================================================

        private bool TryParseVector(string text, out Vector3 vector)
        {
            vector = Vector3.zero;

            if (string.IsNullOrWhiteSpace(text) ||
                text == "UNAVAILABLE" ||
                text == "NA")
                return false;

            string[] p = text.Split(':');

            if (p.Length < 3)
                return false;

            float x = 0f;
            float y = 0f;
            float z = 0f;

            bool xOK = float.TryParse(
                p[0],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out x);

            bool yOK = float.TryParse(
                p[1],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out y);

            bool zOK = float.TryParse(
                p[2],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out z);

            if (!xOK || !yOK || !zOK)
                return false;

            vector = new Vector3(x, y, z);

            return true;
        }
    }
}