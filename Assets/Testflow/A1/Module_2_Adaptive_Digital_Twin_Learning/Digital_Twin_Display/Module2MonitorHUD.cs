using TMPro;
using UnityEngine;
using ATADTRL.Module2;

namespace ATADTRL.UI
{
    public class Module2MonitorHUD : MonoBehaviour
    {
        private Module2Manager module2;

        private TMP_Text changeScoreText;
        private TMP_Text changedComponentsText;

        private TMP_Text edatsText;
        private TMP_Text syncReasonText;

        private TMP_Text twinRobotText;
        private TMP_Text humanStateText;
        private TMP_Text forkliftStateText;

        private TMP_Text taskStateText;

        private readonly Color normalColor =
            Hex("#EAF6FF");

        private readonly Color successColor =
            Hex("#30E88B");

        private readonly Color warningColor =
            Hex("#FFB347");

        private readonly Color dangerColor =
            Hex("#FF5566");

        private readonly Color cyanColor =
            Hex("#00E5FF");

        private void Start()
        {
            FindModule2();
            FindUI();
        }

        private void FindModule2()
        {
            module2 =
                FindAnyObjectByType<Module2Manager>();

            if (module2 == null)
            {
                Debug.LogError(
                    "[ATADTRL Monitor] Module2Manager not found.");
            }
            else
            {
                Debug.Log(
                    "[ATADTRL Monitor] Connected to Module2Manager.");
            }
        }

        private void FindUI()
        {
            changeScoreText =
                FindText("ChangeScoreText");

            changedComponentsText =
                FindText("ChangedComponentsText");

            edatsText =
                FindText("EDATSText");

            syncReasonText =
                FindText("SyncReasonText");

            twinRobotText =
                FindText("TwinRobotText");

            humanStateText =
                FindText("HumanStateText");

            forkliftStateText =
                FindText("ForkliftStateText");

            taskStateText =
                FindText("TaskStateText");
        }

        private void Update()
        {
            if (module2 == null)
            {
                module2 =
                    FindAnyObjectByType<Module2Manager>();

                if (module2 == null)
                    return;
            }

            UpdateChangeDetection();
            UpdateEDATS();
            UpdateTwin();
            UpdateTask();
        }

        // ============================================================
        // CHANGE DETECTION
        // ============================================================

        private void UpdateChangeDetection()
        {
            if (changeScoreText != null)
            {
                float score =
                    module2.CurrentChangeScore;

                float threshold =
                    module2.edatsConfig != null
                        ? module2.edatsConfig.overallChangeThreshold
                        : 0f;

                changeScoreText.text =
                    "<size=16><color=#8DAABD>CHANGE SCORE</color></size>\n" +
                    $"<size=34><b>{score:F3}</b></size>\n" +
                    $"<size=15><color=#8DAABD>Threshold  {threshold:F3}</color></size>";

                if (score > threshold)
                    changeScoreText.color =
                        warningColor;
                else
                    changeScoreText.color =
                        normalColor;
            }

            if (changedComponentsText != null)
            {
                string changed =
                    module2.ChangedComponents;

                bool hasChanges =
                    !string.IsNullOrWhiteSpace(changed);

                changedComponentsText.text =
                    "<size=16><color=#8DAABD>CHANGED COMPONENTS</color></size>\n" +
                    (hasChanges
                        ? $"<color=#00E5FF>{changed}</color>"
                        : "<color=#8DAABD>None detected</color>");
            }
        }

        // ============================================================
        // EDATS
        // ============================================================

        private void UpdateEDATS()
        {
            if (edatsText != null)
            {
                if (module2.CriticalEvent)
                {
                    edatsText.text =
                        "<size=16><color=#8DAABD>EDATS STATUS</color></size>\n" +
                        "<size=28><b>● CRITICAL SYNC</b></size>";

                    edatsText.color =
                        dangerColor;
                }
                else if (module2.SynchronizationTriggered)
                {
                    edatsText.text =
                        "<size=16><color=#8DAABD>EDATS STATUS</color></size>\n" +
                        "<size=28><b>● SYNCHRONIZED</b></size>";

                    edatsText.color =
                        successColor;
                }
                else
                {
                    edatsText.text =
                        "<size=16><color=#8DAABD>EDATS STATUS</color></size>\n" +
                        "<size=28><b>● TWIN MAINTAINED</b></size>";

                    edatsText.color =
                        cyanColor;
                }
            }

            if (syncReasonText != null)
            {
                var twin =
                    module2.CurrentTwin;

                string age =
                    twin != null
                        ? twin.state_age.ToString("F3") + " s"
                        : "--";

                string lastSync =
                    twin != null
                        ? twin.last_sync_timestamp.ToString("F3") + " s"
                        : "--";

                syncReasonText.text =
                    "<size=16><color=#8DAABD>SYNCHRONIZATION REASON</color></size>\n" +
                    $"{module2.SyncReason}\n\n" +

                    "<size=15><color=#8DAABD>LAST SYNC</color></size>\n" +
                    $"{lastSync}\n\n" +

                    "<size=15><color=#8DAABD>TWIN STATE AGE</color></size>\n" +
                    $"{age}";
            }
        }

        // ============================================================
        // DIGITAL TWIN
        // ============================================================

        private void UpdateTwin()
        {
            var twin =
                module2.CurrentTwin;

            if (twin == null)
            {
                if (twinRobotText != null)
                    twinRobotText.text =
                        "Waiting for digital twin initialization...";

                return;
            }

            if (twinRobotText != null)
            {
                twinRobotText.text =
                    "<size=16><color=#8DAABD>ROBOT TWIN STATE</color></size>\n" +
                    $"X       <b>{twin.twin_robot_x:F2} m</b>\n" +
                    $"Y       <b>{twin.twin_robot_y:F2} m</b>\n" +
                    $"Yaw     <b>{twin.twin_robot_yaw:F1}°</b>\n" +
                    $"Speed   <b>{twin.twin_robot_velocity:F2} m/s</b>";
            }

            if (humanStateText != null)
            {
                humanStateText.text =
                    "<size=16><color=#8DAABD>HUMAN TELEMETRY</color></size>\n" +
                    $"H1  <color=#30E88B>●</color>  {ShortPosition(twin.h1_position)}\n" +
                    $"H2  <color=#30E88B>●</color>  {ShortPosition(twin.h2_position)}\n" +
                    $"H3  <color=#30E88B>●</color>  {ShortPosition(twin.h3_position)}\n" +
                    $"H4  <color=#30E88B>●</color>  {ShortPosition(twin.h4_position)}\n" +
                    $"H5  <color=#30E88B>●</color>  {ShortPosition(twin.h5_position)}";
            }

            if (forkliftStateText != null)
            {
                forkliftStateText.text =
                    "<size=16><color=#8DAABD>FORKLIFT TELEMETRY</color></size>\n" +
                    $"F1  <color=#00E5FF>●</color>  {ShortPosition(twin.f1_position)}\n" +
                    $"F2  <color=#00E5FF>●</color>  {ShortPosition(twin.f2_position)}";
            }
        }

        // ============================================================
        // TASK / MISSION
        // ============================================================

        private void UpdateTask()
        {
            var twin =
                module2.CurrentTwin;

            if (twin == null ||
                taskStateText == null)
                return;

            string carrying =
                twin.carrying_status
                    ? "<color=#30E88B>YES</color>"
                    : "<color=#8DAABD>NO</color>";

            string grasp =
                twin.grasp_status
                    ? "<color=#30E88B>LOCKED</color>"
                    : "<color=#8DAABD>OPEN</color>";

            string sync =
                twin.sync_trigger
                    ? "<color=#30E88B>SYNCHRONIZED</color>"
                    : "<color=#00E5FF>MAINTAINED</color>";

            taskStateText.text =
                "<size=16><color=#8DAABD>CURRENT MISSION</color></size>\n\n" +

                $"Scenario       <b>A{twin.scenarioId}</b>\n" +
                $"Episode        <b>{twin.episodeId}</b>\n" +
                $"Step           <b>{twin.stepId}</b>\n\n" +

                $"Parcel         <b>{Safe(twin.parcel_id)}</b>\n" +
                $"Destination    <b>{Safe(twin.destination_id)}</b>\n" +
                $"Rack           <b>{Safe(twin.destination_rack_id)}</b>\n" +
                $"Slot           <b>{Safe(twin.destination_slot_id)}</b>\n\n" +

                $"Stage          <b>{Safe(twin.current_task_stage)}</b>\n" +
                $"Carrying       {carrying}\n" +
                $"Grasp          {grasp}\n\n" +

                $"Twin Status    {sync}";
        }

        // ============================================================
        // HELPERS
        // ============================================================

        private TMP_Text FindText(
            string objectName)
        {
            GameObject obj =
                GameObject.Find(objectName);

            if (obj == null)
                return null;

            return obj.GetComponent<TMP_Text>();
        }

        private string ShortPosition(
            string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "--";

            if (value == "UNAVAILABLE")
                return "<color=#FF5566>UNAVAILABLE</color>";

            string[] parts =
                value.Split(':');

            if (parts.Length == 4)
            {
                return
                    $"({parts[1]}, {parts[3]})";
            }

            if (parts.Length == 3)
            {
                return
                    $"({parts[0]}, {parts[2]})";
            }

            return value;
        }

        private string Safe(
            string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? "--"
                : value;
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
