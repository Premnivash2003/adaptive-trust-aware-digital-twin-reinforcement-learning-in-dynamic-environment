using System.Collections.Generic;
using System.Text;
using UnityEngine;
using ATADTRL.Core;
using ATADTRL.Navigation;

namespace ATADTRL.Environment
{
    /// <summary>
    /// Assembles the authoritative GroundTruthRecord for the current
    /// simulation step, reading only from actual Unity simulation state
    /// (robot transform/velocities, dynamic object movers, warehouse
    /// config) — never from sensor observations.
    /// </summary>
    public class GroundTruthManager : MonoBehaviour
    {
        public WarehouseManager warehouseManager;
        public NavigationManager navigationManager;

        public GroundTruthRecord BuildRecord(int scenarioId, int episodeId, long stepId, double timestamp,
            string scenarioTypeName, List<DynamicObjectMover> dynamicObjects)
        {
            var navState = navigationManager.GetGroundTruthState();

            var obstaclePos = new StringBuilder();
            var obstacleVel = new StringBuilder();
            var humanPos = new StringBuilder();
            var humanVel = new StringBuilder();
            var forkliftPos = new StringBuilder();
            var forkliftVel = new StringBuilder();

            foreach (var obj in dynamicObjects)
            {
                if (obj == null || !obj.IsActive) continue;
                var state = obj.GetState();
                string posStr = System.FormattableString.Invariant($"{state.position.x:F3}:{state.position.y:F3}:{state.position.z:F3}");
                string velStr = System.FormattableString.Invariant($"{state.velocity.x:F3}:{state.velocity.y:F3}:{state.velocity.z:F3}");

                switch (state.type)
                {
                    case DynamicObjectType.Human:
                        AppendPipe(humanPos, posStr);
                        AppendPipe(humanVel, velStr);
                        break;
                    case DynamicObjectType.Forklift:
                        AppendPipe(forkliftPos, posStr);
                        AppendPipe(forkliftVel, velStr);
                        break;
                    default:
                        AppendPipe(obstaclePos, posStr);
                        AppendPipe(obstacleVel, velStr);
                        break;
                }
            }

            return new GroundTruthRecord
            {
                timestamp = timestamp,
                scenarioId = scenarioId,
                episodeId = episodeId,
                stepId = stepId,

                robot_x = navState.position.x,
                robot_y = navState.position.y,
                robot_z = navState.position.z,
                robot_yaw = navState.yaw,
                robot_linear_velocity = navState.linearVelocity,
                robot_angular_velocity = navState.angularVelocity,

                goal_x = navState.goal.x,
                goal_y = navState.goal.y,
                goal_z = navState.goal.z,
                distance_to_goal = navState.distanceToGoal,

                number_of_dynamic_objects = CountActive(dynamicObjects),
                obstacle_positions = obstaclePos.ToString(),
                obstacle_velocities = obstacleVel.ToString(),
                human_positions = humanPos.ToString(),
                human_velocities = humanVel.ToString(),
                forklift_positions = forkliftPos.ToString(),
                forklift_velocities = forkliftVel.ToString(),

                collision = navState.collision,
                goal_reached = navState.goalReached,
                navigation_status = navState.navStatus.ToString(),

                warehouse_length = warehouseManager.config.warehouseLength,
                warehouse_width = warehouseManager.config.warehouseWidth,
                aisle_width = warehouseManager.config.aisleWidth,
                scenario_type = scenarioTypeName
            };
        }

        private static int CountActive(List<DynamicObjectMover> objs)
        {
            int c = 0;
            foreach (var o in objs) if (o != null && o.IsActive) c++;
            return c;
        }

        private static void AppendPipe(StringBuilder sb, string value)
        {
            if (sb.Length > 0) sb.Append('|');
            sb.Append(value);
        }
    }
}
