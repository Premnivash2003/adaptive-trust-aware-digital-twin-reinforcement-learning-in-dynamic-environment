using UnityEngine;
using ATADTRL.Core;
using ATADTRL.Robot;
using ATADTRL.Sensors;

namespace ATADTRL.Navigation
{
    /// <summary>
    /// Ties together the robot, its active INavigationController implementation,
    /// and the sensor manager's obstacle-distance feed. This is the only class
    /// that needs to change if a new navigation strategy (e.g. PPO in Module 4)
    /// is swapped in, since it depends only on the INavigationController interface.
    /// </summary>
    public class NavigationManager : MonoBehaviour
    {
        public RobotController robotController;
        public SensorManager sensorManager;

        private INavigationController _navController;
        private Vector3 _currentGoal;
        private Vector3 _commandedGoal;
        private bool _goalReached;
        private bool _useEncoderLocalization;

        public NavigationStatus Status => _navController != null ? _navController.CurrentStatus : NavigationStatus.Idle;
        public bool GoalReached => _goalReached;
        public float PathLength => _navController != null ? _navController.PathLengthSoFar : 0f;
        public int ReplanningEvents => _navController != null ? _navController.ReplanningEventCount : 0;
        public int StopCount => _navController != null ? _navController.StopCount : 0;
        public bool UsesEncoderLocalization => _useEncoderLocalization;
        public Vector3 CommandedGoal => _commandedGoal;
        public Vector3 EncoderPositionError { get; private set; }

        private void Awake()
        {
            _navController = GetComponent<BaselineNavigationController>();
            if (_navController == null)
            {
                Debug.LogError("NavigationManager requires a BaselineNavigationController (or other INavigationController) on the same GameObject.");
            }
        }

        public void Initialize()
        {
            _navController.Initialize(robotController.transform, robotController.config);
        }

        public void SetGoal(Vector3 goal)
        {
            _currentGoal = goal;
            _commandedGoal = goal;
            _goalReached = false;

            // Optional conventional-baseline mode for experiments that use
            // raw wheel odometry without TAM/fusion. Convert the believed
            // displacement to a NavMesh command so drift affects navigation.
            if (_useEncoderLocalization && sensorManager != null && sensorManager.encoder != null && robotController != null)
            {
                EncoderPositionError = robotController.transform.position - sensorManager.encoder.EstimatedPosition;
                EncoderPositionError = Vector3.ClampMagnitude(EncoderPositionError, 4f);
                _commandedGoal = goal + EncoderPositionError;
                Debug.Log($"ENCODER LOCALIZATION MODE: physical goal={goal}, commanded goal={_commandedGoal}, " +
                          $"pose error={EncoderPositionError.magnitude:F2}m.");
            }

            _navController.SetGoal(_commandedGoal);
        }

        public void SetEncoderLocalizationMode(bool enabled)
        {
            _useEncoderLocalization = enabled;
            EncoderPositionError = Vector3.zero;
            _commandedGoal = _currentGoal;
        }

        /// <summary>
        /// Repositions the robot between episodes/scenarios via the active
        /// navigation controller's Warp implementation, keeping the
        /// NavMeshAgent's internal state in sync instead of fighting it
        /// with a raw transform set.
        /// </summary>
        public void WarpRobot(Vector3 position, Quaternion rotation)
        {
            _navController.WarpTo(position, rotation);
        }

        private void Update()
        {
            if (_navController == null) return;

            if (sensorManager != null && _navController is BaselineNavigationController baseline)
            {
                float forwardDistance = sensorManager.GetForwardObstacleDistance();
                baseline.ReportNearestObstacleDistance(forwardDistance);
            }

            _navController.Tick(Time.deltaTime);

            if (robotController != null && robotController.IsColliding && _navController is BaselineNavigationController bc)
            {
                bc.ReportCollision();
            }

            if (_navController.CurrentStatus == NavigationStatus.GoalReached)
            {
                _goalReached = true;
            }
        }

        public RobotGroundTruthState GetGroundTruthState()
        {
            return robotController.BuildGroundTruthState(_currentGoal, _navController.CurrentStatus, _goalReached);
        }

        public void ResetNavigation()
        {
            _goalReached = false;
            _navController.ResetController();
        }

        public void HoldAtTaskPoint()
        {
            if (_navController is BaselineNavigationController baseline) baseline.HoldAtTaskPoint();
        }
    }
}
