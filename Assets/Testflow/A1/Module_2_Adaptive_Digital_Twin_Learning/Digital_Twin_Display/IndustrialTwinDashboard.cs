using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

using ATADTRL.Core;
using ATADTRL.Module2;
using ATADTRL.Pipeline;

namespace ATADTRL.UI
{
    public class IndustrialTwinDashboard : MonoBehaviour
    {
        // =============================================================
        // BACKEND
        // =============================================================

        private Module2Manager module2;
        private ATADTRLPipelineManager pipeline;

        // =============================================================
        // ROOT
        // =============================================================

        private Canvas canvas;
        private RectTransform root;

        // =============================================================
        // TOP BAR
        // =============================================================

        private TMP_Text titleText;
        private TMP_Text scenarioText;
        private TMP_Text runtimeText;
        private TMP_Text liveText;

        // =============================================================
        // KPI CARDS
        // =============================================================

        private TMP_Text changeValue;
        private TMP_Text twinAgeValue;
        private TMP_Text lastSyncValue;
        private TMP_Text syncCountValue;
        private TMP_Text edatsStatusValue;

        // =============================================================
        // MAIN SECTIONS
        // =============================================================

        private TMP_Text comparisonText;
        private TMP_Text actorHealthText;
        private TMP_Text missionText;
        private TMP_Text eventText;
        private RectTransform spatialMap;
        private readonly Dictionary<string, RectTransform> spatialMarkers = new Dictionary<string, RectTransform>();

        // =============================================================
        // PIPELINE
        // =============================================================

        private TMP_Text observationStage;
        private TMP_Text changeStage;
        private TMP_Text edatsStage;
        private TMP_Text twinStage;
        private TMP_Text contextStage;
        private TMP_Text ppoStage;

        private Image lineObservationChange;
        private Image lineChangeEdats;
        private Image lineEdatsTwin;
        private Image lineTwinContext;
        private Image lineContextPpo;

        // =============================================================
        // SYNC PULSE
        // =============================================================

        private Image syncPulseOuter;
        private Image syncPulseInner;

        private RectTransform majorUpdateBanner;
        private TMP_Text majorUpdateBannerText;
        private Module2Manager subscribedModule2;

        private int previousSyncCount = -1;
        private float pulseStartTime = -100f;

        // =============================================================
        // COLOURS
        // =============================================================

        private readonly Color background =
            Hex("#050C13");

        private readonly Color panel =
            Hex("#0A1520");

        private readonly Color panel2 =
            Hex("#0D1B28");

        private readonly Color border =
            Hex("#173244");

        private readonly Color cyan =
            Hex("#00D9FF");

        private readonly Color blue =
            Hex("#168BFF");

        private readonly Color green =
            Hex("#24E59A");

        private readonly Color amber =
            Hex("#FFC15A");

        private readonly Color red =
            Hex("#FF5364");

        private readonly Color text =
            Hex("#E8F4FB");

        private readonly Color muted =
            Hex("#7695A8");

        // =============================================================
        // INITIALIZATION
        // =============================================================

        private void Awake()
        {
            FindBackend();
            BuildDashboard();
        }

        private void Start()
        {
            FindBackend();
            AttachMajorUpdateEvent();
        }

        private void OnEnable()
        {
            FindBackend();
            AttachMajorUpdateEvent();
        }

        private void OnDisable()
        {
            DetachMajorUpdateEvent();
        }

        private void FindBackend()
        {
            if (module2 == null)
                module2 =
                    FindAnyObjectByType<Module2Manager>();
            if (pipeline == null)
                pipeline = FindAnyObjectByType<ATADTRLPipelineManager>();

            AttachMajorUpdateEvent();
        }

        private void AttachMajorUpdateEvent()
        {
            if (module2 == null || subscribedModule2 == module2)
                return;

            DetachMajorUpdateEvent();
            subscribedModule2 = module2;
            subscribedModule2.MajorUpdateRegistered += HandleMajorUpdateRegistered;
        }

        private void DetachMajorUpdateEvent()
        {
            if (subscribedModule2 != null)
                subscribedModule2.MajorUpdateRegistered -= HandleMajorUpdateRegistered;

            subscribedModule2 = null;
        }

        private void HandleMajorUpdateRegistered()
        {
            ApplyMajorUpdatePresentation();
            Debug.Log("ATADTRL DISPLAY 2: MAJOR UPDATE rendered for A1 human collision.");
        }

        // =============================================================
        // UPDATE
        // =============================================================

        private void Update()
        {
            if (module2 == null)
            {
                FindBackend();
                return;
            }

            UpdateHeader();
            UpdateKPIs();
            UpdateComparison();
            UpdateActorHealth();
            UpdateMission();
            UpdatePipeline();
            UpdateSyncPulse();
            UpdateEventPanel();

            if (module2.MajorUpdateLatched)
                ApplyMajorUpdatePresentation();
            else if (majorUpdateBanner != null && majorUpdateBanner.gameObject.activeSelf)
                majorUpdateBanner.gameObject.SetActive(false);
        }

        // =============================================================
        // BUILD DASHBOARD
        // =============================================================

        private void BuildDashboard()
        {
            GameObject existingCanvas =
                GameObject.Find("MonitorCanvas");

            if (existingCanvas == null)
            {
                Debug.LogError(
                    "[IndustrialDashboard] MonitorCanvas not found.");
                return;
            }

            canvas =
                existingCanvas.GetComponent<Canvas>();

            canvas.renderMode =
                RenderMode.ScreenSpaceOverlay;

            canvas.targetDisplay = 1;
            canvas.sortingOrder = 500;

            CanvasScaler scaler =
                canvas.GetComponent<CanvasScaler>();

            if (scaler == null)
                scaler =
                    canvas.gameObject.AddComponent<CanvasScaler>();

            scaler.uiScaleMode =
                CanvasScaler.ScaleMode.ScaleWithScreenSize;

            scaler.referenceResolution =
                new Vector2(1920, 1080);

            scaler.screenMatchMode =
                CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;

            scaler.matchWidthOrHeight = 0.5f;

            // Hide previous dashboard hierarchy.
            Transform old =
                canvas.transform.Find("BackgroundPanel");

            if (old != null)
                old.gameObject.SetActive(false);

            Transform previous =
                canvas.transform.Find("IndustrialDashboardRoot");

            if (previous != null)
                Destroy(previous.gameObject);

            GameObject rootObject =
                CreateUIObject(
                    "IndustrialDashboardRoot",
                    canvas.transform);

            root =
                rootObject.GetComponent<RectTransform>();

            Stretch(root);

            Image rootImage =
                rootObject.AddComponent<Image>();

            rootImage.color = background;

            BuildHeader();
            BuildKPIStrip();
            BuildMainPanels();
            BuildPipeline();
            BuildMajorUpdateBanner();
        }

        private void BuildMajorUpdateBanner()
        {
            majorUpdateBanner = CreatePanel(
                root,
                "A1MajorUpdateBanner",
                new Vector2(0.19f, 0.675f),
                new Vector2(0.81f, 0.775f),
                Hex("#270910"));

            Image image = majorUpdateBanner.GetComponent<Image>();
            image.color = Hex("#270910");

            Outline outline = majorUpdateBanner.gameObject.AddComponent<Outline>();
            outline.effectColor = red;
            outline.effectDistance = new Vector2(3f, -3f);

            majorUpdateBannerText = CreateText(
                majorUpdateBanner,
                "MajorUpdateText",
                new Vector2(0.025f, 0.08f),
                new Vector2(0.975f, 0.92f),
                19,
                text,
                FontStyles.Bold);
            majorUpdateBannerText.alignment = TextAlignmentOptions.Center;
            majorUpdateBanner.gameObject.SetActive(false);
        }

        private void ApplyMajorUpdatePresentation()
        {
            if (module2 == null || !module2.MajorUpdateLatched)
                return;

            // Update immediately from the collision event, not one frame later.
            UpdateHeader();
            UpdateKPIs();
            UpdateMission();
            UpdateEventPanel();
            UpdatePipeline();

            if (majorUpdateBanner != null && majorUpdateBannerText != null)
            {
                majorUpdateBannerText.text =
                    "<color=#FF5364><b>MAJOR UPDATE — A1 HUMAN COLLISION</b></color>\n" +
                    "Change 1.000  •  EDATS FORCED SYNC  •  TWIN UPDATED  •  TRUST VALIDATED  •  SAFE ACTION GENERATED";
                majorUpdateBanner.gameObject.SetActive(true);
                majorUpdateBanner.SetAsLastSibling();
            }
        }

        // =============================================================
        // HEADER
        // =============================================================

        private void BuildHeader()
        {
            RectTransform header =
                CreatePanel(
                    root,
                    "CommandHeader",
                    new Vector2(0.018f, 0.905f),
                    new Vector2(0.982f, 0.985f),
                    background);

            titleText =
                CreateText(
                    header,
                    "Title",
                    new Vector2(0.015f, 0.05f),
                    new Vector2(0.46f, 0.95f),
                    30,
                    text,
                    FontStyles.Bold);

            titleText.text =
                "ATADTRL  /  DIGITAL TWIN OPERATIONS CENTER";

            scenarioText =
                CreateText(
                    header,
                    "Scenario",
                    new Vector2(0.48f, 0.05f),
                    new Vector2(0.72f, 0.95f),
                    17,
                    cyan,
                    FontStyles.Bold);

            scenarioText.alignment =
                TextAlignmentOptions.MidlineRight;

            runtimeText =
                CreateText(
                    header,
                    "Runtime",
                    new Vector2(0.74f, 0.05f),
                    new Vector2(0.87f, 0.95f),
                    17,
                    text,
                    FontStyles.Bold);

            runtimeText.alignment =
                TextAlignmentOptions.MidlineRight;

            liveText =
                CreateText(
                    header,
                    "Live",
                    new Vector2(0.88f, 0.05f),
                    new Vector2(0.985f, 0.95f),
                    17,
                    green,
                    FontStyles.Bold);

            liveText.alignment =
                TextAlignmentOptions.MidlineRight;
        }

        // =============================================================
        // KPI STRIP
        // =============================================================

        private void BuildKPIStrip()
        {
            float left = 0.018f;
            float right = 0.982f;

            float gap = 0.008f;
            float width =
                (right - left - gap * 4f) / 5f;

            changeValue =
                BuildMetricCard(
                    "CHANGE SCORE",
                    left,
                    width,
                    0);

            twinAgeValue =
                BuildMetricCard(
                    "TWIN AGE",
                    left + (width + gap),
                    width,
                    1);

            lastSyncValue =
                BuildMetricCard(
                    "LAST SYNC",
                    left + 2f * (width + gap),
                    width,
                    2);

            syncCountValue =
                BuildMetricCard(
                    "SYNC COUNT",
                    left + 3f * (width + gap),
                    width,
                    3);

            edatsStatusValue =
                BuildMetricCard(
                    "EDATS STATE",
                    left + 4f * (width + gap),
                    width,
                    4);
        }

        private TMP_Text BuildMetricCard(
            string title,
            float x,
            float width,
            int index)
        {
            RectTransform card =
                CreatePanel(
                    root,
                    "Metric_" + index,
                    new Vector2(x, 0.785f),
                    new Vector2(x + width, 0.885f),
                    panel);

            AddTopAccent(card);

            TMP_Text label =
                CreateText(
                    card,
                    "Label",
                    new Vector2(0.06f, 0.55f),
                    new Vector2(0.94f, 0.90f),
                    13,
                    muted,
                    FontStyles.Bold);

            label.text = title;

            TMP_Text value =
                CreateText(
                    card,
                    "Value",
                    new Vector2(0.06f, 0.08f),
                    new Vector2(0.94f, 0.60f),
                    25,
                    text,
                    FontStyles.Bold);

            value.text = "--";

            return value;
        }

        // =============================================================
        // MAIN PANELS
        // =============================================================

        private void BuildMainPanels()
        {
            // Twin vs Observation
            RectTransform comparison =
                CreatePanel(
                    root,
                    "TwinObservationPanel",
                    new Vector2(0.018f, 0.39f),
                    new Vector2(0.505f, 0.765f),
                    panel);

            AddSectionHeader(
                comparison,
                "TWIN  ↔  OBSERVATION",
                "REAL-TIME STATE DEVIATION");

            comparisonText =
                CreateText(
                    comparison,
                    "Comparison",
                    new Vector2(0.04f, 0.06f),
                    new Vector2(0.59f, 0.80f),
                    15,
                    text,
                    FontStyles.Normal);

            BuildSpatialTwin(comparison);

            // Timeline / event
            RectTransform eventPanel =
                CreatePanel(
                    root,
                    "SynchronizationPanel",
                    new Vector2(0.515f, 0.39f),
                    new Vector2(0.982f, 0.765f),
                    panel);

            AddSectionHeader(
                eventPanel,
                "EDATS EVENT STREAM",
                "EVENT-DRIVEN SYNCHRONIZATION");

            BuildPulse(eventPanel);

            eventText =
                CreateText(
                    eventPanel,
                    "EventData",
                    new Vector2(0.35f, 0.08f),
                    new Vector2(0.96f, 0.78f),
                    17,
                    text,
                    FontStyles.Normal);

            // Actor health
            RectTransform actors =
                CreatePanel(
                    root,
                    "ActorHealthPanel",
                    new Vector2(0.018f, 0.085f),
                    new Vector2(0.505f, 0.37f),
                    panel);

            AddSectionHeader(
                actors,
                "ACTOR / LINK HEALTH",
                "OPERATIONAL TELEMETRY");

            actorHealthText =
                CreateText(
                    actors,
                    "ActorHealth",
                    new Vector2(0.04f, 0.06f),
                    new Vector2(0.96f, 0.78f),
                    16,
                    text,
                    FontStyles.Normal);

            // Mission
            RectTransform mission =
                CreatePanel(
                    root,
                    "MissionPanel",
                    new Vector2(0.515f, 0.085f),
                    new Vector2(0.982f, 0.37f),
                    panel);

            AddSectionHeader(
                mission,
                "ACTIVE MISSION",
                "WAREHOUSE EXECUTION STATE");

            missionText =
                CreateText(
                    mission,
                    "Mission",
                    new Vector2(0.04f, 0.06f),
                    new Vector2(0.96f, 0.78f),
                    15,
                    text,
                    FontStyles.Normal);
        }

        // =============================================================
        // PIPELINE
        // =============================================================

        private void BuildPipeline()
        {
            RectTransform pipeline =
                CreatePanel(
                    root,
                    "Pipeline",
                    new Vector2(0.018f, 0.015f),
                    new Vector2(0.982f, 0.067f),
                    panel2);

            observationStage =
                CreateStage(
                    pipeline,
                    "OBSERVATION",
                    0.02f);

            changeStage =
                CreateStage(
                    pipeline,
                    "CHANGE DETECTION",
                    0.18f);

            edatsStage =
                CreateStage(
                    pipeline,
                    "EDATS",
                    0.37f);

            twinStage =
                CreateStage(
                    pipeline,
                    "ADAPTIVE TWIN",
                    0.51f);

            contextStage =
                CreateStage(
                    pipeline,
                    "CONTEXT + TRUST",
                    0.70f);

            ppoStage =
                CreateStage(
                    pipeline,
                    "SECURE POLICY",
                    0.88f);

            lineObservationChange =
                CreatePipelineLine(
                    pipeline,
                    0.145f,
                    0.18f);

            lineChangeEdats =
                CreatePipelineLine(
                    pipeline,
                    0.335f,
                    0.37f);

            lineEdatsTwin =
                CreatePipelineLine(
                    pipeline,
                    0.475f,
                    0.51f);

            lineTwinContext =
                CreatePipelineLine(
                    pipeline,
                    0.665f,
                    0.70f);

            lineContextPpo =
                CreatePipelineLine(
                    pipeline,
                    0.845f,
                    0.88f);
        }

        // =============================================================
        // UPDATE HEADER
        // =============================================================

        private void UpdateHeader()
        {
            int id =
                module2.CurrentScenarioId;

            scenarioText.text =
                module2.IsIdleLiveTwin
                    ? "LIVE WAREHOUSE  /  NO EPISODE"
                    : id > 0
                    ? $"{ScenarioCode(id)}  /  {ScenarioName(id)}"
                    : "NO ACTIVE SCENARIO";

            TimeSpan time =
                TimeSpan.FromSeconds(
                    Mathf.Max(
                        0,
                        module2.ScenarioRuntime));

            runtimeText.text =
                $"RUNTIME  {time:mm\\:ss\\.ff}";

            float a =
                0.55f +
                0.45f *
                Mathf.Abs(
                    Mathf.Sin(
                        Time.unscaledTime * 3f));

            bool major = module2.MajorUpdateLatched;
            bool monitoring = module2.IsMonitoring;

            Color live =
                major ? red : monitoring ? green : amber;

            live.a = a;

            liveText.color = live;
            liveText.text = major
                ? "●  LIVE / MAJOR EVENT"
                : monitoring
                    ? module2.DataLoggingActive
                        ? "●  LIVE / RECORDING"
                        : "●  LIVE / NO LOGGING"
                    : "●  WAITING FOR DATA";
        }

        // =============================================================
        // UPDATE KPIs
        // =============================================================

        private void UpdateKPIs()
        {
            float score =
                module2.CurrentChangeScore;

            changeValue.text =
                module2.MajorUpdateLatched
                    ? score.ToString("F3") + "  MAJOR"
                    : score.ToString("F3");

            float threshold =
                module2.edatsConfig != null
                    ? module2.edatsConfig.overallChangeThreshold
                    : 0.15f;

            changeValue.color =
                module2.MajorUpdateLatched
                    ? red
                    : score > threshold
                    ? amber
                    : text;

            TwinWorldStateRecord twin =
                module2.CurrentTwin;

            twinAgeValue.text =
                twin != null
                    ? twin.state_age.ToString("F3") + " s"
                    : "--";

            lastSyncValue.text =
                twin != null
                    ? twin.last_sync_timestamp.ToString("F2") + " s"
                    : "--";

            syncCountValue.text =
                module2.SynchronizationCount.ToString();

            if (module2.MajorUpdateLatched)
            {
                edatsStatusValue.text =
                    "MAJOR UPDATE";

                edatsStatusValue.color = red;
            }
            else if (module2.IsIdleLiveTwin)
            {
                edatsStatusValue.text = "CONNECTED";
                edatsStatusValue.color = green;
            }
            else if (module2.CriticalEvent)
            {
                edatsStatusValue.text =
                    "CRITICAL SYNC";

                edatsStatusValue.color = red;
            }
            else if (
                module2.SynchronizationTriggered)
            {
                edatsStatusValue.text =
                    "SYNCHRONIZED";

                edatsStatusValue.color = green;
            }
            else
            {
                edatsStatusValue.text =
                    "TWIN MAINTAINED";

                edatsStatusValue.color = cyan;
            }
        }

        // =============================================================
        // TWIN VS OBSERVATION
        // =============================================================

        private void UpdateComparison()
        {
            ObservationRecord obs =
                module2.LatestObservation;

            TwinWorldStateRecord twin =
                module2.CurrentTwin;

            if (obs == null || twin == null)
            {
                comparisonText.text =
                    "Waiting for observation and digital twin...";
                return;
            }

            float dx =
                Mathf.Abs(
                    obs.estimated_x -
                    twin.twin_robot_x);

            float dy =
                Mathf.Abs(
                    obs.estimated_y -
                    twin.twin_robot_y);

            float dyaw =
                Mathf.Abs(
                    Mathf.DeltaAngle(
                        obs.estimated_yaw,
                        twin.twin_robot_yaw));

            float dv =
                Mathf.Abs(
                    obs.estimated_velocity -
                    twin.twin_robot_velocity);

            comparisonText.text =
                "<color=#7695A8>" +
                "STATE                  OBSERVATION        TWIN              Δ" +
                "</color>\n\n" +

                Row(
                    "Robot X",
                    obs.estimated_x,
                    twin.twin_robot_x,
                    dx,
                    "m") +

                Row(
                    "Robot Y",
                    obs.estimated_y,
                    twin.twin_robot_y,
                    dy,
                    "m") +

                Row(
                    "Yaw",
                    obs.estimated_yaw,
                    twin.twin_robot_yaw,
                    dyaw,
                    "°") +

                Row(
                    "Velocity",
                    obs.estimated_velocity,
                    twin.twin_robot_velocity,
                    dv,
                    "m/s") +

                "\n" +
                $"<color=#7695A8>Observation Step</color>   {obs.stepId}\n" +
                $"<color=#7695A8>Twin Step</color>          {twin.stepId}\n" +
                $"<color=#7695A8>Twin Age</color>           {twin.state_age:F3} s";

            UpdateSpatialTwin(twin);
        }

        private void BuildSpatialTwin(RectTransform parent)
        {
            spatialMap = CreatePanel(parent, "LiveSpatialTwin",
                new Vector2(0.62f, 0.08f), new Vector2(0.96f, 0.78f), panel2);
            TMP_Text title = CreateText(spatialMap, "MapTitle", new Vector2(0.04f, 0.84f),
                new Vector2(0.96f, 0.97f), 12, cyan, FontStyles.Bold);
            title.text = "LIVE SPATIAL TWIN  /  TOP VIEW";
            foreach (string id in new[] { "AMR", "H1", "H2", "H3", "H4", "H5", "F1", "F2" })
            {
                TMP_Text marker = CreateText(spatialMap, "Marker_" + id,
                    new Vector2(0.45f, 0.42f), new Vector2(0.55f, 0.56f),
                    id == "AMR" ? 13 : 11,
                    id == "AMR" ? cyan : (id.StartsWith("F") ? amber : green), FontStyles.Bold);
                marker.text = "●" + id;
                marker.alignment = TextAlignmentOptions.Center;
                marker.textWrappingMode = TextWrappingModes.NoWrap;
                spatialMarkers[id] = marker.rectTransform;
            }
        }

        private void UpdateSpatialTwin(TwinWorldStateRecord twin)
        {
            if (spatialMap == null || twin == null) return;
            PlaceSpatialMarker("AMR", new Vector3(twin.twin_robot_x, 0f, twin.twin_robot_y));
            PlaceSpatialMarker("H1", ParseVector(twin.h1_position));
            PlaceSpatialMarker("H2", ParseVector(twin.h2_position));
            PlaceSpatialMarker("H3", ParseVector(twin.h3_position));
            PlaceSpatialMarker("H4", ParseVector(twin.h4_position));
            PlaceSpatialMarker("H5", ParseVector(twin.h5_position));
            PlaceSpatialMarker("F1", ParseVector(twin.f1_position));
            PlaceSpatialMarker("F2", ParseVector(twin.f2_position));
        }

        private void PlaceSpatialMarker(string id, Vector3 world)
        {
            if (!spatialMarkers.TryGetValue(id, out RectTransform marker)) return;
            float x = Mathf.Lerp(0.05f, 0.85f, Mathf.InverseLerp(-30f, 30f, world.x));
            float y = Mathf.Lerp(0.08f, 0.78f, Mathf.InverseLerp(-20f, 20f, world.z));
            marker.anchorMin = new Vector2(x, y);
            marker.anchorMax = new Vector2(x + 0.12f, y + 0.12f);
            marker.offsetMin = Vector2.zero;
            marker.offsetMax = Vector2.zero;
        }

        private static Vector3 ParseVector(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return Vector3.zero;
            string[] p = value.Split(':');
            if (p.Length < 3) return Vector3.zero;
            return float.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                   float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) &&
                   float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)
                ? new Vector3(x, y, z)
                : Vector3.zero;
        }

        // =============================================================
        // ACTOR VALIDITY
        // =============================================================

        private void UpdateActorHealth()
        {
            ObservationRecord obs =
                module2.LatestObservation;

            if (obs == null)
            {
                actorHealthText.text =
                    "Waiting for operational telemetry...";
                return;
            }

            Dictionary<string, string> validity =
                ParseMap(
                    obs.actor_link_validity);

            Dictionary<string, string> ages =
                ParseMap(
                    obs.actor_message_ages);

            string output =
                "<color=#7695A8>" +
                "ACTOR       LINK STATE       MESSAGE AGE" +
                "</color>\n\n";

            foreach (
                string id in new[]
                {
                    "H1", "H2", "H3",
                    "H4", "H5", "F1", "F2"
                })
            {
                string state =
                    validity.TryGetValue(
                        id,
                        out string v)
                        ? v
                        : "UNKNOWN";

                string age =
                    ages.TryGetValue(
                        id,
                        out string a)
                        ? a
                        : "NA";

                string colour =
                    state == "VALID"
                        ? "#24E59A"
                        : "#FF5364";

                float ageSeconds = 0f;
                bool ageKnown = float.TryParse(age, NumberStyles.Float, CultureInfo.InvariantCulture, out ageSeconds);
                float health = state == "VALID" ? Mathf.Clamp01(1f - ageSeconds / 2f) : 0f;
                string latency = !ageKnown ? "NO DATA" : ageSeconds < 0.001f ? "LIVE" : $"{ageSeconds * 1000f:F0} ms";
                output +=
                    $"{id,-6}" +
                    $"<color={colour}>● {state,-8}</color>   " +
                    $"HEALTH {health * 100f,3:F0}%   {latency}\n";
            }

            output +=
                "\n" +
                $"<color=#7695A8>Max Communication Delay</color>   " +
                $"{obs.communication_delay * 1000f:F0} ms\n" +

                $"<color=#7695A8>LiDAR</color>   " +
                ValidityBadge(obs.lidar_valid) +
                "    " +

                $"<color=#7695A8>Camera</color>   " +
                ValidityBadge(obs.camera_valid);

            actorHealthText.text = output;
        }

        // =============================================================
        // MISSION
        // =============================================================

        private void UpdateMission()
        {
            TwinWorldStateRecord twin =
                module2.CurrentTwin;

            if (twin == null)
            {
                missionText.text =
                    "Waiting for mission state...";
                return;
            }

            if (module2.IsIdleLiveTwin)
            {
                missionText.text =
                    "<color=#25E59A><b>LIVE TWIN CONNECTED</b></color>\n" +
                    "<color=#7695A8>Mode</color>  MEMORY-ONLY MONITORING\n" +
                    "<color=#7695A8>CSV logging</color>  OFF\n" +
                    "<color=#7695A8>Robot and sensors</color>  LIVE\n" +
                    "<color=#7695A8>Next</color>  Press Start on Display 1 to begin the recorded A1 experiment";
                return;
            }

            missionText.text =
                $"<color=#00D9FF><b>{ScenarioCode(twin.scenarioId)}  {ScenarioName(twin.scenarioId)}</b></color>\n" +
                $"<color=#7695A8>Parcel</color>  {Safe(twin.parcel_id)}     <color=#7695A8>Destination</color>  {Safe(twin.destination_id)}\n" +
                $"<color=#7695A8>Rack / Slot</color>  {Safe(twin.destination_rack_id)} / {Safe(twin.destination_slot_id)}\n" +
                $"<color=#7695A8>Stage</color>  {Safe(twin.current_task_stage)}\n" +
                $"<color=#7695A8>Carrying</color> {BoolBadge(twin.carrying_status)}     <color=#7695A8>Grasp</color> {BoolBadge(twin.grasp_status)}\n" +
                $"<color=#7695A8>Episode</color> {twin.episodeId}     <color=#7695A8>Step</color> {twin.stepId}     <color=#7695A8>Records</color> {module2.ProcessedObservationCount}";

            if (pipeline != null)
            {
                missionText.text +=
                    $"\n<color=#00D9FF><b>MODULE 3 / 4 SHADOW</b></color>   " +
                    $"<color=#7695A8>Trust</color> {pipeline.OverallTrust * 100f:F0}%   " +
                    $"<color=#7695A8>Risk</color> {pipeline.CollisionRisk:F2}   " +
                    $"<color=#7695A8>Action</color> {pipeline.SelectedAction}\n" +
                    $"<color=#7695A8>Decision</color>  {pipeline.DecisionReason}";
            }

            if (module2.MajorUpdateLatched)
            {
                missionText.text +=
                    "\n<color=#FF5364><b>BASELINE EVENT: COLLISION_HUMAN</b></color>  " +
                    "Twin terminal state synchronized";
            }
        }

        // =============================================================
        // EVENT / EDATS PANEL
        // =============================================================

        private void UpdateEventPanel()
        {
            TwinWorldStateRecord twin =
                module2.CurrentTwin;

            if (module2.MajorUpdateLatched)
            {
                string secureAction = pipeline != null
                    ? pipeline.SelectedAction.ToString()
                    : "WAIT";
                eventText.text =
                    $"<color=#FF5364><b>{module2.MajorUpdateTitle}</b></color>\n" +
                    $"{module2.SyncReason}\n\n" +
                    $"<color=#7695A8>CHANGED COMPONENTS</color>\n" +
                    $"<color=#00D9FF>{module2.ChangedComponents}</color>\n\n" +
                    $"<color=#7695A8>ARCHITECTURE FLOW</color>\n" +
                    $"{module2.MajorUpdateFlow}\n" +
                    $"GENERATE DECISION: {secureAction}\n" +
                    $"EXECUTION: update package ready — run it from Display 3";
                return;
            }

            if (module2.IsIdleLiveTwin)
            {
                eventText.text =
                    "<color=#25E59A><b>DIGITAL TWIN ONLINE</b></color>\n" +
                    "Continuous in-memory observation is active.\n\n" +
                    "<color=#7695A8>DATA POLICY</color>\n" +
                    "No CSV files are written while idle.\n" +
                    "Recording begins only after Start A1 is pressed.\n\n" +
                    "<color=#7695A8>EDATS</color>\n" +
                    "Monitoring live state; waiting for a significant change.";
                return;
            }

            string changed =
                string.IsNullOrWhiteSpace(
                    module2.ChangedComponents)
                    ? "No significant component change"
                    : module2.ChangedComponents;

            eventText.text =
                $"<color=#7695A8>DECISION</color>\n" +
                $"{module2.SyncReason}\n\n" +

                $"<color=#7695A8>CHANGED COMPONENTS</color>\n" +
                $"<color=#00D9FF>{changed}</color>\n\n" +

                $"<color=#7695A8>CHANGE SCORE</color>   " +
                $"{module2.CurrentChangeScore:F3}\n" +

                $"<color=#7695A8>SYNC COUNT</color>     " +
                $"{module2.SynchronizationCount}\n" +

                $"<color=#7695A8>LAST SYNC RUNTIME</color>   " +
                $"{module2.LastSyncRuntime:F2} s";
        }

        // =============================================================
        // PIPELINE
        // =============================================================

        private void UpdatePipeline()
        {
            if (module2.MajorUpdateLatched)
            {
                observationStage.text = "●  OBSERVATION\nCOLLISION";
                changeStage.text = "●  CHANGE DETECTION\nMAJOR";
                edatsStage.text = "●  EDATS\nFORCED SYNC";
                twinStage.text = "●  ADAPTIVE TWIN\nUPDATED";
                contextStage.text = "●  CONTEXT + TRUST\nVALIDATED";
                ppoStage.text = "●  SECURE POLICY\nSAFE ACTION";

                observationStage.color = red;
                changeStage.color = red;
                edatsStage.color = amber;
                twinStage.color = green;
                contextStage.color = green;
                ppoStage.color = green;
                SetLine(lineObservationChange, true);
                SetLine(lineChangeEdats, true);
                SetLine(lineEdatsTwin, true);
                SetLine(lineTwinContext, true);
                SetLine(lineContextPpo, true);
                return;
            }

            observationStage.text = "●  OBSERVATION";
            changeStage.text = "●  CHANGE DETECTION";
            edatsStage.text = "●  EDATS";
            twinStage.text = "●  ADAPTIVE TWIN";
            contextStage.text = "●  CONTEXT + TRUST";
            ppoStage.text = "●  SECURE POLICY";

            SetStage(
                observationStage,
                true);

            SetStage(
                changeStage,
                module2.LatestObservation != null);

            SetStage(
                edatsStage,
                module2.LatestObservation != null);

            SetStage(
                twinStage,
                module2.CurrentTwin != null);

            SetStage(
                contextStage,
                pipeline != null && pipeline.TrustedStateCount > 0);

            SetStage(
                ppoStage,
                pipeline != null && pipeline.DecisionCount > 0);

            SetLine(
                lineObservationChange,
                module2.LatestObservation != null);

            SetLine(
                lineChangeEdats,
                module2.LatestObservation != null);

            SetLine(
                lineEdatsTwin,
                module2.CurrentTwin != null);

            SetLine(
                lineTwinContext,
                pipeline != null && pipeline.TrustedStateCount > 0);

            SetLine(
                lineContextPpo,
                pipeline != null && pipeline.DecisionCount > 0);
        }

        // =============================================================
        // SYNCHRONIZATION PULSE
        // =============================================================

        private void BuildPulse(
            RectTransform parent)
        {
            RectTransform pulseContainer =
                CreatePanel(
                    parent,
                    "PulseContainer",
                    new Vector2(0.04f, 0.18f),
                    new Vector2(0.29f, 0.72f),
                    panel2);

            syncPulseOuter =
                CreateCircle(
                    pulseContainer,
                    "PulseOuter",
                    150,
                    cyan);

            syncPulseInner =
                CreateCircle(
                    pulseContainer,
                    "PulseInner",
                    70,
                    green);

            TMP_Text label =
                CreateText(
                    pulseContainer,
                    "PulseLabel",
                    new Vector2(0.05f, 0.02f),
                    new Vector2(0.95f, 0.20f),
                    13,
                    muted,
                    FontStyles.Bold);

            label.alignment =
                TextAlignmentOptions.Center;

            label.text = "SYNC PULSE";
        }

        private void UpdateSyncPulse()
        {
            if (module2.SynchronizationCount !=
                previousSyncCount)
            {
                if (previousSyncCount >= 0)
                    pulseStartTime =
                        Time.unscaledTime;

                previousSyncCount =
                    module2.SynchronizationCount;
            }

            float elapsed =
                Time.unscaledTime -
                pulseStartTime;

            float pulse =
                Mathf.Clamp01(
                    1f -
                    elapsed / 1.2f);

            float wave =
                Mathf.Sin(
                    Mathf.Clamp01(
                        elapsed / 1.2f) *
                    Mathf.PI);

            if (syncPulseOuter != null)
            {
                float scale =
                    1f +
                    wave * 0.45f;

                syncPulseOuter.rectTransform.localScale =
                    Vector3.one * scale;

                Color c =
                    module2.CriticalEvent
                        ? red
                        : cyan;

                c.a =
                    0.15f +
                    0.7f * pulse;

                syncPulseOuter.color = c;
            }

            if (syncPulseInner != null)
            {
                Color c =
                    module2.SynchronizationTriggered
                        ? green
                        : cyan;

                c.a =
                    0.65f +
                    0.35f *
                    Mathf.Abs(
                        Mathf.Sin(
                            Time.unscaledTime * 3f));

                syncPulseInner.color = c;
            }
        }

        // =============================================================
        // BUILD HELPERS
        // =============================================================

        private RectTransform CreatePanel(
            RectTransform parent,
            string name,
            Vector2 min,
            Vector2 max,
            Color colour)
        {
            GameObject obj =
                CreateUIObject(
                    name,
                    parent);

            RectTransform rt =
                obj.GetComponent<RectTransform>();

            SetRect(rt, min, max);

            Image image =
                obj.AddComponent<Image>();

            image.color = colour;

            Outline outline =
                obj.AddComponent<Outline>();

            outline.effectColor = border;
            outline.effectDistance =
                new Vector2(1f, -1f);

            return rt;
        }

        private void AddSectionHeader(
            RectTransform panel,
            string title,
            string subtitle)
        {
            AddTopAccent(panel);

            TMP_Text t =
                CreateText(
                    panel,
                    "Title",
                    new Vector2(0.04f, 0.82f),
                    new Vector2(0.68f, 0.96f),
                    19,
                    cyan,
                    FontStyles.Bold);

            t.text = title;

            TMP_Text s =
                CreateText(
                    panel,
                    "Subtitle",
                    new Vector2(0.69f, 0.82f),
                    new Vector2(0.96f, 0.96f),
                    11,
                    muted,
                    FontStyles.Normal);

            s.text = subtitle;
            s.alignment =
                TextAlignmentOptions.MidlineRight;
        }

        private void AddTopAccent(
            RectTransform panel)
        {
            GameObject bar =
                CreateUIObject(
                    "TopAccent",
                    panel);

            RectTransform rt =
                bar.GetComponent<RectTransform>();

            SetRect(
                rt,
                new Vector2(0, 0.972f),
                new Vector2(1, 1));

            Image image =
                bar.AddComponent<Image>();

            image.color = cyan;
        }

        private TMP_Text CreateStage(
            RectTransform parent,
            string label,
            float x)
        {
            TMP_Text stage =
                CreateText(
                    parent,
                    label.Replace(" ", "_"),
                    new Vector2(x, 0.05f),
                    new Vector2(x + 0.13f, 0.95f),
                    13,
                    muted,
                    FontStyles.Bold);

            stage.alignment =
                TextAlignmentOptions.Center;

            stage.text =
                "●  " + label;

            return stage;
        }

        private Image CreatePipelineLine(
            RectTransform parent,
            float x1,
            float x2)
        {
            GameObject obj =
                CreateUIObject(
                    "PipelineLine",
                    parent);

            RectTransform rt =
                obj.GetComponent<RectTransform>();

            SetRect(
                rt,
                new Vector2(x1, 0.48f),
                new Vector2(x2, 0.52f));

            Image image =
                obj.AddComponent<Image>();

            image.color = border;

            return image;
        }

        private Image CreateCircle(
            RectTransform parent,
            string name,
            float size,
            Color colour)
        {
            GameObject obj =
                CreateUIObject(
                    name,
                    parent);

            RectTransform rt =
                obj.GetComponent<RectTransform>();

            rt.anchorMin =
                new Vector2(0.5f, 0.55f);

            rt.anchorMax =
                new Vector2(0.5f, 0.55f);

            rt.sizeDelta =
                new Vector2(size, size);

            rt.anchoredPosition =
                Vector2.zero;

            Image image =
                obj.AddComponent<Image>();

            image.color = colour;

            return image;
        }

        private TMP_Text CreateText(
            RectTransform parent,
            string name,
            Vector2 min,
            Vector2 max,
            float size,
            Color colour,
            FontStyles style)
        {
            GameObject obj =
                new GameObject(
                    name,
                    typeof(RectTransform),
                    typeof(TextMeshProUGUI));

            obj.transform.SetParent(
                parent,
                false);

            RectTransform rt =
                obj.GetComponent<RectTransform>();

            SetRect(rt, min, max);

            TMP_Text textObj =
                obj.GetComponent<TMP_Text>();

            textObj.fontSize = size;
            textObj.color = colour;
            textObj.fontStyle = style;

            textObj.alignment =
                TextAlignmentOptions.TopLeft;

            textObj.textWrappingMode =
                TextWrappingModes.Normal;

            textObj.overflowMode =
                TextOverflowModes.Overflow;

            return textObj;
        }

        private GameObject CreateUIObject(
            string name,
            Transform parent)
        {
            GameObject obj =
                new GameObject(
                    name,
                    typeof(RectTransform));

            obj.transform.SetParent(
                parent,
                false);

            return obj;
        }

        private void SetRect(
            RectTransform rt,
            Vector2 min,
            Vector2 max)
        {
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private void Stretch(
            RectTransform rt)
        {
            SetRect(
                rt,
                Vector2.zero,
                Vector2.one);
        }

        private void SetStage(
            TMP_Text stage,
            bool active)
        {
            if (stage == null)
                return;

            stage.color =
                active
                    ? green
                    : muted;
        }

        private void SetLine(
            Image line,
            bool active)
        {
            if (line == null)
                return;

            line.color =
                active
                    ? cyan
                    : border;
        }

        // =============================================================
        // DATA HELPERS
        // =============================================================

        private string Row(
            string name,
            float observation,
            float twin,
            float difference,
            string unit)
        {
            string colour =
                difference < 0.05f
                    ? "#24E59A"
                    : difference < 0.25f
                        ? "#FFC15A"
                        : "#FF5364";

            return
                $"{name,-15}" +
                $"{observation,9:F2} {unit,-4}" +
                $"{twin,9:F2} {unit,-4}" +
                $"<color={colour}>{difference,7:F3}</color>\n";
        }

        private Dictionary<string, string>
            ParseMap(string value)
        {
            var result =
                new Dictionary<string, string>();

            if (string.IsNullOrWhiteSpace(value))
                return result;

            foreach (
                string item in value.Split('|'))
            {
                int separator =
                    item.IndexOf(':');

                if (separator <= 0)
                    continue;

                string key =
                    item.Substring(
                        0,
                        separator);

                string val =
                    item.Substring(
                        separator + 1);

                result[key] = val;
            }

            return result;
        }

        private string ValidityBadge(
            bool valid)
        {
            return valid
                ? "<color=#24E59A>● VALID</color>"
                : "<color=#FF5364>● INVALID</color>";
        }

        private string BoolBadge(
            bool value)
        {
            return value
                ? "<color=#24E59A>● YES</color>"
                : "<color=#7695A8>○ NO</color>";
        }

        private string Safe(
            string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? "--"
                : value;
        }

        // =============================================================
        // SCENARIO NAMES
        // =============================================================

        private string ScenarioCode(
            int id)
        {
            // Your current Scenario 1 maps to A1.
            // Extend mapping when E/T scenarios are numbered.
            if (id >= 1 && id <= 5)
                return "A" + id;

            if (id >= 6 && id <= 10)
                return "E" + (id - 5);

            if (id >= 11 && id <= 15)
                return "T" + (id - 10);

            return "S" + id;
        }

        private string ScenarioName(
            int id)
        {
            switch (id)
            {
                case 1:
                    return "BOOKS-RACK BLIND-TURN COLLISION";

                case 2:
                    return "DYNAMIC ROUTE OBSTRUCTION";

                case 3:
                    return "BLOCKED DROP-OFF APPROACH";

                case 4:
                    return "RELOCATED DESTINATION RACK";

                case 5:
                    return "PICKUP DOCKING MISALIGNMENT";

                case 6:
                    return "DESTINATION CAPACITY FULL";

                case 7:
                    return "PARCEL NOT READY";

                case 8:
                    return "BARCODE STAGING MISMATCH";

                case 9:
                    return "TWO-DESTINATION RESEQUENCING";

                case 10:
                    return "DYNAMIC ORDER FULFILMENT";

                case 11:
                    return "STALE FORKLIFT TWIN STATE";

                case 12:
                    return "DELAYED HUMAN STATE";

                case 13:
                    return "LIDAR DEGRADATION";

                case 14:
                    return "RGB OBSERVATION DROPOUT";

                case 15:
                    return "MULTI-STATE TWIN DIVERGENCE";

                default:
                    return "WAREHOUSE SCENARIO";
            }
        }

        private static Color Hex(
            string value)
        {
            ColorUtility.TryParseHtmlString(
                value,
                out Color c);

            return c;
        }
    }
}
