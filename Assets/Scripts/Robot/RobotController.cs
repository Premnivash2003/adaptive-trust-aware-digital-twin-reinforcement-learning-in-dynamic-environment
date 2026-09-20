using UnityEngine;
using ATADTRL.Core;
using ATADTRL.Environment;

namespace ATADTRL.Robot
{
    /// <summary>
    /// Holds the robot's physical configuration and exposes its current
    /// kinematic state. Attach to the robot root GameObject alongside a
    /// NavMeshAgent, Rigidbody (kinematic) and CapsuleCollider/BoxCollider.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class RobotController : MonoBehaviour
    {
        [Header("Configuration")]
        public RobotConfig config = new RobotConfig();

        [Header("Runtime State (read-only)")]
        [SerializeField] private float linearVelocity;
        [SerializeField] private float angularVelocity;
        [SerializeField] private bool colliding;

        private Vector3 _lastPosition;
        private float _lastYaw;
        private Rigidbody _rb;

        public float LinearVelocity => linearVelocity;
        public float AngularVelocity => angularVelocity;
        public bool IsColliding => colliding;
        public DynamicObjectType? LastCollisionActorType { get; private set; }

        private void Awake()
        {
            WarehouseActorVisualBuilder.EnsureRobotVisual(gameObject);
            _rb = GetComponent<Rigidbody>();
            _rb.isKinematic = true;
            _rb.useGravity = false;

            var col = GetComponent<Collider>();
            if (col == null)
            {
                var box = gameObject.AddComponent<BoxCollider>();
                box.size = new Vector3(config.robotWidth, config.robotHeight, config.robotLength);
            }

            _lastPosition = transform.position;
            _lastYaw = transform.eulerAngles.y;
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            if (dt <= 0f) return;

            float dist = Vector3.Distance(transform.position, _lastPosition);
            linearVelocity = dist / dt;

            float yaw = transform.eulerAngles.y;
            float dYaw = Mathf.DeltaAngle(_lastYaw, yaw);
            angularVelocity = dYaw / dt;

            _lastPosition = transform.position;
            _lastYaw = yaw;
        }

        public RobotGroundTruthState BuildGroundTruthState(Vector3 goal, NavigationStatus status, bool goalReached)
        {
            return new RobotGroundTruthState
            {
                position = transform.position,
                yaw = transform.eulerAngles.y,
                linearVelocity = linearVelocity,
                angularVelocity = angularVelocity,
                goal = goal,
                distanceToGoal = Vector3.Distance(transform.position, goal),
                collision = colliding,
                goalReached = goalReached,
                navStatus = status
            };
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (collision.gameObject.GetComponent<EnvironmentCollisionTag>() != null)
            {
                colliding = true;
            }
            var dynamicActor = collision.gameObject.GetComponentInParent<DynamicObjectMover>();
            if (dynamicActor != null) LastCollisionActorType = dynamicActor.Type;
        }

        private void OnCollisionExit(Collision collision)
        {
            colliding = false;
        }

        public void ResetRobot(Vector3 position, Quaternion rotation)
        {
            transform.position = position;
            transform.rotation = rotation;
            _lastPosition = position;
            _lastYaw = rotation.eulerAngles.y;
            linearVelocity = 0f;
            angularVelocity = 0f;
            colliding = false;
            LastCollisionActorType = null;
        }
    }
}
