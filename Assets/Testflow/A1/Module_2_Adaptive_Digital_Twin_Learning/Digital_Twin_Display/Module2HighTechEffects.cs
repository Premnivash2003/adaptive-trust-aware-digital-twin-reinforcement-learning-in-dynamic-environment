using TMPro;
using UnityEngine;
using ATADTRL.Module2;

namespace ATADTRL.UI
{
    public class Module2HighTechEffects :
        MonoBehaviour
    {
        private Module2Manager module2;

        private TMP_Text liveBadge;
        private TMP_Text pipelineText;

        private float pulseTimer;

        private readonly Color liveGreen =
            Hex("#30E88B");

        private readonly Color cyan =
            Hex("#00E5FF");

        private readonly Color dim =
            Hex("#527082");

        private void Start()
        {
            module2 =
                FindAnyObjectByType<
                    Module2Manager>();

            CreatePipelineIndicator();
        }

        private void Update()
        {
            AnimateLiveIndicator();
            UpdatePipeline();
        }

        // ============================================================
        // LIVE INDICATOR
        // ============================================================

        private void AnimateLiveIndicator()
        {
            GameObject liveObj =
                GameObject.Find("LiveBadge");

            if (liveObj == null)
                return;

            liveBadge =
                liveObj.GetComponent<TMP_Text>();

            if (liveBadge == null)
                return;

            pulseTimer +=
                Time.unscaledDeltaTime;

            float alpha =
                0.55f +
                0.45f *
                Mathf.Abs(
                    Mathf.Sin(
                        pulseTimer * 2.5f));

            Color c =
                liveGreen;

            c.a = alpha;

            liveBadge.color = c;
        }

        // ============================================================
        // PIPELINE
        // ============================================================

        private void CreatePipelineIndicator()
        {
            GameObject canvas =
                GameObject.Find("MonitorCanvas");

            if (canvas == null)
                return;

            GameObject pipeline =
                new GameObject(
                    "PipelineStatus",
                    typeof(RectTransform),
                    typeof(TextMeshProUGUI));

            pipeline.transform.SetParent(
                canvas.transform,
                false);

            pipelineText =
                pipeline.GetComponent<
                    TextMeshProUGUI>();

            RectTransform rt =
                pipeline.GetComponent<
                    RectTransform>();

            rt.anchorMin =
                new Vector2(0.22f, 0.005f);

            rt.anchorMax =
                new Vector2(0.975f, 0.045f);

            rt.offsetMin =
                Vector2.zero;

            rt.offsetMax =
                Vector2.zero;

            pipelineText.fontSize = 15;

            pipelineText.alignment =
                TextAlignmentOptions.MidlineRight;

            pipelineText.textWrappingMode =
                TextWrappingModes.NoWrap;
        }

        private void UpdatePipeline()
        {
            if (pipelineText == null)
                return;

            bool twinReady =
                module2 != null &&
                module2.CurrentTwin != null;

            bool synced =
                module2 != null &&
                module2.SynchronizationTriggered;

            string observation =
                "<color=#30E88B>● OBSERVATION</color>";

            string change =
                "<color=#00E5FF>● CHANGE DETECTION</color>";

            string edats =
                synced
                    ? "<color=#30E88B>● EDATS</color>"
                    : "<color=#00E5FF>● EDATS</color>";

            string twin =
                twinReady
                    ? "<color=#30E88B>● ADAPTIVE TWIN</color>"
                    : "<color=#527082>○ ADAPTIVE TWIN</color>";

            string context =
                "<color=#527082>○ CONTEXT MODEL</color>";

            string ppo =
                "<color=#527082>○ PPO</color>";

            pipelineText.text =
                observation +
                "  →  " +
                change +
                "  →  " +
                edats +
                "  →  " +
                twin +
                "  →  " +
                context +
                "  →  " +
                ppo;
        }

        private static Color Hex(
            string hex)
        {
            if (ColorUtility.TryParseHtmlString(
                    hex,
                    out Color c))
                return c;

            return Color.white;
        }
    }
}
