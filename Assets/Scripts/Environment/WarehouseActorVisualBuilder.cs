using UnityEngine;
using ATADTRL.Core;

namespace ATADTRL.Environment
{
    /// <summary>
    /// Builds clear, low-poly warehouse actors at runtime.  This keeps the
    /// coursework self-contained while replacing the old single primitive
    /// meshes with recognisable people, forklifts, AMRs and pallet obstacles.
    /// </summary>
    public sealed class WarehouseActorVisual : MonoBehaviour { }

    public static class WarehouseActorVisualBuilder
    {
        private static Material _steel;
        private static Material _safetyOrange;
        private static Material _safetyYellow;
        private static Material _rubber;
        private static Material _skin;
        private static Material _vest;
        private static Material _cardboard;
        private static Material _robotBlue;
        private static Material _glass;

        public static void EnsureRobotVisual(GameObject robot)
        {
            if (!Begin(robot)) return;
            Transform root = CreateVisualRoot(robot.transform);

            Part(root, PrimitiveType.Cube, "AMR body", new Vector3(0f, 0.22f, 0f), new Vector3(0.50f, 0.28f, 0.70f), _RobotBlue);
            Part(root, PrimitiveType.Cube, "Front bumper", new Vector3(0f, 0.14f, 0.37f), new Vector3(0.54f, 0.12f, 0.08f), _Rubber);
            Part(root, PrimitiveType.Cylinder, "Top lidar", new Vector3(0f, 0.44f, 0f), new Vector3(0.15f, 0.07f, 0.15f), _Steel);
            Part(root, PrimitiveType.Cylinder, "Lidar window", new Vector3(0f, 0.49f, 0f), new Vector3(0.12f, 0.025f, 0.12f), _Glass);
            Part(root, PrimitiveType.Cube, "Front camera", new Vector3(0f, 0.33f, 0.34f), new Vector3(0.18f, 0.10f, 0.035f), _Glass);
            Part(root, PrimitiveType.Cube, "IMU module", new Vector3(0f, 0.47f, -0.21f), new Vector3(0.18f, 0.07f, 0.14f), _SafetyOrange);
            Part(root, PrimitiveType.Cylinder, "IMU indicator", new Vector3(0f, 0.52f, -0.21f), new Vector3(0.045f, 0.015f, 0.045f), _SafetyYellow);

            // After grasp verification the parcel is transferred here for
            // transport. The deck is behind the forward RGB camera and the
            // top LiDAR instead of obscuring their field of view.
            var cargoDeck = new GameObject("Rear Cargo Deck").transform;
            cargoDeck.SetParent(root, false);
            cargoDeck.localPosition = new Vector3(0f, 0.37f, -0.48f);
            Part(cargoDeck, PrimitiveType.Cube, "Cargo platform", Vector3.zero,
                new Vector3(0.52f, 0.07f, 0.44f), _Steel);
            Part(cargoDeck, PrimitiveType.Cube, "Rear load guard", new Vector3(0f, 0.17f, -0.19f),
                new Vector3(0.50f, 0.30f, 0.04f), _SafetyOrange);

            var arm = new GameObject("AMR Arm").transform;
            arm.SetParent(root, false);
            arm.localPosition = new Vector3(0f, 0.39f, 0.10f);
            Part(arm, PrimitiveType.Cube, "Arm base", Vector3.zero, new Vector3(0.16f, 0.10f, 0.16f), _Steel);
            var extension = new GameObject("Arm Extension").transform;
            extension.SetParent(arm, false);
            extension.localPosition = new Vector3(0f, 0f, 0.26f);
            Part(extension, PrimitiveType.Cube, "Telescopic arm", new Vector3(0f, 0f, 0.20f), new Vector3(0.10f, 0.10f, 0.42f), _Steel);
            Part(extension, PrimitiveType.Cube, "Gripper", new Vector3(0f, 0f, 0.45f), new Vector3(0.24f, 0.08f, 0.10f), _SafetyOrange);

            for (int side = -1; side <= 1; side += 2)
            {
                for (int axle = -1; axle <= 1; axle += 2)
                {
                    Part(root, PrimitiveType.Cylinder, "Drive wheel", new Vector3(0.29f * side, 0.105f, 0.22f * axle),
                        new Vector3(0.13f, 0.07f, 0.13f), _Rubber, Quaternion.Euler(0f, 0f, 90f));
                }
            }
        }

        public static void EnsureDynamicActorVisual(GameObject actor, DynamicObjectType type)
        {
            if (!Begin(actor)) return;
            if (type == DynamicObjectType.Human) actor.transform.localScale = Vector3.one;

            Transform root = CreateVisualRoot(actor.transform);
            switch (type)
            {
                case DynamicObjectType.Human:
                    BuildHuman(root);
                    break;
                case DynamicObjectType.Forklift:
                    BuildForklift(root);
                    break;
                default:
                    BuildPalletObstacle(root);
                    break;
            }
        }

        // Simple job equipment makes each route meaningful in the scene: the
        // worker at the rack handles a carton, the trolley operator has a
        // trolley, maintenance has a tool cart, and forklifts visibly carry
        // pallets.  These are display-only children, so their disabled
        // colliders never interfere with the actor's safety collider.
        public static void EnsureJobVisual(GameObject actor, string actorId, string jobName, DynamicObjectType type)
        {
            if (actor == null || actor.transform.Find("Job equipment") != null) return;
            Transform root = actor.transform.Find("Detailed visual");
            if (root == null) return;

            var equipment = new GameObject("Job equipment").transform;
            equipment.SetParent(root, false);

            if (type == DynamicObjectType.Forklift)
            {
                Part(equipment, PrimitiveType.Cube, "Pallet load", new Vector3(0f, 0.50f, 1.03f), new Vector3(0.54f, 0.38f, 0.48f), _Cardboard);
                Part(equipment, PrimitiveType.Cube, "Pallet base", new Vector3(0f, 0.27f, 1.03f), new Vector3(0.62f, 0.08f, 0.58f), _Cardboard);
                return;
            }

            if (actorId == "H4")
            {
                Part(equipment, PrimitiveType.Cube, "Trolley tray", new Vector3(0f, 0.38f, 0.48f), new Vector3(0.44f, 0.08f, 0.55f), _Steel);
                Part(equipment, PrimitiveType.Cube, "Trolley handle", new Vector3(0f, 0.68f, 0.69f), new Vector3(0.06f, 0.50f, 0.06f), _Steel, Quaternion.Euler(15f, 0f, 0f));
                Part(equipment, PrimitiveType.Cube, "Trolley carton", new Vector3(0f, 0.59f, 0.48f), new Vector3(0.34f, 0.28f, 0.32f), _Cardboard);
            }
            else if (actorId == "H5")
            {
                Part(equipment, PrimitiveType.Cube, "Maintenance cart", new Vector3(-0.38f, 0.28f, 0.18f), new Vector3(0.30f, 0.32f, 0.42f), _SafetyYellow);
                Part(equipment, PrimitiveType.Cylinder, "Tool beacon", new Vector3(-0.38f, 0.50f, 0.18f), new Vector3(0.08f, 0.04f, 0.08f), _SafetyOrange);
            }
            else if (actorId == "H1")
            {
                Part(equipment, PrimitiveType.Cube, "Picked carton", new Vector3(0.30f, 1.02f, 0.18f), new Vector3(0.25f, 0.20f, 0.22f), _Cardboard);
            }
            else if (actorId == "H3")
            {
                Part(equipment, PrimitiveType.Cube, "Station scanner", new Vector3(0.25f, 1.15f, 0.12f), new Vector3(0.08f, 0.16f, 0.07f), _SafetyOrange);
            }
            else
            {
                Part(equipment, PrimitiveType.Cube, "Transfer tote", new Vector3(-0.28f, 0.92f, 0.14f), new Vector3(0.24f, 0.18f, 0.22f), _Cardboard);
            }
        }

        private static bool Begin(GameObject actor)
        {
            if (actor == null || actor.GetComponent<WarehouseActorVisual>() != null) return false;
            actor.AddComponent<WarehouseActorVisual>();
            var sourceRenderer = actor.GetComponent<Renderer>();
            if (sourceRenderer != null) sourceRenderer.enabled = false;
            return true;
        }

        private static Transform CreateVisualRoot(Transform actor)
        {
            var go = new GameObject("Detailed visual");
            go.transform.SetParent(actor, false);
            return go.transform;
        }

        private static void BuildHuman(Transform root)
        {
            Part(root, PrimitiveType.Capsule, "Torso", new Vector3(0f, 1.10f, 0f), new Vector3(0.36f, 0.58f, 0.24f), _Vest);
            Part(root, PrimitiveType.Sphere, "Head", new Vector3(0f, 1.78f, 0f), new Vector3(0.30f, 0.30f, 0.30f), _Skin);
            Part(root, PrimitiveType.Sphere, "Safety helmet", new Vector3(0f, 1.92f, 0f), new Vector3(0.33f, 0.13f, 0.33f), _SafetyYellow);

            for (int side = -1; side <= 1; side += 2)
            {
                Part(root, PrimitiveType.Capsule, "Arm", new Vector3(0.28f * side, 1.10f, 0f),
                    new Vector3(0.12f, 0.38f, 0.12f), _Skin, Quaternion.Euler(0f, 0f, -12f * side));
                Part(root, PrimitiveType.Cube, "Leg", new Vector3(0.12f * side, 0.35f, 0f),
                    new Vector3(0.14f, 0.62f, 0.16f), _Steel);
            }
        }

        private static void BuildForklift(Transform root)
        {
            Part(root, PrimitiveType.Cube, "Forklift chassis", new Vector3(0f, 0.25f, -0.05f), new Vector3(0.82f, 0.30f, 0.82f), _SafetyOrange);
            Part(root, PrimitiveType.Cube, "Counterweight", new Vector3(0f, 0.56f, -0.28f), new Vector3(0.76f, 0.38f, 0.34f), _SafetyOrange);
            Part(root, PrimitiveType.Cube, "Driver canopy", new Vector3(0f, 1.15f, -0.18f), new Vector3(0.72f, 0.07f, 0.58f), _Steel);
            Part(root, PrimitiveType.Cube, "Seat", new Vector3(0f, 0.64f, -0.06f), new Vector3(0.38f, 0.28f, 0.28f), _Rubber);
            // Each forklift is visibly human-operated rather than appearing
            // to drive itself. The operator is seated within the canopy and
            // travels with F1/F2 on its assigned warehouse task.
            Part(root, PrimitiveType.Capsule, "Forklift operator torso", new Vector3(0f, 0.84f, -0.10f), new Vector3(0.22f, 0.30f, 0.16f), _Vest);
            Part(root, PrimitiveType.Sphere, "Forklift operator head", new Vector3(0f, 1.25f, -0.10f), new Vector3(0.20f, 0.20f, 0.20f), _Skin);
            Part(root, PrimitiveType.Sphere, "Forklift operator helmet", new Vector3(0f, 1.36f, -0.10f), new Vector3(0.22f, 0.07f, 0.22f), _SafetyYellow);
            Part(root, PrimitiveType.Capsule, "Forklift operator arm left", new Vector3(-0.18f, 0.90f, 0.10f), new Vector3(0.07f, 0.24f, 0.07f), _Skin, Quaternion.Euler(55f, 0f, 12f));
            Part(root, PrimitiveType.Capsule, "Forklift operator arm right", new Vector3(0.18f, 0.90f, 0.10f), new Vector3(0.07f, 0.24f, 0.07f), _Skin, Quaternion.Euler(55f, 0f, -12f));

            for (int side = -1; side <= 1; side += 2)
            {
                Part(root, PrimitiveType.Cube, "Canopy post", new Vector3(0.31f * side, 0.78f, -0.18f), new Vector3(0.06f, 0.75f, 0.06f), _Steel);
                for (int axle = -1; axle <= 1; axle += 2)
                {
                    Part(root, PrimitiveType.Cylinder, "Wheel", new Vector3(0.49f * side, 0.17f, 0.30f * axle),
                        new Vector3(0.19f, 0.12f, 0.19f), _Rubber, Quaternion.Euler(0f, 0f, 90f));
                }
            }

            Part(root, PrimitiveType.Cube, "Lift mast left", new Vector3(-0.29f, 1.00f, 0.56f), new Vector3(0.07f, 1.55f, 0.07f), _Steel);
            Part(root, PrimitiveType.Cube, "Lift mast right", new Vector3(0.29f, 1.00f, 0.56f), new Vector3(0.07f, 1.55f, 0.07f), _Steel);
            Part(root, PrimitiveType.Cube, "Fork carriage", new Vector3(0f, 0.50f, 0.62f), new Vector3(0.62f, 0.10f, 0.08f), _Steel);
            Part(root, PrimitiveType.Cube, "Fork left", new Vector3(-0.19f, 0.13f, 0.94f), new Vector3(0.09f, 0.07f, 0.62f), _Steel);
            Part(root, PrimitiveType.Cube, "Fork right", new Vector3(0.19f, 0.13f, 0.94f), new Vector3(0.09f, 0.07f, 0.62f), _Steel);
        }

        private static void BuildPalletObstacle(Transform root)
        {
            Part(root, PrimitiveType.Cube, "Pallet", new Vector3(0f, 0.10f, 0f), new Vector3(1.20f, 0.16f, 1.00f), _Cardboard);
            Part(root, PrimitiveType.Cube, "Carton stack lower", new Vector3(-0.18f, 0.40f, 0f), new Vector3(0.70f, 0.45f, 0.72f), _Cardboard);
            Part(root, PrimitiveType.Cube, "Carton stack upper", new Vector3(0.20f, 0.73f, 0.04f), new Vector3(0.62f, 0.30f, 0.64f), _Cardboard);
            Part(root, PrimitiveType.Cylinder, "Safety beacon", new Vector3(0f, 1.02f, 0f), new Vector3(0.16f, 0.08f, 0.16f), _SafetyYellow);
        }

        private static void Part(Transform parent, PrimitiveType primitive, string partName, Vector3 position,
            Vector3 scale, Material material, Quaternion? rotation = null)
        {
            GameObject part = GameObject.CreatePrimitive(primitive);
            part.name = partName;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localRotation = rotation ?? Quaternion.identity;
            part.transform.localScale = scale;
            Collider collider = part.GetComponent<Collider>();
            if (collider != null) collider.enabled = false;
            part.GetComponent<Renderer>().sharedMaterial = material;
        }

        private static Material MakeMaterial(ref Material cache, Color color, float metallic = 0f, float smoothness = 0.25f)
        {
            if (cache != null) return cache;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ??
                            Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
            cache = new Material(shader) { color = color };
            if (cache.HasProperty("_Metallic")) cache.SetFloat("_Metallic", metallic);
            if (cache.HasProperty("_Smoothness")) cache.SetFloat("_Smoothness", smoothness);
            return cache;
        }

        private static Material _Steel => MakeMaterial(ref _steel, new Color(0.20f, 0.24f, 0.28f), 0.70f, 0.60f);
        private static Material _SafetyOrange => MakeMaterial(ref _safetyOrange, new Color(0.95f, 0.30f, 0.04f), 0.10f, 0.35f);
        private static Material _SafetyYellow => MakeMaterial(ref _safetyYellow, new Color(1.00f, 0.72f, 0.04f), 0.05f, 0.30f);
        private static Material _Rubber => MakeMaterial(ref _rubber, new Color(0.035f, 0.04f, 0.05f), 0f, 0.12f);
        private static Material _Skin => MakeMaterial(ref _skin, new Color(0.55f, 0.31f, 0.19f), 0f, 0.35f);
        private static Material _Vest => MakeMaterial(ref _vest, new Color(0.05f, 0.70f, 0.40f), 0f, 0.30f);
        private static Material _Cardboard => MakeMaterial(ref _cardboard, new Color(0.50f, 0.30f, 0.14f), 0f, 0.18f);
        private static Material _RobotBlue => MakeMaterial(ref _robotBlue, new Color(0.08f, 0.28f, 0.55f), 0.35f, 0.55f);
        private static Material _Glass => MakeMaterial(ref _glass, new Color(0.08f, 0.55f, 0.85f), 0.20f, 0.80f);
    }
}
