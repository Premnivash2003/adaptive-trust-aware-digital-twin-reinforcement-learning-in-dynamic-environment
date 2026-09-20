using System.IO;
using ATADTRL.Core;

namespace ATADTRL.Logging
{
    public class TwinWorldStateLogger
    {
        private readonly string datasetRoot;

        private CSVLogger scenarioLogger;
        private CSVLogger combinedLogger;

        public string CurrentScenarioFilePath =>
            scenarioLogger?.FilePath;

        public string CombinedFilePath =>
            combinedLogger?.FilePath;

        public TwinWorldStateLogger(
            string datasetRoot)
        {
            this.datasetRoot = datasetRoot;
        }

        public void BeginScenario(
            int scenarioId)
        {
            EndScenario();

            if (combinedLogger == null)
            {
                combinedLogger = new CSVLogger();
                combinedLogger.Open(
                    Path.Combine(datasetRoot, "Twin_World_State_All.csv"),
                    TwinWorldStateRecord.CsvHeader());
            }

            string folder =
                Path.Combine(
                    datasetRoot,
                    $"Scenario_{scenarioId:D2}");

            scenarioLogger =
                new CSVLogger();

            scenarioLogger.Open(
                Path.Combine(
                    folder,
                    "Twin_World_State.csv"),
                TwinWorldStateRecord.CsvHeader());
        }

        public void LogRecord(
            TwinWorldStateRecord record)
        {
            if (record == null)
                return;

            string row =
                record.ToCsvRow();

            scenarioLogger?.WriteRow(row);
            combinedLogger?.WriteRow(row);
        }

        public void EndScenario()
        {
            scenarioLogger?.Close();
            scenarioLogger = null;
        }

        public void CloseAll()
        {
            EndScenario();

            combinedLogger?.Close();
        }
    }
}
