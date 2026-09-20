using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ATADTRL.Core;
using ATADTRL.Environment;
using ATADTRL.Scenarios;
using UnityEngine;

namespace ATADTRL.Sensors
{
    // A conventional timestamped telemetry cache, intentionally without TAM
    // or adaptive synchronization. Truth is captured before channel faults.
    public sealed class OperationalObservationChannel
    {
        private sealed class Frame
        {
            public float time;
            public Dictionary<string, DynamicObjectState> actors = new Dictionary<string, DynamicObjectState>();
            public Dictionary<string, string> work = new Dictionary<string, string>();
            public bool[] stations;
        }
        private readonly List<Frame> _history = new List<Frame>();
        public void Clear() => _history.Clear();
        public void Capture(float time, IEnumerable<DynamicObjectMover> actors, bool[] stations)
        {
            var states=new List<DynamicObjectState>();var jobs=new Dictionary<string,string>();
            foreach (var actor in actors)
            {
                if (actor == null || !actor.IsActive) continue;
                states.Add(actor.GetState());
                jobs[actor.ObjectId] = $"{actor.JobName}:{actor.CurrentWorkStage}:load={actor.IsCarryingCargo}:handoffs={actor.CompletedWorkTrips}";
            }
            CaptureSamples(time,states,jobs,stations);
        }
        public void CaptureSamples(float time,IEnumerable<DynamicObjectState> states,IReadOnlyDictionary<string,string> jobs,bool[] stations)
        {
            var frame = new Frame { time=time, stations=(bool[])stations.Clone() };
            foreach(var state in states)
            {
                frame.actors[state.objectId]=new DynamicObjectState { objectId=state.objectId,type=state.type,
                    position=state.position,velocity=state.velocity,pattern=state.pattern,active=state.active };
                frame.work[state.objectId]=jobs.TryGetValue(state.objectId,out string job) ? job : "Unavailable";
            }
            _history.Add(frame);
            while (_history.Count > 1 && time - _history[0].time > 20f) _history.RemoveAt(0);
        }
        private Frame At(float cutoff)
        {
            for (int i = _history.Count - 1; i >= 0; i--) if (_history[i].time <= cutoff + 0.0001f) return _history[i];
            return null;
        }
        public void Apply(ObservationRecord record, float time, ScenarioDisturbanceRuntime fault)
        {
            var humanP = new List<string>(); var humanV = new List<string>();
            var forkP = new List<string>(); var forkV = new List<string>();
            var ages = new List<string>(); var stamps = new List<string>(); var valid = new List<string>(); var work = new List<string>();
            float maxAge = 0f;
            foreach (string id in new[] { "H1", "H2", "H3", "H4", "H5", "F1", "F2" })
            {
                bool linkLost = id == "H2" && fault != null && fault.WorkerTelemetryDropout;
                float cutoff = linkLost ? fault.EventStartedAt : time - (fault?.GetObservationDelaySeconds(id) ?? 0f);
                Frame frame = At(cutoff);
                bool present = frame != null && frame.actors.TryGetValue(id, out _);
                string p = "UNAVAILABLE", v = "UNAVAILABLE";
                if (present)
                {
                    var actor = frame.actors[id]; p = Vector(actor.position); v = Vector(actor.velocity);
                    if (id.StartsWith("H")) { humanP.Add(id + ":" + p); humanV.Add(id + ":" + v); }
                    else { forkP.Add(id + ":" + p); forkV.Add(id + ":" + v); }
                    float age = Mathf.Max(0f, time - frame.time); maxAge = Mathf.Max(maxAge, age);
                    ages.Add(id + ":" + Number(age)); stamps.Add(id + ":" + Number(frame.time));
                    work.Add(id + ":" + frame.work[id]);
                }
                else { ages.Add(id + ":NA"); stamps.Add(id + ":NA"); }
                valid.Add(id + ":" + (present && !linkLost ? "VALID" : "MISSING"));
                switch (id)
                {
                    case "H1": record.h1_position=p; record.h1_velocity=v; break;
                    case "H2": record.h2_position=p; record.h2_velocity=v; break;
                    case "H3": record.h3_position=p; record.h3_velocity=v; break;
                    case "H4": record.h4_position=p; record.h4_velocity=v; break;
                    case "H5": record.h5_position=p; record.h5_velocity=v; break;
                    case "F1": record.f1_position=p; record.f1_velocity=v; break;
                    case "F2": record.f2_position=p; record.f2_velocity=v; break;
                }
            }
            record.human_positions=string.Join("|",humanP); record.human_velocities=string.Join("|",humanV);
            record.forklift_positions=string.Join("|",forkP); record.forklift_velocities=string.Join("|",forkV);
            record.actor_message_ages=string.Join("|",ages); record.actor_sample_timestamps=string.Join("|",stamps);
            record.actor_link_validity=string.Join("|",valid); record.actor_work_states=string.Join("|",work);
            record.communication_delay=maxAge;
            Frame stationFrame=At(time - (fault?.StationTelemetryDelaySeconds ?? 0f));
            record.station_sample_timestamp=stationFrame != null ? Number(stationFrame.time) : "NA";
            if (stationFrame != null)
            {
                bool[] s=stationFrame.stations;
                record.PD1_available=record.P1_available=s[0]; record.PD2_available=record.P2_available=s[1]; record.PD3_available=record.P3_available=s[2];
                record.D1_available=s[3]; record.D2_available=s[4]; record.D3_available=s[5];
            }
            else
            {
                // No historical sample is not evidence that a bay is available.
                record.PD1_available=record.PD2_available=record.PD3_available=false;
                record.P1_available=record.P2_available=record.P3_available=false;
                record.D1_available=record.D2_available=record.D3_available=false;
            }
        }
        private static string Number(float n)=>n.ToString("F3",CultureInfo.InvariantCulture);
        private static string Vector(Vector3 p)=>Number(p.x)+":"+Number(p.y)+":"+Number(p.z);
    }
}
