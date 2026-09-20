using System;
using System.IO;
using ATADTRL.Core;

namespace ATADTRL.Logging
{
    /// <summary>
    /// Writes Unified_Observation.csv and publishes every completed
    /// ObservationRecord to downstream modules.
    /// </summary>
    public class ObservationLogger
    {
        private readonly string _datasetRoot;

        private CSVLogger _scenarioLogger;
        private CSVLogger _combinedLogger;

        // ============================================================
        // EVENTS FOR MODULE 2
        // ============================================================

        public static event Action<ObservationRecord>
            ObservationLogged;

        public static event Action<int>
            ScenarioStarted;

        public static event Action
            ScenarioEnded;

        // ============================================================

        public string CurrentScenarioFilePath =>
            _scenarioLogger?.FilePath;

        public string CombinedFilePath =>
            _combinedLogger?.FilePath;

        public bool IsScenarioOpen =>
            _scenarioLogger != null &&
            _scenarioLogger.IsOpen;

        public ObservationLogger(
            string datasetRoot)
        {
            _datasetRoot =
                datasetRoot;
        }

        public void BeginScenario(
            int scenarioId)
        {
            EndScenario();

            if (_combinedLogger == null)
            {
                _combinedLogger = new CSVLogger();
                _combinedLogger.Open(
                    Path.Combine(_datasetRoot, "Unified_Observation_All.csv"),
                    ObservationRecord.CsvHeader());
            }

            string folder =
                Path.Combine(
                    _datasetRoot,
                    $"Scenario_{scenarioId:D2}");

            _scenarioLogger =
                new CSVLogger();

            _scenarioLogger.Open(
                Path.Combine(
                    folder,
                    "Unified_Observation.csv"),
                ObservationRecord.CsvHeader());

            // Notify Module 2
            ScenarioStarted?.Invoke(
                scenarioId);
        }

        public void LogRecord(
            ObservationRecord record)
        {
            if (record == null)
                return;

            string row =
                record.ToCsvRow();

            _scenarioLogger?.WriteRow(
                row);

            _combinedLogger?.WriteRow(
                row);

            // ========================================================
            // IMPORTANT:
            // Send same in-memory Unified Observation to Module 2.
            // ========================================================

            ObservationLogged?.Invoke(
                record);
        }

        public void EndScenario()
        {
            if (_scenarioLogger != null)
            {
                _scenarioLogger.Close();
                _scenarioLogger = null;

                ScenarioEnded?.Invoke();
            }
        }

        public void CloseAll()
        {
            EndScenario();

            _combinedLogger?.Close();
        }
    }
}
