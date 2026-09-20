using System;
using System.Collections.Generic;
using UnityEngine;

namespace ATADTRL.Core
{
    // ============================================================
    // ENUMS
    // ============================================================

    public enum NavigationStatus
    {
        Idle,
        Planning,
        Moving,
        Avoiding,
        Stopped,
        GoalReached,
        Collided,
        Failed
    }

    public enum ScenarioCategory
    {
        StaticNavigation,
        StaticObstacle,
        DynamicObstacle,
        HumanInteraction,
        ForkliftInteraction,
        MultiAgentTraffic,
        AisleBlockage,
        SensorDegradation,
        DigitalTwin,
        StressTest
    }

    public enum DynamicObjectType
    {
        Human,
        Forklift,
        MovableObstacle
    }

    public enum MovementPattern
    {
        Static,
        LinearPatrol,
        RandomWalk,
        CrossPath,
        Scripted
    }

    // Module 1 exposes this complete task-action vocabulary even though its
    // baseline controller follows predefined NavMesh routes rather than a
    // learned policy. Later modules can consume the same action space.
    public enum RobotTaskAction
    {
        Forward,
        Backward,
        Left,
        Right,
        Wait,
        Pick,
        Place
    }

    // ============================================================
    // CONFIGURATION STRUCTURES (Inspector-editable)
    // ============================================================

    [Serializable]
    public class WarehouseConfig
    {
        [Header("Warehouse Dimensions (meters)")]
        public float warehouseLength = 60f;
        public float warehouseWidth = 40f;
        public float warehouseHeight = 8f;

        [Header("Rack Dimensions")]
        public float rackLength = 6f;
        public float rackWidth = 1.2f;
        public float rackHeight = 4f;

        [Header("Layout")]
        public int rackRows = 4;
        public int rackColumns = 6;
        public float aisleWidth = 3.0f;

        [Header("Zones")]
        public Vector2 loadingZoneSize = new Vector2(8f, 6f);
        public Vector2 safetyZoneSize = new Vector2(2f, 2f);

        [Header("Robot Areas")]
        public Vector3 robotStartArea = new Vector3(-25f, 0f, -15f);
        public Vector3 chargingStationPosition = new Vector3(24f, 0f, -12f);
        public Vector3 safetyZonePosition = new Vector3(24f, 0f, -15f);
        [Header("Material Handling Stations")]
        public List<Vector3> pickupStationPositions = new List<Vector3>
        {
            new Vector3(-24f, 0f, -15f), // P1: south-west inbound dock
            new Vector3(24f, 0f, -15f),  // P2: south-east inbound dock
            new Vector3(-24f, 0f, 15f)   // P3: north-west inbound dock
        };
        public List<Vector3> dropStationPositions = new List<Vector3>
        {
            new Vector3(-12.3f, 0f, 13.5f), // D1: Electronics rack access
            new Vector3(12.3f, 0f, 13.5f),  // D2: Apparel rack access
            new Vector3(-12.3f, 0f, -13.5f) // D3: Healthcare rack access
        };
        public List<Vector3> goalAreas = new List<Vector3>();
    }

    [Serializable]
    public class RobotConfig
    {
        [Header("Physical Dimensions")]
        public float robotLength = 0.7f;
        public float robotWidth = 0.5f;
        public float robotHeight = 0.4f;
        public float wheelRadius = 0.1f;
        public float wheelBaseDistance = 0.4f;

        [Header("Kinematics")]
        public float maxLinearSpeed = 1.5f;      // m/s
        public float maxAngularSpeed = 90f;       // deg/s
        public float acceleration = 1.0f;         // m/s^2
        public float deceleration = 2.0f;         // m/s^2

        [Header("Safety")]
        public float obstacleStopDistance = 0.6f;
        public float obstacleResumeDistance = 1.2f;
        public float goalTolerance = 0.4f;
    }

    // Note: SensorNoiseConfig lives in its own file, Core/SensorNoiseConfig.cs.

    // ============================================================
    // RUNTIME STATE STRUCTURES
    // ============================================================

    [Serializable]
    public class DynamicObjectState
    {
        public string objectId;
        public DynamicObjectType type;
        public Vector3 position;
        public Vector3 velocity;
        public MovementPattern pattern;
        public bool active;
    }

    public class RobotGroundTruthState
    {
        public Vector3 position;
        public float yaw;
        public float linearVelocity;
        public float angularVelocity;
        public Vector3 goal;
        public float distanceToGoal;
        public bool collision;
        public bool goalReached;
        public NavigationStatus navStatus;
    }

    // ============================================================
    // DATASET ROW STRUCTURES
    // ============================================================

    public class GroundTruthRecord
    {
        public double timestamp;
        public int scenarioId;
        public int episodeId;
        public long stepId;

        public float robot_x, robot_y, robot_z, robot_yaw;
        public float robot_linear_velocity, robot_angular_velocity;

        public float goal_x, goal_y, goal_z, distance_to_goal;

        public int number_of_dynamic_objects;
        public string obstacle_positions;   // serialized "x:y:z|x:y:z"
        public string obstacle_velocities;
        public string human_positions;
        public string human_velocities;
        public string forklift_positions;
        public string forklift_velocities;

        public bool collision;
        public bool goal_reached;
        public string navigation_status;

        public float warehouse_length, warehouse_width, aisle_width;
        public string scenario_type;
        public string station_availability, actor_work_states;

        public static string CsvHeader()
        {
            return "timestamp,scenario_id,episode_id,step_id,robot_x,robot_y,robot_z,robot_yaw," +
                   "robot_linear_velocity,robot_angular_velocity,goal_x,goal_y,goal_z,distance_to_goal," +
                   "number_of_dynamic_objects,obstacle_positions,obstacle_velocities,human_positions," +
                   "human_velocities,forklift_positions,forklift_velocities,collision,goal_reached," +
                   "navigation_status,warehouse_length,warehouse_width,aisle_width,scenario_type,station_availability,actor_work_states";
        }

        public string ToCsvRow()
        {
            return string.Join(",",
                timestamp.ToString("F4", System.Globalization.CultureInfo.InvariantCulture), scenarioId, episodeId, stepId,
                robot_x.ToString("F4", System.Globalization.CultureInfo.InvariantCulture), robot_y.ToString("F4", System.Globalization.CultureInfo.InvariantCulture), robot_z.ToString("F4", System.Globalization.CultureInfo.InvariantCulture), robot_yaw.ToString("F4", System.Globalization.CultureInfo.InvariantCulture),
                robot_linear_velocity.ToString("F4", System.Globalization.CultureInfo.InvariantCulture), robot_angular_velocity.ToString("F4", System.Globalization.CultureInfo.InvariantCulture),
                goal_x.ToString("F4", System.Globalization.CultureInfo.InvariantCulture), goal_y.ToString("F4", System.Globalization.CultureInfo.InvariantCulture), goal_z.ToString("F4", System.Globalization.CultureInfo.InvariantCulture), distance_to_goal.ToString("F4", System.Globalization.CultureInfo.InvariantCulture),
                number_of_dynamic_objects,
                Q(obstacle_positions), Q(obstacle_velocities), Q(human_positions), Q(human_velocities),
                Q(forklift_positions), Q(forklift_velocities),
                collision, goal_reached, navigation_status,
                warehouse_length.ToString("F2", System.Globalization.CultureInfo.InvariantCulture), warehouse_width.ToString("F2", System.Globalization.CultureInfo.InvariantCulture), aisle_width.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                scenario_type, Q(station_availability), Q(actor_work_states));
        }

        private static string Q(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
    }

    public class ObservationRecord
    {
        public double timestamp;
        public int scenarioId;
        public int episodeId;
        public long stepId;

        public float lidar_min_distance, lidar_front_distance, lidar_left_distance, lidar_right_distance;
        public int lidar_measurement_count;

        public int camera_detection_count;
        public string camera_detected_objects; // serialized

        public float ax, ay, az, gx, gy, gz;

        public float left_wheel_distance, right_wheel_distance;
        public float left_wheel_velocity, right_wheel_velocity;

        public float estimated_x, estimated_y, estimated_yaw, estimated_velocity;

        public bool missing_sensor_flag;
        public bool sensor_dropout_flag;

        // Complete warehouse-task context required by the ATADTRL dataset.
        public string parcel_id, parcel_position, source_id, destination_id;
        public string barcode_id, parcel_category, destination_rack_id, destination_slot_id;
        public float parcel_mass;
        public int empty_slot_count;
        public bool carrying_status, grasp_status;
        public bool rack_available, PD1_available, PD2_available, PD3_available;
        public string human_positions, human_velocities, forklift_positions, forklift_velocities;
        public string h1_position, h1_velocity, h2_position, h2_velocity, h3_position, h3_velocity,
            h4_position, h4_velocity, h5_position, h5_velocity, f1_position, f1_velocity, f2_position, f2_velocity;
        public float battery_level, communication_delay;
        public string sensor_status, current_task_stage;
        public int task_priority;
        public bool P1_available, P2_available, P3_available, D1_available, D2_available, D3_available;
        public string actor_sample_timestamps, actor_message_ages, actor_link_validity, actor_work_states, station_sample_timestamp;
        public bool camera_valid, lidar_valid;

        public static string CsvHeader()
        {
            return "timestamp,scenario_id,episode_id,step_id,lidar_min_distance,lidar_front_distance," +
                   "lidar_left_distance,lidar_right_distance,lidar_measurement_count,camera_detection_count," +
                   "camera_detected_objects,ax,ay,az,gx,gy,gz,left_wheel_distance,right_wheel_distance," +
                   "left_wheel_velocity,right_wheel_velocity,estimated_x,estimated_y,estimated_yaw," +
                   "estimated_velocity,missing_sensor_flag,sensor_dropout_flag," +
                   "parcel_id,parcel_position,source_id,destination_id,barcode_id,parcel_category,destination_rack_id,destination_slot_id," +
                   "parcel_mass,empty_slot_count,carrying_status,grasp_status,rack_available,PD1_available,PD2_available,PD3_available,human_positions,human_velocities," +
                   "forklift_positions,forklift_velocities,h1_position,h1_velocity,h2_position,h2_velocity," +
                   "h3_position,h3_velocity,h4_position,h4_velocity,h5_position,h5_velocity,f1_position,f1_velocity," +
                   "f2_position,f2_velocity,battery_level,sensor_status,communication_delay," +
                   "task_priority,current_task_stage,P1_available,P2_available,P3_available,D1_available,D2_available,D3_available," +
                   "actor_sample_timestamps,actor_message_ages,actor_link_validity,actor_work_states,station_sample_timestamp,camera_valid,lidar_valid";
        }

        public string ToCsvRow()
        {
            return string.Join(",",
                timestamp.ToString("F4", System.Globalization.CultureInfo.InvariantCulture), scenarioId, episodeId, stepId,
                lidar_min_distance.ToString("F4", System.Globalization.CultureInfo.InvariantCulture), lidar_front_distance.ToString("F4", System.Globalization.CultureInfo.InvariantCulture),
                lidar_left_distance.ToString("F4", System.Globalization.CultureInfo.InvariantCulture), lidar_right_distance.ToString("F4", System.Globalization.CultureInfo.InvariantCulture), lidar_measurement_count,
                camera_detection_count, "\"" + (camera_detected_objects ?? "") + "\"",
                ax.ToString("F4", System.Globalization.CultureInfo.InvariantCulture), ay.ToString("F4", System.Globalization.CultureInfo.InvariantCulture), az.ToString("F4", System.Globalization.CultureInfo.InvariantCulture),
                gx.ToString("F4", System.Globalization.CultureInfo.InvariantCulture), gy.ToString("F4", System.Globalization.CultureInfo.InvariantCulture), gz.ToString("F4", System.Globalization.CultureInfo.InvariantCulture),
                left_wheel_distance.ToString("F4", System.Globalization.CultureInfo.InvariantCulture), right_wheel_distance.ToString("F4", System.Globalization.CultureInfo.InvariantCulture),
                left_wheel_velocity.ToString("F4", System.Globalization.CultureInfo.InvariantCulture), right_wheel_velocity.ToString("F4", System.Globalization.CultureInfo.InvariantCulture),
                estimated_x.ToString("F4", System.Globalization.CultureInfo.InvariantCulture), estimated_y.ToString("F4", System.Globalization.CultureInfo.InvariantCulture), estimated_yaw.ToString("F4", System.Globalization.CultureInfo.InvariantCulture),
                estimated_velocity.ToString("F4", System.Globalization.CultureInfo.InvariantCulture), missing_sensor_flag, sensor_dropout_flag,
                Q(parcel_id), Q(parcel_position), Q(source_id), Q(destination_id), Q(barcode_id), Q(parcel_category),
                Q(destination_rack_id), Q(destination_slot_id), parcel_mass.ToString("F3", System.Globalization.CultureInfo.InvariantCulture), empty_slot_count,
                carrying_status, grasp_status, rack_available, PD1_available, PD2_available, PD3_available,
                Q(human_positions), Q(human_velocities), Q(forklift_positions), Q(forklift_velocities),
                Q(h1_position), Q(h1_velocity), Q(h2_position), Q(h2_velocity), Q(h3_position), Q(h3_velocity),
                Q(h4_position), Q(h4_velocity), Q(h5_position), Q(h5_velocity), Q(f1_position), Q(f1_velocity),
                Q(f2_position), Q(f2_velocity),
                battery_level.ToString("F3", System.Globalization.CultureInfo.InvariantCulture), Q(sensor_status), communication_delay.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
                task_priority, Q(current_task_stage),P1_available,P2_available,P3_available,D1_available,D2_available,D3_available,
                Q(actor_sample_timestamps),Q(actor_message_ages),Q(actor_link_validity),Q(actor_work_states),Q(station_sample_timestamp),camera_valid,lidar_valid);
        }

        private static string Q(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
    }
    public class TwinWorldStateRecord
    {
        public double timestamp;
        public int scenarioId;
        public int episodeId;
        public long stepId;

        public float twin_robot_x;
        public float twin_robot_y;
        public float twin_robot_yaw;
        public float twin_robot_velocity;

        public float robot_position_divergence;
        public float robot_yaw_divergence;
        public float robot_velocity_divergence;
        public float max_actor_position_divergence;

        public bool divergence_triggered;
        public bool stale_state_triggered;

        public string h1_position;
        public string h1_velocity;

        public string h2_position;
        public string h2_velocity;

        public string h3_position;
        public string h3_velocity;

        public string h4_position;
        public string h4_velocity;

        public string h5_position;
        public string h5_velocity;

        public string f1_position;
        public string f1_velocity;

        public string f2_position;
        public string f2_velocity;

        public string parcel_id;
        public string parcel_position;
        public string destination_id;
        public string destination_rack_id;
        public string destination_slot_id;

        public bool carrying_status;
        public bool grasp_status;
        public bool rack_available;

        public bool P1_available;
        public bool P2_available;
        public bool P3_available;

        public bool D1_available;
        public bool D2_available;
        public bool D3_available;

        public string current_task_stage;

        public float change_score;

        public bool sync_trigger;
        public bool critical_event;

        public string sync_reason;
        public string changed_components;

        public double last_sync_timestamp;
        public float state_age;

        public static string CsvHeader()
        {
            return
                "timestamp,scenario_id,episode_id,step_id," +
                "twin_robot_x,twin_robot_y,twin_robot_yaw,twin_robot_velocity," +

                "h1_position,h1_velocity," +
                "h2_position,h2_velocity," +
                "h3_position,h3_velocity," +
                "h4_position,h4_velocity," +
                "h5_position,h5_velocity," +

                "f1_position,f1_velocity," +
                "f2_position,f2_velocity," +

                "parcel_id,parcel_position,destination_id," +
                "destination_rack_id,destination_slot_id," +

                "carrying_status,grasp_status,rack_available," +

                "P1_available,P2_available,P3_available," +
                "D1_available,D2_available,D3_available," +

                "current_task_stage," +

                "change_score,sync_trigger,critical_event," +
                "sync_reason,changed_components," +

                "last_sync_timestamp,state_age"+
                ",robot_position_divergence" +
                ",robot_yaw_divergence" +
                ",robot_velocity_divergence" +
                ",max_actor_position_divergence" +
                ",divergence_triggered" +
                ",stale_state_triggered";
        }

        public string ToCsvRow()
        {
            var c =
                System.Globalization.CultureInfo.InvariantCulture;

            return string.Join(",",
                timestamp.ToString("F4", c),
                scenarioId,
                episodeId,
                stepId,

                twin_robot_x.ToString("F4", c),
                twin_robot_y.ToString("F4", c),
                twin_robot_yaw.ToString("F4", c),
                twin_robot_velocity.ToString("F4", c),

                Q(h1_position),
                Q(h1_velocity),

                Q(h2_position),
                Q(h2_velocity),

                Q(h3_position),
                Q(h3_velocity),

                Q(h4_position),
                Q(h4_velocity),

                Q(h5_position),
                Q(h5_velocity),

                Q(f1_position),
                Q(f1_velocity),

                Q(f2_position),
                Q(f2_velocity),

                Q(parcel_id),
                Q(parcel_position),
                Q(destination_id),
                Q(destination_rack_id),
                Q(destination_slot_id),

                carrying_status,
                grasp_status,
                rack_available,

                P1_available,
                P2_available,
                P3_available,

                D1_available,
                D2_available,
                D3_available,

                Q(current_task_stage),

                change_score.ToString("F5", c),
                sync_trigger,
                critical_event,

                Q(sync_reason),
                Q(changed_components),

                last_sync_timestamp.ToString("F4", c),
                state_age.ToString("F4", c),

                robot_position_divergence.ToString("F4", c),
                robot_yaw_divergence.ToString("F4", c),
                robot_velocity_divergence.ToString("F4", c),
                max_actor_position_divergence.ToString("F4", c),
                divergence_triggered,
                stale_state_triggered
            );
        }

        private static string Q(string value)
        {
            return "\"" +
                   (value ?? "").Replace("\"", "\"\"") +
                   "\"";
        }
    }
    public class PerformanceRecord
    {
        public int scenarioId;
        public int episodeId;
        public float episodeDuration;
        public float pathLength;
        public float navigationTime;
        public int collisionCount;
        public bool goalSuccess;
        public float finalDistanceToGoal;
        public float averageVelocity;
        public float maxVelocity;
        public int numberOfStops;
        public int replanningEvents;
        public string completionStatus;

        public static string CsvHeader()
        {
            return "scenario_id,episode_id,episode_duration,path_length,navigation_time,collision_count," +
                   "goal_success,final_distance_to_goal,average_velocity,max_velocity,number_of_stops," +
                   "replanning_events,completion_status";
        }

        public string ToCsvRow()
        {
            return string.Join(",",
                scenarioId, episodeId, episodeDuration.ToString("F3", System.Globalization.CultureInfo.InvariantCulture), pathLength.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
                navigationTime.ToString("F3", System.Globalization.CultureInfo.InvariantCulture), collisionCount, goalSuccess, finalDistanceToGoal.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
                averageVelocity.ToString("F3", System.Globalization.CultureInfo.InvariantCulture), maxVelocity.ToString("F3", System.Globalization.CultureInfo.InvariantCulture), numberOfStops, replanningEvents,
                completionStatus);
        }
    }

    // One concise row per completed benchmark episode.  This is intentionally
    // separate from the high-frequency sensor files so experiment outcomes
    // can be compared directly in the next analysis step.
    public class EpisodeOutcomeRecord
    {
        public int benchmarkEpisodeNumber;
        public int benchmarkEpisodeTotal;
        public int globalEpisodeId;
        public int scenarioId;
        public string scenarioCode;
        public string scenarioName;
        public string outcome;
        public float durationSeconds;
        public bool missionSuccess;
        public int collisions;
        public int completedParcelTasks;
        public int totalParcelTasks;
        public float batteryLevel;
        public string finalTaskStage;

        public static string CsvHeader()
        {
            return "benchmark_episode,benchmark_episode_total,global_episode_id,scenario_id,scenario_code,scenario_name," +
                   "outcome,duration_seconds,mission_success,collisions,completed_parcel_tasks,total_parcel_tasks," +
                   "battery_level,final_task_stage";
        }

        public string ToCsvRow()
        {
            return string.Join(",", benchmarkEpisodeNumber, benchmarkEpisodeTotal, globalEpisodeId, scenarioId,
                Quote(scenarioCode), Quote(scenarioName), Quote(outcome), durationSeconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture), missionSuccess, collisions,
                completedParcelTasks, totalParcelTasks, batteryLevel.ToString("F2", System.Globalization.CultureInfo.InvariantCulture), Quote(finalTaskStage));
        }

        private static string Quote(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
    }

    public class ValidationRecord
    {
        public string datasetName;
        public int scenarioId;
        public int totalRows;
        public int missingValues;
        public int duplicateRows;
        public int invalidValues;
        public int timestampErrors;
        public string validationStatus;

        public static string CsvHeader()
        {
            return "dataset_name,scenario_id,total_rows,missing_values,duplicate_rows,invalid_values," +
                   "timestamp_errors,validation_status";
        }

        public string ToCsvRow()
        {
            return string.Join(",", datasetName, scenarioId, totalRows, missingValues, duplicateRows,
                invalidValues, timestampErrors, validationStatus);
        }
    }
}
