using System.IO;
using ATADTRL.Core;

namespace ATADTRL.Logging
{
    /// <summary>
    /// Writes GroundTruth_Environment.csv per scenario folder plus an
    /// appended combined GroundTruth_Environment_All.csv.
    /// </summary>
    public class GroundTruthLogger
    {
        private readonly string _datasetRoot;
        private CSVLogger _scenarioLogger;
        private CSVLogger _combinedLogger;

        public string CurrentScenarioFilePath => _scenarioLogger?.FilePath;
        public string CombinedFilePath => _combinedLogger?.FilePath;
        public bool IsScenarioOpen => _scenarioLogger != null && _scenarioLogger.IsOpen;

        public GroundTruthLogger(string datasetRoot)
        {
            _datasetRoot = datasetRoot;
        }

        public void BeginScenario(int scenarioId)
        {
            EndScenario();
            if (_combinedLogger == null)
            {
                _combinedLogger = new CSVLogger();
                _combinedLogger.Open(Path.Combine(_datasetRoot, "GroundTruth_Environment_All.csv"), GroundTruthRecord.CsvHeader());
            }
            string folder = Path.Combine(_datasetRoot, $"Scenario_{scenarioId:D2}");
            _scenarioLogger = new CSVLogger();
            _scenarioLogger.Open(Path.Combine(folder, "GroundTruth_Environment.csv"), GroundTruthRecord.CsvHeader());
        }

        public void LogRecord(GroundTruthRecord record)
        {
            string row = record.ToCsvRow();
            _scenarioLogger?.WriteRow(row);
            _combinedLogger?.WriteRow(row);
        }

        public void EndScenario()
        {
            _scenarioLogger?.Close();
            _scenarioLogger = null;
        }

        public void CloseAll()
        {
            EndScenario();
            _combinedLogger?.Close();
        }
    }
}
