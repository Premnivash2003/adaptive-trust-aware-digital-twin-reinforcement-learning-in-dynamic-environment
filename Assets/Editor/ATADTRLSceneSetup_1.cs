#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.AI;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using TMPro.EditorUtilities;
using ATADTRL.Core;
using ATADTRL.Environment;
using ATADTRL.Robot;
using ATADTRL.Navigation;
using ATADTRL.Sensors;
using ATADTRL.Scenarios;
using ATADTRL.UI;

namespace ATADTRL.EditorTools
{
    /// <summary>
    /// One-click scene builder. Creates and wires every GameObject described
    /// in README.md section 2, so a brand-new/empty scene becomes a fully
    /// playable Module 1 setup without manual clicking. Safe to re-run —
    /// it detects and reuses existing top-level objects by name.
    /// </summary>
    public static class ATADTRLSceneSetup
    {
        [MenuItem("ATADTRL/Remove Missing Scripts From Scene")]
        public static void RemoveMissingScriptsFromScene()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            int totalRemoved = 0;
            int objectsScanned = 0;

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    objectsScanned++;
                    int removed = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                    if (removed > 0)
                    {
                        Debug.Log($"ATADTRL: Removed {removed} missing-script component(s) from '{t.name}'.");
                        totalRemoved += removed;
                    }
                }
            }

            EditorSceneManager_MarkDirty();
            Debug.Log($"ATADTRL: Scan complete — checked {objectsScanned} GameObjects, removed {totalRemoved} " +
                      "missing-script component(s) total. Now re-run ATADTRL > Setup Module 1 Scene (One-Click) " +
                      "to re-add and re-wire any components that were just cleared.");
        }

        [MenuItem("ATADTRL/Setup Module 1 Scene (One-Click)")]
        public static void SetupScene()
        {
            EnsureEventSystem();

            var warehouseManager = BuildWarehouseManager();
            var (robotGO, robotController, navController, navigationManager, sensorManager) = BuildRobot();
            var (humanPrefab, forkliftPrefab, obstaclePrefab) = BuildDynamicPrefabs();
            var (scenarioManager, groundTruthManager) = BuildScenarioManager(
                warehouseManager, robotController, navigationManager, sensorManager,
                humanPrefab, forkliftPrefab, obstaclePrefab);
            BuildGameManager(scenarioManager);
            BuildControlPanelUI(scenarioManager);
            BuildDemoModeUI(scenarioManager);

            // Build the warehouse geometry immediately so it's visible in the
            // Scene view without requiring Play mode, then bake the NavMesh
            // against it so the robot has a walkable surface right away too.
            warehouseManager.BuildWarehouse();
            robotGO.transform.position = warehouseManager.config.robotStartArea;
#pragma warning disable 0618
            UnityEditor.AI.NavMeshBuilder.BuildNavMesh();
#pragma warning restore 0618

            EditorSceneManager_MarkDirty();

            Debug.Log("ATADTRL Module 1: scene setup complete. Warehouse geometry and NavMesh are already " +
                      "built and visible in the Scene view. Press Play, then use the Scenario Control Panel " +
                      "/ Demo Mode canvases to run scenarios.");
        }

        [MenuItem("ATADTRL/Bake NavMesh Now")]
        public static void BakeNavMeshNow()
        {
#pragma warning disable 0618
            UnityEditor.AI.NavMeshBuilder.BuildNavMesh();
#pragma warning restore 0618
            Debug.Log("ATADTRL: NavMesh baked.");
        }

        // ------------------------------------------------------------
        // Warehouse
        // ------------------------------------------------------------

        private static WarehouseManager BuildWarehouseManager()
        {
            var go = FindOrCreate("WarehouseManager");
            var wm = go.GetComponent<WarehouseManager>();
            if (wm == null) wm = go.AddComponent<WarehouseManager>();
            return wm;
        }

        // ------------------------------------------------------------
        // Robot
        // ------------------------------------------------------------

        private static (GameObject, RobotController, BaselineNavigationController, NavigationManager, SensorManager) BuildRobot()
        {
            var robotGO = FindOrCreate("Robot");
            robotGO.transform.position = new Vector3(-20f, 0.2f, -10f);

            var rb = robotGO.GetComponent<Rigidbody>();
            if (rb == null) rb = robotGO.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            var box = robotGO.GetComponent<BoxCollider>();
            if (box == null) box = robotGO.AddComponent<BoxCollider>();
            box.size = new Vector3(0.5f, 0.4f, 0.7f);

            var agent = robotGO.GetComponent<NavMeshAgent>();
            if (agent == null) agent = robotGO.AddComponent<NavMeshAgent>();

            var robotController = robotGO.GetComponent<RobotController>();
            if (robotController == null) robotController = robotGO.AddComponent<RobotController>();

            var navController = robotGO.GetComponent<BaselineNavigationController>();
            if (navController == null) navController = robotGO.AddComponent<BaselineNavigationController>();

            var sensorManager = robotGO.GetComponent<SensorManager>();
            if (sensorManager == null) sensorManager = robotGO.AddComponent<SensorManager>();

            var navigationManager = robotGO.GetComponent<NavigationManager>();
            if (navigationManager == null) navigationManager = robotGO.AddComponent<NavigationManager>();
            navigationManager.robotController = robotController;
            navigationManager.sensorManager = sensorManager;

            // LiDAR mount
            var lidarMountT = robotGO.transform.Find("LidarMount");
            GameObject lidarMount = lidarMountT != null ? lidarMountT.gameObject : new GameObject("LidarMount");
            lidarMount.transform.SetParent(robotGO.transform, false);
            lidarMount.transform.localPosition = new Vector3(0f, 0.2f, 0.35f);
            var lidar = lidarMount.GetComponent<LidarSensor>();
            if (lidar == null) lidar = lidarMount.AddComponent<LidarSensor>();

            // Camera mount
            var camMountT = robotGO.transform.Find("CameraMount");
            GameObject camMount = camMountT != null ? camMountT.gameObject : new GameObject("CameraMount");
            camMount.transform.SetParent(robotGO.transform, false);
            camMount.transform.localPosition = new Vector3(0f, 0.3f, 0.35f);
            var cam = camMount.GetComponent<Camera>();
            if (cam == null) cam = camMount.AddComponent<Camera>();
            cam.enabled = false; // metadata-only sensor; doesn't need to render to screen
            var rgb = camMount.GetComponent<RGBSensor>();
            if (rgb == null) rgb = camMount.AddComponent<RGBSensor>();
            rgb.sourceCamera = cam;

            // IMU + Encoder on robot root
            var imu = robotGO.GetComponent<IMUSensor>();
            if (imu == null) imu = robotGO.AddComponent<IMUSensor>();

            var encoder = robotGO.GetComponent<WheelEncoder>();
            if (encoder == null) encoder = robotGO.AddComponent<WheelEncoder>();

            sensorManager.lidar = lidar;
            sensorManager.camera = rgb;
            sensorManager.imu = imu;
            sensorManager.encoder = encoder;

            return (robotGO, robotController, navController, navigationManager, sensorManager);
        }

        // ------------------------------------------------------------
        // Dynamic object prefabs (human / forklift / obstacle)
        // ------------------------------------------------------------

        private static (GameObject, GameObject, GameObject) BuildDynamicPrefabs()
        {
            const string dir = "Assets/Prefabs";
            if (!AssetDatabase.IsValidFolder(dir))
            {
                AssetDatabase.CreateFolder("Assets", "Prefabs");
            }

            GameObject human = LoadOrBuildPrefab($"{dir}/Human.prefab", "Human", PrimitiveType.Capsule,
                new Color(0.2f, 0.5f, 1f), new Vector3(0.5f, 1.8f, 0.5f));
            GameObject forklift = LoadOrBuildPrefab($"{dir}/Forklift.prefab", "Forklift", PrimitiveType.Cube,
                new Color(1f, 0.8f, 0f), new Vector3(1.2f, 1.5f, 2.2f));
            GameObject obstacle = LoadOrBuildPrefab($"{dir}/MovableObstacle.prefab", "MovableObstacle", PrimitiveType.Cube,
                new Color(0.8f, 0.15f, 0.15f), new Vector3(0.8f, 0.8f, 0.8f));

            return (human, forklift, obstacle);
        }

        private static GameObject LoadOrBuildPrefab(string path, string name, PrimitiveType prim, Color color, Vector3 scale)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            GameObject temp = GameObject.CreatePrimitive(prim);
            temp.name = name;
            temp.transform.localScale = scale;

            var renderer = temp.GetComponent<Renderer>();
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Diffuse");
            if (shader != null)
            {
                var mat = new Material(shader) { color = color };
                renderer.sharedMaterial = mat;
            }

            var rb = temp.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            EnvironmentCollisionTag.Attach(temp, EnvironmentCollisionTag.Kind.DynamicObstacle);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(temp, path);
            Object.DestroyImmediate(temp);
            return prefab;
        }

        // ------------------------------------------------------------
        // ScenarioManager + GroundTruthManager
        // ------------------------------------------------------------

        private static (ScenarioManager, GroundTruthManager) BuildScenarioManager(
            WarehouseManager warehouseManager, RobotController robotController,
            NavigationManager navigationManager, SensorManager sensorManager,
            GameObject humanPrefab, GameObject forkliftPrefab, GameObject obstaclePrefab)
        {
            var go = FindOrCreate("ScenarioManager");

            var groundTruthManager = go.GetComponent<GroundTruthManager>();
            if (groundTruthManager == null) groundTruthManager = go.AddComponent<GroundTruthManager>();
            groundTruthManager.warehouseManager = warehouseManager;
            groundTruthManager.navigationManager = navigationManager;

            var scenarioManager = go.GetComponent<ScenarioManager>();
            if (scenarioManager == null) scenarioManager = go.AddComponent<ScenarioManager>();
            scenarioManager.warehouseManager = warehouseManager;
            scenarioManager.groundTruthManager = groundTruthManager;
            scenarioManager.robotController = robotController;
            scenarioManager.navigationManager = navigationManager;
            scenarioManager.sensorManager = sensorManager;
            scenarioManager.humanPrefab = humanPrefab;
            scenarioManager.forkliftPrefab = forkliftPrefab;
            scenarioManager.obstaclePrefab = obstaclePrefab;

            return (scenarioManager, groundTruthManager);
        }

        // ------------------------------------------------------------
        // GameManager
        // ------------------------------------------------------------

        private static void BuildGameManager(ScenarioManager scenarioManager)
        {
            var go = FindOrCreate("GameManager");
            var gm = go.GetComponent<GameManager>();
            if (gm == null) gm = go.AddComponent<GameManager>();
            gm.scenarioManager = scenarioManager;
        }

        // ------------------------------------------------------------
        // UI — Scenario Control Panel
        // ------------------------------------------------------------

        private static void BuildControlPanelUI(ScenarioManager scenarioManager)
        {
            GameObject canvasGO = FindOrCreate("ATADTRL Module 1 Scenario Control Panel");
            EnsureCanvas(canvasGO);

            var resources = new TMP_DefaultControls.Resources();

            GameObject dropdownGO = TMP_DefaultControls.CreateDropdown(resources);
            dropdownGO.name = "ScenarioDropdown";
            Parent(dropdownGO, canvasGO, new Vector2(-500, 260), new Vector2(320, 40));
            var dropdown = dropdownGO.GetComponent<TMP_Dropdown>();

            GameObject descGO = TMP_DefaultControls.CreateText(resources);
            descGO.name = "DescriptionText";
            Parent(descGO, canvasGO, new Vector2(-500, 180), new Vector2(420, 120));
            var descText = descGO.GetComponent<TMP_Text>();
            descText.fontSize = 18;
            descText.text = "Scenario description";

            GameObject statusGO = TMP_DefaultControls.CreateText(resources);
            statusGO.name = "StatusText";
            Parent(statusGO, canvasGO, new Vector2(-500, 60), new Vector2(420, 60));
            var statusText = statusGO.GetComponent<TMP_Text>();
            statusText.fontSize = 16;
            statusText.text = "Idle";

            Button start = CreateButton(resources, canvasGO, "Start", new Vector2(-500, -20));
            Button stop = CreateButton(resources, canvasGO, "Stop", new Vector2(-350, -20));
            Button reset = CreateButton(resources, canvasGO, "Reset", new Vector2(-200, -20));
            Button next = CreateButton(resources, canvasGO, "Next", new Vector2(-500, -80));
            Button prev = CreateButton(resources, canvasGO, "Previous", new Vector2(-350, -80));
            Button runAll = CreateButton(resources, canvasGO, "Run All", new Vector2(-200, -80));
            Button returnBtn = CreateButton(resources, canvasGO, "Return to Main Menu", new Vector2(-425, -140));

            var uiManager = canvasGO.GetComponent<ScenarioUIManager>();
            if (uiManager == null) uiManager = canvasGO.AddComponent<ScenarioUIManager>();
            uiManager.scenarioManager = scenarioManager;
            uiManager.scenarioDropdown = dropdown;
            uiManager.scenarioDescriptionText = descText;
            uiManager.statusText = statusText;
            uiManager.startButton = start;
            uiManager.stopButton = stop;
            uiManager.resetButton = reset;
            uiManager.nextButton = next;
            uiManager.previousButton = prev;
            uiManager.runAllButton = runAll;
            uiManager.returnToMenuButton = returnBtn;
            uiManager.mainMenuSceneName = "";
        }

        // ------------------------------------------------------------
        // UI — Demo Mode (per-scenario buttons + Run All)
        // ------------------------------------------------------------

        private static void BuildDemoModeUI(ScenarioManager scenarioManager)
        {
            GameObject canvasGO = FindOrCreate("ATADTRL Demo Mode");
            EnsureCanvas(canvasGO);
            canvasGO.SetActive(false); // secondary screen; enable manually or via your own menu flow

            var resources = new TMP_DefaultControls.Resources();

            GameObject titleGO = TMP_DefaultControls.CreateText(resources);
            titleGO.name = "TitleText";
            Parent(titleGO, canvasGO, new Vector2(0, 400), new Vector2(600, 100));
            var titleText = titleGO.GetComponent<TMP_Text>();
            titleText.fontSize = 28;
            titleText.alignment = TextAlignmentOptions.Center;

            GameObject containerGO = new GameObject("ScenarioButtonContainer", typeof(RectTransform));
            Parent(containerGO, canvasGO, new Vector2(0, -20), new Vector2(760, 560));
            var grid = containerGO.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(140, 40);
            grid.spacing = new Vector2(10, 10);
            grid.childAlignment = TextAnchor.UpperCenter;

            // Prefab-like template button, kept inactive as a template asset in the scene.
            GameObject buttonTemplate = TMP_DefaultControls.CreateButton(resources);
            buttonTemplate.name = "ScenarioButtonTemplate";
            Parent(buttonTemplate, canvasGO, new Vector2(2000, 2000), new Vector2(140, 40));
            buttonTemplate.SetActive(false);

            Button runAll = CreateButton(resources, canvasGO, "RUN ALL", new Vector2(0, -320));

            GameObject bannerRoot = new GameObject("CompletionBanner", typeof(RectTransform));
            Parent(bannerRoot, canvasGO, new Vector2(0, 0), new Vector2(500, 80));
            GameObject bannerTextGO = TMP_DefaultControls.CreateText(resources);
            bannerTextGO.name = "CompletionBannerText";
            Parent(bannerTextGO, bannerRoot, Vector2.zero, new Vector2(500, 80));
            var bannerText = bannerTextGO.GetComponent<TMP_Text>();
            bannerText.alignment = TextAlignmentOptions.Center;
            bannerText.fontSize = 24;
            bannerRoot.SetActive(false);

            var demo = canvasGO.GetComponent<DemoController>();
            if (demo == null) demo = canvasGO.AddComponent<DemoController>();
            demo.scenarioManager = scenarioManager;
            demo.scenarioButtonContainer = containerGO.transform;
            demo.scenarioButtonPrefab = buttonTemplate;
            demo.runAllButton = runAll;
            demo.titleText = titleText;
            demo.completionBannerText = bannerText;
            demo.completionBannerRoot = bannerRoot;
        }

        // ------------------------------------------------------------
        // Small helpers
        // ------------------------------------------------------------

        private static GameObject FindOrCreate(string name)
        {
            // GameObject.Find() skips inactive objects (e.g. the Demo Mode
            // canvas, which is intentionally created inactive), which would
            // cause a duplicate to be created on every re-run. Search all
            // root objects in the active scene, including inactive ones.
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == name) return root;
                var childMatch = FindChildByNameIncludingInactive(root.transform, name);
                if (childMatch != null) return childMatch.gameObject;
            }
            return new GameObject(name);
        }

        private static Transform FindChildByNameIncludingInactive(Transform parent, string name)
        {
            if (parent.name == name) return parent;
            for (int i = 0; i < parent.childCount; i++)
            {
                var result = FindChildByNameIncludingInactive(parent.GetChild(i), name);
                if (result != null) return result;
            }
            return null;
        }

        private static Canvas EnsureCanvas(GameObject go)
        {
            var canvas = go.GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = go.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                var scaler = go.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                go.AddComponent<GraphicRaycaster>();
            }
            return canvas;
        }

        private static void EnsureEventSystem()
        {
#pragma warning disable 0618
            if (Object.FindFirstObjectByType<EventSystem>() != null) return;
#pragma warning restore 0618
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<StandaloneInputModule>();
        }

        private static void Parent(GameObject child, GameObject parent, Vector2 anchoredPos, Vector2 size)
        {
            child.transform.SetParent(parent.transform, false);
            var rt = child.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = anchoredPos;
                rt.sizeDelta = size;
            }
        }

        private static Button CreateButton(TMP_DefaultControls.Resources resources, GameObject parent, string label, Vector2 pos)
        {
            GameObject btnGO = TMP_DefaultControls.CreateButton(resources);
            btnGO.name = $"Btn_{label.Replace(" ", "")}";
            Parent(btnGO, parent, pos, new Vector2(130, 40));
            var text = btnGO.GetComponentInChildren<TMP_Text>();
            if (text != null) text.text = label;
            return btnGO.GetComponent<Button>();
        }

        private static void EditorSceneManager_MarkDirty()
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
        }
    }
}
#endif
