using System.IO;
using ATADTRL.Core;

namespace ATADTRL.Logging
{
    /// <summary>Writes one audit-friendly row for every completed episode.</summary>
    public class EpisodeOutcomeLogger
    {
        private readonly string _datasetRoot;
        private CSVLogger _logger;

        public EpisodeOutcomeLogger(string datasetRoot)
        {
            _datasetRoot = datasetRoot;
        }

        public void Log(EpisodeOutcomeRecord record)
        {
            if (_logger == null)
            {
                _logger = new CSVLogger();
                _logger.Open(Path.Combine(_datasetRoot, "Episode_Outcome_Report.csv"), EpisodeOutcomeRecord.CsvHeader());
            }
            _logger.WriteRow(record.ToCsvRow());
        }

        public void Close() => _logger?.Close();
    }
}
