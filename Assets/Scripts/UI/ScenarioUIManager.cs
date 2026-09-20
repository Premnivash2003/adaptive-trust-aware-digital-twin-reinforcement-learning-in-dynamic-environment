using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ATADTRL.Scenarios;
using ATADTRL.Core;
using ATADTRL.Environment;
using ATADTRL.Pipeline;

namespace ATADTRL.UI
{
    /// <summary>
    /// Drives the "ATADTRL Module 1 Scenario Control Panel" Canvas: scenario
    /// dropdown + description, Start/Stop/Reset/Next/Previous/RunAll/Return
    /// buttons. Wires directly into ScenarioManager's public API so no code
    /// changes are ever needed to run a different scenario.
    /// </summary>
    [ExecuteAlways]
    public class ScenarioUIManager : MonoBehaviour
    {
        [Header("References")]
        public ScenarioManager scenarioManager;

        [Header("UI Widgets")]
        public TMP_Dropdown scenarioDropdown;
        public TMP_Text scenarioDescriptionText;
        public TMP_Text statusText;
        public TMP_Text liveStatsText;

        public Button startButton;
        public Button stopButton;
        public Button resetButton;
        public Button nextButton;
        public Button previousButton;
        public Button runAllButton;
        public Button returnToMenuButton;

        [Header("Run Options")]
        [Tooltip("When enabled, Run All runs every scenario once in a different random order.")]
        public bool runAllInRandomOrder = false;

        [Header("Scenes")]
        public string mainMenuSceneName = "MainMenu";

        private bool _uiInitialized;
        private WarehouseManagementUI _managementUI;
        private ScrollRect _descriptionScroll;
        private TMP_Text _operationsState;
        private TMP_Text _disturbanceText;
        private TMP_Text _datasetText;
        private Image _episodeProgress;
        private Image _stateIndicator;
        private Button _pipelineModeButton;
        private TMP_Text _pipelineModeLabel;
        private ATADTRLPipelineManager _pipeline;
        private float _nextTelemetryRefresh;

        public const float OperationsCardWidth = 500f;
        public const float OperationsCardHeight = 694f;
        public const float DesignerButtonTop = 625f;

#if UNITY_EDITOR
        // Older scene-builder runs left repeated direct children under this
        // panel. Remove them from the SAVED scene, not just during Play mode.
        private void OnEnable()
        {
            if (!Application.isPlaying) RemoveDuplicateControlsFromScene();
        }

        private void OnValidate()
        {
            if (!Application.isPlaying) RemoveDuplicateControlsFromScene();
        }

        private void RemoveDuplicateControlsFromScene()
        {
            // Unity can invoke OnValidate before serialized references have
            // been restored during a domain reload. Do not touch the scene
            // until the canonical control set is available.
            if (scenarioDropdown == null || scenarioDescriptionText == null || statusText == null ||
                startButton == null || stopButton == null || resetButton == null || nextButton == null ||
                previousButton == null || runAllButton == null || returnToMenuButton == null)
            {
                return;
            }

            var duplicates = new List<GameObject>();
            foreach (Transform child in transform)
            {
                if (!IsControlName(child.name) || IsReferencedControl(child)) continue;
                duplicates.Add(child.gameObject);
            }

            foreach (var duplicate in duplicates)
            {
                DestroyImmediate(duplicate);
            }

            foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include))
            {
                if (canvas.gameObject.name == "Canvas" && canvas.gameObject != gameObject)
                {
                    DestroyImmediate(canvas.gameObject);
                }
            }
        }

        private bool IsReferencedControl(Transform child)
        {
            return child == scenarioDropdown?.transform ||
                   child == scenarioDescriptionText?.transform ||
                   child == statusText?.transform ||
                   child == startButton?.transform ||
                   child == stopButton?.transform ||
                   child == resetButton?.transform ||
                   child == nextButton?.transform ||
                   child == previousButton?.transform ||
                   child == runAllButton?.transform ||
                   child == returnToMenuButton?.transform;
        }

        private static bool IsControlName(string name)
        {
            return name == "ScenarioDropdown" || name == "DescriptionText" || name == "StatusText" ||
                   name == "Btn_Start" || name == "Btn_Stop" || name == "Btn_Reset" ||
                   name == "Btn_Next" || name == "Btn_Previous" || name == "Btn_RunAll" ||
                   name == "Btn_ReturntoMainMenu";
        }
#endif

        private void Start()
        {
            if (scenarioManager == null)
            {
                Debug.LogError("ScenarioUIManager: ScenarioManager reference is missing.");
                return;
            }

            scenarioManager.OnStatusMessage += HandleStatus;
            scenarioManager.OnScenarioStarted += HandleScenarioStarted;
            scenarioManager.OnScenarioCompleted += HandleScenarioCompleted;
            scenarioManager.OnAllScenariosCompleted += HandleAllCompleted;
            scenarioManager.OnInitialized += InitializeUI;
            scenarioManager.OnScenarioCatalogChanged += HandleScenarioCatalogChanged;

            if (scenarioManager.IsInitialized)
            {
                InitializeUI();
            }
            else
            {
                HandleStatus("Loading scenarios...");
            }
        }

        private void InitializeUI()
        {
            if (scenarioDropdown == null)
            {
                Debug.LogError("ScenarioUIManager: Scenario Dropdown reference is missing.");
                return;
            }

            if (_uiInitialized)
            {
                RefreshScenarioCatalog(scenarioManager.CurrentScenarioIndex);
                InitializeManagementPage();
                return;
            }

            PopulateDropdown();
            WireButtons();
            ApplyTopLeftBlueTheme();
            DisableDuplicateCanvases();
            InitializeManagementPage();
            InitializePipelineSelector();
            _uiInitialized = true;

            scenarioDropdown.SetValueWithoutNotify(scenarioManager.CurrentScenarioIndex);
            UpdateDescription(scenarioManager.CurrentScenarioIndex);
            HandleStatus($"Selected {scenarioManager.Scenarios[scenarioManager.CurrentScenarioIndex].scenarioName}");
        }

        private void PopulateDropdown()
        {
            scenarioDropdown.onValueChanged.RemoveListener(HandleDropdownValueChanged);
            scenarioDropdown.ClearOptions();
            var options = new List<string>();
            foreach (var s in scenarioManager.Scenarios)
            {
                options.Add($"{s.scenarioCode}: {s.scenarioName}");
            }
            scenarioDropdown.AddOptions(options);
            scenarioDropdown.onValueChanged.AddListener(HandleDropdownValueChanged);
        }

        private void HandleDropdownValueChanged(int index)
        {
            scenarioManager.SelectScenario(index);
            UpdateDescription(index);
        }

        /// <summary>Rebuilds the selector after a custom scenario or layout is created.</summary>
        public void RefreshScenarioCatalog(int selectedIndex = -1)
        {
            if (scenarioDropdown == null || scenarioManager == null || scenarioManager.Scenarios.Count == 0) return;
            int index = selectedIndex >= 0 ? selectedIndex : scenarioManager.CurrentScenarioIndex;
            index = Mathf.Clamp(index, 0, scenarioManager.Scenarios.Count - 1);
            PopulateDropdown();
            scenarioManager.SelectScenario(index);
            SelectScenario(index);
        }

        private void InitializeManagementPage()
        {
            _managementUI = GetComponent<WarehouseManagementUI>();
            if (_managementUI == null) _managementUI = gameObject.AddComponent<WarehouseManagementUI>();
            WarehouseManager warehouse = scenarioManager != null ? scenarioManager.warehouseManager : null;
            _managementUI.Initialize(this, warehouse != null ? warehouse.Inventory : null, warehouse, scenarioManager);
        }

        private void InitializePipelineSelector()
        {
            _pipeline = FindAnyObjectByType<ATADTRLPipelineManager>();
            // A1 comparison is visualized on Displays 2 and 3. Keep the
            // original Display-1 Module-1 control panel unchanged.
            Transform obsolete = transform.Find("PipelineModeButton");
            if (obsolete != null) obsolete.gameObject.SetActive(false);
        }

        private void RefreshPipelineSelector()
        {
            if (_pipelineModeLabel == null || _pipeline == null) return;
            _pipelineModeLabel.text = "A1  /  BASELINE + SHADOW";
            Image image = _pipelineModeButton != null ? _pipelineModeButton.GetComponent<Image>() : null;
            if (image != null) image.color = _pipeline.FullPipelineEnabled
                ? new Color(0.02f, 0.52f, 0.67f, 1f)
                : new Color(0.25f, 0.31f, 0.36f, 1f);
        }

        private void HandleScenarioCatalogChanged()
        {
            RefreshScenarioCatalog(scenarioManager.CurrentScenarioIndex);
            InitializeManagementPage();
        }

        private void WireButtons()
        {
            startButton?.onClick.AddListener(() =>
            {
                int idx = scenarioDropdown.value;
                if (idx >= 0 && idx < scenarioManager.Scenarios.Count)
                {
                    scenarioManager.StartScenario(scenarioManager.Scenarios[idx].scenarioId);
                }
            });

            stopButton?.onClick.AddListener(() => scenarioManager.StopScenario());
            resetButton?.onClick.AddListener(() => scenarioManager.ResetScenario());

            nextButton?.onClick.AddListener(() =>
            {
                scenarioManager.NextScenario();
                SelectScenario(scenarioManager.CurrentScenarioIndex);
            });

            previousButton?.onClick.AddListener(() =>
            {
                scenarioManager.PreviousScenario();
                SelectScenario(scenarioManager.CurrentScenarioIndex);
            });

            // Run All is deliberately deterministic: it executes the entire
            // 01–15 experiment set in order, making Console and CSV results
            // easy to audit. Individual random experiments remain available
            // from ScenarioManager.StartRandomScenario().
            runAllButton?.onClick.AddListener(() => scenarioManager.StartRunAll(false));

            returnToMenuButton?.onClick.AddListener(() =>
            {
                if (!string.IsNullOrEmpty(mainMenuSceneName) &&
                    Application.CanStreamedLevelBeLoaded(mainMenuSceneName))
                {
                    UnityEngine.SceneManagement.SceneManager.LoadScene(mainMenuSceneName);
                }
                else
                {
                    // This project currently ships only scene1.  Avoid a failed
                    // scene-load when no separate menu has been added yet.
                    scenarioManager.ResetScenario();
                    HandleStatus("No MainMenu scene is configured; the scenario was reset.");
                    Debug.LogWarning("ScenarioUIManager: MainMenu scene is not in Build Settings. Reset the scenario instead.");
                }
            });
        }

        private void UpdateDescription(int index)
        {
            if (index < 0 || index >= scenarioManager.Scenarios.Count) return;
            var s = scenarioManager.Scenarios[index];
            if (scenarioDescriptionText != null)
            {
                // Preserve the complete brief inside a scrollable viewport.
                scenarioDescriptionText.text =
                    $"<color=#87AAC0>{s.category.ToString().ToUpperInvariant()}</color>\n\n{s.description}";
                if (_descriptionScroll != null) _descriptionScroll.verticalNormalizedPosition = 1f;
            }
            HandleStatus($"Selected {s.scenarioCode}: {s.scenarioName}");
        }

        private static string CompactForControlPanel(string value, int maxCharacters = 410)
        {
            if (string.IsNullOrWhiteSpace(value)) return "No scenario description supplied.";

            string normalized = string.Join(" ", value.Split(
                new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
            if (normalized.Length <= maxCharacters) return normalized;

            int sentenceEnd = normalized.LastIndexOf(". ", maxCharacters - 1, StringComparison.Ordinal);
            int cut = sentenceEnd >= maxCharacters / 2
                ? sentenceEnd + 1
                : normalized.LastIndexOf(' ', maxCharacters - 1);
            if (cut < 1) cut = maxCharacters;
            return normalized.Substring(0, cut).TrimEnd() + "…";
        }

        private void SelectScenario(int index)
        {
            if (scenarioDropdown == null || index < 0 || index >= scenarioManager.Scenarios.Count) return;
            scenarioDropdown.SetValueWithoutNotify(index);
            UpdateDescription(index);
        }

        // Keeps the control panel legible in both the Scene and Game views.
        // The previous default TMP controls used pale text on pale buttons.
        private void ApplyTopLeftBlueTheme()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                canvas.pixelPerfect = true;
                var scaler = canvas.GetComponent<CanvasScaler>();
                if (scaler == null) scaler = canvas.gameObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1280f, 720f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            }

            var panel = AddHudPanel("ControlPanelBackdrop", new Vector2(18f, -18f),
                new Vector2(OperationsCardWidth, OperationsCardHeight), new Color(0.025f, 0.045f, 0.068f, 0.975f));
            panel.transform.SetAsFirstSibling();
            AddHudPanel("OperationsAccent", new Vector2(18f, -18f), new Vector2(500f, 4f), new Color(0.10f, 0.59f, 0.90f));
            AddHudText("OperationsTitle", "WAREHOUSE OPERATIONS", new Vector2(30f, -28f), new Vector2(472f, 32f), 24f, FontStyles.Bold);
            AddHudText("OperationsSubtitle", "MODULE 01   /   BASELINE DATA ACQUISITION", new Vector2(30f, -62f), new Vector2(472f, 21f), 15f);
            Layout(scenarioDropdown.GetComponent<RectTransform>(), new Vector2(30f, -90f), new Vector2(472f, 42f));
            AddHudText("MissionBriefLabel", "MISSION BRIEF                               SCROLL FOR DETAILS", new Vector2(30f, -143f), new Vector2(472f, 20f), 15f, FontStyles.Bold);
            BuildDescriptionViewport();

            AddHudPanel("TelemetryBackdrop", new Vector2(30f, -300f), new Vector2(476f, 132f), new Color(0.050f, 0.090f, 0.127f));
            _stateIndicator = AddHudPanel("StateIndicator", new Vector2(42f, -311f), new Vector2(7f, 16f), new Color(0.28f, 0.68f, 0.84f));
            _operationsState = AddHudText("OperationsState", "READY", new Vector2(58f, -307f), new Vector2(432f, 24f), 18f, FontStyles.Bold);
            if (liveStatsText == null)
                liveStatsText = AddHudText("LiveOperationsTelemetry", "", new Vector2(42f, -337f), new Vector2(448f, 88f), 17f);
            Layout(liveStatsText.rectTransform, new Vector2(42f, -337f), new Vector2(448f, 88f));
            liveStatsText.raycastTarget = false;
            liveStatsText.overflowMode = TextOverflowModes.Ellipsis;
            AddHudPanel("EpisodeProgressTrack", new Vector2(30f, -436f), new Vector2(476f, 3f), new Color(0.10f, 0.19f, 0.26f));
            _episodeProgress = AddHudPanel("EpisodeProgress", new Vector2(30f, -436f), new Vector2(0f, 3f), new Color(0.10f, 0.68f, 0.87f));
            _disturbanceText = AddHudText("DisturbanceStatus", "", new Vector2(30f, -448f), new Vector2(472f, 37f), 17f);
            Layout(statusText != null ? statusText.rectTransform : null, new Vector2(30f, -490f), new Vector2(472f, 39f));
            Layout(startButton != null ? startButton.GetComponent<RectTransform>() : null, new Vector2(30f, -537f), new Vector2(110f, 36f));
            Layout(stopButton != null ? stopButton.GetComponent<RectTransform>() : null, new Vector2(148f, -537f), new Vector2(110f, 36f));
            Layout(resetButton != null ? resetButton.GetComponent<RectTransform>() : null, new Vector2(266f, -537f), new Vector2(110f, 36f));
            Layout(nextButton != null ? nextButton.GetComponent<RectTransform>() : null, new Vector2(384f, -537f), new Vector2(110f, 36f));
            Layout(previousButton != null ? previousButton.GetComponent<RectTransform>() : null, new Vector2(30f, -581f), new Vector2(144f, 34f));
            Layout(runAllButton != null ? runAllButton.GetComponent<RectTransform>() : null, new Vector2(182f, -581f), new Vector2(144f, 34f));
            Layout(returnToMenuButton != null ? returnToMenuButton.GetComponent<RectTransform>() : null, new Vector2(334f, -581f), new Vector2(160f, 34f));
            _datasetText = AddHudText("DatasetLocation", "", new Vector2(30f, -665f), new Vector2(472f, 32f), 14f);

            if (scenarioDescriptionText != null)
            {
                scenarioDescriptionText.fontSize = 20f;
                scenarioDescriptionText.enableAutoSizing = false;
                scenarioDescriptionText.alignment = TextAlignmentOptions.TopLeft;
                scenarioDescriptionText.textWrappingMode = TextWrappingModes.Normal;
                scenarioDescriptionText.overflowMode = TextOverflowModes.Overflow;
                scenarioDescriptionText.lineSpacing = 1f;
                scenarioDescriptionText.margin = new Vector4(0f, 3f, 7f, 4f);
                scenarioDescriptionText.raycastTarget = false;
                scenarioDescriptionText.color = new Color(0.85f, 0.91f, 0.95f);
            }
            if (statusText != null)
            {
                statusText.fontSize = 17f;
                statusText.color = new Color(0.72f, 0.84f, 0.90f);
                statusText.alignment = TextAlignmentOptions.TopLeft;
                statusText.textWrappingMode = TextWrappingModes.Normal;
                statusText.overflowMode = TextOverflowModes.Ellipsis;
                statusText.raycastTarget = false;
            }
            foreach (var button in new[] { startButton, stopButton, resetButton, nextButton, previousButton, runAllButton, returnToMenuButton })
            {
                if (button == null) continue;
                var image = button.GetComponent<Image>();
                if (image != null) image.color = button == startButton ? new Color(0.06f, 0.49f, 0.79f)
                    : button == stopButton ? new Color(0.54f, 0.19f, 0.20f) : new Color(0.15f, 0.21f, 0.29f);
                ColorBlock colors = button.colors;
                colors.normalColor = Color.white;
                colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
                colors.pressedColor = new Color(0.74f, 0.84f, 0.91f);
                colors.disabledColor = new Color(0.38f, 0.43f, 0.47f, 0.65f);
                button.colors = colors;
                foreach (var label in button.GetComponentsInChildren<TMP_Text>(true))
                {
                    label.enableAutoSizing = false;
                    label.fontSize = 18f;
                    label.color = Color.white;
                    label.fontStyle = FontStyles.Bold;
                    label.textWrappingMode = TextWrappingModes.NoWrap;
                    label.overflowMode = TextOverflowModes.Ellipsis;
                    label.raycastTarget = false;
                    if (button == returnToMenuButton) label.text = "Main menu";
                }
            }
            foreach (var label in scenarioDropdown.GetComponentsInChildren<TMP_Text>(true))
            {
                label.color = new Color(0.02f, 0.08f, 0.16f);
                label.fontSize = 20f;
                label.enableAutoSizing = false;
                label.overflowMode = TextOverflowModes.Ellipsis;
            }
            if (scenarioDropdown.captionText != null) scenarioDropdown.captionText.fontStyle = FontStyles.Bold;
            ConfigureReadableScenarioOptions();
        }

        private void ConfigureReadableScenarioOptions()
        {
            if (scenarioDropdown == null || scenarioDropdown.template == null || scenarioDropdown.itemText == null) return;
            // The saved TMP template uses 20px rows. A 20pt font with Ellipsis
            // can discard its entire first line when that line does not fit.
            // Configure the SOURCE template so every runtime option inherits
            // adequate height, black lettering and a distinct highlight.
            TMP_Text label = scenarioDropdown.itemText;
            Toggle item = label.GetComponentInParent<Toggle>(true);
            if (item == null) return;
            const float rowHeight = 56f;
            RectTransform row = item.GetComponent<RectTransform>();
            row.anchorMin = new Vector2(0f, 0.5f);
            row.anchorMax = new Vector2(1f, 0.5f);
            row.pivot = new Vector2(0.5f, 0.5f);
            row.anchoredPosition = Vector2.zero;
            row.sizeDelta = new Vector2(0f, rowHeight);
            RectTransform content = row.parent as RectTransform;
            if (content != null)
            {
                content.anchorMin = new Vector2(0f, 1f);
                content.anchorMax = new Vector2(1f, 1f);
                content.pivot = new Vector2(0.5f, 1f);
                content.anchoredPosition = Vector2.zero;
                content.sizeDelta = new Vector2(0f, rowHeight);
            }
            scenarioDropdown.template.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 336f);
            Image popup = scenarioDropdown.template.GetComponent<Image>();
            if (popup != null) popup.color = Color.white;
            var scroll = scenarioDropdown.template.GetComponent<ScrollRect>();
            if (scroll != null)
            {
                scroll.horizontal = false; scroll.vertical = true;
                scroll.scrollSensitivity = 32f;
            }
            label.color = Color.black;
            label.fontSize = 20f;
            label.enableAutoSizing = false;
            label.fontStyle = FontStyles.Normal;
            label.alignment = TextAlignmentOptions.Left;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Truncate;
            label.margin = Vector4.zero;
            label.raycastTarget = false;
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = new Vector2(30f, 4f);
            label.rectTransform.offsetMax = new Vector2(-12f, -4f);
            // Keep selection tint on the background, never on the label.
            Image background = row.Find("Item Background")?.GetComponent<Image>();
            if (background != null) { background.color = Color.white; item.targetGraphic = background; }
            item.transition = Selectable.Transition.ColorTint;
            ColorBlock colours = item.colors;
            colours.normalColor = Color.white;
            colours.highlightedColor = new Color(0.82f, 0.91f, 1f);
            colours.selectedColor = new Color(0.72f, 0.85f, 0.98f);
            colours.pressedColor = new Color(0.65f, 0.79f, 0.94f);
            colours.disabledColor = new Color(0.88f, 0.88f, 0.88f);
            colours.colorMultiplier = 1f;
            item.colors = colours;
        }

        private Image AddHudPanel(string name, Vector2 position, Vector2 size, Color color)
        {
            Transform existing = transform.Find(name);
            GameObject go = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            Layout(go.GetComponent<RectTransform>(), position, size);
            Image image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private TMP_Text AddHudText(string name, string value, Vector2 position, Vector2 size, float fontSize, FontStyles style = FontStyles.Normal)
        {
            Transform existing = transform.Find(name);
            GameObject go = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(transform, false);
            TMP_Text text = go.GetComponent<TMP_Text>();
            if (scenarioDescriptionText != null) text.font = scenarioDescriptionText.font;
            text.text = value;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = new Color(0.78f, 0.87f, 0.92f);
            text.alignment = TextAlignmentOptions.TopLeft;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            Layout(text.rectTransform, position, size);
            return text;
        }

        private void BuildDescriptionViewport()
        {
            if (scenarioDescriptionText == null) return;
            Image viewport = AddHudPanel("MissionDescriptionViewport", new Vector2(30f, -177f), new Vector2(472f, 112f), new Color(0f, 0f, 0f, 0.001f));
            viewport.raycastTarget = true;
            if (viewport.GetComponent<RectMask2D>() == null) viewport.gameObject.AddComponent<RectMask2D>();
            _descriptionScroll = viewport.GetComponent<ScrollRect>();
            if (_descriptionScroll == null) _descriptionScroll = viewport.gameObject.AddComponent<ScrollRect>();
            RectTransform content = scenarioDescriptionText.rectTransform;
            content.SetParent(viewport.transform, false);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, 112f);
            var fitter = content.GetComponent<ContentSizeFitter>();
            if (fitter == null) fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _descriptionScroll.viewport = viewport.rectTransform;
            _descriptionScroll.content = content;
            _descriptionScroll.horizontal = false;
            _descriptionScroll.vertical = true;
            _descriptionScroll.movementType = ScrollRect.MovementType.Clamped;
            _descriptionScroll.scrollSensitivity = 24f;
        }

        private void DisableDuplicateCanvases()
        {
            foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include))
            {
                if (canvas == null || canvas.gameObject == gameObject) continue;

                // Never disable another monitor. The former implementation
                // switched off every canvas that happened to initialize
                // before Display 1, including the complete Display-3 outcome
                // dashboard and safe-replay controls. Only an unnamed legacy
                // Display-1 canvas is considered a duplicate here.
                if (canvas.targetDisplay != 0)
                {
                    // A prior Play session or serialized scene state may have
                    // left the monitor disabled. Secondary displays belong to
                    // their own cameras and must always remain active.
                    if (!canvas.gameObject.activeSelf) canvas.gameObject.SetActive(true);
                    continue;
                }
                if (canvas.gameObject.name == "Canvas")
                    canvas.gameObject.SetActive(false);
            }
        }

        private static void Layout(RectTransform rect, Vector2 position, Vector2 size)
        {
            if (rect == null) return;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private void HandleStatus(string msg)
        {
            if (statusText != null) statusText.text = msg;
        }

        private void HandleScenarioStarted(ScenarioDefinition scenario)
        {
            if (statusText != null) statusText.text = $"Running: {scenario.scenarioName}";
        }

        private void HandleScenarioCompleted(ScenarioDefinition scenario, PerformanceRecord perf)
        {
            if (statusText != null)
            {
                statusText.text = $"Scenario {scenario.scenarioId:D2} complete — {perf.completionStatus} " +
                                   $"(collisions: {perf.collisionCount}, duration: {perf.episodeDuration:F1}s)";
            }
        }

        private void HandleAllCompleted()
        {
            if (statusText != null) statusText.text = "ALL SCENARIOS COMPLETED";
        }

        private void Update()
        {
            if (!Application.isPlaying || !_uiInitialized || scenarioManager == null || Time.unscaledTime < _nextTelemetryRefresh) return;
            _nextTelemetryRefresh = Time.unscaledTime + 0.2f;
            bool running = scenarioManager.IsRunning;
            var robot = scenarioManager.robotController;
            var navigation = scenarioManager.navigationManager;
            if (_operationsState != null) _operationsState.text = running
                ? $"RUNNING   /   {scenarioManager.CurrentRunMode.ToString().ToUpperInvariant()}"
                : "READY / STOPPED";
#if UNITY_EDITOR
            if (running && UnityEditor.EditorApplication.isPaused && _operationsState != null)
                _operationsState.text = "PAUSED IN UNITY EDITOR";
#endif
            if (running && Time.timeScale <= 0f && _operationsState != null)
                _operationsState.text = "PAUSED / TIME SCALE 0";
            if (_stateIndicator != null) _stateIndicator.color = running ? new Color(0.24f, 0.82f, 0.54f) : new Color(0.42f, 0.59f, 0.69f);
            if (liveStatsText != null)
            {
                string parcel = string.IsNullOrWhiteSpace(scenarioManager.CurrentParcelBarcode) ? "No parcel assigned" : scenarioManager.CurrentParcelBarcode;
                liveStatsText.text =
                    $"<color=#86AABD>EPISODE</color>  {scenarioManager.CurrentEpisodeNumber}/{Mathf.Max(1, scenarioManager.CurrentEpisodeTotal)}     " +
                    $"<color=#86AABD>TIME</color>  {scenarioManager.EpisodeElapsedSeconds:F0} / {scenarioManager.EpisodeDurationLimitSeconds:F0} s\n" +
                    $"<color=#86AABD>TASK</color>  {scenarioManager.CurrentTaskNumber}/{scenarioManager.CurrentTaskCount}  ·  {scenarioManager.CurrentTaskStage}\n" +
                    $"<color=#86AABD>AMR</color>  {(robot != null ? robot.LinearVelocity : 0f):F2} m/s  ·  Battery {scenarioManager.BatteryLevelPercent:F0}%  ·  {(navigation != null ? navigation.Status.ToString() : "Idle")}\n" +
                    $"<color=#86AABD>LOAD</color>  {(scenarioManager.IsCarryingParcel ? "SECURED" : "EMPTY")}  ·  {parcel}";
                liveStatsText.fontSize = 17f;
            }
            if (_episodeProgress != null) _episodeProgress.rectTransform.sizeDelta = new Vector2(
                476f * Mathf.Clamp01(scenarioManager.EpisodeElapsedSeconds / Mathf.Max(1f, scenarioManager.EpisodeDurationLimitSeconds)), 3f);
            if (_disturbanceText != null) _disturbanceText.text = $"<color=#E9BA65>EVENT</color>  {scenarioManager.DisturbanceStatus}";
            if (_datasetText != null) _datasetText.text = "CSV OUTPUT\n" + scenarioManager.DatasetRootPath;
            if (startButton != null) startButton.interactable = !running;
            if (runAllButton != null) runAllButton.interactable = !running;
            if (stopButton != null) stopButton.interactable = running;
            if (scenarioDropdown != null) scenarioDropdown.interactable = !running;
            if (nextButton != null) nextButton.interactable = !running;
            if (previousButton != null) previousButton.interactable = !running;
            if (_pipelineModeButton != null) _pipelineModeButton.interactable = false;
            RefreshPipelineSelector();
        }

        private void OnDestroy()
        {
            if (scenarioManager != null)
            {
                scenarioManager.OnStatusMessage -= HandleStatus;
                scenarioManager.OnScenarioStarted -= HandleScenarioStarted;
                scenarioManager.OnScenarioCompleted -= HandleScenarioCompleted;
                scenarioManager.OnAllScenariosCompleted -= HandleAllCompleted;
                scenarioManager.OnInitialized -= InitializeUI;
                scenarioManager.OnScenarioCatalogChanged -= HandleScenarioCatalogChanged;
            }
        }
    }
}
