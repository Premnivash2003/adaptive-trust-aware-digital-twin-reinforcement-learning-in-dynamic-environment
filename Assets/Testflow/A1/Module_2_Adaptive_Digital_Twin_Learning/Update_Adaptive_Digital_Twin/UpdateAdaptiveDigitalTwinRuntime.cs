using System;
using System.Collections.Generic;
using ATADTRL.Core;

namespace ATADTRL.Module2
{
    public class AdaptiveDigitalTwin
    {
        private TwinWorldStateRecord twin;

        private bool initialized;

        private double lastSyncTimestamp;

        public bool IsInitialized => initialized;

        public TwinWorldStateRecord Current => twin;
        public double LastSyncTimestamp => lastSyncTimestamp;

        public void Initialize(
            ObservationRecord observation)
        {
            twin = BuildFromObservation(observation);

            initialized = true;
            lastSyncTimestamp = observation.timestamp;
        }

        public TwinWorldStateRecord Update(
            ObservationRecord observation,
            EDATSDecision decision)
        {
            if (!initialized)
            {
                Initialize(observation);

                twin.sync_trigger = true;
                twin.critical_event = true;
                twin.sync_reason =
                    "Initial twin construction";

                twin.changed_components =
                    "ALL";

                twin.change_score = 1f;
                twin.last_sync_timestamp =
                    observation.timestamp;

                twin.state_age = 0f;

                return twin;
            }

            twin.timestamp =
                observation.timestamp;

            twin.scenarioId =
                observation.scenarioId;

            twin.episodeId =
                observation.episodeId;

            twin.stepId =
                observation.stepId;

            twin.change_score =
                decision.changeScore;

            twin.sync_trigger =
                decision.synchronize;

            twin.critical_event =
                decision.criticalEvent;

            twin.sync_reason =
                decision.reason;

            twin.changed_components =
                string.Join(
                    "|",
                    decision.changedComponents);

            if (decision.synchronize)
            {
                ApplySelectiveUpdates(
                    observation,
                    decision.changedComponents);

                lastSyncTimestamp =
                    observation.timestamp;
            }

            twin.last_sync_timestamp =
                lastSyncTimestamp;

            twin.state_age =
                (float)(
                    observation.timestamp -
                    lastSyncTimestamp);

            return twin;
        }

        // =============================================================
        // SELECTIVE UPDATE
        // =============================================================

        private void ApplySelectiveUpdates(
            ObservationRecord source,
            List<string> changed)
        {
            foreach (string component in changed)
            {
                if (component.StartsWith("robot."))
                {
                    twin.twin_robot_x =
                        source.estimated_x;

                    twin.twin_robot_y =
                        source.estimated_y;

                    twin.twin_robot_yaw =
                        source.estimated_yaw;

                    twin.twin_robot_velocity =
                        source.estimated_velocity;
                }

                else if (component.StartsWith("H1."))
                    SetH1(source);

                else if (component.StartsWith("H2."))
                    SetH2(source);

                else if (component.StartsWith("H3."))
                    SetH3(source);

                else if (component.StartsWith("H4."))
                    SetH4(source);

                else if (component.StartsWith("H5."))
                    SetH5(source);

                else if (component.StartsWith("F1."))
                    SetF1(source);

                else if (component.StartsWith("F2."))
                    SetF2(source);

                else if (component ==
                         "parcel.position")
                {
                    twin.parcel_id =
                        source.parcel_id;

                    twin.parcel_position =
                        source.parcel_position;

                    twin.destination_id =
                        source.destination_id;

                    twin.destination_rack_id =
                        source.destination_rack_id;

                    twin.destination_slot_id =
                        source.destination_slot_id;
                }

                else if (component.StartsWith("P") &&
                         component.EndsWith(".available"))
                {
                    SetStations(source);
                }

                else if (component.StartsWith("D") &&
                         component.EndsWith(".available"))
                {
                    SetStations(source);
                }

                else if (component ==
                         "rack.available")
                {
                    twin.rack_available =
                        source.rack_available;
                }

                else if (component.StartsWith("task."))
                {
                    SetTask(source);
                }
            }
        }

        // =============================================================
        // INITIAL CONSTRUCTION
        // =============================================================

        private static TwinWorldStateRecord
            BuildFromObservation(
                ObservationRecord o)
        {
            return new TwinWorldStateRecord
            {
                timestamp = o.timestamp,

                scenarioId = o.scenarioId,
                episodeId = o.episodeId,
                stepId = o.stepId,

                twin_robot_x = o.estimated_x,
                twin_robot_y = o.estimated_y,
                twin_robot_yaw = o.estimated_yaw,
                twin_robot_velocity =
                    o.estimated_velocity,

                h1_position = o.h1_position,
                h1_velocity = o.h1_velocity,

                h2_position = o.h2_position,
                h2_velocity = o.h2_velocity,

                h3_position = o.h3_position,
                h3_velocity = o.h3_velocity,

                h4_position = o.h4_position,
                h4_velocity = o.h4_velocity,

                h5_position = o.h5_position,
                h5_velocity = o.h5_velocity,

                f1_position = o.f1_position,
                f1_velocity = o.f1_velocity,

                f2_position = o.f2_position,
                f2_velocity = o.f2_velocity,

                parcel_id = o.parcel_id,
                parcel_position =
                    o.parcel_position,

                destination_id =
                    o.destination_id,

                destination_rack_id =
                    o.destination_rack_id,

                destination_slot_id =
                    o.destination_slot_id,

                carrying_status =
                    o.carrying_status,

                grasp_status =
                    o.grasp_status,

                rack_available =
                    o.rack_available,

                P1_available =
                    o.P1_available,

                P2_available =
                    o.P2_available,

                P3_available =
                    o.P3_available,

                D1_available =
                    o.D1_available,

                D2_available =
                    o.D2_available,

                D3_available =
                    o.D3_available,

                current_task_stage =
                    o.current_task_stage
            };
        }

        private void SetH1(ObservationRecord o)
        {
            twin.h1_position = o.h1_position;
            twin.h1_velocity = o.h1_velocity;
        }

        private void SetH2(ObservationRecord o)
        {
            twin.h2_position = o.h2_position;
            twin.h2_velocity = o.h2_velocity;
        }

        private void SetH3(ObservationRecord o)
        {
            twin.h3_position = o.h3_position;
            twin.h3_velocity = o.h3_velocity;
        }

        private void SetH4(ObservationRecord o)
        {
            twin.h4_position = o.h4_position;
            twin.h4_velocity = o.h4_velocity;
        }

        private void SetH5(ObservationRecord o)
        {
            twin.h5_position = o.h5_position;
            twin.h5_velocity = o.h5_velocity;
        }

        private void SetF1(ObservationRecord o)
        {
            twin.f1_position = o.f1_position;
            twin.f1_velocity = o.f1_velocity;
        }

        private void SetF2(ObservationRecord o)
        {
            twin.f2_position = o.f2_position;
            twin.f2_velocity = o.f2_velocity;
        }

        private void SetStations(
            ObservationRecord o)
        {
            twin.P1_available = o.P1_available;
            twin.P2_available = o.P2_available;
            twin.P3_available = o.P3_available;

            twin.D1_available = o.D1_available;
            twin.D2_available = o.D2_available;
            twin.D3_available = o.D3_available;
        }

        private void SetTask(
            ObservationRecord o)
        {
            twin.carrying_status =
                o.carrying_status;

            twin.grasp_status =
                o.grasp_status;

            twin.current_task_stage =
                o.current_task_stage;

            twin.destination_id =
                o.destination_id;

            twin.destination_rack_id =
                o.destination_rack_id;

            twin.destination_slot_id =
                o.destination_slot_id;
        }
    }
}