using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ATADTRL.Scenarios;
using ATADTRL.Core;

namespace ATADTRL.UI
{
    /// <summary>
    /// "ATADTRL MODULE 1 — WAREHOUSE ENVIRONMENT & DATA ACQUISITION" main
    /// screen. Spawns one button per scenario plus a RUN ALL button.
    /// Selecting a scenario and pressing START executes it end-to-end with
    /// zero code changes required.
    /// </summary>
    public class DemoController : MonoBehaviour
    {
        [Header("References")]
        public ScenarioManager scenarioManager;

        [Header("UI Layout")]
        public Transform scenarioButtonContainer;
        public GameObject scenarioButtonPrefab; // expects a Button with a TMP_Text child
        public Button runAllButton;
        public TMP_Text titleText;
        public TMP_Text completionBannerText;
        public GameObject completionBannerRoot;

        private readonly List<Button> _scenarioButtons = new List<Button>();
        private int _selectedScenarioId = -1;

        private void Start()
        {
            if (titleText != null)
            {
                titleText.text = "ATADTRL\nMODULE 1\nWAREHOUSE ENVIRONMENT & DATA ACQUISITION";
            }
            if (completionBannerRoot != null) completionBannerRoot.SetActive(false);

            BuildScenarioButtons();

            if (runAllButton != null)
            {
                runAllButton.onClick.AddListener(() =>
                {
                    HideBanner();
                    scenarioManager.StartRunAll();
                });
            }

            scenarioManager.OnAllScenariosCompleted += ShowCompletionBanner;
        }

        private void BuildScenarioButtons()
        {
            if (scenarioButtonContainer == null || scenarioButtonPrefab == null) return;

            foreach (var scenario in scenarioManager.Scenarios)
            {
                GameObject btnGO = Instantiate(scenarioButtonPrefab, scenarioButtonContainer);
                btnGO.name = $"Btn_Scenario_{scenario.scenarioId:D2}";

                var label = btnGO.GetComponentInChildren<TMP_Text>();
                if (label != null) label.text = string.IsNullOrEmpty(scenario.scenarioCode)
                    ? $"Scenario {scenario.scenarioId:D2}"
                    : scenario.scenarioCode;

                var button = btnGO.GetComponent<Button>();
                int capturedId = scenario.scenarioId;
                button.onClick.AddListener(() => SelectAndStart(capturedId));

                _scenarioButtons.Add(button);
            }
        }

        private void SelectAndStart(int scenarioId)
        {
            _selectedScenarioId = scenarioId;
            HideBanner();
            scenarioManager.StartScenario(scenarioId);
        }

        private void ShowCompletionBanner()
        {
            if (completionBannerRoot == null) return;
            completionBannerRoot.SetActive(true);
            if (completionBannerText != null) completionBannerText.text = "ALL SCENARIOS COMPLETED";
        }

        private void HideBanner()
        {
            if (completionBannerRoot != null) completionBannerRoot.SetActive(false);
        }

        private void OnDestroy()
        {
            if (scenarioManager != null)
            {
                scenarioManager.OnAllScenariosCompleted -= ShowCompletionBanner;
            }
        }
    }
}
