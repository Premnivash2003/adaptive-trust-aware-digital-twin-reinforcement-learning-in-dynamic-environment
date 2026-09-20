using UnityEngine;
using System.Globalization;

namespace ATADTRL.TestFlow
{
    public class ContextState
    {
        public bool valid;
        public float robotHumanDistance;
        public float robotCornerDistance;
        public float humanCornerDistance;

        public float robotTTC;
        public float humanTTC;
        public float ttcDifference;

        public float collisionRisk;
        public bool blindCornerConflict;
    }

    public class ContextAwareWorldModel
    {
        private Vector2 blindCorner;

        public ContextAwareWorldModel(Vector2 corner)
        {
            blindCorner = corner;
        }

        public ContextState Build(A1TwinState twin)
        {
            ContextState s = new ContextState();

            if (twin == null ||
                !TryParseVector(twin.h1Position, out Vector3 humanPosition) ||
                !TryParseVector(twin.h1Velocity, out Vector3 humanVelocity))
                return s;

            s.valid = true;

            Vector2 robot =
                new Vector2(twin.robotX, twin.robotY);

            Vector2 human =
                new Vector2(humanPosition.x, humanPosition.z);

            s.robotHumanDistance =
                Vector2.Distance(robot, human);

            s.robotCornerDistance =
                Vector2.Distance(robot, blindCorner);

            s.humanCornerDistance =
                Vector2.Distance(human, blindCorner);

            float humanSpeed =
                new Vector2(humanVelocity.x, humanVelocity.z).magnitude;

            s.robotTTC =
                s.robotCornerDistance /
                Mathf.Max(twin.robotVelocity, 0.01f);

            s.humanTTC =
                s.humanCornerDistance /
                Mathf.Max(humanSpeed, 0.01f);

            s.ttcDifference =
                Mathf.Abs(s.robotTTC - s.humanTTC);

            s.blindCornerConflict =
                s.robotCornerDistance < 4f &&
                s.humanCornerDistance < 4f &&
                s.ttcDifference < 1.5f;

            s.collisionRisk =
                s.blindCornerConflict
                ? Mathf.Clamp01(1f - s.ttcDifference / 1.5f)
                : 0f;

            return s;
        }

        private bool TryParseVector(string text, out Vector3 value)
        {
            value = Vector3.zero;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            string[] p = text.Split(':');

            if (p.Length < 3)
                return false;

            if (!float.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
                !float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) ||
                !float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                return false;
            if (float.IsNaN(x) || float.IsNaN(y) || float.IsNaN(z) ||
                float.IsInfinity(x) || float.IsInfinity(y) || float.IsInfinity(z))
                return false;
            value = new Vector3(x, y, z);
            return true;
        }
    }
}
