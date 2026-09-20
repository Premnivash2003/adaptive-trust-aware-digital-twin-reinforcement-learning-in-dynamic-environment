using UnityEngine;
using UnityEngine.AI;
using ATADTRL.Core;
using ATADTRL.Scenarios;

namespace ATADTRL.Environment
{
    /// <summary>
    /// Drives a single dynamic environment object (human, forklift, or movable
    /// obstacle) according to a DynamicObjectSpawn definition. Ground truth for
    /// this object is read by GroundTruthManager via GetState().
    /// </summary>
    public class DynamicObjectMover : MonoBehaviour
    {
        public string ObjectId { get; private set; }
        public string JobName { get; private set; }
        public DynamicObjectType Type { get; private set; }
        public bool IsActive { get; private set; }
        public string CurrentWorkStage { get; private set; } = "Assigned";
        public bool IsCarryingCargo { get; private set; }
        public int CompletedWorkTrips { get; private set; }
        public Vector3 WorkSource => _start;
        public Vector3 WorkDestination => _target;

        private Vector3 _start;
        private Vector3 _target;
        private float _speed;
        private MovementPattern _pattern;
        private float _activationDelay;
        private float _spawnTime;
        private int _direction = 1;
        private Vector3 _velocity;
        private float _avoidanceRadius;
        private float _robotReactionTime;
        private float _robotFirstSeenTime = -1f;
        private bool _stopAtTarget;
        private bool _legacyFirstLeg;
        private bool _repeatWork = true;
        private float _workTimer, _holdUntil, _currentSpeed, _nextPathTime, _walkPhase;
        private Vector3[] _corners;
        private int _cornerIndex;
        private NavMeshPath _workPath;
        private readonly RaycastHit[] _movementHits = new RaycastHit[16];

        private static int _idCounter = 0;

        private void Awake()
        {
            // NavMeshPath allocates native Unity state. Prefab constructors
            // and field initializers may run on the loading thread.
            if (_workPath == null) _workPath = new NavMeshPath();
        }

        public void Initialize(DynamicObjectSpawn spawn)
        {
            // Inactive prefab instances can be configured before Awake.
            if (_workPath == null) _workPath = new NavMeshPath();
            Type = spawn.type;
            _start = spawn.startPosition;
            _target = spawn.targetPosition;
            _speed = spawn.speed;
            _pattern = spawn.pattern;
            _activationDelay = spawn.activationDelay;
            _robotReactionTime = Mathf.Max(0f, spawn.robotReactionTime);
            _robotFirstSeenTime = -1f;
            _stopAtTarget = spawn.stopAtTarget;
            _legacyFirstLeg = spawn.robotReactionTime > 0f || spawn.stopAtTarget;
            _direction = 1;
            IsCarryingCargo = _legacyFirstLeg;
            CurrentWorkStage = _legacyFirstLeg ? "Transport" : "Load / inspect at source";
            _workTimer = _legacyFirstLeg ? 0f : 2.5f;
            _spawnTime = Time.time;
            if (!_legacyFirstLeg && spawn.type != DynamicObjectType.MovableObstacle)
            {
                if (NavMesh.SamplePosition(_start,out NavMeshHit startHit,2f,NavMesh.AllAreas)) _start=startHit.position;
                if (NavMesh.SamplePosition(_target,out NavMeshHit endHit,2f,NavMesh.AllAreas)) _target=endHit.position;
            }
            ObjectId = string.IsNullOrWhiteSpace(spawn.actorId) ? $"{Type}_{++_idCounter}" : spawn.actorId;
            JobName = string.IsNullOrWhiteSpace(spawn.jobName) ? Type.ToString() : spawn.jobName;
            _avoidanceRadius = Type == DynamicObjectType.Forklift ? 0.55f : Type == DynamicObjectType.Human ? 0.28f : 0.40f;

            transform.position = _start;
            IsActive = _activationDelay <= 0f;
            WarehouseActorVisualBuilder.EnsureDynamicActorVisual(gameObject, Type);
            WarehouseActorVisualBuilder.EnsureJobVisual(gameObject, ObjectId, JobName, Type);
            if (Type==DynamicObjectType.Human && GetComponent<CapsuleCollider>() is CapsuleCollider humanShape)
            { humanShape.center=new Vector3(0f,0.95f,0f);humanShape.height=1.9f;humanShape.radius=0.26f; }
            if (Type==DynamicObjectType.Forklift && GetComponent<BoxCollider>() is BoxCollider forkliftShape)
            { forkliftShape.center=new Vector3(0f,0.72f,0.15f);forkliftShape.size=new Vector3(1.05f,1.45f,1.85f); }
            SetPresentationAndCollision(IsActive);

            EnvironmentCollisionTag.Attach(gameObject, EnvironmentCollisionTag.Kind.DynamicObstacle);
            UpdateJobPresentation();
        }

        public void AssignWorkRoute(Vector3 source, Vector3 destination, string jobName,
            bool repeat = true, bool startsLoaded = false)
        {
            _start = NavMesh.SamplePosition(source,out NavMeshHit a,2f,NavMesh.AllAreas) ? a.position : source;
            _target = NavMesh.SamplePosition(destination,out NavMeshHit b,2f,NavMesh.AllAreas) ? b.position : destination;
            JobName = jobName;
            _repeatWork = repeat; _legacyFirstLeg = false; _stopAtTarget = false;
            _pattern = MovementPattern.LinearPatrol; _activationDelay = 0f;
            IsActive = true; _direction = startsLoaded ? 1 : -1;
            IsCarryingCargo = startsLoaded; _workTimer = 0f; _holdUntil = 0f;
            _corners = null; _nextPathTime = 0f;
            CurrentWorkStage = startsLoaded ? "Transport" : "Travel to collection";
            SetPresentationAndCollision(true);
            UpdateJobPresentation();
        }

        public void HoldForWork(string stage, float seconds)
        {
            CurrentWorkStage = stage;
            _holdUntil = Time.time + Mathf.Max(0f, seconds);
            _velocity = Vector3.zero;
        }

        private void Update()
        {
            if (!IsActive)
            {
                if (Time.time - _spawnTime >= _activationDelay)
                {
                    IsActive = true;
                    SetPresentationAndCollision(true);
                }
                else
                {
                    return;
                }
            }

            if (Time.time < _holdUntil) { _velocity = Vector3.zero; AnimateWork(); return; }
            if (!_legacyFirstLeg && Type != DynamicObjectType.MovableObstacle &&
                (_pattern == MovementPattern.LinearPatrol || _pattern == MovementPattern.CrossPath))
            {
                PerformWorkCycle();
                AnimateWork();
                return;
            }

            switch (_pattern)
            {
                case MovementPattern.Static:
                    _velocity = Vector3.zero;
                    break;

                case MovementPattern.LinearPatrol:
                case MovementPattern.CrossPath:
                    MoveBackAndForth();
                    break;

                case MovementPattern.RandomWalk:
                    MoveBackAndForth();
                    break;

                case MovementPattern.Scripted:
                    // Reserved for future extension; no-op in Module 1.
                    _velocity = Vector3.zero;
                    break;
            }
            AnimateWork();
        }

        private void PerformWorkCycle()
        {
            if (_workTimer > 0f)
            {
                _velocity = Vector3.zero; _currentSpeed = 0f;
                _workTimer -= Time.deltaTime;
                if (_workTimer > 0f) return;
                IsCarryingCargo = _direction > 0 && ObjectId != "H3" && ObjectId != "H5";
                UpdateJobPresentation();
            }
            Vector3 destination = _direction > 0 ? _target : _start;
            Vector3 planar = destination - transform.position; planar.y = 0f;
            if (planar.magnitude <= 0.28f)
            {
                _velocity = Vector3.zero;
                if (_direction > 0)
                {
                    CompletedWorkTrips++;
                    CurrentWorkStage = ObjectId == "H5" ? "Inspect / replenish rack" :
                        ObjectId == "H3" ? "Scan / verify station order" : "Unload / confirm hand-off";
                    _direction = -1;
                    Debug.Log($"ATADTRL WORK: {ObjectId} | {JobName} | hand-off {CompletedWorkTrips}");
                    if (!_repeatWork) { _pattern = MovementPattern.Static; IsCarryingCargo = false; UpdateJobPresentation(); return; }
                }
                else { CurrentWorkStage = "Load / inspect at source"; _direction = 1; }
                _workTimer = Type == DynamicObjectType.Forklift ? 3f : ObjectId == "H5" ? 4f : 1.8f;
                _corners = null;
                return;
            }
            if (_corners == null || Time.time >= _nextPathTime)
            {
                _nextPathTime = Time.time + 2f;
                if (!NavMesh.SamplePosition(transform.position, out NavMeshHit from, 1.3f, NavMesh.AllAreas) ||
                    !NavMesh.SamplePosition(destination, out NavMeshHit to, 1.3f, NavMesh.AllAreas) ||
                    !NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, _workPath) ||
                    _workPath.status != NavMeshPathStatus.PathComplete)
                {
                    _corners = null; _cornerIndex = 0; _currentSpeed = 0f;
                    CurrentWorkStage = "Waiting for accessible work route"; _velocity = Vector3.zero; return;
                }
                _corners = _workPath.corners; _cornerIndex = _corners.Length > 1 ? 1 : 0;
            }
            if (_corners == null || _corners.Length == 0) return;
            while (_cornerIndex < _corners.Length - 1 &&
                   Vector3.Distance(transform.position, _corners[_cornerIndex]) < 0.35f) _cornerIndex++;
            Vector3 dir = _corners[_cornerIndex] - transform.position; dir.y = 0f;
            if (dir.sqrMagnitude < 0.001f) { _corners = null; return; }
            dir.Normalize();
            float lookAhead = _avoidanceRadius + 0.25f + _currentSpeed * _currentSpeed / 2f;
            Vector3 clear = FindClearMovementDirection(dir, lookAhead);
            float targetSpeed = 0f;
            if (clear == Vector3.zero) CurrentWorkStage = "Yield to occupied route";
            else
            {
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(clear),
                    (Type == DynamicObjectType.Forklift ? 65f : 160f) * Time.deltaTime);
                float turn = Vector3.Angle(transform.forward, clear);
                targetSpeed = _speed * (IsCarryingCargo ? 0.82f : 1f) * Mathf.Clamp01(1f - turn / 70f);
                CurrentWorkStage = _direction > 0 ? "Transport to work bay" : "Return for next assignment";
                // A forklift must steer into its route, not translate sideways.
                clear = transform.forward;
                Vector3 swept = FindClearMovementDirection(clear, lookAhead);
                if (swept == Vector3.zero || Vector3.Angle(swept, clear) > 5f) clear = Vector3.zero;
            }
            _currentSpeed = Mathf.MoveTowards(_currentSpeed, targetSpeed, Time.deltaTime * (targetSpeed < _currentSpeed ? 2f : 0.8f));
            Vector3 step = clear * Mathf.Min(planar.magnitude, _currentSpeed * Time.deltaTime);
            if (step.sqrMagnitude > 0f && NavMesh.Raycast(transform.position, transform.position + step, out _, NavMesh.AllAreas)) step = Vector3.zero;
            transform.position += step;
            _velocity = Time.deltaTime > 0f ? step / Time.deltaTime : Vector3.zero;
        }

        private void UpdateJobPresentation()
        {
            Transform detail = transform.Find("Detailed visual/Job equipment");
            if (detail == null) return;
            foreach (Transform item in detail)
                if (item.name.Contains("load") || item.name.Contains("carton") || item.name.Contains("tote") || item.name == "Pallet base")
                    item.gameObject.SetActive(IsCarryingCargo);
        }

        private void AnimateWork()
        {
            Transform detail = transform.Find("Detailed visual");
            if (detail == null) return;
            _walkPhase += _velocity.magnitude * Time.deltaTime * 6f;
            int side = 1;
            foreach (Transform part in detail)
            {
                if (Type==DynamicObjectType.Human && part.name=="Arm")
                    part.localRotation=Quaternion.Euler(IsCarryingCargo ? -55f : Mathf.Sin(_walkPhase)*12f*Mathf.Clamp01(_velocity.magnitude),0f,0f);
                if (Type == DynamicObjectType.Human && part.name == "Leg")
                {
                    part.localRotation = Quaternion.Euler(Mathf.Sin(_walkPhase) * 15f * side * Mathf.Clamp01(_velocity.magnitude), 0f, 0f);
                    side *= -1;
                }
                if (Type == DynamicObjectType.Forklift && (part.name == "Fork left" || part.name == "Fork right"))
                {
                    Vector3 p = part.localPosition;
                    p.y = Mathf.MoveTowards(p.y, IsCarryingCargo ? 0.23f : 0.13f, Time.deltaTime * 0.15f);
                    part.localPosition = p;
                }
            }
        }

        private void MoveBackAndForth()
        {
            Vector3 dest = _direction > 0 ? _target : _start;
            Vector3 toDest = dest - transform.position;
            float dist = toDest.magnitude;

            if (dist < 0.15f)
            {
                if (_stopAtTarget && _direction > 0)
                {
                    transform.position = dest;
                    _velocity = Vector3.zero;
                    _pattern = MovementPattern.Static;
                    Debug.Log($"ATADTRL ACTOR: {ObjectId} completed '{JobName}' and is holding at " +
                              $"({dest.x:F2}, {dest.y:F2}, {dest.z:F2}).");
                    return;
                }
                _direction *= -1;
                return;
            }

            Vector3 dir = toDest.normalized;
            Vector3 movementDirection = FindClearMovementDirection(
                dir, Mathf.Min(dist, _speed * Time.deltaTime + _avoidanceRadius));
            if (movementDirection == Vector3.zero)
            {
                _velocity = Vector3.zero;
                return;
            }

            _velocity = movementDirection * _speed;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(movementDirection), Time.deltaTime * 8f);
            transform.position += _velocity * Time.deltaTime;
        }

        // Actors avoid parcels, robots, forklifts, people and rack/wall
        // colliders. A finite reaction interval can model a worker who sees a
        // hazard late at a blind rack end; the actor still follows its normal
        // route and becomes fully responsive when that interval has elapsed.
        private Vector3 FindClearMovementDirection(Vector3 preferred, float distance)
        {
            if (IsMovementClear(preferred, distance)) return preferred;
            Vector3 left = Quaternion.Euler(0f, -55f, 0f) * preferred;
            if (IsMovementClear(left, distance)) return left;
            Vector3 right = Quaternion.Euler(0f, 55f, 0f) * preferred;
            if (IsMovementClear(right, distance)) return right;
            return Vector3.zero;
        }

        private bool IsMovementClear(Vector3 direction, float distance)
        {
            Vector3 origin = transform.position + Vector3.up * 0.55f;
            int hitCount = Physics.SphereCastNonAlloc(origin, _avoidanceRadius, direction, _movementHits,
                Mathf.Max(0.15f, distance), ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hitCount; i++)
            {
                var hit = _movementHits[i];
                if (hit.collider == null || hit.collider.transform.IsChildOf(transform)) continue;

                // The warehouse floor and other decorative geometry are
                // colliders too.  Only physical actors and tagged warehouse
                // objects are movement blockers; otherwise every worker
                // would falsely see the floor as an obstacle and stand still.
                var tag = hit.collider.GetComponentInParent<EnvironmentCollisionTag>();
                var mover = hit.collider.GetComponentInParent<DynamicObjectMover>();
                var robot = hit.collider.GetComponentInParent<ATADTRL.Robot.RobotController>();
                if (robot != null && _robotReactionTime > 0f)
                {
                    if (_robotFirstSeenTime < 0f) _robotFirstSeenTime = Time.time;
                    if (Time.time - _robotFirstSeenTime < _robotReactionTime) continue;
                }
                if (tag == null && mover == null && robot == null) continue;
                return false;
            }
            return true;
        }

        private float _randomTimer;
        private Vector3 _randomDir = Vector3.forward;

        private void MoveRandom()
        {
            _randomTimer -= Time.deltaTime;
            if (_randomTimer <= 0f)
            {
                float angle = Random.Range(0f, 360f);
                _randomDir = Quaternion.Euler(0, angle, 0) * Vector3.forward;
                _randomTimer = Random.Range(1.5f, 4f);
            }
            _velocity = _randomDir * _speed;
            transform.position += _velocity * Time.deltaTime;
        }

        public void Deactivate()
        {
            IsActive = false;
            SetPresentationAndCollision(false);
        }

        public void ActivateNow()
        {
            if (IsActive) return;
            IsActive = true;
            _spawnTime = Time.time;
            _velocity = Vector3.zero;
            SetPresentationAndCollision(true);
        }

        // Do not deactivate this GameObject for delayed spawns: an inactive
        // object receives no Update calls and therefore can never reach its
        // activation time.  Hide it and disable its colliders instead.
        private void SetPresentationAndCollision(bool enabled)
        {
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                // The prefab's original single cube/capsule is retained only
                // for its collider. The detailed child model is its display.
                bool isHiddenSourceMesh = renderer.gameObject == gameObject && GetComponent<WarehouseActorVisual>() != null;
                renderer.enabled = enabled && !isHiddenSourceMesh;
            }

            foreach (var collider in GetComponentsInChildren<Collider>(true))
            {
                // The root collider is the single deliberate safety volume.
                // Primitive colliders used to assemble the detailed visual
                // stay off, otherwise one actor gets dozens of overlapping
                // physics surfaces and produces false collision reports.
                collider.enabled = enabled && collider.gameObject == gameObject;
            }
        }

        public DynamicObjectState GetState()
        {
            return new DynamicObjectState
            {
                objectId = ObjectId,
                type = Type,
                position = transform.position,
                velocity = _velocity,
                pattern = _pattern,
                active = IsActive
            };
        }
    }
}
