using UnityEngine;
using ATADTRL.Core;

namespace ATADTRL.Navigation
{
    /// <summary>
    /// Abstraction over the decision mechanism that drives the robot toward a
    /// goal. Module 1 ships BaselineNavigationController (NavMesh-based).
    /// Module 4 will add a PPO-based implementation of this same interface
    /// without requiring changes to RobotController or ScenarioManager.
    /// </summary>
    public interface INavigationController
    {
        void Initialize(Transform robotTransform, RobotConfig config);
        void SetGoal(Vector3 goal);
        void Tick(float deltaTime);
        NavigationStatus CurrentStatus { get; }
        bool HasGoal { get; }
        float PathLengthSoFar { get; }
        int ReplanningEventCount { get; }
        int StopCount { get; }
        void ResetController();

        /// <summary>
        /// Repositions the robot between episodes. Implementations backed by
        /// a NavMeshAgent MUST use NavMeshAgent.Warp() rather than a raw
        /// transform.position set — the agent otherwise silently overwrites
        /// the transform back to its own internal (stale) position on the
        /// next frame, since it drives the transform itself while enabled.
        /// </summary>
        void WarpTo(Vector3 position, Quaternion rotation);
    }
}
