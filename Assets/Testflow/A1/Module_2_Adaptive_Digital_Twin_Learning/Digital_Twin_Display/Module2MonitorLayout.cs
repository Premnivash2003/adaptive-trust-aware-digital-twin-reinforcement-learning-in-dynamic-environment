using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ATADTRL.UI
{
    public class Module2MonitorLayout : MonoBehaviour
    {
        private Canvas monitorCanvas;

        private RectTransform backgroundPanel;
        private RectTransform changeDetectionPanel;
        private RectTransform edatsPanel;
        private RectTransform twinStatePanel;
        private RectTransform scenarioPanel;

        private TMP_Text titleText;
        private TMP_Text changeScoreText;
        private TMP_Text changedComponentsText;
        private TMP_Text edatsText;
        private TMP_Text syncReasonText;
        private TMP_Text twinRobotText;
        private TMP_Text humanStateText;
        private TMP_Text forkliftStateText;
        private TMP_Text taskStateText;

        // ------------------------------------------------------------
        // HIGH-TECH COLOUR PALETTE
        // ------------------------------------------------------------

        private readonly Color backgroundColor =
            Hex("#071019");

        private readonly Color panelColor =
            Hex("#0D1824");

        private readonly Color panelSecondary =
            Hex("#101E2C");

        private readonly Color accentBlue =
            Hex("#00B8FF");

        private readonly Color accentCyan =
            Hex("#00E5FF");

        private readonly Color successGreen =
            Hex("#30E88B");

        private readonly Color warningOrange =
            Hex("#FFB347");

        private readonly Color dangerRed =
            Hex("#FF5566");

        private readonly Color mainText =
            Hex("#EAF6FF");

        private readonly Color secondaryText =
            Hex("#8DAABD");

        private readonly Color borderColor =
            Hex("#1E4157");

        private void Awake()
        {
            FindUI();
            BuildHighTechUI();
        }

        private void Start()
        {
            // Reapply once Canvas layout is fully initialized.
            BuildHighTechUI();
        }

        // ============================================================
        // FIND EXISTING UI
        // ============================================================

        private void FindUI()
        {
            GameObject canvasObject =
                GameObject.Find("MonitorCanvas");

            if (canvasObject == null)
            {
                Debug.LogError(
                    "[Module2MonitorLayout] MonitorCanvas not found.");
                return;
            }

            monitorCanvas =
                canvasObject.GetComponent<Canvas>();

            backgroundPanel =
                FindRect("BackgroundPanel");

            changeDetectionPanel =
                FindRect("ChangeDetectionPanel");

            edatsPanel =
                FindRect("EDATSPanel");

            twinStatePanel =
                FindRect("TwinStatePanel");

            scenarioPanel =
                FindRect("ScenarioPanel");

            titleText =
                FindTMP("TitleText");

            changeScoreText =
                FindTMP("ChangeScoreText");

            changedComponentsText =
                FindTMP("ChangedComponentsText");

            edatsText =
                FindTMP("EDATSText");

            syncReasonText =
                FindTMP("SyncReasonText");

            twinRobotText =
                FindTMP("TwinRobotText");

            humanStateText =
                FindTMP("HumanStateText");

            forkliftStateText =
                FindTMP("ForkliftStateText");

            taskStateText =
                FindTMP("TaskStateText");
        }

        // ============================================================
        // MAIN BUILD
        // ============================================================

        private void BuildHighTechUI()
        {
            if (monitorCanvas == null)
                return;

            ConfigureCanvas();
            ConfigureBackground();
            ConfigureTitle();

            ConfigurePanel(
                changeDetectionPanel,
                new Vector2(0.025f, 0.515f),
                new Vector2(0.492f, 0.875f),
                "ENVIRONMENT CHANGE",
                "01");

            ConfigurePanel(
                edatsPanel,
                new Vector2(0.508f, 0.515f),
                new Vector2(0.975f, 0.875f),
                "EDATS SYNCHRONIZATION",
                "02");

            ConfigurePanel(
                twinStatePanel,
                new Vector2(0.025f, 0.055f),
                new Vector2(0.492f, 0.485f),
                "ADAPTIVE DIGITAL TWIN",
                "03");

            ConfigurePanel(
                scenarioPanel,
                new Vector2(0.508f, 0.055f),
                new Vector2(0.975f, 0.485f),
                "MISSION / TASK STATE",
                "04");

            ConfigureExistingTexts();

            CreateTopStatusStrip();
            CreateFooter();

            Debug.Log(
                "[Module2MonitorLayout] High-tech Module 2 UI applied.");
        }

        // ============================================================
        // CANVAS
        // ============================================================

        private void ConfigureCanvas()
        {
            monitorCanvas.renderMode =
                RenderMode.ScreenSpaceOverlay;

            monitorCanvas.targetDisplay = 1;

            monitorCanvas.sortingOrder = 100;

            CanvasScaler scaler =
                monitorCanvas.GetComponent<CanvasScaler>();

            if (scaler == null)
                scaler =
                    monitorCanvas.gameObject.AddComponent<CanvasScaler>();

            scaler.uiScaleMode =
                CanvasScaler.ScaleMode.ScaleWithScreenSize;

            scaler.referenceResolution =
                new Vector2(1920f, 1080f);

            scaler.screenMatchMode =
                CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;

            scaler.matchWidthOrHeight = 0.5f;
        }

        // ============================================================
        // BACKGROUND
        // ============================================================

        private void ConfigureBackground()
        {
            if (backgroundPanel == null)
                return;

            backgroundPanel.anchorMin =
                Vector2.zero;

            backgroundPanel.anchorMax =
                Vector2.one;

            backgroundPanel.offsetMin =
                Vector2.zero;

            backgroundPanel.offsetMax =
                Vector2.zero;

            Image background =
                GetOrAddImage(backgroundPanel.gameObject);

            background.color = backgroundColor;

            backgroundPanel.SetAsFirstSibling();
        }

        // ============================================================
        // TITLE
        // ============================================================

        private void ConfigureTitle()
        {
            if (titleText == null)
                return;

            RectTransform rt =
                titleText.rectTransform;

            rt.anchorMin =
                new Vector2(0.025f, 0.915f);

            rt.anchorMax =
                new Vector2(0.74f, 0.985f);

            rt.offsetMin =
                Vector2.zero;

            rt.offsetMax =
                Vector2.zero;

            titleText.text =
                "ATADTRL DIGITAL TWIN CONTROL CENTER";

            titleText.fontSize = 34;

            titleText.fontStyle =
                FontStyles.Bold;

            titleText.alignment =
                TextAlignmentOptions.MidlineLeft;

            titleText.color =
                mainText;

            titleText.textWrappingMode =
                TextWrappingModes.NoWrap;
        }

        // ============================================================
        // PANELS
        // ============================================================

        private void ConfigurePanel(
            RectTransform panel,
            Vector2 anchorMin,
            Vector2 anchorMax,
            string header,
            string moduleNumber)
        {
            if (panel == null)
                return;

            panel.anchorMin = anchorMin;
            panel.anchorMax = anchorMax;

            panel.offsetMin =
                Vector2.zero;

            panel.offsetMax =
                Vector2.zero;

            Image image =
                GetOrAddImage(panel.gameObject);

            image.color = panelColor;

            Outline outline =
                panel.GetComponent<Outline>();

            if (outline == null)
                outline =
                    panel.gameObject.AddComponent<Outline>();

            outline.effectColor =
                borderColor;

            outline.effectDistance =
                new Vector2(1.2f, -1.2f);

            CreateAccentBar(panel);

            CreatePanelHeader(
                panel,
                header,
                moduleNumber);
        }

        private void CreateAccentBar(
            RectTransform panel)
        {
            string name = "AccentBar";

            Transform existing =
                panel.Find(name);

            RectTransform rt;

            if (existing == null)
            {
                GameObject bar =
                    new GameObject(
                        name,
                        typeof(RectTransform),
                        typeof(Image));

                bar.transform.SetParent(
                    panel,
                    false);

                rt =
                    bar.GetComponent<RectTransform>();
            }
            else
            {
                rt =
                    existing.GetComponent<RectTransform>();
            }

            rt.anchorMin =
                new Vector2(0f, 0.975f);

            rt.anchorMax =
                new Vector2(1f, 1f);

            rt.offsetMin =
                Vector2.zero;

            rt.offsetMax =
                Vector2.zero;

            rt.GetComponent<Image>().color =
                accentBlue;
        }

        private void CreatePanelHeader(
            RectTransform panel,
            string header,
            string number)
        {
            TMP_Text headerText =
                CreateOrGetTMP(
                    panel,
                    "SectionHeader");

            RectTransform rt =
                headerText.rectTransform;

            rt.anchorMin =
                new Vector2(0.04f, 0.82f);

            rt.anchorMax =
                new Vector2(0.78f, 0.965f);

            rt.offsetMin =
                Vector2.zero;

            rt.offsetMax =
                Vector2.zero;

            headerText.text =
                header;

            headerText.fontSize = 22;

            headerText.fontStyle =
                FontStyles.Bold;

            headerText.color =
                accentCyan;

            headerText.alignment =
                TextAlignmentOptions.MidlineLeft;

            TMP_Text numberText =
                CreateOrGetTMP(
                    panel,
                    "ModuleNumber");

            RectTransform numberRT =
                numberText.rectTransform;

            numberRT.anchorMin =
                new Vector2(0.80f, 0.82f);

            numberRT.anchorMax =
                new Vector2(0.95f, 0.965f);

            numberRT.offsetMin =
                Vector2.zero;

            numberRT.offsetMax =
                Vector2.zero;

            numberText.text =
                number;

            numberText.fontSize = 22;

            numberText.fontStyle =
                FontStyles.Bold;

            numberText.color =
                secondaryText;

            numberText.alignment =
                TextAlignmentOptions.MidlineRight;
        }

        // ============================================================
        // EXISTING TEXT
        // ============================================================

        private void ConfigureExistingTexts()
        {
            ConfigureText(
                changeScoreText,
                new Vector2(0.04f, 0.50f),
                new Vector2(0.96f, 0.80f),
                28);

            ConfigureText(
                changedComponentsText,
                new Vector2(0.04f, 0.07f),
                new Vector2(0.96f, 0.47f),
                19);

            ConfigureText(
                edatsText,
                new Vector2(0.04f, 0.50f),
                new Vector2(0.96f, 0.80f),
                27);

            ConfigureText(
                syncReasonText,
                new Vector2(0.04f, 0.07f),
                new Vector2(0.96f, 0.47f),
                19);

            ConfigureText(
                twinRobotText,
                new Vector2(0.04f, 0.55f),
                new Vector2(0.96f, 0.80f),
                20);

            ConfigureText(
                humanStateText,
                new Vector2(0.04f, 0.25f),
                new Vector2(0.96f, 0.53f),
                17);

            ConfigureText(
                forkliftStateText,
                new Vector2(0.04f, 0.06f),
                new Vector2(0.96f, 0.23f),
                17);

            ConfigureText(
                taskStateText,
                new Vector2(0.04f, 0.07f),
                new Vector2(0.96f, 0.80f),
                20);
        }

        private void ConfigureText(
            TMP_Text text,
            Vector2 min,
            Vector2 max,
            float fontSize)
        {
            if (text == null)
                return;

            RectTransform rt =
                text.rectTransform;

            rt.anchorMin = min;
            rt.anchorMax = max;

            rt.offsetMin =
                Vector2.zero;

            rt.offsetMax =
                Vector2.zero;

            text.fontSize =
                fontSize;

            text.color =
                mainText;

            text.alignment =
                TextAlignmentOptions.TopLeft;

            text.textWrappingMode =
                TextWrappingModes.Normal;

            text.overflowMode =
                TextOverflowModes.Overflow;
        }

        // ============================================================
        // TOP STATUS STRIP
        // ============================================================

        private void CreateTopStatusStrip()
        {
            RectTransform parent =
                monitorCanvas.transform as RectTransform;

            TMP_Text moduleBadge =
                CreateOrGetTMP(
                    parent,
                    "ModuleBadge");

            RectTransform moduleRT =
                moduleBadge.rectTransform;

            moduleRT.anchorMin =
                new Vector2(0.755f, 0.925f);

            moduleRT.anchorMax =
                new Vector2(0.86f, 0.975f);

            moduleRT.offsetMin =
                Vector2.zero;

            moduleRT.offsetMax =
                Vector2.zero;

            moduleBadge.text =
                "MODULE 02";

            moduleBadge.fontSize = 19;

            moduleBadge.fontStyle =
                FontStyles.Bold;

            moduleBadge.color =
                accentBlue;

            moduleBadge.alignment =
                TextAlignmentOptions.Center;

            TMP_Text liveBadge =
                CreateOrGetTMP(
                    parent,
                    "LiveBadge");

            RectTransform liveRT =
                liveBadge.rectTransform;

            liveRT.anchorMin =
                new Vector2(0.865f, 0.925f);

            liveRT.anchorMax =
                new Vector2(0.975f, 0.975f);

            liveRT.offsetMin =
                Vector2.zero;

            liveRT.offsetMax =
                Vector2.zero;

            liveBadge.text =
                "●  LIVE RUNTIME";

            liveBadge.fontSize = 18;

            liveBadge.fontStyle =
                FontStyles.Bold;

            liveBadge.color =
                successGreen;

            liveBadge.alignment =
                TextAlignmentOptions.Center;
        }

        // ============================================================
        // FOOTER
        // ============================================================

        private void CreateFooter()
        {
            RectTransform parent =
                monitorCanvas.transform as RectTransform;

            TMP_Text footer =
                CreateOrGetTMP(
                    parent,
                    "FooterText");

            RectTransform rt =
                footer.rectTransform;

            rt.anchorMin =
                new Vector2(0.025f, 0.005f);

            rt.anchorMax =
                new Vector2(0.975f, 0.04f);

            rt.offsetMin =
                Vector2.zero;

            rt.offsetMax =
                Vector2.zero;

            footer.text =
                "ATADTRL  |  ADAPTIVE DIGITAL TWIN LEARNING  |  UNITY RUNTIME MONITOR";

            footer.fontSize = 14;

            footer.color =
                secondaryText;

            footer.alignment =
                TextAlignmentOptions.MidlineLeft;
        }

        // ============================================================
        // HELPERS
        // ============================================================

        private RectTransform FindRect(
            string objectName)
        {
            GameObject obj =
                GameObject.Find(objectName);

            if (obj == null)
            {
                Debug.LogError(
                    "[Module2MonitorLayout] Missing: "
                    + objectName);

                return null;
            }

            return obj.GetComponent<RectTransform>();
        }

        private TMP_Text FindTMP(
            string objectName)
        {
            GameObject obj =
                GameObject.Find(objectName);

            if (obj == null)
            {
                Debug.LogError(
                    "[Module2MonitorLayout] Missing: "
                    + objectName);

                return null;
            }

            return obj.GetComponent<TMP_Text>();
        }

        private TMP_Text CreateOrGetTMP(
            RectTransform parent,
            string objectName)
        {
            Transform existing =
                parent.Find(objectName);

            TMP_Text text;

            if (existing == null)
            {
                GameObject obj =
                    new GameObject(
                        objectName,
                        typeof(RectTransform),
                        typeof(TextMeshProUGUI));

                obj.transform.SetParent(
                    parent,
                    false);

                text =
                    obj.GetComponent<TextMeshProUGUI>();

                if (titleText != null &&
                    titleText.font != null)
                {
                    text.font =
                        titleText.font;
                }
            }
            else
            {
                text =
                    existing.GetComponent<TMP_Text>();
            }

            return text;
        }

        private Image GetOrAddImage(
            GameObject obj)
        {
            Image image =
                obj.GetComponent<Image>();

            if (image == null)
                image =
                    obj.AddComponent<Image>();

            return image;
        }

        private static Color Hex(
            string hex)
        {
            if (ColorUtility.TryParseHtmlString(
                    hex,
                    out Color color))
            {
                return color;
            }

            return Color.white;
        }
    }
}
