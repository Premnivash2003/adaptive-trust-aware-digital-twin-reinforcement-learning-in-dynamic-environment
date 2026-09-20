using UnityEngine;
using ATADTRL.Core;

namespace ATADTRL.TestFlow
{
    public class A1TwinState
    {
        public double timestamp;

        public float robotX;
        public float robotY;
        public float robotYaw;
        public float robotVelocity;

        public string h1Position;
        public string h1Velocity;

        public string parcelId;
        public string barcodeId;
        public string destinationId;

        public float lidarFront;

        public float stateAge;
        public bool synchronized;
    }

    public class AdaptiveDigitalTwin
    {
        public A1TwinState Current { get; private set; }

        public A1TwinState Update(
            ObservationRecord o,
            EDATSDecision decision)
        {
            // First observation always initializes Twin
            if (Current == null)
                decision.synchronize = true;

            if (decision.synchronize)
            {
                Current = new A1TwinState
                {
                    timestamp = o.timestamp,

                    robotX = o.estimated_x,
                    robotY = o.estimated_y,
                    robotYaw = o.estimated_yaw,
                    robotVelocity = o.estimated_velocity,

                    h1Position = o.h1_position,
                    h1Velocity = o.h1_velocity,

                    parcelId = o.parcel_id,
                    barcodeId = o.barcode_id,
                    destinationId = o.destination_id,

                    lidarFront = o.lidar_front_distance,

                    stateAge = 0f,
                    synchronized = true
                };
            }
            else
            {
                Current.stateAge =
                    (float)(o.timestamp - Current.timestamp);

                Current.synchronized = false;
            }

            return Current;
        }
    }
}