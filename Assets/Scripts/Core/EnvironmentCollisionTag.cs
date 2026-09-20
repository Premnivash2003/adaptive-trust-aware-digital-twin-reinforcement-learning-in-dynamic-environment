using UnityEngine;

namespace ATADTRL.Core
{
    /// <summary>
    /// Marks a GameObject as a specific kind of environment object for
    /// collision detection (RobotController) and camera detection
    /// (RGBSensor), without depending on Unity's project-level Tag Manager.
    /// Attach automatically at creation time — see WarehouseManager (walls,
    /// racks) and the dynamic-object prefabs (human, forklift, obstacle).
    /// </summary>
    public class EnvironmentCollisionTag : MonoBehaviour
    {
        public enum Kind { Wall, Rack, DynamicObstacle }

        public Kind kind;

        public static EnvironmentCollisionTag Attach(GameObject go, Kind kind)
        {
            var existing = go.GetComponent<EnvironmentCollisionTag>();
            if (existing == null) existing = go.AddComponent<EnvironmentCollisionTag>();
            existing.kind = kind;
            return existing;
        }
    }
}
