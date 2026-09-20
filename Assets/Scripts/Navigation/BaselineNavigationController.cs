using UnityEngine;
using UnityEngine.AI;
using ATADTRL.Core;

namespace ATADTRL.Navigation
{
    /// <summary>
    /// Conventional (non-RL) NavMeshAgent-based navigation controller.
    /// Handles obstacle-proximity stopping/resuming, goal-arrival detection,
    /// and basic replanning-event counting. This is the Module 1 baseline
    /// that later modules' RL policy will be benchmarked against.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class BaselineNavigationController : MonoBehaviour, INavigationController
    {
        private NavMeshAgent _agent;
        private RobotConfig _config;
        private Vector3 _goal;
        private NavigationStatus _status = NavigationStatus.Idle;
        private bool _hasGoal;
        private bool _stoppedForObstacle;
        private float _pathLength;
        private Vector3 _lastPos;
        private int _replanCount;
        private int _stopCount;
        private float[] _lidarDistancesCache;

        public NavigationStatus CurrentStatus => _status;
        public bool HasGoal => _hasGoal;
        public float PathLengthSoFar => _pathLength;
        public int ReplanningEventCount => _replanCount;
        public int StopCount => _stopCount;

        public void Initialize(Transform robotTransform, RobotConfig config)
        {
            _config = config;
            _agent = GetComponent<NavMeshAgent>();
            _agent.speed = config.maxLinearSpeed;
            _agent.angularSpeed = config.maxAngularSpeed;
            _agent.acceleration = Mathf.Max(0.1f, config.acceleration);
            _agent.radius = Mathf.Max(config.robotWidth, config.robotLength) / 2f;
            _agent.height = config.robotHeight;
            _agent.stoppingDistance = config.goalTolerance * 0.5f;
            _agent.autoBraking = true;
            _lastPos = robotTransform.position;
        }

        public void SetGoal(Vector3 goal)
        {
            _goal = goal;
            _hasGoal = true;
            _lastPos = transform.position;
            _stoppedForObstacle = false;

            if (_agent.isOnNavMesh)
            {
                _agent.isStopped = false;
                bool ok = _agent.SetDestination(goal);
                _status = ok ? NavigationStatus.Planning : NavigationStatus.Failed;
                if (!ok)
                {
                    Debug.LogWarning($"BaselineNavigationController.SetGoal: SetDestination({goal}) returned false " +
                                      $"from robot position {transform.position}. The goal is likely off the baked " +
                                      "NavMesh, or unreachable from the robot's current position.");
                }
            }
            else
            {
                _status = NavigationStatus.Failed;
                Debug.LogWarning($"BaselineNavigationController.SetGoal: agent is NOT on the NavMesh at position " +
                                  $"{transform.position}. The robot cannot navigate until it's placed on walkable " +
                                  "area. Check that the NavMesh was baked (see GameManager's bake-verification log) " +
                                  "and that this position falls within the baked walkable surface.");
            }
        }

        /// <summary>
        /// Called by NavigationManager each tick with the nearest obstacle
        /// distance (derived from LiDAR) so this controller can implement
        /// reactive stop/resume behavior without knowing about sensors.
        /// </summary>
        public void ReportNearestObstacleDistance(float distance)
        {
            if (!_hasGoal) return;

            float brakingDistance = _agent.velocity.sqrMagnitude / (2f * Mathf.Max(0.1f, _config.deceleration));
            float stoppingThreshold = _config.obstacleStopDistance + brakingDistance;
            float resumeThreshold = Mathf.Max(_config.obstacleResumeDistance, stoppingThreshold + 0.25f);

            if (!_stoppedForObstacle && distance < stoppingThreshold)
            {
                _stoppedForObstacle = true;
                _agent.isStopped = true;
                _status = NavigationStatus.Stopped;
                _stopCount++;
            }
            else if (_stoppedForObstacle && distance > resumeThreshold)
            {
                _stoppedForObstacle = false;
                _agent.isStopped = false;
                _status = NavigationStatus.Moving;
                _replanCount++;
            }
        }

        public void Tick(float deltaTime)
        {
            if (!_hasGoal || _agent == null || !_agent.isOnNavMesh) return;

            float step = Vector3.Distance(transform.position, _lastPos);
            _pathLength += step;
            _lastPos = transform.position;

            if (_agent.pathPending)
            {
                _status = NavigationStatus.Planning;
                return;
            }

            if (!_stoppedForObstacle)
            {
                _status = _agent.velocity.magnitude > 0.01f ? NavigationStatus.Moving : NavigationStatus.Idle;
            }

            float distToGoal = Vector3.Distance(transform.position, _goal);
            if (distToGoal <= _config.goalTolerance)
            {
                _status = NavigationStatus.GoalReached;
                _agent.isStopped = true;
            }
        }

        public void ReportCollision()
        {
            _status = NavigationStatus.Collided;
        }

        public void HoldAtTaskPoint()
        {
            _hasGoal = false;
            if (_agent != null && _agent.isOnNavMesh) _agent.isStopped = true;
            _status = NavigationStatus.Idle;
        }

        public void SetPayload(float massKg)
        {
            if (_agent==null || _config==null) return;
            bool loaded=massKg>0f;
            _agent.speed=_config.maxLinearSpeed*(loaded?0.8f:1f);
            _agent.angularSpeed=_config.maxAngularSpeed*(loaded?0.75f:1f);
            _agent.acceleration=_config.acceleration*(loaded?0.65f:1f);
        }

        public void ResetController()
        {
            _hasGoal = false;
            _status = NavigationStatus.Idle;
            _stoppedForObstacle = false;
            _pathLength = 0f;
            _replanCount = 0;
            _stopCount = 0;
            if (_agent != null && _agent.isOnNavMesh)
            {
                _agent.ResetPath();
                _agent.isStopped = true;
            }
        }

        public void WarpTo(Vector3 position, Quaternion rotation)
        {
            if (_agent != null)
            {
                bool warped = _agent.Warp(position);
                if (!warped)
                {
                    // Warp fails if `position` isn't within the agent's
                    // NavMesh sampling radius (e.g. outside all baked
                    // walkable area). Fall back to a raw transform set so
                    // the robot is at least visually placed, and log so the
                    // mismatch is diagnosable instead of silently stalling.
                    Debug.LogWarning($"BaselineNavigationController.WarpTo: NavMeshAgent.Warp failed at {position} " +
                                      "(not close enough to baked NavMesh). Falling back to a direct transform set — " +
                                      "the agent will not be considered on-mesh until it reaches walkable area.");
                    transform.position = position;
                }
            }
            else
            {
                transform.position = position;
            }
            transform.rotation = rotation;
        }
    }
}
