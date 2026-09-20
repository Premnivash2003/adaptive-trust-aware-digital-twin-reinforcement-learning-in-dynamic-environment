using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using TMPro;
using ATADTRL.Core;

namespace ATADTRL.Environment
{
    /// <summary>
    /// Builds a data-driven medium-scale warehouse from WarehouseConfig:
    /// floor, outer walls, rack grid (aisles), loading zone, safety
    /// boundaries, and marks the result NavMesh-static so it can be baked.
    /// Attach to an empty GameObject named "WarehouseManager" in the scene.
    /// </summary>
    public class WarehouseManager : MonoBehaviour
    {
        [Header("Configuration")]
        public WarehouseConfig config = new WarehouseConfig();

        [Header("Prefabs / Materials (optional, primitives used if null)")]
        public Material floorMaterial;
        public Material wallMaterial;
        public Material rackMaterial;
        public Material loadingZoneMaterial;
        public Material safetyZoneMaterial;

        [HideInInspector] public Transform RackRoot;
        [HideInInspector] public Transform DynamicObjectRoot;
        [HideInInspector] public List<Bounds> AisleBounds = new List<Bounds>();
        [HideInInspector] public WarehouseInventory Inventory;

        private Transform _root;
        private Material _rackBeamMaterial;
        private Material _cartonMaterial;
        private Material _concreteJointMaterial;
        private Material _laneWhiteMaterial;
        private Material _pedestrianGreenMaterial;
        private Material _structuralSteelMaterial;
        private Material _galvanisedMaterial;
        private Material _dockDoorMaterial;
        private Material _rubberMaterial;
        private Material _safetyYellowMaterial;
        private Material _hazardBlackMaterial;
        private Material _lightHousingMaterial;
        private Material _lightEmitterMaterial;
        private Material _fireEquipmentMaterial;
        private Material _informationBlueMaterial;
        private Texture2D _concreteTexture;
        private static readonly Dictionary<TMP_FontAsset, Material> PhysicalFontMaterials = new Dictionary<TMP_FontAsset, Material>();

        public void BuildWarehouse()
        {
            ClearExisting();
            EnsureStationConfiguration();

            // A restrained industrial palette makes the aisles, storage racks,
            // pickup and dispatch areas readable from the robot and overview
            // cameras without requiring external art assets.
            floorMaterial = SetMaterialColor(floorMaterial, new Color(0.43f, 0.44f, 0.43f));
            wallMaterial = SetMaterialColor(wallMaterial, new Color(0.76f, 0.75f, 0.70f));
            rackMaterial = SetMaterialColor(rackMaterial, new Color(0.22f, 0.27f, 0.31f));
            loadingZoneMaterial = SetMaterialColor(loadingZoneMaterial, new Color(0.18f, 0.31f, 0.27f));
            safetyZoneMaterial = SetMaterialColor(safetyZoneMaterial, new Color(0.94f, 0.62f, 0.035f));
            ConfigureSurface(floorMaterial, 0f, 0.16f);
            ConfigureSurface(wallMaterial, 0f, 0.08f);
            ConfigureSurface(rackMaterial, 0.72f, 0.30f);
            ConfigureSurface(loadingZoneMaterial, 0f, 0.22f);
            ConfigureSurface(safetyZoneMaterial, 0f, 0.23f);
            PrepareIndustrialMaterials();
            PrepareSurfaceTextures();
            ConfigureScenePresentation();

            _root = new GameObject("WarehouseGeometry").transform;
            _root.SetParent(transform, false);

            var inventoryRoot = new GameObject("RackInventoryMap");
            inventoryRoot.transform.SetParent(_root, false);
            Inventory = inventoryRoot.AddComponent<WarehouseInventory>();

            BuildFloor();
            BuildFloorDetails();
            BuildWalls();
            BuildIndustrialStructure();
            BuildRacks();
            BuildLoadingZone();
            BuildLoadingDockDetails();
            BuildTaskStations();
            BuildChargingStation();
            BuildSafetyZones();
            BuildSafetyEquipmentAndSigns();

            DynamicObjectRoot = new GameObject("DynamicObjects").transform;
            DynamicObjectRoot.SetParent(transform, false);

            GameObjectUtility_MarkStatic(_root.gameObject);
        }

        private void EnsureStationConfiguration()
        {
            if (config.pickupStationPositions == null || config.pickupStationPositions.Count < 3)
            {
                config.pickupStationPositions = new List<Vector3>
                {
                    new Vector3(-24f, 0f, -15f), new Vector3(24f, 0f, -15f), new Vector3(-24f, 0f, 15f)
                };
            }
            if (config.dropStationPositions == null || config.dropStationPositions.Count < 3)
            {
                config.dropStationPositions = new List<Vector3>
                {
                    new Vector3(-12.3f, 0f, 13.5f), new Vector3(12.3f, 0f, 13.5f), new Vector3(-12.3f, 0f, -13.5f)
                };
            }
        }

        private void ClearExisting()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                DestroyImmediateOrRuntime(transform.GetChild(i).gameObject);
            }
        }

        private void DestroyImmediateOrRuntime(GameObject go)
        {
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }

        private static Material CreateRuntimeMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Diffuse");
            var material = new Material(shader);
            material.color = color;
            return material;
        }

        private static Material CreateIndustrialMaterial(Color color, float metallic, float smoothness)
        {
            Material material = CreateRuntimeMaterial(color);
            ConfigureSurface(material, metallic, smoothness);
            return material;
        }

        private static void ConfigureSurface(Material material, float metallic, float smoothness)
        {
            if (material == null) return;
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", Mathf.Clamp01(metallic));
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", Mathf.Clamp01(smoothness));
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", Mathf.Clamp01(smoothness));
        }

        private static void ConfigureEmission(Material material, Color emission)
        {
            if (material == null || !material.HasProperty("_EmissionColor")) return;
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", emission);
        }

        private void PrepareIndustrialMaterials()
        {
            _concreteJointMaterial ??= CreateIndustrialMaterial(new Color(0.18f, 0.19f, 0.19f), 0f, 0.05f);
            _laneWhiteMaterial ??= CreateIndustrialMaterial(new Color(0.86f, 0.85f, 0.72f), 0f, 0.22f);
            _pedestrianGreenMaterial ??= CreateIndustrialMaterial(new Color(0.18f, 0.42f, 0.30f), 0f, 0.18f);
            _structuralSteelMaterial ??= CreateIndustrialMaterial(new Color(0.17f, 0.21f, 0.24f), 0.82f, 0.27f);
            _galvanisedMaterial ??= CreateIndustrialMaterial(new Color(0.47f, 0.51f, 0.53f), 0.76f, 0.34f);
            _dockDoorMaterial ??= CreateIndustrialMaterial(new Color(0.32f, 0.35f, 0.36f), 0.68f, 0.23f);
            _rubberMaterial ??= CreateIndustrialMaterial(new Color(0.045f, 0.048f, 0.05f), 0f, 0.10f);
            _safetyYellowMaterial ??= CreateIndustrialMaterial(new Color(0.94f, 0.62f, 0.035f), 0f, 0.23f);
            _hazardBlackMaterial ??= CreateIndustrialMaterial(new Color(0.055f, 0.06f, 0.06f), 0f, 0.12f);
            _lightHousingMaterial ??= CreateIndustrialMaterial(new Color(0.20f, 0.22f, 0.23f), 0.65f, 0.28f);
            _lightEmitterMaterial ??= CreateIndustrialMaterial(new Color(0.92f, 0.95f, 1f), 0f, 0.72f);
            _fireEquipmentMaterial ??= CreateIndustrialMaterial(new Color(0.68f, 0.045f, 0.03f), 0.40f, 0.24f);
            _informationBlueMaterial ??= CreateIndustrialMaterial(new Color(0.035f, 0.20f, 0.35f), 0.10f, 0.24f);
            ConfigureEmission(_lightEmitterMaterial, new Color(2.4f, 2.55f, 2.8f));
        }

        private void ConfigureScenePresentation()
        {
            // Neutral indoor lighting removes the old blue wash and keeps
            // concrete, cardboard, steel and safety colours recognisable in
            // both the robot sensor view and the overview camera.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.58f, 0.60f, 0.61f);
            RenderSettings.ambientEquatorColor = new Color(0.38f, 0.39f, 0.38f);
            RenderSettings.ambientGroundColor = new Color(0.19f, 0.19f, 0.18f);
            RenderSettings.ambientIntensity = 0.78f;
            RenderSettings.reflectionIntensity = 0.55f;
            RenderSettings.fog = false;

            Light directional = RenderSettings.sun;
            if (directional == null)
            {
                foreach (Light candidate in FindObjectsByType<Light>(FindObjectsInactive.Exclude))
                {
                    if (candidate.type != LightType.Directional) continue;
                    directional = candidate;
                    break;
                }
            }
            if (directional != null)
            {
                directional.color = new Color(1f, 0.97f, 0.90f);
                directional.intensity = 0.62f;
                directional.shadows = LightShadows.Soft;
                directional.shadowStrength = 0.55f;
                RenderSettings.sun = directional;
            }

            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                mainCamera.allowHDR = true;
                mainCamera.allowMSAA = true;
                mainCamera.nearClipPlane = Mathf.Min(mainCamera.nearClipPlane, 0.08f);
                mainCamera.farClipPlane = Mathf.Max(mainCamera.farClipPlane, 180f);
                mainCamera.backgroundColor = new Color(0.14f, 0.16f, 0.17f);
            }
        }

        private void PrepareSurfaceTextures()
        {
            if (_concreteTexture == null)
            {
                const int size = 128;
                _concreteTexture = new Texture2D(size, size, TextureFormat.RGBA32, true)
                {
                    name = "Procedural sealed concrete",
                    wrapMode = TextureWrapMode.Repeat,
                    filterMode = FilterMode.Bilinear,
                    anisoLevel = 4
                };
                var pixels = new Color[size * size];
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float broad = Mathf.PerlinNoise(x * 0.045f + 7.3f, y * 0.045f + 2.1f);
                        float grain = Mathf.PerlinNoise(x * 0.31f + 21.7f, y * 0.31f + 13.2f);
                        float value = Mathf.Clamp01(0.39f + broad * 0.10f + grain * 0.035f);
                        pixels[y * size + x] = new Color(value * 1.01f, value, value * 0.97f, 1f);
                    }
                }
                _concreteTexture.SetPixels(pixels);
                _concreteTexture.Apply(true, false);
            }

            if (floorMaterial == null) return;
            floorMaterial.mainTexture = _concreteTexture;
            floorMaterial.mainTextureScale = new Vector2(
                Mathf.Max(1f, config.warehouseLength / 4f),
                Mathf.Max(1f, config.warehouseWidth / 4f));
            if (floorMaterial.HasProperty("_BaseMap"))
            {
                floorMaterial.SetTexture("_BaseMap", _concreteTexture);
                floorMaterial.SetTextureScale("_BaseMap", floorMaterial.mainTextureScale);
            }
        }

        private static Material SetMaterialColor(Material material, Color color)
        {
            if (material == null) material = CreateRuntimeMaterial(color);
            else material.color = color;
            return material;
        }

        private void BuildFloor()
        {
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            floor.transform.SetParent(_root, false);
            floor.transform.localScale = new Vector3(config.warehouseLength, 0.2f, config.warehouseWidth);
            floor.transform.localPosition = new Vector3(0, -0.1f, 0);
            if (floorMaterial != null) floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
        }

        /// <summary>
        /// Adds the small cues that make a single grey slab read as a sealed
        /// industrial concrete floor. Every element is visual-only, so the
        /// robot, humans and forklifts continue to use the original floor
        /// collider and baked navigation surface.
        /// </summary>
        private void BuildFloorDetails()
        {
            var details = new GameObject("IndustrialFloorDetails").transform;
            details.SetParent(_root, false);

            const float jointWidth = 0.025f;
            const float markHeight = 0.008f;
            float halfLength = config.warehouseLength * 0.5f;
            float halfWidth = config.warehouseWidth * 0.5f;

            // Saw-cut expansion joints at realistic slab intervals.
            for (float x = -halfLength + 5f; x < halfLength - 1f; x += 5f)
            {
                CreateVisualBox(details, "Concrete expansion joint X", new Vector3(x, markHeight * 0.5f, 0f),
                    new Vector3(jointWidth, markHeight, config.warehouseWidth - 0.8f), _concreteJointMaterial, false);
            }
            for (float z = -halfWidth + 5f; z < halfWidth - 1f; z += 5f)
            {
                CreateVisualBox(details, "Concrete expansion joint Z", new Vector3(0f, markHeight * 0.5f, z),
                    new Vector3(config.warehouseLength - 0.8f, markHeight, jointWidth), _concreteJointMaterial, false);
            }

            // A protected pedestrian lane follows the west perimeter.
            float walkwayWidth = Mathf.Clamp(config.aisleWidth * 0.62f, 1.35f, 2.15f);
            float walkwayX = -halfLength + 1.15f + walkwayWidth * 0.5f;
            CreateVisualBox(details, "Pedestrian walkway", new Vector3(walkwayX, 0.008f, 0f),
                new Vector3(walkwayWidth, 0.012f, config.warehouseWidth - 3.0f), _pedestrianGreenMaterial, false);
            CreateVisualBox(details, "Walkway inside boundary", new Vector3(walkwayX + walkwayWidth * 0.5f, 0.016f, 0f),
                new Vector3(0.075f, 0.014f, config.warehouseWidth - 3.0f), _laneWhiteMaterial, false);
            CreateVisualBox(details, "Walkway wall boundary", new Vector3(walkwayX - walkwayWidth * 0.5f, 0.016f, 0f),
                new Vector3(0.075f, 0.014f, config.warehouseWidth - 3.0f), _laneWhiteMaterial, false);

            // Painted aisle edge lines are derived from the rack layout but
            // do not alter any rack or station coordinates.
            float rackAreaWidth = config.rackColumns * config.rackWidth +
                                  Mathf.Max(0, config.rackColumns - 1) * config.aisleWidth;
            float rackStartX = -rackAreaWidth * 0.5f + config.rackWidth * 0.5f;
            for (int col = 0; col < config.rackColumns - 1; col++)
            {
                float rackX = rackStartX + col * (config.rackWidth + config.aisleWidth);
                float aisleX = rackX + config.rackWidth * 0.5f + config.aisleWidth * 0.5f;
                float edgeOffset = Mathf.Max(0.35f, config.aisleWidth * 0.5f - 0.14f);
                foreach (float edge in new[] { aisleX - edgeOffset, aisleX + edgeOffset })
                {
                    CreateVisualBox(details, "Vehicle aisle boundary", new Vector3(edge, 0.015f, 0f),
                        new Vector3(0.055f, 0.012f, config.warehouseWidth - 5f), _laneWhiteMaterial, false);
                }
            }

            // Zebra crossing at the front end of the central rack aisle.
            float centralAisleX = 0f;
            if (config.rackColumns > 1)
            {
                int centralGap = Mathf.Clamp((config.rackColumns - 2) / 2, 0, config.rackColumns - 2);
                float rackX = rackStartX + centralGap * (config.rackWidth + config.aisleWidth);
                centralAisleX = rackX + config.rackWidth * 0.5f + config.aisleWidth * 0.5f;
            }
            float crossingZ = -halfWidth + Mathf.Clamp(4.4f, 2.5f, config.warehouseWidth * 0.25f);
            for (int stripe = -4; stripe <= 4; stripe++)
            {
                CreateVisualBox(details, "Pedestrian crossing stripe", new Vector3(centralAisleX, 0.019f, crossingZ + stripe * 0.42f),
                    new Vector3(Mathf.Max(1.6f, config.aisleWidth - 0.35f), 0.015f, 0.23f), _laneWhiteMaterial, false);
            }
        }

        private static GameObject CreateVisualBox(Transform parent, string name, Vector3 localPosition,
            Vector3 localScale, Material material, bool castShadows)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            box.transform.localPosition = localPosition;
            box.transform.localScale = localScale;
            var collider = box.GetComponent<Collider>();
            if (collider != null) collider.enabled = false;
            var renderer = box.GetComponent<Renderer>();
            if (material != null) renderer.sharedMaterial = material;
            renderer.shadowCastingMode = castShadows
                ? UnityEngine.Rendering.ShadowCastingMode.On
                : UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = castShadows;
            return box;
        }

        private void BuildWalls()
        {
            float t = 0.3f;
            float h = config.warehouseHeight;
            CreateWall("Wall_North", new Vector3(0, h / 2f, config.warehouseWidth / 2f), new Vector3(config.warehouseLength, h, t));
            CreateWall("Wall_South", new Vector3(0, h / 2f, -config.warehouseWidth / 2f), new Vector3(config.warehouseLength, h, t));
            CreateWall("Wall_East", new Vector3(config.warehouseLength / 2f, h / 2f, 0), new Vector3(t, h, config.warehouseWidth));
            CreateWall("Wall_West", new Vector3(-config.warehouseLength / 2f, h / 2f, 0), new Vector3(t, h, config.warehouseWidth));
        }

        private void CreateWall(string name, Vector3 pos, Vector3 scale)
        {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = name;
            wall.transform.SetParent(_root, false);
            wall.transform.localPosition = pos;
            wall.transform.localScale = scale;
            if (wallMaterial != null) wall.GetComponent<Renderer>().sharedMaterial = wallMaterial;
            EnvironmentCollisionTag.Attach(wall, EnvironmentCollisionTag.Kind.Wall);
        }

        private void BuildIndustrialStructure()
        {
            var structure = new GameObject("IndustrialStructure").transform;
            structure.SetParent(_root, false);

            float halfLength = config.warehouseLength * 0.5f;
            float halfWidth = config.warehouseWidth * 0.5f;
            float height = config.warehouseHeight;

            // Washable impact-resistant lower wall panels give the room a
            // clear ground plane and break up the large untextured walls.
            Material wallDado = CreateIndustrialMaterial(new Color(0.28f, 0.31f, 0.31f), 0.18f, 0.12f);
            CreateVisualBox(structure, "North wall impact dado", new Vector3(0f, 0.62f, halfWidth - 0.165f),
                new Vector3(config.warehouseLength - 0.7f, 1.20f, 0.035f), wallDado, false);
            CreateVisualBox(structure, "South wall impact dado", new Vector3(0f, 0.62f, -halfWidth + 0.165f),
                new Vector3(config.warehouseLength - 0.7f, 1.20f, 0.035f), wallDado, false);
            CreateVisualBox(structure, "East wall impact dado", new Vector3(halfLength - 0.165f, 0.62f, 0f),
                new Vector3(0.035f, 1.20f, config.warehouseWidth - 0.7f), wallDado, false);
            CreateVisualBox(structure, "West wall impact dado", new Vector3(-halfLength + 0.165f, 0.62f, 0f),
                new Vector3(0.035f, 1.20f, config.warehouseWidth - 0.7f), wallDado, false);

            // Steel portal-frame columns. The walls retain the only
            // collision surfaces; these members are visual architectural
            // detail and therefore cannot change a scenario route.
            float columnStep = Mathf.Clamp(Mathf.Min(config.warehouseLength, config.warehouseWidth) / 5f, 5f, 8f);
            for (float x = -halfLength + columnStep; x < halfLength - 1f; x += columnStep)
            {
                CreateVisualBox(structure, "North portal column", new Vector3(x, height * 0.5f, halfWidth - 0.34f),
                    new Vector3(0.26f, height, 0.30f), _structuralSteelMaterial, true);
                CreateVisualBox(structure, "South portal column", new Vector3(x, height * 0.5f, -halfWidth + 0.34f),
                    new Vector3(0.26f, height, 0.30f), _structuralSteelMaterial, true);
            }
            for (float z = -halfWidth + columnStep; z < halfWidth - 1f; z += columnStep)
            {
                CreateVisualBox(structure, "East portal column", new Vector3(halfLength - 0.34f, height * 0.5f, z),
                    new Vector3(0.30f, height, 0.26f), _structuralSteelMaterial, true);
                CreateVisualBox(structure, "West portal column", new Vector3(-halfLength + 0.34f, height * 0.5f, z),
                    new Vector3(0.30f, height, 0.26f), _structuralSteelMaterial, true);
            }

            var roof = new GameObject("OpenRoofTrusses").transform;
            roof.SetParent(structure, false);
            float roofY = height - 0.30f;
            float trussStep = Mathf.Clamp(config.warehouseLength / 6f, 7f, 11f);
            for (float x = -halfLength + trussStep * 0.5f; x < halfLength; x += trussStep)
            {
                CreateVisualBox(roof, "Primary roof truss", new Vector3(x, roofY, 0f),
                    new Vector3(0.20f, 0.24f, config.warehouseWidth - 0.65f), _structuralSteelMaterial, true);
            }
            float purlinStep = Mathf.Clamp(config.warehouseWidth / 6f, 4.5f, 7f);
            for (float z = -halfWidth + purlinStep * 0.5f; z < halfWidth; z += purlinStep)
            {
                CreateVisualBox(roof, "Roof purlin", new Vector3(0f, roofY - 0.17f, z),
                    new Vector3(config.warehouseLength - 0.65f, 0.10f, 0.13f), _galvanisedMaterial, true);
            }

            BuildHighBayLighting(structure, roofY - 0.28f);
        }

        private void BuildHighBayLighting(Transform structure, float y)
        {
            var lighting = new GameObject("HighBayLighting").transform;
            lighting.SetParent(structure, false);

            int columns = Mathf.Clamp(Mathf.RoundToInt(config.warehouseLength / 12f), 3, 5);
            int rows = Mathf.Clamp(Mathf.RoundToInt(config.warehouseWidth / 13f), 2, 3);
            float spanX = config.warehouseLength * 0.70f;
            float spanZ = config.warehouseWidth * 0.56f;

            for (int xIndex = 0; xIndex < columns; xIndex++)
            {
                float x = columns == 1 ? 0f : Mathf.Lerp(-spanX * 0.5f, spanX * 0.5f, xIndex / (float)(columns - 1));
                for (int zIndex = 0; zIndex < rows; zIndex++)
                {
                    float z = rows == 1 ? 0f : Mathf.Lerp(-spanZ * 0.5f, spanZ * 0.5f, zIndex / (float)(rows - 1));
                    Transform fixture = new GameObject($"High-bay luminaire {xIndex + 1}-{zIndex + 1}").transform;
                    fixture.SetParent(lighting, false);
                    fixture.localPosition = new Vector3(x, y, z);

                    CreateVisualBox(fixture, "Housing", Vector3.zero, new Vector3(2.30f, 0.14f, 0.38f),
                        _lightHousingMaterial, true);
                    CreateVisualBox(fixture, "Diffuser", new Vector3(0f, -0.085f, 0f), new Vector3(2.05f, 0.035f, 0.29f),
                        _lightEmitterMaterial, false);

                    // Only alternate fittings create realtime lights. The
                    // emissive diffusers preserve a continuous visual row
                    // while keeping the runtime light count inexpensive.
                    if ((xIndex + zIndex) % 2 == 0)
                    {
                        Light light = fixture.gameObject.AddComponent<Light>();
                        light.type = LightType.Point;
                        light.color = new Color(0.86f, 0.91f, 1f);
                        light.intensity = 1.25f;
                        light.range = Mathf.Clamp(config.warehouseHeight * 1.55f, 8f, 14f);
                        light.shadows = LightShadows.None;
                        light.renderMode = LightRenderMode.Auto;
                    }
                }
            }
        }

        private void BuildRacks()
        {
            RackRoot = new GameObject("Racks").transform;
            RackRoot.SetParent(_root, false);
            AisleBounds.Clear();

            float totalRackAreaWidth = config.rackColumns * config.rackWidth + (config.rackColumns - 1) * config.aisleWidth;
            float startX = -totalRackAreaWidth / 2f + config.rackWidth / 2f;

            float totalRowDepth = config.rackRows * config.rackLength + (config.rackRows + 1) * config.aisleWidth;
            float startZ = -totalRowDepth / 2f + config.aisleWidth + config.rackLength / 2f;

            for (int col = 0; col < config.rackColumns; col++)
            {
                float x = startX + col * (config.rackWidth + config.aisleWidth);

                for (int row = 0; row < config.rackRows; row++)
                {
                    float z = startZ + row * (config.rackLength + config.aisleWidth);

                    CreateStorageRack($"Rack_{col}_{row}", new Vector3(x, 0f, z));
                }

                // Record aisle to the right of this rack column (except after the last column)
                if (col < config.rackColumns - 1)
                {
                    float aisleCenterX = x + config.rackWidth / 2f + config.aisleWidth / 2f;
                    Bounds b = new Bounds(
                        new Vector3(aisleCenterX, 1f, 0f),
                        new Vector3(config.aisleWidth, 2f, totalRowDepth));
                    AisleBounds.Add(b);
                }
            }
        }

        // The navigation footprint remains a single solid rack, while the
        // visible geometry is a realistic pallet-rack frame with uprights,
        // safety beams and stored cartons.
        private void CreateStorageRack(string rackName, Vector3 localPosition)
        {
            var rack = new GameObject(rackName).transform;
            rack.SetParent(RackRoot, false);
            rack.localPosition = localPosition;

            var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blocker.name = "NavigationFootprint";
            blocker.transform.SetParent(rack, false);
            blocker.transform.localPosition = new Vector3(0f, config.rackHeight / 2f, 0f);
            blocker.transform.localScale = new Vector3(config.rackWidth, config.rackHeight, config.rackLength);
            blocker.GetComponent<Renderer>().enabled = false;
            EnvironmentCollisionTag.Attach(blocker, EnvironmentCollisionTag.Kind.Rack);

            _rackBeamMaterial ??= CreateIndustrialMaterial(new Color(0.78f, 0.24f, 0.035f), 0.62f, 0.28f);
            _cartonMaterial ??= CreateIndustrialMaterial(new Color(0.49f, 0.31f, 0.15f), 0f, 0.12f);

            float halfWidth = config.rackWidth * 0.5f - 0.08f;
            float halfLength = config.rackLength * 0.5f - 0.08f;
            foreach (float x in new[] { -halfWidth, halfWidth })
            {
                foreach (float z in new[] { -halfLength, halfLength })
                {
                    CreateRackPart(rack, "Upright", new Vector3(x, config.rackHeight * 0.5f, z),
                        new Vector3(0.10f, config.rackHeight, 0.10f), rackMaterial, true);
                    CreateRackPart(rack, "Upright base plate", new Vector3(x, 0.035f, z),
                        new Vector3(0.24f, 0.07f, 0.24f), _galvanisedMaterial, true);
                }
            }

            foreach (float y in new[] { 0.28f, 1.45f, 2.62f, 3.78f })
            {
                // Real pallet racking uses longitudinal orange beams rather
                // than one solid orange block. A thin galvanised mesh deck
                // between them supports the inventory visuals.
                CreateRackPart(rack, "Front load beam", new Vector3(-halfWidth, y, 0f),
                    new Vector3(0.12f, 0.12f, config.rackLength), _rackBeamMaterial, true);
                CreateRackPart(rack, "Rear load beam", new Vector3(halfWidth, y, 0f),
                    new Vector3(0.12f, 0.12f, config.rackLength), _rackBeamMaterial, true);
                CreateRackPart(rack, "End cross beam A", new Vector3(0f, y, -halfLength),
                    new Vector3(config.rackWidth, 0.10f, 0.10f), _rackBeamMaterial, true);
                CreateRackPart(rack, "End cross beam B", new Vector3(0f, y, halfLength),
                    new Vector3(config.rackWidth, 0.10f, 0.10f), _rackBeamMaterial, true);
                CreateRackPart(rack, "Galvanised wire deck", new Vector3(0f, y + 0.065f, 0f),
                    new Vector3(config.rackWidth - 0.18f, 0.025f, config.rackLength - 0.16f), _galvanisedMaterial, true);
            }

            string category = Inventory.GetCategoryForRack(rackName);
            Inventory.RegisterRack(rackName, category, rack, config.rackWidth, config.rackLength, config.rackHeight);
            CreateRackCategoryLabel(rack, category);
            CreateRackSlotLabels(rack);
        }

        private void CreateRackCategoryLabel(Transform rack, string category)
        {
            // Rack information is printed on small end-frame plaques at eye
            // level, as in a working fulfilment warehouse. The previous text
            // floated above every bay and could be read backwards through the
            // rack, which made an overview camera look like a debug display.
            string location = FormatRackLocation(rack.name);
            string categoryName = string.Equals(category, "General", StringComparison.OrdinalIgnoreCase)
                ? "GENERAL STORAGE"
                : category.ToUpperInvariant();
            string plaqueText = $"{location}\n{categoryName}";
            float signY = Mathf.Clamp(config.rackHeight * 0.54f, 1.85f, 2.30f);
            float signWidth = Mathf.Clamp(config.rackWidth * 0.78f, 0.90f, 1.42f);
            Vector2 signSize = new Vector2(signWidth, 0.46f);
            float endZ = config.rackLength * 0.5f + 0.035f;

            CreateIndustrialSign(rack, "South rack location plaque",
                new Vector3(0f, signY, -endZ), Quaternion.identity, signSize,
                plaqueText, _informationBlueMaterial, Color.white, 0.075f);
            CreateIndustrialSign(rack, "North rack location plaque",
                new Vector3(0f, signY, endZ), Quaternion.Euler(0f, 180f, 0f), signSize,
                plaqueText, _informationBlueMaterial, Color.white, 0.075f);
        }

        private static string FormatRackLocation(string rackName)
        {
            string[] pieces = (rackName ?? string.Empty).Split('_');
            if (pieces.Length >= 3 && int.TryParse(pieces[1], out int column) &&
                int.TryParse(pieces[2], out int row))
            {
                return $"R{column + 1:00}-{row + 1:00}";
            }

            return string.IsNullOrWhiteSpace(rackName)
                ? "RACK"
                : rackName.Replace('_', '-').ToUpperInvariant();
        }

        private void CreateRackSlotLabels(Transform rack)
        {
            // Small fixed shelf-edge labels identify the same level/bay slots
            // used by the inventory ledger. They do not billboard or float.
            foreach (RackSlot slot in Inventory.GetSlotsForRack(rack.name))
            {
                float face = slot.approachPosition.x < rack.position.x ? -1f : 1f;
                Vector3 local = rack.InverseTransformPoint(slot.storagePosition);
                float beamY = new[] { 0.28f, 1.45f, 2.62f, 3.78f }[Mathf.Clamp(slot.levelIndex - 1, 0, 3)];
                CreateIndustrialSign(rack, $"Shelf address L{slot.levelIndex:00} B{slot.bayIndex:00}",
                    new Vector3(face * (config.rackWidth * 0.5f + 0.02f), beamY, local.z),
                    Quaternion.Euler(0f, face < 0f ? 90f : -90f, 0f), new Vector2(0.48f, 0.115f),
                    $"L{slot.levelIndex:00} · B{slot.bayIndex:00}", _laneWhiteMaterial,
                    new Color(0.06f, 0.08f, 0.09f), 0.025f);
            }
        }

        private static void CreateRackPart(Transform parent, string partName, Vector3 localPosition,
            Vector3 localScale, Material material, bool castShadows)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = partName;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
            var collider = part.GetComponent<Collider>();
            if (collider != null) collider.enabled = false;
            var renderer = part.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = castShadows
                ? UnityEngine.Rendering.ShadowCastingMode.On
                : UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private void BuildLoadingZone()
        {
            GameObject zone = GameObject.CreatePrimitive(PrimitiveType.Cube);
            zone.name = "LoadingZone";
            zone.transform.SetParent(_root, false);
            zone.transform.localScale = new Vector3(config.loadingZoneSize.x, 0.05f, config.loadingZoneSize.y);
            zone.transform.localPosition = new Vector3(
                config.warehouseLength / 2f - config.loadingZoneSize.x / 2f - 1f,
                0.03f,
                config.warehouseWidth / 2f - config.loadingZoneSize.y / 2f - 1f);
            if (loadingZoneMaterial != null) zone.GetComponent<Renderer>().sharedMaterial = loadingZoneMaterial;

            // Loading zone floor should not block navigation; remove its collider from nav baking
            var col = zone.GetComponent<Collider>();
            if (col != null) col.enabled = false;
        }

        private void BuildLoadingDockDetails()
        {
            var docks = new GameObject("InboundLoadingDocks").transform;
            docks.SetParent(_root, false);

            Vector3 loadingCenter = new Vector3(
                config.warehouseLength * 0.5f - config.loadingZoneSize.x * 0.5f - 1f,
                0f,
                config.warehouseWidth * 0.5f - config.loadingZoneSize.y * 0.5f - 1f);
            float innerNorthWall = config.warehouseWidth * 0.5f - 0.17f;
            float doorWidth = Mathf.Clamp(config.loadingZoneSize.x * 0.36f, 2.7f, 3.6f);
            float doorHeight = Mathf.Clamp(config.warehouseHeight * 0.50f, 3.4f, 4.4f);

            for (int index = 0; index < 2; index++)
            {
                float offsetX = (index == 0 ? -1f : 1f) * doorWidth * 0.58f;
                float doorX = Mathf.Clamp(loadingCenter.x + offsetX,
                    -config.warehouseLength * 0.5f + doorWidth,
                    config.warehouseLength * 0.5f - doorWidth);
                Transform dock = new GameObject($"Dock {index + 1:00}").transform;
                dock.SetParent(docks, false);
                dock.localPosition = new Vector3(doorX, 0f, innerNorthWall);

                CreateVisualBox(dock, "Sectional overhead door", new Vector3(0f, doorHeight * 0.5f, -0.025f),
                    new Vector3(doorWidth, doorHeight, 0.055f), _dockDoorMaterial, true);
                CreateVisualBox(dock, "Left door frame", new Vector3(-doorWidth * 0.5f - 0.08f, doorHeight * 0.5f, -0.07f),
                    new Vector3(0.16f, doorHeight + 0.30f, 0.15f), _structuralSteelMaterial, true);
                CreateVisualBox(dock, "Right door frame", new Vector3(doorWidth * 0.5f + 0.08f, doorHeight * 0.5f, -0.07f),
                    new Vector3(0.16f, doorHeight + 0.30f, 0.15f), _structuralSteelMaterial, true);
                CreateVisualBox(dock, "Door header", new Vector3(0f, doorHeight + 0.10f, -0.07f),
                    new Vector3(doorWidth + 0.30f, 0.20f, 0.15f), _structuralSteelMaterial, true);

                for (int slat = 1; slat < 9; slat++)
                {
                    float slatY = doorHeight * slat / 9f;
                    CreateVisualBox(dock, "Door panel seam", new Vector3(0f, slatY, -0.058f),
                        new Vector3(doorWidth - 0.08f, 0.022f, 0.018f), _rubberMaterial, false);
                }

                foreach (float side in new[] { -1f, 1f })
                {
                    CreateVisualBox(dock, "Dock rubber bumper", new Vector3(side * (doorWidth * 0.5f - 0.22f), 0.43f, -0.20f),
                        new Vector3(0.30f, 0.78f, 0.24f), _rubberMaterial, true);
                    CreateVisualBox(dock, "Safety bollard", new Vector3(side * (doorWidth * 0.5f + 0.38f), 0.52f, -0.72f),
                        new Vector3(0.17f, 1.04f, 0.17f), _safetyYellowMaterial, true);
                }

                // Recessed dock approach marking.
                CreateVisualBox(dock, "Dock approach stop line", new Vector3(0f, 0.018f, -1.22f),
                    new Vector3(doorWidth + 0.65f, 0.014f, 0.10f), _safetyYellowMaterial, false);

                CreateIndustrialSign(dock, "Dock number sign", new Vector3(0f, doorHeight + 0.62f, -0.10f),
                    Quaternion.identity, new Vector2(1.65f, 0.48f), $"DOCK {index + 1:00}",
                    _structuralSteelMaterial, Color.white, 0.17f);
            }

            CreateIndustrialSign(docks, "Inbound operations sign",
                new Vector3(loadingCenter.x, Mathf.Min(config.warehouseHeight - 0.75f, 6.4f), innerNorthWall - 0.04f),
                Quaternion.identity, new Vector2(Mathf.Min(7.5f, config.loadingZoneSize.x * 0.82f), 0.72f),
                "INBOUND RECEIVING  |  AUTHORISED PERSONNEL ONLY", _structuralSteelMaterial,
                new Color(0.94f, 0.94f, 0.89f), 0.15f);
        }

        private static Transform CreateIndustrialSign(Transform parent, string name, Vector3 localPosition,
            Quaternion localRotation, Vector2 size, string text, Material backingMaterial, Color textColour,
            float characterSize)
        {
            Transform sign = new GameObject(name).transform;
            sign.SetParent(parent, false);
            sign.localPosition = localPosition;
            sign.localRotation = localRotation;

            CreateVisualBox(sign, "Sign backing", Vector3.zero,
                new Vector3(size.x, size.y, 0.035f), backingMaterial, false);
            TextMeshPro label = new GameObject("Printed lettering", typeof(RectTransform)).AddComponent<TextMeshPro>();
            label.transform.SetParent(sign, false);
            label.transform.localPosition = new Vector3(0f, 0f, -0.023f);
            label.text = text;
            if (TMP_Settings.defaultFontAsset != null) label.font = TMP_Settings.defaultFontAsset;
            label.rectTransform.sizeDelta = new Vector2(size.x * 0.90f, size.y * 0.80f);
            label.fontSizeMax = Mathf.Clamp(size.y * 12f, 1f, 22f);
            label.fontSizeMin = 0.3f;
            label.fontSize = label.fontSizeMax;
            label.enableAutoSizing = true;
            label.fontStyle = FontStyles.Bold;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.alignment = TextAlignmentOptions.Center;
            label.color = textColour;
            label.isOverlay = false;
            label.richText = false;
            label.fontSharedMaterial = GetPhysicalFontMaterial(label.font);
            MeshRenderer letteringRenderer = label.GetComponent<MeshRenderer>();
            if (letteringRenderer != null)
            {
                letteringRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                letteringRenderer.receiveShadows = false;
            }
            // A backing plane hides the reverse of the print; depth-tested
            // lettering cannot remain visible through cartons or walls.
            label.ForceMeshUpdate();
            if (size.x >= 0.75f && size.y >= 0.35f)
            {
                foreach (float x in new[] { -1f, 1f })
                    foreach (float y in new[] { -1f, 1f })
                        CreateVisualBox(sign, "Mounting rivet", new Vector3(x * size.x * 0.46f, y * size.y * 0.40f, -0.023f),
                            new Vector3(0.018f, 0.018f, 0.008f), backingMaterial, false);
            }
            return sign;
        }

        private static Material GetPhysicalFontMaterial(TMP_FontAsset font)
        {
            if (font == null) return null;
            if (PhysicalFontMaterials.TryGetValue(font, out Material cached) && cached != null) return cached;
            Material material = new Material(font.material) { name = "Warehouse printed lettering (depth tested)" };
            // The installed surface font shader uses normal geometry depth
            // testing. Legacy TextMesh uses GUI/Text Shader, which draws over
            // opaque geometry and is unsuitable for warehouse signs.
            Shader surface = Shader.Find("TextMeshPro/Distance Field (Surface)");
            if (surface != null && surface.isSupported) material.shader = surface;
            material.SetFloat("unity_GUIZTestMode", (float)UnityEngine.Rendering.CompareFunction.LessEqual);
            if (material.HasProperty("_CullMode")) material.SetFloat("_CullMode", (float)UnityEngine.Rendering.CullMode.Back);
            if (material.HasProperty("_FaceColor")) material.SetColor("_FaceColor", Color.white);
            if (material.HasProperty("_OutlineWidth")) material.SetFloat("_OutlineWidth", 0f);
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            PhysicalFontMaterials[font] = material;
            return material;
        }

        public static Transform CreatePhysicalSign(Transform parent, string name, Vector3 localPosition,
            Quaternion localRotation, Vector2 size, string text, Color backingColour, Color letteringColour)
        {
            Material backing = CreateIndustrialMaterial(backingColour, 0.05f, 0.22f);
            return CreateIndustrialSign(parent, name, localPosition, localRotation, size,
                text, backing, letteringColour, size.y * 0.20f);
        }

        // Three inbound stations provide long, perimeter-to-rack tasks. Each
        // has a clear parcel staging shelf and a label so human/forklift
        // hand-offs are visible before the robot picks the barcode parcel.
        private void BuildTaskStations()
        {
            for (int i = 0; i < config.pickupStationPositions.Count; i++)
            {
                CreateTaskStation($"P{i + 1}", "PICKUP / INBOUND", config.pickupStationPositions[i], new Color(0.08f, 0.40f, 0.76f));
            }

            string[] categories = { "Electronics", "Apparel", "Healthcare" };
            for (int i = 0; i < config.dropStationPositions.Count; i++)
            {
                string category = i < categories.Length ? categories[i] : "Putaway";
                CreateTaskStation($"D{i + 1}", $"DROP / {category}", config.dropStationPositions[i], new Color(0.10f, 0.62f, 0.30f));
            }
        }

        private void CreateTaskStation(string id, string title, Vector3 position, Color colour)
        {
            var station = new GameObject($"Station_{id}_{title.Replace(' ', '_')}").transform;
            station.SetParent(_root, false);
            station.localPosition = position;

            var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pad.name = "Robot approach pad";
            pad.transform.SetParent(station, false);
            pad.transform.localPosition = new Vector3(0f, 0.025f, 0f);
            pad.transform.localScale = new Vector3(2.25f, 0.05f, 1.65f);
            pad.GetComponent<Renderer>().sharedMaterial = SetMaterialColor(null, colour);
            pad.GetComponent<Collider>().enabled = false;

            var shelf = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shelf.name = "Parcel staging shelf";
            shelf.transform.SetParent(station, false);
            // Pickup stock waits behind the approach pad, toward the nearest
            // perimeter wall. The robot can reach it with the arm without
            // driving its LiDAR/camera directly into the carton.
            Vector3 shelfOffset = GetStationRearOffset(position, 0.95f);
            shelf.transform.localPosition = shelfOffset + Vector3.up * 0.42f;
            shelf.transform.localScale = new Vector3(0.78f, 0.78f, 0.70f);
            shelf.GetComponent<Renderer>().sharedMaterial = SetMaterialColor(null, new Color(0.24f, 0.27f, 0.30f));
            // The shelf is presentation only. The spawned parcel retains its
            // own collider, so workers/vehicles still avoid the parcel while
            // the station furniture cannot stop the robot before its task
            // state machine reaches the pickup point.
            shelf.GetComponent<Collider>().enabled = false;

            // A freestanding identification board replaces the former
            // tilted, floating lettering. It faces the warehouse interior so
            // the robot camera and operators see the text the right way round.
            Vector3 outward = new Vector3(position.x, 0f, position.z);
            if (outward.sqrMagnitude < 0.01f) outward = Vector3.right;
            outward.Normalize();
            Vector3 signOffset = outward * 1.30f;
            CreateVisualBox(station, "Station sign post", signOffset + Vector3.up * 0.68f,
                new Vector3(0.075f, 1.36f, 0.075f), _structuralSteelMaterial, true);
            CreateIndustrialSign(station, "Station identification sign",
                signOffset + Vector3.up * 1.43f, Quaternion.LookRotation(outward, Vector3.up),
                new Vector2(1.82f, 0.62f), $"{id}\n{title}",
                _informationBlueMaterial, Color.white, 0.105f);
        }

        public static Vector3 GetStationRearOffset(Vector3 stationPosition, float distance)
        {
            Vector3 outward = new Vector3(stationPosition.x, 0f, stationPosition.z);
            if (outward.sqrMagnitude < 0.01f) outward = Vector3.right;
            return outward.normalized * Mathf.Max(0f, distance);
        }

        private void BuildChargingStation()
        {
            var station = new GameObject("ChargingStation").transform;
            station.SetParent(_root, false);
            station.localPosition = config.chargingStationPosition;

            var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pad.name = "Charging Pad";
            pad.transform.SetParent(station, false);
            pad.transform.localPosition = new Vector3(0f, 0.025f, 0f);
            pad.transform.localScale = new Vector3(2.4f, 0.05f, 1.8f);
            pad.GetComponent<Renderer>().sharedMaterial = SetMaterialColor(null, new Color(0.08f, 0.34f, 0.22f));
            pad.GetComponent<Collider>().enabled = false;

            var post = GameObject.CreatePrimitive(PrimitiveType.Cube);
            post.name = "Charging Cabinet";
            post.transform.SetParent(station, false);
            post.transform.localPosition = new Vector3(-0.9f, 0.70f, -0.55f);
            post.transform.localScale = new Vector3(0.30f, 1.35f, 0.28f);
            post.GetComponent<Renderer>().sharedMaterial = SetMaterialColor(null, new Color(0.16f, 0.18f, 0.20f));
            EnvironmentCollisionTag.Attach(post, EnvironmentCollisionTag.Kind.DynamicObstacle);

            var indicator = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            indicator.name = "Charging Indicator";
            indicator.transform.SetParent(station, false);
            indicator.transform.localPosition = new Vector3(-0.9f, 1.08f, -0.71f);
            indicator.transform.localScale = Vector3.one * 0.13f;
            indicator.GetComponent<Renderer>().sharedMaterial = SetMaterialColor(null, new Color(0.15f, 0.95f, 0.35f));
            indicator.GetComponent<Collider>().enabled = false;
        }

        /// <summary>
        /// Builds visual, non-colliding safety-boundary strips using
        /// config.safetyZoneSize: a border around the loading zone, and a
        /// perimeter strip just inside the outer walls. Purely a floor
        /// marking layer — it never blocks NavMesh baking or robot motion.
        /// </summary>
        private void BuildSafetyZones()
        {
            var safetyRoot = new GameObject("SafetyZones").transform;
            safetyRoot.SetParent(_root, false);

            float stripWidth = Mathf.Max(0.05f, config.safetyZoneSize.x);

            // Border strip around the loading zone.
            Vector3 loadingCenter = new Vector3(
                config.warehouseLength / 2f - config.loadingZoneSize.x / 2f - 1f,
                0.025f,
                config.warehouseWidth / 2f - config.loadingZoneSize.y / 2f - 1f);

            CreateSafetyStrip(safetyRoot, "SafetyBorder_LoadingZone_N",
                loadingCenter + new Vector3(0, 0, config.loadingZoneSize.y / 2f + stripWidth / 2f),
                new Vector3(config.loadingZoneSize.x + stripWidth * 2f, stripWidth));
            CreateSafetyStrip(safetyRoot, "SafetyBorder_LoadingZone_S",
                loadingCenter - new Vector3(0, 0, config.loadingZoneSize.y / 2f + stripWidth / 2f),
                new Vector3(config.loadingZoneSize.x + stripWidth * 2f, stripWidth));
            CreateSafetyStrip(safetyRoot, "SafetyBorder_LoadingZone_E",
                loadingCenter + new Vector3(config.loadingZoneSize.x / 2f + stripWidth / 2f, 0, 0),
                new Vector3(stripWidth, config.loadingZoneSize.y));
            CreateSafetyStrip(safetyRoot, "SafetyBorder_LoadingZone_W",
                loadingCenter - new Vector3(config.loadingZoneSize.x / 2f + stripWidth / 2f, 0, 0),
                new Vector3(stripWidth, config.loadingZoneSize.y));

            // Perimeter safety strip just inside each outer wall.
            float inset = 0.5f;
            CreateSafetyStrip(safetyRoot, "SafetyPerimeter_North",
                new Vector3(0, 0.025f, config.warehouseWidth / 2f - inset - stripWidth / 2f),
                new Vector3(config.warehouseLength - inset * 2f, stripWidth));
            CreateSafetyStrip(safetyRoot, "SafetyPerimeter_South",
                new Vector3(0, 0.025f, -config.warehouseWidth / 2f + inset + stripWidth / 2f),
                new Vector3(config.warehouseLength - inset * 2f, stripWidth));
            CreateSafetyStrip(safetyRoot, "SafetyPerimeter_East",
                new Vector3(config.warehouseLength / 2f - inset - stripWidth / 2f, 0.025f, 0),
                new Vector3(stripWidth, config.warehouseWidth - inset * 2f));
            CreateSafetyStrip(safetyRoot, "SafetyPerimeter_West",
                new Vector3(-config.warehouseLength / 2f + inset + stripWidth / 2f, 0.025f, 0),
                new Vector3(stripWidth, config.warehouseWidth - inset * 2f));
        }

        private void BuildSafetyEquipmentAndSigns()
        {
            var details = new GameObject("SafetyEquipmentAndWayfinding").transform;
            details.SetParent(_root, false);

            float halfLength = config.warehouseLength * 0.5f;
            float halfWidth = config.warehouseWidth * 0.5f;
            float signY = Mathf.Clamp(config.warehouseHeight * 0.68f, 3.8f, 5.8f);
            Material exitGreen = CreateIndustrialMaterial(new Color(0.035f, 0.39f, 0.19f), 0f, 0.24f);
            Material informationBlue = _informationBlueMaterial;

            // The main identity and emergency information are mounted on
            // opposing walls so they remain legible from both camera views.
            CreateIndustrialSign(details, "Warehouse identity sign",
                new Vector3(0f, signY, halfWidth - 0.205f), Quaternion.identity,
                new Vector2(Mathf.Min(8.4f, config.warehouseLength * 0.24f), 0.78f),
                "ATADTRL AUTONOMOUS FULFILMENT WAREHOUSE", informationBlue, Color.white, 0.16f);
            CreateIndustrialSign(details, "Emergency exit south",
                new Vector3(0f, Mathf.Min(3.2f, config.warehouseHeight - 0.8f), -halfWidth + 0.205f),
                Quaternion.Euler(0f, 180f, 0f), new Vector2(2.9f, 0.62f),
                "EMERGENCY EXIT  →", exitGreen, Color.white, 0.19f);
            CreateIndustrialSign(details, "Emergency exit west",
                new Vector3(-halfLength + 0.205f, Mathf.Min(3.2f, config.warehouseHeight - 0.8f), 0f),
                Quaternion.Euler(0f, -90f, 0f), new Vector2(2.9f, 0.62f),
                "←  EMERGENCY EXIT", exitGreen, Color.white, 0.19f);

            // Number every longitudinal vehicle aisle. These signs are
            // suspended at the aisle mouth, outside the actors' height.
            float rackAreaWidth = config.rackColumns * config.rackWidth +
                                  Mathf.Max(0, config.rackColumns - 1) * config.aisleWidth;
            float rackStartX = -rackAreaWidth * 0.5f + config.rackWidth * 0.5f;
            for (int gap = 0; gap < config.rackColumns - 1; gap++)
            {
                float rackX = rackStartX + gap * (config.rackWidth + config.aisleWidth);
                float aisleX = rackX + config.rackWidth * 0.5f + config.aisleWidth * 0.5f;
                CreateIndustrialSign(details, $"Aisle {gap + 1:00} marker",
                    new Vector3(aisleX, Mathf.Clamp(config.rackHeight + 0.75f, 4.5f, config.warehouseHeight - 0.7f),
                        -halfWidth + 0.42f),
                    Quaternion.Euler(0f, 180f, 0f), new Vector2(1.30f, 0.56f),
                    $"AISLE {gap + 1:00}", informationBlue, Color.white, 0.18f);
            }

            BuildFireSafetyPoint(details, "Fire point west", new Vector3(-halfLength + 0.34f, 0f, -halfWidth * 0.30f),
                Quaternion.Euler(0f, -90f, 0f));
            BuildFireSafetyPoint(details, "Fire point east", new Vector3(halfLength - 0.34f, 0f, halfWidth * 0.28f),
                Quaternion.Euler(0f, 90f, 0f));

            // Low guard rails visually protect the loading zone corners.
            Vector3 loadingCenter = new Vector3(
                halfLength - config.loadingZoneSize.x * 0.5f - 1f,
                0f,
                halfWidth - config.loadingZoneSize.y * 0.5f - 1f);
            float railX = loadingCenter.x - config.loadingZoneSize.x * 0.5f - 0.28f;
            float railZ = loadingCenter.z - config.loadingZoneSize.y * 0.5f;
            CreateGuardRail(details, new Vector3(railX, 0f, railZ), Mathf.Min(4.2f, config.loadingZoneSize.y * 0.58f), true);

            // Wall-mounted CCTV bodies support the sensor-rich research
            // setting without creating physics objects in the environment.
            BuildCctvCamera(details, "CCTV north-west", new Vector3(-halfLength + 0.55f, signY, halfWidth - 0.50f), 135f);
            BuildCctvCamera(details, "CCTV south-east", new Vector3(halfLength - 0.55f, signY, -halfWidth + 0.50f), -45f);
        }

        private void BuildFireSafetyPoint(Transform parent, string name, Vector3 localPosition, Quaternion localRotation)
        {
            Transform point = new GameObject(name).transform;
            point.SetParent(parent, false);
            point.localPosition = localPosition;
            point.localRotation = localRotation;

            CreateIndustrialSign(point, "Fire point sign", new Vector3(0f, 1.62f, -0.06f), Quaternion.identity,
                new Vector2(0.88f, 0.48f), "FIRE\nPOINT", _fireEquipmentMaterial, Color.white, 0.15f);
            CreateVisualBox(point, "Extinguisher wall bracket", new Vector3(0f, 0.83f, -0.08f),
                new Vector3(0.38f, 0.76f, 0.11f), _rubberMaterial, false);
            GameObject body = CreateVisualCylinder(point, "Fire extinguisher", new Vector3(0f, 0.82f, -0.19f),
                new Vector3(0.17f, 0.34f, 0.17f), _fireEquipmentMaterial, true);
            body.transform.localRotation = Quaternion.identity;
            CreateVisualBox(point, "Extinguisher handle", new Vector3(0f, 1.19f, -0.19f),
                new Vector3(0.18f, 0.07f, 0.07f), _rubberMaterial, true);
        }

        private void CreateGuardRail(Transform parent, Vector3 localPosition, float length, bool alongZ)
        {
            Transform rail = new GameObject("Loading-zone guard rail").transform;
            rail.SetParent(parent, false);
            rail.localPosition = localPosition;
            Vector3 longitudinal = alongZ ? new Vector3(0.12f, 0.12f, length) : new Vector3(length, 0.12f, 0.12f);
            CreateVisualBox(rail, "Upper safety rail", new Vector3(0f, 0.92f, 0f), longitudinal, _safetyYellowMaterial, true);
            CreateVisualBox(rail, "Lower safety rail", new Vector3(0f, 0.48f, 0f), longitudinal, _safetyYellowMaterial, true);
            for (int end = -1; end <= 1; end += 2)
            {
                Vector3 position = alongZ
                    ? new Vector3(0f, 0.52f, end * length * 0.5f)
                    : new Vector3(end * length * 0.5f, 0.52f, 0f);
                CreateVisualBox(rail, "Guard rail post", position, new Vector3(0.15f, 1.04f, 0.15f),
                    _safetyYellowMaterial, true);
            }
        }

        private void BuildCctvCamera(Transform parent, string name, Vector3 localPosition, float yaw)
        {
            Transform camera = new GameObject(name).transform;
            camera.SetParent(parent, false);
            camera.localPosition = localPosition;
            camera.localRotation = Quaternion.Euler(18f, yaw, 0f);
            CreateVisualBox(camera, "Camera wall arm", new Vector3(0f, 0f, 0.18f),
                new Vector3(0.10f, 0.10f, 0.48f), _galvanisedMaterial, true);
            CreateVisualBox(camera, "Camera enclosure", new Vector3(0f, -0.04f, 0.52f),
                new Vector3(0.31f, 0.24f, 0.54f), _lightHousingMaterial, true);
            GameObject lens = CreateVisualCylinder(camera, "Camera lens", new Vector3(0f, -0.04f, 0.82f),
                new Vector3(0.10f, 0.035f, 0.10f), _rubberMaterial, false);
            lens.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }

        private static GameObject CreateVisualCylinder(Transform parent, string name, Vector3 localPosition,
            Vector3 localScale, Material material, bool castShadows)
        {
            GameObject cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cylinder.name = name;
            cylinder.transform.SetParent(parent, false);
            cylinder.transform.localPosition = localPosition;
            cylinder.transform.localScale = localScale;
            var collider = cylinder.GetComponent<Collider>();
            if (collider != null) collider.enabled = false;
            var renderer = cylinder.GetComponent<Renderer>();
            if (material != null) renderer.sharedMaterial = material;
            renderer.shadowCastingMode = castShadows
                ? UnityEngine.Rendering.ShadowCastingMode.On
                : UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = castShadows;
            return cylinder;
        }

        private void CreateSafetyStrip(Transform parent, string name, Vector3 localPos, Vector2 sizeXZ)
        {
            GameObject strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            strip.name = name;
            strip.transform.SetParent(parent, false);
            strip.transform.localScale = new Vector3(Mathf.Max(0.05f, sizeXZ.x), 0.03f, Mathf.Max(0.05f, sizeXZ.y));
            strip.transform.localPosition = localPos;

            if (safetyZoneMaterial != null)
            {
                strip.GetComponent<Renderer>().sharedMaterial = safetyZoneMaterial;
            }
            else
            {
                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Diffuse"));
                mat.color = new Color(1f, 0.85f, 0f, 1f); // hazard yellow
                strip.GetComponent<Renderer>().sharedMaterial = mat;
            }

            // Visual-only: never blocks navigation or collides with the robot.
            var col = strip.GetComponent<Collider>();
            if (col != null) col.enabled = false;
        }

        /// <summary>Returns the aisle bounds for a given index, used for temporary blockage scenarios.</summary>
        public Bounds GetAisleBounds(int index)
        {
            if (index < 0 || index >= AisleBounds.Count) return new Bounds(Vector3.zero, Vector3.zero);
            return AisleBounds[index];
        }

        private void GameObjectUtility_MarkStatic(GameObject go)
        {
#if UNITY_EDITOR
#pragma warning disable 0618
            UnityEditor.GameObjectUtility.SetStaticEditorFlags(go, UnityEditor.StaticEditorFlags.NavigationStatic);
            foreach (Transform child in go.GetComponentsInChildren<Transform>(true))
            {
                UnityEditor.GameObjectUtility.SetStaticEditorFlags(child.gameObject, UnityEditor.StaticEditorFlags.NavigationStatic);
            }
#pragma warning restore 0618
#endif
        }

        private void Reset()
        {
            config.goalAreas = new List<Vector3>
            {
                new Vector3(config.warehouseLength * 0.35f, 0, config.warehouseWidth * 0.3f),
                new Vector3(-config.warehouseLength * 0.35f, 0, config.warehouseWidth * 0.3f),
                new Vector3(config.warehouseLength * 0.35f, 0, -config.warehouseWidth * 0.3f)
            };
        }
    }
}
