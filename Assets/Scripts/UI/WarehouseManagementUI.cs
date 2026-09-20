using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ATADTRL.Environment;
using ATADTRL.Scenarios;

namespace ATADTRL.UI
{
    /// <summary>
    /// Runtime-built Module 1 inventory and experiment designer. The complete
    /// page is deliberately parented to ScenarioUIManager's existing Canvas;
    /// it never creates a second Canvas and therefore remains compatible with
    /// ScenarioUIManager's duplicate-canvas cleanup.
    ///
    /// Manager integration is invoked by name so this component can be added
    /// before the scenario/layout persistence APIs are compiled. The expected
    /// public methods are documented beside each invocation below.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WarehouseManagementUI : MonoBehaviour
    {
        private const string LauncherName = "InventoryDesignerLauncher";
        private const string PageName = "WarehouseManagementPage";

        private static readonly Color PageBackground = new Color(0.018f, 0.045f, 0.075f, 0.985f);
        private static readonly Color HeaderBackground = new Color(0.025f, 0.18f, 0.31f, 1f);
        private static readonly Color CardBackground = new Color(0.035f, 0.095f, 0.145f, 0.98f);
        private static readonly Color RowBackgroundA = new Color(0.055f, 0.13f, 0.19f, 0.94f);
        private static readonly Color RowBackgroundB = new Color(0.043f, 0.108f, 0.164f, 0.94f);
        private static readonly Color Accent = new Color(0.10f, 0.61f, 0.94f, 1f);
        private static readonly Color AccentDark = new Color(0.015f, 0.16f, 0.28f, 1f);
        private static readonly Color MainText = new Color(0.93f, 0.97f, 1f, 1f);
        private static readonly Color MutedText = new Color(0.67f, 0.78f, 0.86f, 1f);
        private static readonly Color Good = new Color(0.33f, 0.90f, 0.58f, 1f);
        private static readonly Color Warning = new Color(1f, 0.74f, 0.25f, 1f);
        private static readonly Color Empty = new Color(0.39f, 0.75f, 0.96f, 1f);

        private ScenarioUIManager _scenarioUI;
        private WarehouseInventory _inventory;
        private WarehouseManager _warehouseManager;
        private ScenarioManager _scenarioManager;
        private RectTransform _canvasRoot;
        private RectTransform _page;
        private Button _launcher;
        private TMP_FontAsset _font;
        private TMP_Text _pageStatus;
        private RectTransform _inventoryCard;
        private RectTransform _environmentCard;
        private RectTransform _scenarioCard;
        private RectTransform _contextCard;
        private TMP_Text _contextText;
        private TMP_Text _warehouseTotals;
        private TMP_Text _runningNotice;
        private readonly List<Button> _pageTabs = new List<Button>();
        private int _selectedTab;
        private TMP_Text _rackTitle;
        private TMP_Text _rackCounter;
        private TMP_Text _rackSummary;
        private TMP_Text _inventoryNotice;
        private readonly List<SlotRow> _slotRows = new List<SlotRow>(12);
        private readonly List<string> _rackIds = new List<string>();
        private int _rackIndex;
        private float _nextRefreshTime;
        private int _lastObservedScenarioCount = -1;
        private bool _built;

        private TMP_InputField _lengthInput;
        private TMP_InputField _widthInput;
        private TMP_InputField _rowsInput;
        private TMP_InputField _columnsInput;
        private TMP_InputField _aisleInput;
        private TMP_InputField _scenarioNameInput;
        private TMP_Text _sourceButtonText;
        private TMP_Text _destinationButtonText;
        private TMP_Text _humansButtonText;
        private TMP_Text _forkliftsButtonText;
        private TMP_Text _obstacleButtonText;
        private TMP_Text _deliveryModeButtonText;
        private TMP_Text _durationButtonText;
        private TMP_Text _customSummary;

        private readonly string[] _sources = { "P1", "P2", "P3" };
        private readonly string[] _destinations = { "D1", "D2", "D3" };
        private readonly string[] _deliveryModes =
        {
            "Station to station",
            "Station to rack",
            "Rack to station",
            "Rack to rack"
        };
        private readonly int[] _durations = { 30, 60, 90, 120 };
        private int _sourceIndex;
        private int _destinationIndex = 1;
        private int _humanCount = 5;
        private int _forkliftCount = 2;
        private bool _obstacleEnabled = true;
        private int _deliveryModeIndex;
        private int _durationIndex = 3;

        public bool IsOpen => _page != null && _page.gameObject.activeSelf;

        /// <summary>
        /// Attaches the designer to the existing Scenario UI Canvas. Calling
        /// Initialize more than once updates references without duplicating UI.
        /// </summary>
        public void Initialize(
            ScenarioUIManager scenarioUI,
            WarehouseInventory inventory,
            WarehouseManager warehouseManager,
            ScenarioManager scenarioManager)
        {
            if (_inventory != null) _inventory.InventoryChanged -= HandleInventoryChanged;
            _scenarioUI = scenarioUI;
            _inventory = inventory;
            _warehouseManager = warehouseManager;
            _scenarioManager = scenarioManager;
            if (_inventory != null) _inventory.InventoryChanged += HandleInventoryChanged;

            Canvas canvas = scenarioUI != null ? scenarioUI.GetComponentInParent<Canvas>(true) : null;
            if (canvas == null)
            {
                Debug.LogWarning("WarehouseManagementUI: no existing ScenarioUIManager Canvas was supplied.");
                return;
            }

            RectTransform canvasRect = canvas.transform as RectTransform;
            if (canvasRect == null)
            {
                Debug.LogWarning("WarehouseManagementUI: the Scenario UI Canvas has no RectTransform.");
                return;
            }

            _font = scenarioUI.scenarioDescriptionText != null
                ? scenarioUI.scenarioDescriptionText.font
                : TMP_Settings.defaultFontAsset;

            if (_built && _canvasRoot == canvasRect)
            {
                PopulateLayoutInputs();
                RefreshInventory();
                UpdateCustomControls();
                return;
            }

            _canvasRoot = canvasRect;
            RemoveOldRuntimeUi();
            BuildLauncher();
            BuildPage();
            _built = true;
            PopulateLayoutInputs();
            RefreshInventory();
            UpdateCustomControls();
            _lastObservedScenarioCount = GetScenarioCount();
        }

        public void ShowPage()
        {
            if (_page == null) return;
            _page.gameObject.SetActive(true);
            _page.SetAsLastSibling();
            SelectPageTab(_selectedTab);
            RefreshInventory();

            UpdateContextPanel();
            PopulateLayoutInputs();
            UpdateCustomControls();
            SetStatus("Inventory and experiment designer ready.", false);
        }

        public void HidePage()
        {
            if (_page != null) _page.gameObject.SetActive(false);
        }

        /// <summary>May be called by an inventory event after a slot changes.</summary>
        public void RefreshInventory()
        {
            if (_rackTitle == null) return;
            if (_warehouseTotals != null && _inventory != null)
                _warehouseTotals.text = $"CAPACITY  {_inventory.TotalCapacity}   |   OCCUPIED  {_inventory.TotalOccupied}   |   RESERVED  {_inventory.TotalReserved}   |   VACANT  {_inventory.TotalVacant}";
            if (_runningNotice != null)
                _runningNotice.text = _scenarioManager != null && _scenarioManager.IsRunning
                    ? "LIVE OPERATION  /  Stop the current episode before changing the layout or scenario."
                    : "CONFIGURATION READY  /  Layout changes rebuild navigation and reset inventory.";

            List<object> slots = GetInventorySlots();
            string selectedRack = _rackIds.Count > 0 && _rackIndex >= 0 && _rackIndex < _rackIds.Count
                ? _rackIds[_rackIndex]
                : null;

            _rackIds.Clear();
            _rackIds.AddRange(slots
                .Select(slot => ReadString(slot, "rackId", "RackId"))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase));

            if (_rackIds.Count == 0)
            {
                _rackIndex = 0;
                _rackTitle.text = "No rack inventory loaded";
                _rackCounter.text = "Rack 0 / 0";
                _rackSummary.text = "Capacity 0   •   Occupied 0   •   Reserved 0   •   Vacant 0";
                _inventoryNotice.text = _inventory == null
                    ? "WarehouseInventory is unavailable. Start Module 1 or assign the inventory reference."
                    : "The warehouse has no registered rack slots yet.";
                ClearSlotRows();
                return;
            }

            int preservedIndex = string.IsNullOrEmpty(selectedRack)
                ? -1
                : _rackIds.FindIndex(id => string.Equals(id, selectedRack, StringComparison.OrdinalIgnoreCase));
            _rackIndex = preservedIndex >= 0 ? preservedIndex : Mathf.Clamp(_rackIndex, 0, _rackIds.Count - 1);

            string rackId = _rackIds[_rackIndex];
            List<object> rackSlots = slots
                .Where(slot => string.Equals(ReadString(slot, "rackId", "RackId"), rackId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(slot => ReadString(slot, "slotId", "SlotId"), StringComparer.OrdinalIgnoreCase)
                .ToList();

            string category = rackSlots.Count > 0
                ? ReadString(rackSlots[0], "category", "Category")
                : "General";
            if (string.IsNullOrWhiteSpace(category)) category = "General";
            string displayName = GetRackDisplayName(rackId, category);
            int occupied = rackSlots.Count(slot => GetSlotState(slot) == "OCCUPIED");
            int reserved = rackSlots.Count(slot => GetSlotState(slot) == "RESERVED");
            int vacant = Mathf.Max(0, rackSlots.Count - occupied - reserved);

            _rackTitle.text = displayName.IndexOf(rackId, StringComparison.OrdinalIgnoreCase) >= 0
                ? displayName
                : $"{displayName}  <color=#8EA8BA>({rackId})</color>";
            _rackCounter.text = $"Rack {_rackIndex + 1} / {_rackIds.Count}";
            _rackSummary.text =
                $"Category  <b>{category}</b>     •     Capacity  <b>{rackSlots.Count}</b>     •     " +
                $"Occupied  <b>{occupied}</b>     •     Reserved  <b>{reserved}</b>     •     Vacant  <b>{vacant}</b>";
            _inventoryNotice.text = vacant > 0
                ? $"{vacant} barcode-addressable storage position{(vacant == 1 ? " is" : "s are")} currently available."
                : "This rack is full; a placement task must choose another valid rack or wait for a release.";
            _inventoryNotice.color = vacant > 0 ? Good : Warning;

            for (int i = 0; i < _slotRows.Count; i++)
            {
                if (i < rackSlots.Count) PopulateSlotRow(_slotRows[i], rackSlots[i]);
                else ClearSlotRow(_slotRows[i]);
            }

        }

        /// <summary>
        /// Updates the original scenario dropdown after a custom scenario is
        /// created. ScenarioUIManager.RefreshScenarioCatalog(int) is preferred;
        /// this method also contains a safe fallback for the current UI class.
        /// </summary>
        public void RefreshScenarioCatalog(int selectedIndex = -1)
        {
            if (_scenarioUI == null || _scenarioManager == null) return;

            MethodInfo refreshMethod = FindMethod(_scenarioUI, "RefreshScenarioCatalog", 1);
            if (refreshMethod != null)
            {
                try
                {
                    refreshMethod.Invoke(_scenarioUI, new object[] { selectedIndex });
                    _lastObservedScenarioCount = GetScenarioCount();
                    return;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"WarehouseManagementUI: catalog refresh API failed; using fallback. {Unwrap(ex).Message}");
                }
            }

            TMP_Dropdown dropdown = _scenarioUI.scenarioDropdown;
            if (dropdown == null) return;
            List<ScenarioDefinition> scenarios = _scenarioManager.Scenarios;
            dropdown.ClearOptions();
            dropdown.AddOptions(scenarios.Select(s => $"{s.scenarioCode}: {s.scenarioName}").ToList());
            if (scenarios.Count > 0)
            {
                int index = selectedIndex >= 0
                    ? Mathf.Clamp(selectedIndex, 0, scenarios.Count - 1)
                    : Mathf.Clamp(_scenarioManager.CurrentScenarioIndex, 0, scenarios.Count - 1);
                dropdown.SetValueWithoutNotify(index);
                _scenarioManager.SelectScenario(index);
                ScenarioDefinition scenario = scenarios[index];
                if (_scenarioUI.scenarioDescriptionText != null)
                {
                    _scenarioUI.scenarioDescriptionText.text =
                        $"<b>{scenario.scenarioCode}: {scenario.scenarioName}</b>\n" +
                        $"Category: {scenario.category}\n{scenario.description}";
                }
                if (_scenarioUI.statusText != null)
                    _scenarioUI.statusText.text = $"Selected {scenario.scenarioCode}: {scenario.scenarioName}";
            }
            _lastObservedScenarioCount = scenarios.Count;
        }

        private void Update()
        {
            if (!_built) return;
            if (IsOpen && Input.GetKeyDown(KeyCode.Escape)) HidePage();
            if (!IsOpen || Time.unscaledTime < _nextRefreshTime) return;

            _nextRefreshTime = Time.unscaledTime + 0.75f;
            // A reservation/placement changes metadata without changing the
            // slot count. Refresh while visible so the ledger also stays live
            // if a third-party inventory implementation does not raise events.
            RefreshInventory();

            int scenarioCount = GetScenarioCount();
            if (scenarioCount != _lastObservedScenarioCount) RefreshScenarioCatalog();
        }

        private void RemoveOldRuntimeUi()
        {
            if (_canvasRoot == null) return;
            Transform oldLauncher = _canvasRoot.Find(LauncherName);
            Transform oldPage = _canvasRoot.Find(PageName);
            Transform oldDataLauncher = _canvasRoot.Find("OpenDatasetFolder");
            if (oldLauncher != null) Destroy(oldLauncher.gameObject);
            if (oldPage != null) Destroy(oldPage.gameObject);
            if (oldDataLauncher != null) Destroy(oldDataLauncher.gameObject);
        }

        private void BuildLauncher()
        {
            // This is a fixed-width control inside the scenario card. The old
            // top-stretch layout anchored it to both sides of the full Canvas,
            // which produced the screen-wide blue bar visible in Game view.
            _launcher = CreateButton(
                _canvasRoot, LauncherName, "INVENTORY & DESIGN", ShowPage);
            RectTransform launcherRect = _launcher.GetComponent<RectTransform>();
            launcherRect.anchorMin = new Vector2(0f, 1f);
            launcherRect.anchorMax = new Vector2(0f, 1f);
            launcherRect.pivot = new Vector2(0f, 1f);
            launcherRect.anchoredPosition = new Vector2(30f, -ScenarioUIManager.DesignerButtonTop);
            launcherRect.sizeDelta = new Vector2(270f, 36f);
            Button dataset = CreateButton(_canvasRoot, "OpenDatasetFolder", "OPEN CSV FOLDER", OpenDatasetFolder);
            RectTransform datasetRect = dataset.GetComponent<RectTransform>();
            datasetRect.anchorMin = datasetRect.anchorMax = new Vector2(0f, 1f);
            datasetRect.pivot = new Vector2(0f, 1f);
            datasetRect.anchoredPosition = new Vector2(308f, -ScenarioUIManager.DesignerButtonTop);
            datasetRect.sizeDelta = new Vector2(186f, 36f);
            dataset.GetComponent<Image>().color = AccentDark;
            GetButtonText(dataset).color = MainText;
            GetButtonText(dataset).fontSize = 16f;
            GetButtonText(_launcher).fontSize = 18f;
        }

        private void BuildPage()
        {
            _page = CreatePanel(_canvasRoot, PageName, PageBackground);
            Stretch(_page, 0f, 0f, 1f, 1f, Vector2.zero, Vector2.zero);

            RectTransform header = CreatePanel(_page, "Header", HeaderBackground);
            SetTopStretch(header, 0f, 0f, 0f, 88f);
            CreateTextFraction(header, "Title", "WAREHOUSE CONTROL CENTRE",
                0f, 0.60f, 30f, 10f, 8f, 34f, 26f, MainText, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            CreateTextFraction(header, "Subtitle", "MODULE 01   /   INVENTORY  ·  FACILITY DESIGN  ·  EXPERIMENT CONFIGURATION",
                0f, 0.65f, 30f, 47f, 8f, 22f, 12f, MutedText, TextAlignmentOptions.MidlineLeft);
            _pageStatus = CreateTextFraction(header, "PageStatus", "Ready",
                0.65f, 0.85f, 8f, 14f, 8f, 52f, 12.5f, MainText, TextAlignmentOptions.MidlineRight);
            CreateButtonFraction(header, "BackButton", "BACK TO SIMULATION", 0.885f, 1f,
                6f, 20f, 24f, 44f, HidePage);

            string[] tabs = { "01  INVENTORY", "02  FACILITY LAYOUT", "03  SCENARIO DESIGN" };
            for (int i = 0; i < tabs.Length; i++)
            {
                int tabIndex = i;
                _pageTabs.Add(CreateButtonFraction(_page, "WorkspaceTab" + i, tabs[i],
                    0.018f + i * 0.196f, 0.208f + i * 0.196f, 0f, 104f, 0f, 40f, () => SelectPageTab(tabIndex)));
            }
            _warehouseTotals = CreateTextFraction(_page, "WarehouseTotals", "", 0.61f, 0.982f,
                0f, 104f, 0f, 40f, 12f, MutedText, TextAlignmentOptions.MidlineRight);

            BuildInventoryCard();
            BuildEnvironmentCard();
            BuildScenarioCard();
            _contextCard = CreatePanel(_page, "OperationsContextCard", CardBackground);
            Stretch(_contextCard, 0.63f, 0f, 0.982f, 1f, new Vector2(0f, 78f), new Vector2(0f, -160f));
            CreateTextTop(_contextCard, "ContextTitle", "OPERATING CONTEXT", 24f, 20f, 24f, 35f,
                21f, MainText, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            _contextText = CreateTextTop(_contextCard, "ContextDetails", "", 24f, 76f, 24f, 480f,
                16f, MutedText, TextAlignmentOptions.TopLeft);
            _runningNotice = CreateTextFraction(_page, "RunningNotice", "", 0.018f, 0.80f,
                0f, 0f, 0f, 36f, 13f, MutedText, TextAlignmentOptions.MidlineLeft);
            _runningNotice.rectTransform.anchorMin = new Vector2(0.018f, 0f);
            _runningNotice.rectTransform.anchorMax = new Vector2(0.80f, 0f);
            _runningNotice.rectTransform.pivot = new Vector2(0.5f, 0f);
            _runningNotice.rectTransform.offsetMin = new Vector2(0f, 24f);
            _runningNotice.rectTransform.offsetMax = new Vector2(0f, 60f);
            Button openData = CreateButton(_page, "WorkspaceDatasetFolder", "OPEN CSV FOLDER", OpenDatasetFolder);
            RectTransform dataRect = openData.GetComponent<RectTransform>();
            dataRect.anchorMin = new Vector2(0.84f, 0f);
            dataRect.anchorMax = new Vector2(0.982f, 0f);
            dataRect.offsetMin = new Vector2(0f, 24f);
            dataRect.offsetMax = new Vector2(0f, 60f);
            SelectPageTab(0);
            _page.gameObject.SetActive(false);
        }

        private void SelectPageTab(int index)
        {
            _selectedTab = Mathf.Clamp(index, 0, 2);
            if (_inventoryCard != null) _inventoryCard.gameObject.SetActive(_selectedTab == 0);
            if (_environmentCard != null) _environmentCard.gameObject.SetActive(_selectedTab == 1);
            if (_scenarioCard != null) _scenarioCard.gameObject.SetActive(_selectedTab == 2);
            if (_contextCard != null) _contextCard.gameObject.SetActive(_selectedTab != 0);
            for (int i = 0; i < _pageTabs.Count; i++)
            {
                _pageTabs[i].GetComponent<Image>().color = i == _selectedTab ? Accent : AccentDark;
                GetButtonText(_pageTabs[i]).color = i == _selectedTab ? AccentDark : MainText;
            }
            UpdateContextPanel();
        }

        private void UpdateContextPanel()
        {
            if (_contextText == null) return;
            if (_selectedTab == 1)
            {
                _contextText.text =
                    "<color=#F0F6FA><b>PHYSICAL FACILITY</b></color>\n\n" +
                    "Dimensions are measured in metres. Rack rows, aisle clearance and station approaches are rebuilt together.\n\n" +
                    "<color=#F0F6FA><b>ADDRESSABLE STORAGE</b></color>\n\n" +
                    "Each rack has a category, level and bay address. Occupied and reserved positions cannot accept a second parcel.\n\n" +
                    "<color=#F0F6FA><b>CONFIGURATION CONTROL</b></color>\n\n" +
                    "Stop the episode before rebuilding. Rebuilding restores initial stock and recalculates the walkable navigation surface. Keep a fixed layout when comparing baseline and later ATADTRL runs.";
            }
            else
            {
                _contextText.text =
                    "<color=#F0F6FA><b>COMPLETE MATERIAL HANDLING</b></color>\n\n" +
                    "Scan source parcel → navigate and align → pick → verify grasp → secure load → transport → place → verify release.\n\n" +
                    "<color=#F0F6FA><b>CONTROLLED EXPERIMENT</b></color>\n\n" +
                    "Choose source, destination, workers, transport vehicles and duration. Custom experiments remain separate from the controlled A1–T5 research sequence.\n\n" +
                    "<color=#F0F6FA><b>MEASURED OUTCOME</b></color>\n\n" +
                    "Episode result, collision and stop counts, task completion, sensor observations and ground truth are recorded for later synchronization and trust assessment.";
            }
        }

        private void OpenDatasetFolder()
        {
            string path = _scenarioManager != null ? _scenarioManager.DatasetRootPath : null;
            if (string.IsNullOrWhiteSpace(path) || !System.IO.Directory.Exists(path))
            {
                SetStatus("The dataset folder is not available until Module 1 initializes.", true);
                return;
            }
            Application.OpenURL(new Uri(path + System.IO.Path.DirectorySeparatorChar).AbsoluteUri);
        }

        private void BuildInventoryCard()
        {
            RectTransform card = CreatePanel(_page, "InventoryCard", CardBackground);
            _inventoryCard = card;
            Stretch(card, 0.018f, 0f, 0.982f, 1f, new Vector2(0f, 78f), new Vector2(0f, -160f));

            CreateTextTop(card, "SectionTitle", "RACK & PARCEL INVENTORY", 16f, 12f, 300f, 32f,
                21f, MainText, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            CreateButtonFraction(card, "PreviousRack", "◀  PREVIOUS RACK", 0f, 0.19f,
                16f, 53f, 5f, 35f, PreviousRack);
            _rackCounter = CreateTextFraction(card, "RackCounter", "Rack 0 / 0", 0.19f, 0.34f,
                4f, 53f, 4f, 35f, 15f, MutedText, TextAlignmentOptions.Center);
            CreateButtonFraction(card, "NextRack", "NEXT RACK  ▶", 0.34f, 0.53f,
                5f, 53f, 5f, 35f, NextRack);

            _rackTitle = CreateTextTop(card, "RackTitle", "No rack inventory loaded", 16f, 98f, 16f, 36f,
                22f, MainText, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            _rackSummary = CreateTextTop(card, "RackSummary",
                "Capacity 0   •   Occupied 0   •   Reserved 0   •   Vacant 0", 16f, 136f, 16f, 28f,
                15f, MutedText, TextAlignmentOptions.MidlineLeft);
            _inventoryNotice = CreateTextTop(card, "InventoryNotice", "Waiting for warehouse inventory...",
                16f, 166f, 16f, 25f, 14f, Warning, TextAlignmentOptions.MidlineLeft);

            RectTransform columnHeader = CreatePanel(card, "SlotColumnHeader", AccentDark);
            SetTopStretch(columnHeader, 12f, 197f, 12f, 31f);
            CreateSlotColumns(columnHeader, null, true);

            RectTransform viewport = CreatePanel(card, "SlotViewport", new Color(0.015f, 0.04f, 0.06f, 0.55f));
            viewport.anchorMin = new Vector2(0f, 0f);
            viewport.anchorMax = new Vector2(1f, 1f);
            viewport.offsetMin = new Vector2(12f, 14f);
            viewport.offsetMax = new Vector2(-12f, -232f);
            viewport.gameObject.AddComponent<RectMask2D>();

            RectTransform content = CreateRect(viewport, "SlotRows");
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, 12f * 42f + 2f);

            ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28f;

            for (int i = 0; i < 12; i++)
            {
                RectTransform rowRoot = CreatePanel(content, $"SlotRow_{i + 1:D2}",
                    i % 2 == 0 ? RowBackgroundA : RowBackgroundB);
                SetTopStretch(rowRoot, 0f, i * 42f, 0f, 40f);
                SlotRow row = new SlotRow { Root = rowRoot };
                CreateSlotColumns(rowRoot, row, false);
                _slotRows.Add(row);
            }
        }

        private void CreateSlotColumns(RectTransform parent, SlotRow row, bool header)
        {
            string[] labels = header
                ? new[] { "SLOT", "STATE", "PARCEL NO.", "BARCODE", "DETAILS", "MASS", "SOURCE → DEST." }
                : new[] { "—", "VACANT", "—", "—", "Available storage position", "—", "—" };
            float[] boundaries = { 0f, 0.095f, 0.205f, 0.34f, 0.505f, 0.72f, 0.80f, 1f };
            TMP_Text[] fields = new TMP_Text[7];
            for (int i = 0; i < fields.Length; i++)
            {
                fields[i] = CreateTextFraction(parent, $"Column_{i}", labels[i], boundaries[i], boundaries[i + 1],
                    7f, 0f, 4f, 40f, header ? 12.5f : 13.5f,
                    header ? MutedText : MainText,
                    i == 6 ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.MidlineLeft,
                    header ? FontStyles.Bold : FontStyles.Normal);
                fields[i].textWrappingMode = TextWrappingModes.NoWrap;
                fields[i].overflowMode = TextOverflowModes.Ellipsis;
            }

            if (row == null) return;
            row.Slot = fields[0];
            row.State = fields[1];
            row.Parcel = fields[2];
            row.Barcode = fields[3];
            row.Details = fields[4];
            row.Mass = fields[5];
            row.Route = fields[6];
        }

        private void BuildEnvironmentCard()
        {
            RectTransform card = CreatePanel(_page, "EnvironmentCard", CardBackground);
            _environmentCard = card;
            Stretch(card, 0.018f, 0f, 0.612f, 1f, new Vector2(0f, 78f), new Vector2(0f, -160f));

            CreateTextTop(card, "SectionTitle", "CUSTOM WAREHOUSE ENVIRONMENT", 16f, 10f, 16f, 31f,
                20f, MainText, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            CreateTextTop(card, "Guidance",
                "Edit physical dimensions, then rebuild geometry, rack inventory and the runtime NavMesh.",
                16f, 42f, 16f, 35f, 13f, MutedText, TextAlignmentOptions.TopLeft);

            _lengthInput = CreateLabeledInput(card, "Warehouse length (m)", "60", 79f);
            _widthInput = CreateLabeledInput(card, "Warehouse width (m)", "40", 125f);
            _rowsInput = CreateLabeledInput(card, "Rack rows (4–10)", "4", 171f);
            _columnsInput = CreateLabeledInput(card, "Rack columns (6–14)", "6", 217f);
            _aisleInput = CreateLabeledInput(card, "Aisle width (m)", "3", 263f);

            CreateButtonFraction(card, "ApplyLayout", "APPLY & REBUILD", 0f, 0.55f,
                16f, 318f, 5f, 40f, ApplyEnvironmentLayout);
            CreateButtonFraction(card, "RestoreLayout", "RESTORE BASELINE", 0.55f, 1f,
                5f, 318f, 16f, 40f, RestoreEnvironmentLayout);
            CreateTextTop(card, "LayoutFootnote",
                "Research compatibility keeps at least 4 rows and 6 columns so R1–R3 and A1–T5 remain addressable.\n" +
                $"Runtime CSV folder: {(_scenarioManager != null ? _scenarioManager.DatasetRootPath : "initializing...")}",
                16f, 365f, 16f, 48f, 11.5f, MutedText, TextAlignmentOptions.TopLeft);
        }

        private void BuildScenarioCard()
        {
            RectTransform card = CreatePanel(_page, "ScenarioCard", CardBackground);
            _scenarioCard = card;
            Stretch(card, 0.018f, 0f, 0.612f, 1f, new Vector2(0f, 78f), new Vector2(0f, -160f));

            CreateTextTop(card, "SectionTitle", "CUSTOM DATA-ACQUISITION SCENARIO", 16f, 10f, 16f, 31f,
                20f, MainText, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            _scenarioNameInput = CreateLabeledInput(card, "Scenario name", "Custom warehouse mission", 49f);

            Button source = CreateButtonFraction(card, "Source", "", 0f, 0.50f, 16f, 98f, 5f, 38f,
                () => { _sourceIndex = (_sourceIndex + 1) % _sources.Length; UpdateCustomControls(); });
            _sourceButtonText = GetButtonText(source);
            Button destination = CreateButtonFraction(card, "Destination", "", 0.50f, 1f, 5f, 98f, 16f, 38f,
                () => { _destinationIndex = (_destinationIndex + 1) % _destinations.Length; UpdateCustomControls(); });
            _destinationButtonText = GetButtonText(destination);

            Button humans = CreateButtonFraction(card, "Humans", "", 0f, 0.50f, 16f, 145f, 5f, 38f,
                () => { _humanCount = _humanCount >= 5 ? 1 : _humanCount + 1; UpdateCustomControls(); });
            _humansButtonText = GetButtonText(humans);
            Button forklifts = CreateButtonFraction(card, "Forklifts", "", 0.50f, 1f, 5f, 145f, 16f, 38f,
                () => { _forkliftCount = _forkliftCount >= 2 ? 0 : _forkliftCount + 1; UpdateCustomControls(); });
            _forkliftsButtonText = GetButtonText(forklifts);

            Button obstacle = CreateButtonFraction(card, "Obstacle", "", 0f, 0.50f, 16f, 192f, 5f, 38f,
                () => { _obstacleEnabled = !_obstacleEnabled; UpdateCustomControls(); });
            _obstacleButtonText = GetButtonText(obstacle);
            Button mode = CreateButtonFraction(card, "DeliveryMode", "", 0.50f, 1f, 5f, 192f, 16f, 38f,
                () => { _deliveryModeIndex = (_deliveryModeIndex + 1) % _deliveryModes.Length; UpdateCustomControls(); });
            _deliveryModeButtonText = GetButtonText(mode);

            Button duration = CreateButtonFraction(card, "Duration", "", 0f, 0.50f, 16f, 239f, 5f, 38f,
                () => { _durationIndex = (_durationIndex + 1) % _durations.Length; UpdateCustomControls(); });
            _durationButtonText = GetButtonText(duration);
            CreateButtonFraction(card, "CreateScenario", "CREATE, SAVE & SELECT", 0.50f, 1f,
                5f, 239f, 16f, 38f, CreateCustomScenario);

            _customSummary = CreateTextTop(card, "CustomSummary", "", 16f, 290f, 16f, 62f,
                13.5f, MutedText, TextAlignmentOptions.TopLeft);
            CreateTextTop(card, "ScenarioFootnote",
                "Created scenarios are additional experiments. Run All remains the controlled A1–T5 research set.",
                16f, 359f, 16f, 34f, 12.5f, MutedText, TextAlignmentOptions.TopLeft);
        }

        private void PreviousRack()
        {
            if (_rackIds.Count == 0) return;
            _rackIndex = (_rackIndex - 1 + _rackIds.Count) % _rackIds.Count;
            RefreshInventory();
        }

        private void NextRack()
        {
            if (_rackIds.Count == 0) return;
            _rackIndex = (_rackIndex + 1) % _rackIds.Count;
            RefreshInventory();
        }

        private void HandleInventoryChanged()
        {
            if (IsOpen) RefreshInventory();
        }

        private void ApplyEnvironmentLayout()
        {
            if (!TryReadFloat(_lengthInput, "warehouse length", out float length) ||
                !TryReadFloat(_widthInput, "warehouse width", out float width) ||
                !TryReadInt(_rowsInput, "rack rows", out int rows) ||
                !TryReadInt(_columnsInput, "rack columns", out int columns) ||
                !TryReadFloat(_aisleInput, "aisle width", out float aisle))
            {
                return;
            }

            if (rows < 4 || rows > 10 || columns < 6 || columns > 14)
            {
                SetStatus("Layout rejected: use 4–10 rack rows and 6–14 rack columns.", true);
                return;
            }
            if (length < 48f || length > 120f || width < 40f || width > 100f || aisle < 2.4f || aisle > 6f)
            {
                SetStatus("Layout rejected: length 48–120 m, width 40–100 m and aisle width 2.4–6 m are supported.", true);
                return;
            }

            if (_warehouseManager != null && _warehouseManager.config != null)
            {
                float requiredLength = columns * _warehouseManager.config.rackWidth +
                    Mathf.Max(0, columns - 1) * aisle + 5f;
                float requiredWidth = rows * _warehouseManager.config.rackLength +
                    (rows + 1) * aisle + 0.5f;
                if (length < requiredLength || width < requiredWidth)
                {
                    SetStatus($"Layout rejected: this rack grid needs at least {requiredLength:F1} m × {requiredWidth:F1} m.", true);
                    return;
                }
            }

            if (TryApplyLayout(length, width, rows, columns, aisle, out string message))
            {
                SetStatus(string.IsNullOrWhiteSpace(message)
                    ? "Warehouse rebuild requested; geometry, inventory and NavMesh will refresh together."
                    : message, false);
                StartCoroutine(RefreshAfterRebuild());
            }
            else
            {
                SetStatus(message, true);
            }
        }

        private void RestoreEnvironmentLayout()
        {
            if (TryRestoreLayout(out string message))
            {
                SetStatus(string.IsNullOrWhiteSpace(message) ? "Baseline warehouse layout restored." : message, false);
                StartCoroutine(RefreshAfterRebuild());
            }
            else
            {
                SetStatus(message, true);
            }
        }

        private void CreateCustomScenario()
        {
            if (_scenarioManager == null)
            {
                SetStatus("Custom scenario cannot be created: ScenarioManager is unavailable.", true);
                return;
            }

            string scenarioName = _scenarioNameInput != null ? _scenarioNameInput.text.Trim() : string.Empty;
            if (string.IsNullOrWhiteSpace(scenarioName))
            {
                SetStatus("Enter a descriptive scenario name before creating it.", true);
                return;
            }
            if (scenarioName.Length > 80)
            {
                SetStatus("Scenario name is too long; use 80 characters or fewer.", true);
                return;
            }

            string sourceId = _sources[_sourceIndex];
            string destinationId = _destinations[_destinationIndex];
            string deliveryMode = _deliveryModes[_deliveryModeIndex];
            float duration = _durations[_durationIndex];
            if (TryCreateScenario(scenarioName, sourceId, destinationId, _humanCount, _forkliftCount,
                    _obstacleEnabled, deliveryMode, duration, out int scenarioIndex, out string message))
            {
                RefreshScenarioCatalog(scenarioIndex);
                SetStatus(string.IsNullOrWhiteSpace(message)
                    ? $"Created and selected custom scenario '{scenarioName}'."
                    : message, false);
            }
            else
            {
                SetStatus(message, true);
            }
        }

        private void UpdateCustomControls()
        {
            if (_sourceButtonText != null) _sourceButtonText.text = $"SOURCE:  {_sources[_sourceIndex]}";
            if (_destinationButtonText != null) _destinationButtonText.text = $"DESTINATION:  {_destinations[_destinationIndex]}";
            if (_humansButtonText != null) _humansButtonText.text = $"ACTIVE HUMANS:  {_humanCount}";
            if (_forkliftsButtonText != null) _forkliftsButtonText.text = $"ACTIVE FORKLIFTS:  {_forkliftCount}";
            if (_obstacleButtonText != null) _obstacleButtonText.text = _obstacleEnabled
                ? "SCENARIO OBSTACLE:  YES"
                : "SCENARIO OBSTACLE:  NO";
            if (_deliveryModeButtonText != null) _deliveryModeButtonText.text = _deliveryModes[_deliveryModeIndex].ToUpperInvariant();
            if (_durationButtonText != null) _durationButtonText.text = $"LIMIT:  {_durations[_durationIndex]} SEC";
            if (_customSummary != null)
            {
                _customSummary.text =
                    $"Mission preview: {_sources[_sourceIndex]} → {_destinations[_destinationIndex]}  •  " +
                    $"{_deliveryModes[_deliveryModeIndex]}  •  {_humanCount} active humans  •  " +
                    $"{_forkliftCount} active forklifts  •  " +
                    $"{(_obstacleEnabled ? "scenario-specific obstacle" : "no injected obstacle")}.";
            }
        }

        private void PopulateLayoutInputs()
        {
            if (_warehouseManager == null || _warehouseManager.config == null) return;
            SetInputWithoutNotify(_lengthInput, _warehouseManager.config.warehouseLength.ToString("0.##", CultureInfo.InvariantCulture));
            SetInputWithoutNotify(_widthInput, _warehouseManager.config.warehouseWidth.ToString("0.##", CultureInfo.InvariantCulture));
            SetInputWithoutNotify(_rowsInput, _warehouseManager.config.rackRows.ToString(CultureInfo.InvariantCulture));
            SetInputWithoutNotify(_columnsInput, _warehouseManager.config.rackColumns.ToString(CultureInfo.InvariantCulture));
            SetInputWithoutNotify(_aisleInput, _warehouseManager.config.aisleWidth.ToString("0.##", CultureInfo.InvariantCulture));
        }

        private IEnumerator RefreshAfterRebuild()
        {
            yield return null;
            yield return null;
            PopulateLayoutInputs();
            RefreshInventory();
            RefreshScenarioCatalog();
        }

        private bool TryReadFloat(TMP_InputField input, string label, out float value)
        {
            value = 0f;
            string text = input != null ? input.text.Trim() : string.Empty;
            bool parsed = float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
                          float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
            if (!parsed || float.IsNaN(value) || float.IsInfinity(value))
                SetStatus($"Enter a valid number for {label}.", true);
            return parsed && !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private bool TryReadInt(TMP_InputField input, string label, out int value)
        {
            value = 0;
            string text = input != null ? input.text.Trim() : string.Empty;
            bool parsed = int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
            if (!parsed) SetStatus($"Enter a whole number for {label}.", true);
            return parsed;
        }

        private bool TryApplyLayout(float length, float width, int rows, int columns, float aisleWidth, out string message)
        {
            // Expected API on ScenarioManager (preferred) or WarehouseManager:
            // bool TryApplyWarehouseLayout(float, float, int, int, float, out string)
            object[] arguments = { length, width, rows, columns, aisleWidth, string.Empty };
            if (TryInvoke(_scenarioManager, "TryApplyWarehouseLayout", arguments, out object result, out Exception error) ||
                TryInvoke(_warehouseManager, "TryApplyWarehouseLayout", arguments, out result, out error))
            {
                message = arguments[5] as string ?? string.Empty;
                return result is bool success && success;
            }

            message = error != null
                ? $"Warehouse rebuild failed: {Unwrap(error).Message}"
                : "Warehouse rebuild API is not available yet (TryApplyWarehouseLayout).";
            return false;
        }

        private bool TryRestoreLayout(out string message)
        {
            // Expected API on ScenarioManager (preferred) or WarehouseManager:
            // bool TryRestoreBaselineWarehouseLayout(out string)
            object[] arguments = { string.Empty };
            if (TryInvoke(_scenarioManager, "TryRestoreBaselineWarehouseLayout", arguments, out object result, out Exception error) ||
                TryInvoke(_warehouseManager, "TryRestoreBaselineWarehouseLayout", arguments, out result, out error))
            {
                message = arguments[0] as string ?? string.Empty;
                return result is bool success && success;
            }

            message = error != null
                ? $"Baseline restore failed: {Unwrap(error).Message}"
                : "Baseline restore API is not available yet (TryRestoreBaselineWarehouseLayout).";
            return false;
        }

        private bool TryCreateScenario(
            string scenarioName,
            string sourceId,
            string destinationId,
            int humanCount,
            int forkliftCount,
            bool obstacleEnabled,
            string deliveryMode,
            float durationSeconds,
            out int scenarioIndex,
            out string message)
        {
            // Expected API on ScenarioManager:
            // bool TryCreateCustomScenario(string, string, string, int, int,
            //     bool, string, float, out int, out string)
            object[] arguments =
            {
                scenarioName, sourceId, destinationId, humanCount, forkliftCount,
                obstacleEnabled, deliveryMode, durationSeconds, -1, string.Empty
            };
            if (TryInvoke(_scenarioManager, "TryCreateCustomScenario", arguments, out object result, out Exception error))
            {
                scenarioIndex = arguments[8] is int index ? index : -1;
                message = arguments[9] as string ?? string.Empty;
                return result is bool success && success;
            }

            scenarioIndex = -1;
            message = error != null
                ? $"Custom scenario creation failed: {Unwrap(error).Message}"
                : "Custom scenario API is not available yet (TryCreateCustomScenario).";
            return false;
        }

        private static bool TryInvoke(
            object target,
            string methodName,
            object[] arguments,
            out object result,
            out Exception error)
        {
            result = null;
            error = null;
            if (target == null) return false;
            MethodInfo method = FindMethod(target, methodName, arguments.Length);
            if (method == null) return false;
            try
            {
                result = method.Invoke(target, arguments);
                return true;
            }
            catch (Exception ex)
            {
                error = ex;
                return false;
            }
        }

        private static MethodInfo FindMethod(object target, string name, int parameterCount)
        {
            if (target == null) return null;
            return target.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(method => method.Name == name && method.GetParameters().Length == parameterCount);
        }

        private List<object> GetInventorySlots()
        {
            var result = new List<object>();
            if (_inventory == null) return result;
            object value = ReadMember(_inventory, "Slots", "slots");
            if (!(value is IEnumerable enumerable)) return result;
            foreach (object slot in enumerable)
                if (slot != null) result.Add(slot);
            return result;
        }

        private string GetRackDisplayName(string rackId, string category)
        {
            foreach (string methodName in new[] { "GetRackDisplayName", "GetRackName" })
            {
                MethodInfo method = FindMethod(_inventory, methodName, 1);
                if (method == null) continue;
                try
                {
                    string value = method.Invoke(_inventory, new object[] { rackId }) as string;
                    if (!string.IsNullOrWhiteSpace(value)) return value;
                }
                catch (Exception) { }
            }
            return $"{category} Rack";
        }

        private void PopulateSlotRow(SlotRow row, object slot)
        {
            string state = GetSlotState(slot);
            object parcel = ReadMember(slot, "currentParcel", "CurrentParcel", "parcel", "Parcel", "parcelRecord", "ParcelRecord");
            string slotId = ReadString(slot, "slotId", "SlotId");
            string parcelNumber = ReadFirstString(parcel, slot,
                new[] { "parcelNumber", "ParcelNumber", "parcelId", "ParcelId", "number", "Number" });
            string barcode = ReadFirstString(parcel, slot,
                new[] { "barcode", "Barcode", "parcelBarcode", "ParcelBarcode" });
            string details = ReadFirstString(parcel, slot,
                new[] { "details", "Details", "description", "Description", "parcelDetails", "ParcelDetails" });
            float mass = ReadFirstFloat(parcel, slot,
                new[] { "massKg", "MassKg", "mass", "Mass", "parcelMass", "ParcelMass" });
            string source = ReadFirstString(parcel, slot,
                new[] { "sourceId", "SourceId", "source", "Source" });
            string destination = ReadFirstString(parcel, slot,
                new[] { "destinationId", "DestinationId", "destination", "Destination" });

            bool hasParcel = state == "OCCUPIED" || state == "RESERVED";
            row.Root.gameObject.SetActive(true);
            row.Slot.text = ShortSlotId(slotId);
            row.State.text = state == "EMPTY" ? "VACANT" : state;
            row.State.color = state == "OCCUPIED" ? Good : state == "RESERVED" ? Warning : Empty;
            row.Parcel.text = hasParcel ? ValueOr(parcelNumber, state == "RESERVED" ? "Pending" : "Stock item") : "—";
            row.Barcode.text = hasParcel ? ValueOr(barcode, "Unscanned") : "—";
            row.Details.text = hasParcel ? ValueOr(details, state == "RESERVED" ? "Reserved placement" : "Stored parcel") : "Available storage position";
            row.Mass.text = hasParcel && mass > 0f ? $"{mass:0.0} kg" : "—";
            row.Route.text = hasParcel && (!string.IsNullOrWhiteSpace(source) || !string.IsNullOrWhiteSpace(destination))
                ? $"{ValueOr(source, "—")} → {ValueOr(destination, "—")}" : "—";
        }

        private static string GetSlotState(object slot)
        {
            object stateValue = ReadMember(slot, "state", "State", "slotState", "SlotState");
            if (stateValue != null)
            {
                string state = stateValue.ToString().Trim().ToUpperInvariant();
                if (state.Contains("RESERV")) return "RESERVED";
                if (state.Contains("OCCUP") || state.Contains("STOCK")) return "OCCUPIED";
                return "EMPTY";
            }

            object reserved = ReadMember(slot, "reserved", "Reserved", "isReserved", "IsReserved");
            if (reserved is bool isReserved && isReserved) return "RESERVED";
            object occupied = ReadMember(slot, "occupied", "Occupied", "isOccupied", "IsOccupied");
            return occupied is bool isOccupied && isOccupied ? "OCCUPIED" : "EMPTY";
        }

        private void ClearSlotRows()
        {
            foreach (SlotRow row in _slotRows) ClearSlotRow(row);
        }

        private static void ClearSlotRow(SlotRow row)
        {
            if (row == null || row.Root == null) return;
            row.Root.gameObject.SetActive(false);
        }

        private int GetScenarioCount()
        {
            return _scenarioManager != null && _scenarioManager.Scenarios != null
                ? _scenarioManager.Scenarios.Count
                : 0;
        }

        private void SetStatus(string message, bool error)
        {
            if (string.IsNullOrWhiteSpace(message)) message = error ? "Operation failed." : "Ready.";
            if (_pageStatus != null)
            {
                _pageStatus.text = message;
                _pageStatus.color = error ? new Color(1f, 0.50f, 0.42f, 1f) : MainText;
            }
            if (_scenarioUI != null && _scenarioUI.statusText != null)
                _scenarioUI.statusText.text = message;
            if (error) Debug.LogWarning($"WarehouseManagementUI: {message}");
            else Debug.Log($"WarehouseManagementUI: {message}");
        }

        private static object ReadMember(object target, params string[] names)
        {
            if (target == null) return null;
            Type type = target.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (string name in names)
            {
                PropertyInfo property = type.GetProperty(name, flags);
                if (property != null && property.GetIndexParameters().Length == 0)
                {
                    try { return property.GetValue(target, null); }
                    catch (Exception) { }
                }
                FieldInfo field = type.GetField(name, flags);
                if (field != null)
                {
                    try { return field.GetValue(target); }
                    catch (Exception) { }
                }
            }

            foreach (PropertyInfo property in type.GetProperties(flags))
            {
                if (property.GetIndexParameters().Length != 0 || !names.Any(name =>
                        string.Equals(name, property.Name, StringComparison.OrdinalIgnoreCase))) continue;
                try { return property.GetValue(target, null); }
                catch (Exception) { }
            }
            foreach (FieldInfo field in type.GetFields(flags))
            {
                if (!names.Any(name => string.Equals(name, field.Name, StringComparison.OrdinalIgnoreCase))) continue;
                try { return field.GetValue(target); }
                catch (Exception) { }
            }
            return null;
        }

        private static string ReadString(object target, params string[] names)
        {
            object value = ReadMember(target, names);
            return value != null ? Convert.ToString(value, CultureInfo.InvariantCulture) : string.Empty;
        }

        private static string ReadFirstString(object primary, object fallback, string[] names)
        {
            string value = ReadString(primary, names);
            return string.IsNullOrWhiteSpace(value) ? ReadString(fallback, names) : value;
        }

        private static float ReadFirstFloat(object primary, object fallback, string[] names)
        {
            object value = ReadMember(primary, names) ?? ReadMember(fallback, names);
            if (value == null) return 0f;
            try { return Convert.ToSingle(value, CultureInfo.InvariantCulture); }
            catch (Exception) { return 0f; }
        }

        private static string ShortSlotId(string slotId)
        {
            if (string.IsNullOrWhiteSpace(slotId)) return "—";
            int separator = slotId.LastIndexOf('-');
            return separator >= 0 && separator < slotId.Length - 1 ? slotId.Substring(separator + 1) : slotId;
        }

        private static string ValueOr(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private static Exception Unwrap(Exception exception)
        {
            return exception is TargetInvocationException invocation && invocation.InnerException != null
                ? invocation.InnerException
                : exception;
        }

        private void OnDestroy()
        {
            if (_inventory != null) _inventory.InventoryChanged -= HandleInventoryChanged;
        }

        private RectTransform CreatePanel(Transform parent, string name, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            Image image = go.GetComponent<Image>();
            image.color = color;
            return rect;
        }

        private RectTransform CreateRect(Transform parent, string name)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private TMP_Text CreateTextTop(
            Transform parent,
            string name,
            string value,
            float left,
            float top,
            float right,
            float height,
            float fontSize,
            Color color,
            TextAlignmentOptions alignment,
            FontStyles style = FontStyles.Normal)
        {
            TMP_Text text = CreateText(parent, name, value, fontSize, color, alignment, style);
            SetTopStretch(text.rectTransform, left, top, right, height);
            return text;
        }

        private TMP_Text CreateTextFraction(
            Transform parent,
            string name,
            string value,
            float minX,
            float maxX,
            float leftInset,
            float top,
            float rightInset,
            float height,
            float fontSize,
            Color color,
            TextAlignmentOptions alignment,
            FontStyles style = FontStyles.Normal)
        {
            TMP_Text text = CreateText(parent, name, value, fontSize, color, alignment, style);
            SetTopFraction(text.rectTransform, minX, maxX, leftInset, top, rightInset, height);
            return text;
        }

        private TMP_Text CreateText(
            Transform parent,
            string name,
            string value,
            float fontSize,
            Color color,
            TextAlignmentOptions alignment,
            FontStyles style)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
            if (_font != null) text.font = _font;
            text.text = value;
            text.fontSize = Mathf.Max(16f, fontSize);
            text.enableAutoSizing = false;
            text.fontStyle = style;
            text.color = color;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.richText = true;
            text.raycastTarget = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            return text;
        }

        private Button CreateButtonTop(
            Transform parent,
            string name,
            string label,
            float left,
            float top,
            float right,
            float height,
            UnityEngine.Events.UnityAction action)
        {
            Button button = CreateButton(parent, name, label, action);
            SetTopStretch(button.GetComponent<RectTransform>(), left, top, right, height);
            return button;
        }

        private Button CreateButtonFraction(
            Transform parent,
            string name,
            string label,
            float minX,
            float maxX,
            float leftInset,
            float top,
            float rightInset,
            float height,
            UnityEngine.Events.UnityAction action)
        {
            Button button = CreateButton(parent, name, label, action);
            SetTopFraction(button.GetComponent<RectTransform>(), minX, maxX, leftInset, top, rightInset, height);
            return button;
        }

        private Button CreateButton(Transform parent, string name, string label, UnityEngine.Events.UnityAction action)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            Image image = go.GetComponent<Image>();
            image.color = Accent;
            Button button = go.GetComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
            colors.pressedColor = new Color(0.72f, 0.82f, 0.90f, 1f);
            colors.selectedColor = Color.white;
            button.colors = colors;
            if (action != null) button.onClick.AddListener(action);

            TMP_Text buttonText = CreateText(go.transform, "Label", label, 14f, AccentDark,
                TextAlignmentOptions.Center, FontStyles.Bold);
            Stretch(buttonText.rectTransform, 0f, 0f, 1f, 1f, new Vector2(5f, 2f), new Vector2(-5f, -2f));
            return button;
        }

        private TMP_InputField CreateLabeledInput(Transform parent, string label, string initialValue, float top)
        {
            CreateTextFraction(parent, label + "_Label", label, 0f, 0.43f, 16f, top, 6f, 36f,
                14f, MainText, TextAlignmentOptions.MidlineLeft);

            GameObject inputObject = new GameObject(label + "_Input", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            RectTransform inputRect = inputObject.GetComponent<RectTransform>();
            inputRect.SetParent(parent, false);
            SetTopFraction(inputRect, 0.43f, 1f, 6f, top, 16f, 36f);
            inputObject.GetComponent<Image>().color = new Color(0.93f, 0.96f, 0.98f, 1f);

            RectTransform textArea = CreateRect(inputRect, "Text Area");
            Stretch(textArea, 0f, 0f, 1f, 1f, new Vector2(9f, 3f), new Vector2(-9f, -3f));
            textArea.gameObject.AddComponent<RectMask2D>();

            TMP_Text placeholder = CreateText(textArea, "Placeholder", "Enter value", 15f,
                new Color(0.35f, 0.42f, 0.47f, 0.72f), TextAlignmentOptions.MidlineLeft, FontStyles.Italic);
            Stretch(placeholder.rectTransform, 0f, 0f, 1f, 1f, Vector2.zero, Vector2.zero);
            TMP_Text inputText = CreateText(textArea, "Text", initialValue, 15f,
                new Color(0.025f, 0.075f, 0.11f, 1f), TextAlignmentOptions.MidlineLeft, FontStyles.Normal);
            Stretch(inputText.rectTransform, 0f, 0f, 1f, 1f, Vector2.zero, Vector2.zero);
            inputText.textWrappingMode = TextWrappingModes.NoWrap;

            TMP_InputField input = inputObject.GetComponent<TMP_InputField>();
            input.textViewport = textArea;
            input.textComponent = inputText;
            input.placeholder = placeholder;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.contentType = TMP_InputField.ContentType.Standard;
            input.SetTextWithoutNotify(initialValue);
            return input;
        }

        private static TMP_Text GetButtonText(Button button)
        {
            return button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;
        }

        private static void SetInputWithoutNotify(TMP_InputField field, string value)
        {
            if (field != null) field.SetTextWithoutNotify(value);
        }

        private static void SetTopStretch(RectTransform rect, float left, float top, float right, float height)
        {
            if (rect == null) return;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(left, -top - height);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private static void SetTopFraction(
            RectTransform rect,
            float minX,
            float maxX,
            float leftInset,
            float top,
            float rightInset,
            float height)
        {
            if (rect == null) return;
            rect.anchorMin = new Vector2(minX, 1f);
            rect.anchorMax = new Vector2(maxX, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(leftInset, -top - height);
            rect.offsetMax = new Vector2(-rightInset, -top);
        }

        private static void Stretch(
            RectTransform rect,
            float minX,
            float minY,
            float maxX,
            float maxY,
            Vector2 offsetMin,
            Vector2 offsetMax)
        {
            if (rect == null) return;
            rect.anchorMin = new Vector2(minX, minY);
            rect.anchorMax = new Vector2(maxX, maxY);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        private sealed class SlotRow
        {
            public RectTransform Root;
            public TMP_Text Slot;
            public TMP_Text State;
            public TMP_Text Parcel;
            public TMP_Text Barcode;
            public TMP_Text Details;
            public TMP_Text Mass;
            public TMP_Text Route;
        }
    }
}
