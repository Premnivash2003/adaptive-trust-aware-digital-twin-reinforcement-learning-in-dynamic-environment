using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using ATADTRL.Pipeline;

namespace ATADTRL.UI
{
    /// <summary>Display 3: Module 3/4 explainability and A1 shadow outcome.</summary>
    [DisallowMultipleComponent]
    public sealed class ATADTRLOutcomeDashboard : MonoBehaviour
    {
        private ATADTRLPipelineManager pipeline;
        private Camera display3Camera;
        private TMP_Text trustText, contextText, decisionText, outcomeText, sampleText;
        private TMP_Text changeText, applyButtonLabel;
        private TMP_Text contextRobotText, contextHazardText;
        private Image missionProgress;
        private Button applyButton;
        private GameObject trustContextPage, policyOutcomePage;
        private Button pageSwitchButton;
        private TMP_Text pageSwitchLabel;
        private bool showPolicyOutcomePage;
        private readonly Dictionary<string, Image> policyBars = new Dictionary<string, Image>();
        private readonly Dictionary<string, TMP_Text> policyValues = new Dictionary<string, TMP_Text>();
        private readonly Color background = Hex("#040A101A");
        private readonly Color panel = Hex("#091722D9");
        private readonly Color panel2 = Hex("#0E2230D9");
        private readonly Color cyan = Hex("#00D8FF");
        private readonly Color green = Hex("#25E59A");
        private readonly Color amber = Hex("#FFC15A");
        private readonly Color muted = Hex("#7799AA");
        private readonly Color white = Hex("#EAF6FC");

        private void Awake()
        {
            pipeline = GetComponent<ATADTRLPipelineManager>();
            if (Display.displays.Length > 2) Display.displays[2].Activate();
            EnsureDisplay3Camera();
            Build();
            EnsureSingleEventSystem();
            EnsureSingleAudioListener();
        }

        private void EnsureDisplay3Camera()
        {
            const string cameraName = "ATADTRL_Display3_Camera";
            GameObject cameraObject = GameObject.Find(cameraName);
            if (cameraObject == null)
            {
                cameraObject = new GameObject(cameraName, typeof(Camera));
                DontDestroyOnLoad(cameraObject);
            }

            display3Camera = cameraObject.GetComponent<Camera>();
            display3Camera.targetDisplay = 2;
            display3Camera.clearFlags = CameraClearFlags.SolidColor;
            display3Camera.backgroundColor = background;
            display3Camera.cullingMask = ~0;
            display3Camera.depth = -100f;
            display3Camera.orthographic = true;
            display3Camera.transform.position = new Vector3(0f, 1000f, -1000f);
            display3Camera.transform.rotation = Quaternion.identity;
            display3Camera.enabled = true;
        }

        private void Build()
        {
            GameObject canvasObject = new GameObject("ATADTRL_Display3_Outcome", typeof(RectTransform),
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.layer = ATADTRL.TestFlow.A1.UI.A1ShadowSimulationDisplay.ShadowLayer;
            DontDestroyOnLoad(canvasObject);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            // A camera-space canvas can be physically occluded by a parcel or
            // rack near the camera. Display 3 uses a true overlay so the
            // compact dashboard always remains readable above the warehouse.
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.worldCamera = null;
            canvas.targetDisplay = 2;
            canvas.sortingOrder = 1200;
            canvasObject.hideFlags = HideFlags.HideInHierarchy;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            // Keep the warehouse view dominant. All explainability cards are
            // grouped into one compact top-right dashboard instead of being
            // spread across the camera image.
            RectTransform root = Panel(canvas.transform, "Root", Vector2.zero, Vector2.one, Color.clear);
            // A presentation-sized left dashboard.  The earlier 26%-wide dock
            // forced meaningful labels and values into ellipses.  This uses
            // enough width and height for an end-user view while leaving more
            // than half of Display 3 unobstructed for the warehouse replay.
            RectTransform dock = Panel(root, "CompactDashboard", new Vector2(.012f, .12f),
                new Vector2(.46f, .985f), new Color(.02f, .045f, .065f, .92f));
            RectTransform header = Panel(dock, "Header", new Vector2(.025f, .875f), new Vector2(.975f, .985f), panel);
            Text(header, "Title", "ATADTRL  /  SAFE REPLAY", new Vector2(.04f, .16f),
                new Vector2(.96f, .88f), 24, white, FontStyles.Bold);

            RectTransform pageArea = Panel(dock, "PageArea", new Vector2(.025f, .105f),
                new Vector2(.975f, .86f), Color.clear);
            trustContextPage = Panel(pageArea, "TrustContextPage", Vector2.zero, Vector2.one, Color.clear).gameObject;
            policyOutcomePage = Panel(pageArea, "PolicyOutcomePage", Vector2.zero, Vector2.one, Color.clear).gameObject;

            RectTransform trust = Card(trustContextPage.GetComponent<RectTransform>(), "MODULE 3  /  TRUST ASSESSMENT", "MULTI-SOURCE RELIABILITY",
                new Vector2(0f, .515f), new Vector2(1f, 1f));
            trustText = Text(trust, "TrustValues", "Waiting for trusted observations...", new Vector2(.06f, .06f),
                new Vector2(.94f, .73f), 17, white);

            RectTransform context = Card(trustContextPage.GetComponent<RectTransform>(), "CONTEXT LEARNING", "BLIND-CORNER FUTURE STATE",
                new Vector2(0f, 0f), new Vector2(1f, .485f));
            contextText = Text(context, "Context", "Waiting for context samples...", new Vector2(.06f, .48f),
                new Vector2(.94f, .73f), 16, white);
            BuildContextDiagram(context);

            RectTransform policy = Card(policyOutcomePage.GetComponent<RectTransform>(), "MODULE 4  /  SECURE SCENARIO POLICY", "ACTION DISTRIBUTION",
                new Vector2(0f, .52f), new Vector2(1f, 1f));
            decisionText = Text(policy, "Decision", "Waiting for policy inference...", new Vector2(.06f, .47f),
                new Vector2(.94f, .74f), 14, white, FontStyles.Bold);
            BuildPolicyBars(policy);

            RectTransform update = Card(policyOutcomePage.GetComponent<RectTransform>(), "LEARNED UPDATE PACKAGE", "WHAT CHANGED / WHICH MODULE HELPED",
                new Vector2(0f, .19f), new Vector2(1f, .50f));
            changeText = Text(update, "Changes",
                "Run the Display-1 A1 baseline first.\n\nAfter the collision, Modules 2–4 prepare a safe replay update.",
                new Vector2(.055f, .31f), new Vector2(.945f, .73f), 15, white);
            applyButton = ActionButton(update, "ApplyUpdate", "WAITING FOR A1 COLLISION",
                new Vector2(.055f, .06f), new Vector2(.945f, .255f));
            applyButton.onClick.AddListener(ApplyUpdate);
            applyButtonLabel = applyButton.GetComponentInChildren<TMP_Text>();

            RectTransform outcome = Card(policyOutcomePage.GetComponent<RectTransform>(), "SAFE REPLAY OUTCOME", "DISPLAY 1 BASELINE REMAINS UNCHANGED",
                new Vector2(0f, 0f), new Vector2(1f, .17f));
            outcomeText = Text(outcome, "Outcome", "Start A1 on Display 1. The safe replay becomes available after its collision.",
                new Vector2(.035f, .40f), new Vector2(.965f, .73f), 15, white, FontStyles.Bold);
            sampleText = Text(outcome, "Samples", "", new Vector2(.035f, .18f), new Vector2(.965f, .40f), 11, muted);
            Image track = Panel(outcome, "ProgressTrack", new Vector2(.035f, .07f), new Vector2(.965f, .105f), panel2).GetComponent<Image>();
            missionProgress = Panel(track.rectTransform, "Progress", Vector2.zero, new Vector2(0f, 1f), green).GetComponent<Image>();

            pageSwitchButton = ActionButton(dock, "PageSwitch", "SHOW POLICY + OUTCOME  >",
                new Vector2(.025f, .018f), new Vector2(.975f, .090f));
            pageSwitchLabel = pageSwitchButton.GetComponentInChildren<TMP_Text>();
            pageSwitchButton.onClick.AddListener(ToggleDashboardPage);
            ApplyDashboardPage();

        }

        private void ToggleDashboardPage()
        {
            showPolicyOutcomePage = !showPolicyOutcomePage;
            ApplyDashboardPage();
        }

        private void ApplyDashboardPage()
        {
            if (trustContextPage != null) trustContextPage.SetActive(!showPolicyOutcomePage);
            if (policyOutcomePage != null) policyOutcomePage.SetActive(showPolicyOutcomePage);
            if (pageSwitchLabel != null)
                pageSwitchLabel.text = showPolicyOutcomePage
                    ? "<  SHOW TRUST + CONTEXT"
                    : "SHOW POLICY + OUTCOME  >";
        }

        private void BuildContextDiagram(RectTransform parent)
        {
            RectTransform zone = Panel(parent, "PredictionZone", new Vector2(.07f, .09f), new Vector2(.93f, .45f), panel2);
            contextRobotText = Text(zone, "Robot", "AMR  ─────►  BLIND CORNER", new Vector2(.06f, .58f),
                new Vector2(.94f, .91f), 14, cyan, FontStyles.Bold);
            contextRobotText.alignment = TextAlignmentOptions.Center;
            contextHazardText = Text(zone, "Human", "H1   ─────►  PREDICTED CONFLICT", new Vector2(.06f, .12f),
                new Vector2(.94f, .45f), 14, amber, FontStyles.Bold);
            contextHazardText.alignment = TextAlignmentOptions.Center;
        }

        private void BuildPolicyBars(RectTransform parent)
        {
            string[] labels = { "FORWARD", "SLOW", "WAIT", "LEFT", "RIGHT" };
            for (int i = 0; i < labels.Length; i++)
            {
                float top = .38f - i * .070f;
                RectTransform track = Panel(parent, "Track_" + labels[i], new Vector2(.06f, top + .006f),
                    new Vector2(.94f, top + .060f), panel2);
                Image fill = Panel(track, "Fill", Vector2.zero, new Vector2(.02f, 1f),
                    labels[i] == "WAIT" ? amber : cyan).GetComponent<Image>();
                policyBars[labels[i]] = fill;
                TMP_Text value = Text(track, "Meaning_" + labels[i], ActionMeaning(labels[i]) + "    0%",
                    new Vector2(.025f, .05f), new Vector2(.975f, .95f), 12, white, FontStyles.Bold);
                value.alignment = TextAlignmentOptions.MidlineLeft;
                value.overflowMode = TextOverflowModes.Overflow;
                policyValues[labels[i]] = value;
            }
        }

        private void Update()
        {
            if (pipeline == null || trustText == null || contextText == null || decisionText == null ||
                outcomeText == null || sampleText == null || changeText == null)
                return;
            trustText.text =
                Row("LiDAR", pipeline.LidarTrust) + Row("RGB Camera", pipeline.CameraTrust) +
                Row("IMU", pipeline.ImuTrust) + Row("Wheel Encoder", pipeline.EncoderTrust) +
                Row("Communication", pipeline.CommunicationTrust) +
                $"\n<color=#00D8FF><b>TRUSTED STATE  {pipeline.OverallTrust * 100f:F0}%</b></color>";
            contextText.text =
                $"{pipeline.ContextStatus}\n\n" +
                $"Risk score              <b>{pipeline.CollisionRisk:F3}</b>\n" +
                $"Minimum separation      <b>{pipeline.PredictedMinimumSeparation:F2} m</b>\n" +
                $"Time to closest point   <b>{pipeline.TimeToClosestApproach:F2} s</b>";
            string modelStatus = pipeline.ActiveScenarioCode != "A1" || pipeline.PPOModelLoaded
                ? "#25E59A" : "#FF5364";
            string tamInput = pipeline.ActiveScenarioCode == "T3"
                ? "LiDAR DOWN-WEIGHTED  •  RGB + IMU + ENCODER TRUSTED"
                : pipeline.ActiveScenarioCode == "A4"
                    ? "SYNCHRONIZED RACK + DESTINATION STATE"
                    : "TRUSTED TWIN + CONTEXT STATE";
            string displayedDecision = pipeline.ActiveScenarioCode == "T3" && pipeline.ReplayCompleted
                ? "FUSED NAVIGATE → D2 COMPLETE"
                : ActionLabel(pipeline.SelectedAction);
            decisionText.text =
                $"PPO MODEL   <color={modelStatus}>{pipeline.ActivePolicyId}</color>\n" +
                $"TAM INPUT   <color=#00D8FF>{tamInput}</color>\n" +
                $"DECISION    <color=#25E59A>{displayedDecision}</color>\n" +
                $"WHY         <color=#EAF6FC>{pipeline.DecisionReason}</color>";
            if (contextRobotText != null && contextHazardText != null)
            {
                if (pipeline.ActiveScenarioCode == "A4")
                {
                    contextRobotText.text = "AMR  ─────►  STALE RACK GOAL";
                    contextHazardText.text = "RACK_4_3  ─────►  RELOCATED TARGET";
                }
                else if (pipeline.ActiveScenarioCode == "T3")
                {
                    contextRobotText.text = "LIDAR  ─────►  DOWN-WEIGHTED";
                    contextHazardText.text = "RGB + IMU + ENCODER  ─────►  TRUSTED FUSION";
                }
                else
                {
                    contextRobotText.text = "AMR  ─────►  BLIND CORNER";
                    contextHazardText.text = "H1   ─────►  PREDICTED CONFLICT";
                }
            }
            SetBar("FORWARD", pipeline.ForwardProbability);
            SetBar("SLOW", pipeline.SlowProbability);
            SetBar("WAIT", pipeline.WaitProbability);
            SetBar("LEFT", pipeline.LeftProbability);
            SetBar("RIGHT", pipeline.RightProbability);
            outcomeText.text =
                $"<color=#00D8FF>{pipeline.ShadowMissionStage}</color>\n\n" +
                $"RESULT   <color=#25E59A>{pipeline.ShadowOutcome}</color>";
            changeText.text = pipeline.ReplayReady || pipeline.ReplayRunning || pipeline.ReplayCompleted
                ? $"<color=#00D8FF><b>POLICY REVISION {pipeline.PolicyRevision}</b></color>\n\n{pipeline.AppliedChanges}"
                : "<color=#7799AA><b>NO UPDATE PACKAGE YET</b></color>\n\n" +
                  $"Display 2 is monitoring continuously. Start {pipeline.ActiveScenarioCode} on Display 1 to trigger its scenario update.";
            UpdateApplyButton();
            sampleText.text =
                $"Context samples  {pipeline.ContextSampleCount}     •     Trusted states  {pipeline.TrustedStateCount}     •     Policy decisions  {pipeline.DecisionCount}\n" +
                "Display 1 records the unchanged Module-1 collision. Display 3 runs only after the operator applies the update.";
            if (missionProgress != null)
            {
                RectTransform rt = missionProgress.rectTransform;
                rt.anchorMax = new Vector2(Mathf.Clamp01(pipeline.ShadowMissionProgress), 1f);
                rt.offsetMin = rt.offsetMax = Vector2.zero;
            }
        }

        private static void EnsureSingleEventSystem()
        {
            EventSystem[] systems = FindObjectsByType<EventSystem>(FindObjectsInactive.Include);
            EventSystem keeper = EventSystem.current;
            if (keeper == null)
            {
                foreach (EventSystem system in systems)
                    if (system != null && system.gameObject.activeInHierarchy) { keeper = system; break; }
            }
            foreach (EventSystem system in systems)
                if (system != null && system != keeper) system.enabled = false;
        }

        private static void EnsureSingleAudioListener()
        {
            AudioListener[] listeners = FindObjectsByType<AudioListener>(FindObjectsInactive.Include);
            AudioListener keeper = null;
            Camera main = Camera.main;
            if (main != null) keeper = main.GetComponent<AudioListener>();
            if (keeper == null)
            {
                foreach (AudioListener listener in listeners)
                    if (listener != null && listener.gameObject.activeInHierarchy) { keeper = listener; break; }
            }
            foreach (AudioListener listener in listeners)
                if (listener != null && listener != keeper) listener.enabled = false;
        }

        private void ApplyUpdate()
        {
            if (pipeline == null) return;
            pipeline.ApplyUpdateAndRunSafeReplay();
            UpdateApplyButton();
        }

        private void UpdateApplyButton()
        {
            if (applyButton == null || applyButtonLabel == null) return;
            bool canApply = pipeline.ReplayReady && !pipeline.ReplayRunning;
            applyButton.interactable = canApply;
            Image image = applyButton.GetComponent<Image>();
            if (pipeline.ReplayCompleted)
            {
                applyButtonLabel.text = "✓  SAFE REPLAY COMPLETED";
                image.color = new Color(.10f, .35f, .27f, .96f);
            }
            else if (pipeline.ReplayRunning)
            {
                applyButtonLabel.text = "SAFE REPLAY RUNNING…";
                image.color = new Color(.05f, .32f, .40f, .96f);
            }
            else if (canApply)
            {
                applyButtonLabel.text = "APPLY UPDATE & RUN SAFE REPLAY";
                image.color = new Color(0f, .55f, .68f, .98f);
            }
            else
            {
                applyButtonLabel.text = "WAITING FOR A1 COLLISION";
                image.color = new Color(.12f, .18f, .22f, .92f);
            }
        }

        private string Row(string name, float value)
        {
            string color = value >= .8f ? "#25E59A" : value >= .5f ? "#FFC15A" : "#FF5364";
            return $"{name,-20}<color={color}>● {value * 100f,5:F0}%</color>\n";
        }

        private void SetBar(string key, float value)
        {
            if (!policyBars.TryGetValue(key, out Image bar)) return;
            float probability = Mathf.Clamp01(value);
            bar.rectTransform.anchorMax = new Vector2(probability, 1f);
            bar.rectTransform.offsetMin = bar.rectTransform.offsetMax = Vector2.zero;
            if (policyValues.TryGetValue(key, out TMP_Text valueText))
                valueText.text = $"{ActionMeaning(key)}    {probability * 100f:F0}%";
        }

        private static string ActionMeaning(string action)
        {
            return action == "FORWARD" ? "FORWARD — CONTINUE TO D2" :
                action == "SLOW" ? "SLOW — REDUCE SPEED" :
                action == "WAIT" ? "WAIT — SAFE STOP" :
                action == "LEFT" ? "LEFT — SAFE LEFT DETOUR" :
                "RIGHT — SAFE RIGHT DETOUR";
        }

        private static string ActionLabel(ATADTRLPipelineManager.SecureAction action)
        {
            return action == ATADTRLPipelineManager.SecureAction.FusedNavigate
                ? "FUSED NAVIGATE"
                : action == ATADTRLPipelineManager.SecureAction.SlowDown
                    ? "SLOW DOWN"
                    : action.ToString().ToUpperInvariant();
        }

        private RectTransform Card(RectTransform parent, string title, string subtitle, Vector2 min, Vector2 max)
        {
            RectTransform card = Panel(parent, title.Replace(" ", "_"), min, max, panel);
            Panel(card, "Accent", new Vector2(0f, .982f), Vector2.one, cyan);
            Text(card, "Title", title, new Vector2(.04f, .83f), new Vector2(.96f, .96f), 18, cyan, FontStyles.Bold);
            TMP_Text sub = Text(card, "Subtitle", subtitle, new Vector2(.04f, .75f), new Vector2(.96f, .83f), 11, muted);
            sub.alignment = TextAlignmentOptions.TopLeft;
            return card;
        }

        private static RectTransform Panel(Transform parent, string name, Vector2 min, Vector2 max, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.layer = ATADTRL.TestFlow.A1.UI.A1ShadowSimulationDisplay.ShadowLayer;
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = min; rt.anchorMax = max; rt.offsetMin = rt.offsetMax = Vector2.zero;
            go.GetComponent<Image>().color = color;
            return rt;
        }

        private static TMP_Text Text(Transform parent, string name, string value, Vector2 min, Vector2 max,
            float size, Color color, FontStyles style = FontStyles.Normal)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.layer = ATADTRL.TestFlow.A1.UI.A1ShadowSimulationDisplay.ShadowLayer;
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = min; rt.anchorMax = max; rt.offsetMin = rt.offsetMax = Vector2.zero;
            TMP_Text text = go.GetComponent<TMP_Text>();
            text.text = value; text.fontSize = size; text.color = color; text.fontStyle = style;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            return text;
        }

        private Button ActionButton(Transform parent, string name, string label, Vector2 min, Vector2 max)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.layer = ATADTRL.TestFlow.A1.UI.A1ShadowSimulationDisplay.ShadowLayer;
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = min; rt.anchorMax = max; rt.offsetMin = rt.offsetMax = Vector2.zero;
            Image image = go.GetComponent<Image>();
            image.color = new Color(.12f, .18f, .22f, .92f);
            Button button = go.GetComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(.78f, 1f, 1f);
            colors.pressedColor = new Color(.55f, .90f, .96f);
            colors.disabledColor = new Color(.62f, .68f, .72f, .72f);
            button.colors = colors;
            TMP_Text text = Text(rt, "Label", label, new Vector2(.03f, .08f), new Vector2(.97f, .92f),
                15, white, FontStyles.Bold);
            text.alignment = TextAlignmentOptions.Center;
            return button;
        }

        private static Color Hex(string value)
        {
            ColorUtility.TryParseHtmlString(value, out Color color);
            return color;
        }
    }
}
