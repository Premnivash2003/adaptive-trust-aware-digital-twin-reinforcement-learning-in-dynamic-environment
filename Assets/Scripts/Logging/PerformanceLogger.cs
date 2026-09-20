using System.IO;
using ATADTRL.Core;

namespace ATADTRL.Logging
{
    /// <summary>
    /// Records per-episode baseline navigation metrics into
    /// Dataset/Performance_Baseline.csv. This becomes the reference used to
    /// compare against ATADTRL's trained policy in later modules.
    /// </summary>
    public class PerformanceLogger
    {
        private readonly string _datasetRoot;
        private CSVLogger _logger;

        public PerformanceLogger(string datasetRoot)
        {
            _datasetRoot = datasetRoot;
        }

        public void LogEpisode(PerformanceRecord record)
        {
            if (_logger == null)
            {
                _logger = new CSVLogger();
                _logger.Open(Path.Combine(_datasetRoot, "Performance_Baseline.csv"), PerformanceRecord.CsvHeader());
            }
            _logger.WriteRow(record.ToCsvRow());
        }

        public void Close()
        {
            _logger?.Close();
        }
    }
}
