using System;
using System.Collections.Generic;
using UnityEngine;
using ATADTRL.Environment;
using ATADTRL.Sensors;
using ATADTRL.Core;

namespace ATADTRL.Scenarios
{
    // Controlled Module-1 fault fixture. It changes the physical scene or the
    // observation channel; no policy, trust score or desired result is supplied.
    public sealed class ScenarioDisturbanceRuntime
    {
        private ScenarioDefinition _scenario;
        private Transform _robot;
        private WarehouseManager _warehouse;
        private SensorManager _sensors;
        private IReadOnlyList<DynamicObjectMover> _actors;
        private readonly Dictionary<string,Vector3[]> _originalRoutes=new Dictionary<string,Vector3[]>();
        private readonly Dictionary<string,string> _originalJobs=new Dictionary<string,string>();
        private readonly List<GameObject> _props = new List<GameObject>();
        private readonly Dictionary<Collider, Vector3> _lastPropPositions = new Dictionary<Collider, Vector3>();
        private float _lastPropSampleTime = -1f;
        private bool _dispatched, _recovered;
        private float _dispatchAt;
        private float _pickupArrivalTime;
        private Vector3 _deliveryBay;
        public bool Triggered { get; private set; }
        public bool IsActive { get; private set; }
        public float EventStartedAt { get; private set; }
        public string Status { get; private set; } = "Normal warehouse operations";
        public string Phase => !_dispatched ? "ARMED" : _recovered ? "RECOVERED" : IsActive ? "ACTIVE" : "DISPATCHED";
        public string EventCode => _scenario != null ? _scenario.scenarioCode : "NONE";
        public bool WorkerTelemetryDropout => EventCode == "T2" && IsActive;
        public bool CameraDropoutActive => EventCode == "T4" && IsActive;
        public float StationTelemetryDelaySeconds => EventCode == "T5" && IsActive ? 3f : 0f;
        public string CurrentStationUnavailableId { get; private set; } = "";
        public event Action<string, string> EventChanged;

        public void Initialize(ScenarioDefinition scenario, Transform robot, WarehouseManager warehouse,
            SensorManager sensors, IReadOnlyList<DynamicObjectMover> actors)
        {
            _scenario = scenario; _robot = robot; _warehouse = warehouse; _sensors = sensors; _actors = actors;
            if (!IsOperational) return;
            foreach(var a in actors)
            { if(a==null)continue;_originalRoutes[a.ObjectId]=new[]{a.WorkSource,a.WorkDestination};_originalJobs[a.ObjectId]=a.JobName; }
            Status = "Assigned warehouse jobs; awaiting loaded travel";
        }

        private bool IsOperational => _scenario != null && (_scenario.scenarioCode.StartsWith("E") || _scenario.scenarioCode.StartsWith("T"));

        public float GetObservationDelaySeconds(string actorId)
        {
            if (!IsActive) return 0f;
            if (EventCode == "T1" && actorId == "F1") return 2.5f;
            if (EventCode == "T5" && (actorId == "F1" || actorId == "F2" || actorId == "H1" || actorId == "H2" || actorId == "H3")) return 1.5f;
            return 0f;
        }

        public bool IsStationBusy(Vector3 point) => !string.IsNullOrEmpty(CurrentStationUnavailableId) &&
            Vector3.Distance(point, _deliveryBay) < 1.8f;

        public void Tick(float elapsed, bool carrying, string taskStage, string destinationId, Vector3 destination)
        {
            if (!IsOperational) return;

            // E2/E3 concern the pickup side and must be evaluated before the
            // parcel is ever picked, independent of the post-dispatch gate
            // below (which only fires once the robot is already carrying).
            if (EventCode == "E2")
            {
                if (!Triggered && taskStage == "AlignAtSource")
                {
                    StartEvent(elapsed, "Assigned parcel not yet staged at pickup: ParcelReady = 0");
                    _pickupArrivalTime = elapsed;
                    Vector3 pickupPoint = _scenario.pickupPositions != null && _scenario.pickupPositions.Count > 0
                        ? _scenario.pickupPositions[0] : _robot.position;
                    Actor("H1")?.AssignWorkRoute(pickupPoint + Vector3.left * 1.5f, pickupPoint + Vector3.right * 0.4f,
                        "Staging assigned parcel at pickup station", false, true);
                }
                if (Triggered && !_recovered && elapsed - EventStartedAt >= (_scenario.parcelReadyDelaySeconds > 0f ? _scenario.parcelReadyDelaySeconds : 9f))
                    Recover("Human picking process complete: ParcelReady = 1");
                return;
            }
            if (EventCode == "E3")
            {
                if (!Triggered && taskStage == "ScanBarcode")
                    StartEvent(elapsed, $"BARCODE MISMATCH \u2014 ASSIGNED PARCEL NOT PRESENT (expected {(_scenario.parcelBarcodes != null && _scenario.parcelBarcodes.Count > 0 ? _scenario.parcelBarcodes[0] : "?")}, observed {_scenario.mismatchedBarcodeObserved})");
                return;
            }

            if (!_dispatched && carrying && taskStage == "NavigateToDestination")
            {
                _dispatched = true; _dispatchAt = elapsed; _deliveryBay = destination;
                DispatchJobs();
                Emit("DISPATCHED", "Operational order released after verified AMR pickup");
            }
            if (!_dispatched || _recovered) return;
            float age = elapsed - _dispatchAt;
            DynamicObjectMover f1 = Actor("F1"), h5 = Actor("H5");
            if (EventCode == "E1")
            {
                if (!Triggered)
                {
                    StartEvent(elapsed, "Destination station at maximum receiving capacity: Current_Load >= Maximum_Capacity");
                    CurrentStationUnavailableId = destinationId;
                    Actor("H3")?.AssignWorkRoute(destination + Vector3.left * 1.2f, destination + Vector3.right * 0.4f,
                        "Receiving-dock inspection: station at maximum capacity", false, true);
                }
                if (Triggered && elapsed - EventStartedAt >= 16f) Recover("Outbound pallet cleared; destination capacity available");
            }
            else if (EventCode == "E4")
            {
                if (!Triggered)
                {
                    StartEvent(elapsed, "S2 temporarily unavailable: legitimate warehouse operation in progress");
                    CurrentStationUnavailableId = destinationId;
                    Actor("H3")?.AssignWorkRoute(destination + Vector3.left * 1.2f, destination + Vector3.right * 0.4f,
                        "S2 receiving bay occupied: legitimate handling in progress", false, true);
                }
                if (Triggered && elapsed - EventStartedAt >= 18f) Recover("S2 operation complete; station available again. PKG_E402 still bound for S3.");
            }
            else if (EventCode == "E5")
            {
                if (!Triggered)
                {
                    StartEvent(elapsed, $"{destinationId} temporarily unavailable: legitimate warehouse operation in progress");
                    CurrentStationUnavailableId = destinationId;
                    Actor("H3")?.AssignWorkRoute(destination + Vector3.left * 1.2f, destination + Vector3.right * 0.4f,
                        "Station receiving bay occupied: legitimate handling in progress", false, true);
                }
                if (Triggered && elapsed - EventStartedAt >= 18f) Recover("Station reopened after legitimate operation completed");
            }
            else if (EventCode == "T3")
            {
                if (!Triggered && f1 != null && Vector3.Distance(_robot.position, f1.transform.position) < 7f)
                {
                    StartEvent(elapsed, "Wrapped load exposure: increased LiDAR measurement variance");
                    var ov = ScenarioOverride.Default; ov.lidarNoiseMultiplier = 6f; _sensors.ApplyScenarioOverrides(ov);
                }
                if (Triggered && elapsed - EventStartedAt >= 8f) Recover("LiDAR exposure ended; nominal sensor profile restored");
            }
            else
            {
                if (!Triggered && age >= 1f)
                {
                    StartEvent(elapsed, EventCode == "T1" ? "F1 telemetry latency: 2.5 seconds" :
                        EventCode == "T2" ? "H2 worker-tag link interrupted" : EventCode == "T4" ? "Camera stream unavailable" :
                        EventCode == "T5" ? "Dispatch update backlog: actor and station channels delayed" : "Shared crossing work assignments active");
                    if (EventCode == "T4") { var ov = ScenarioOverride.Default; ov.forceCameraDropout = true; _sensors.ApplyScenarioOverrides(ov); }
                }
                if (EventCode == "T5")
                    CurrentStationUnavailableId = Actor("H3") != null && Vector3.Distance(Actor("H3").transform.position, _deliveryBay)<1.1f ? destinationId : "";
                float duration = EventCode == "T1" ? 12f : EventCode == "T2" ? 8f : EventCode == "T4" ? 7f : 10f;
                if (Triggered && elapsed - EventStartedAt >= duration) Recover("Disturbance window cleared; ordinary jobs continue");
            }
        }

        private void DispatchJobs()
        {
            Vector3 c = _scenario.disturbanceLocation;
            if (EventCode == "E1" || EventCode == "E3" || EventCode == "T3")
                Actor("F1")?.AssignWorkRoute(c + Vector3.forward * 3f, EventCode == "E1" ? c : c + Vector3.back * 3f,
                    "Inbound replenishment across shared handoff", false, true);
            if (EventCode == "E2" || EventCode == "T5")
            {
                Actor("H1")?.AssignWorkRoute(c + Vector3.forward * 3f, c + Vector3.back * 3f, "Picking wave: rack carton to dispatch", true, true);
                Actor("H2")?.AssignWorkRoute(c + Vector3.back * 4f, c + Vector3.forward * 4f, "Picking wave: replenish station bins", true, true);
                Actor("H4")?.AssignWorkRoute(c + Vector3.left * 2f, c + Vector3.right * 2f, "Trolley transfer for picking wave", true, true);
            }
            if (EventCode == "E4") Actor("H5")?.AssignWorkRoute(Actor("H5").transform.position,
                c + Vector3.forward * 2f, "Legitimate operation occupying S2 receiving bay", false, true);
            if (EventCode == "E5" || EventCode == "T5")
            {
                Actor("H3")?.AssignWorkRoute(Actor("H3").transform.position, _deliveryBay + Vector3.forward * 0.7f,
                    EventCode == "E5" ? "Legitimate operation occupying station receiving bay" : "Dispatch manifest and barcode verification", false, true);
                Actor("F2")?.AssignWorkRoute(Actor("F2").transform.position, _deliveryBay + Vector3.back * 1.4f,
                    "Outbound dispatch pallet collection", true, true);
            }
            if (EventCode == "T5") Actor("F1")?.AssignWorkRoute(c + Vector3.forward * 2f, c + Vector3.back * 4f,
                "Reassigned inbound consignment", true, true);
        }

        private DynamicObjectMover Actor(string id)
        {
            foreach (var a in _actors) if (a != null && a.ObjectId == id) return a;
            return null;
        }
        private void StartEvent(float time, string status)
        { Triggered = true; IsActive = true; EventStartedAt = time; Emit("ACTIVE", status); }
        private void Recover(string status)
        {
            IsActive = false; _recovered = true; CurrentStationUnavailableId = ""; _sensors.ClearOverrides();
            if(EventCode.StartsWith("E") || EventCode=="T3" || EventCode=="T5")
                foreach(var a in _actors)
                {
                    if(a==null||!_originalRoutes.TryGetValue(a.ObjectId,out Vector3[] route))continue;
                    a.AssignWorkRoute(route[0],route[1],_originalJobs[a.ObjectId],true,a.IsCarryingCargo);
                }
            Emit("RECOVERED", status);
        }
        private void Emit(string phase, string status)
        { Status = status; Debug.Log($"ATADTRL EVENT {EventCode} {phase}: {status}"); EventChanged?.Invoke(phase, status); }
        public void Restore()
        {
            _sensors?.ClearOverrides();
            foreach (var prop in _props) if (prop != null) UnityEngine.Object.Destroy(prop);
            _props.Clear();
            _lastPropPositions.Clear();
        }
        public void AppendPhysicalGroundTruth(GroundTruthRecord record, float time)
        {
            float dt = time - _lastPropSampleTime;
            foreach (GameObject prop in _props)
            {
                if (prop == null) continue;
                foreach (Collider shape in prop.GetComponentsInChildren<Collider>())
                {
                    if (!shape.enabled || shape.isTrigger) continue;
                    Vector3 p = shape.bounds.center;
                    Vector3 v = _lastPropSampleTime >= 0f && dt > 0f && _lastPropPositions.TryGetValue(shape, out Vector3 previous)
                        ? (p - previous) / dt : Vector3.zero;
                    record.obstacle_positions = Append(record.obstacle_positions, p);
                    record.obstacle_velocities = Append(record.obstacle_velocities, v);
                    record.number_of_dynamic_objects++;
                    _lastPropPositions[shape] = p;
                }
            }
            _lastPropSampleTime = time;
        }
        private static string Append(string existing, Vector3 vector) =>
            (string.IsNullOrEmpty(existing) ? "" : existing + "|") +
            FormattableString.Invariant($"{vector.x:F3}:{vector.y:F3}:{vector.z:F3}");
        private static GameObject MakeBox(string name, Transform parent, Vector3 position, Vector3 scale, Color color, bool collides)
        {
            var obj = GameObject.CreatePrimitive(PrimitiveType.Cube); obj.name = name;
            obj.transform.SetParent(parent, false); obj.transform.localPosition = position; obj.transform.localScale = scale;
            obj.GetComponent<Renderer>().material.color = color;
            obj.GetComponent<Collider>().enabled = collides;
            EnvironmentCollisionTag.Attach(obj, EnvironmentCollisionTag.Kind.DynamicObstacle);
            return obj;
        }
    }
}
