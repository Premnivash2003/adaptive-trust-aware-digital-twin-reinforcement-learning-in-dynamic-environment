using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ATADTRL.Core;
using ATADTRL.Logging;

namespace ATADTRL.Validation
{
    /// <summary>
    /// Validates a completed scenario's CSV datasets for structural
    /// integrity (missing/duplicate/invalid values, timestamp ordering) and
    /// writes results into Dataset_Validation_Report.csv.
    /// </summary>
    public class DatasetValidator
    {
        private readonly string _datasetRoot;
        private readonly List<ValidationRecord> _records = new List<ValidationRecord>();

        public DatasetValidator(string datasetRoot)
        {
            _datasetRoot = datasetRoot;
        }

        public ValidationRecord ValidateScenarioFile(int scenarioId, string datasetName, string filePath)
        {
            var record = new ValidationRecord
            {
                datasetName = datasetName,
                scenarioId = scenarioId,
                validationStatus = "PASS"
            };

            if (!File.Exists(filePath))
            {
                record.validationStatus = "FAIL_FILE_MISSING";
                _records.Add(record);
                return record;
            }

            string[] lines = File.ReadAllLines(filePath);
            if (lines.Length < 2)
            {
                record.validationStatus = "FAIL_EMPTY";
                _records.Add(record);
                return record;
            }

            string header = lines[0];
            int timestampCol = Array.IndexOf(header.Split(','), "timestamp");
            int scenarioCol = Array.IndexOf(header.Split(','), "scenario_id");
            int episodeCol = Array.IndexOf(header.Split(','), "episode_id");
            int stepCol = Array.IndexOf(header.Split(','), "step_id");
            int expectedColumns = header.Split(',').Length;
            if (timestampCol < 0 || scenarioCol < 0 || episodeCol < 0 || stepCol < 0)
            {
                record.validationStatus = "FAIL_REQUIRED_COLUMNS";
                _records.Add(record);
                return record;
            }

            var seenRows = new HashSet<string>();
            var seenTimestamps = new HashSet<string>();
            var lastTimestampByEpisode = new Dictionary<string, double>();

            int total = 0, missing = 0, duplicates = 0, invalid = 0, timestampErrors = 0;

            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;
                total++;

                string[] cols = SplitCsvRespectingQuotes(line);
                if (cols.Length != expectedColumns) { invalid++; continue; }

                if (cols.Any(c => string.IsNullOrWhiteSpace(c)))
                {
                    // Quoted-empty fields (e.g. "") for optional lists are acceptable;
                    // only count truly missing required identifier columns.
                    bool missingRequired =
                        (timestampCol >= 0 && timestampCol < cols.Length && string.IsNullOrWhiteSpace(cols[timestampCol])) ||
                        (scenarioCol >= 0 && scenarioCol < cols.Length && string.IsNullOrWhiteSpace(cols[scenarioCol])) ||
                        (episodeCol >= 0 && episodeCol < cols.Length && string.IsNullOrWhiteSpace(cols[episodeCol])) ||
                        (stepCol >= 0 && stepCol < cols.Length && string.IsNullOrWhiteSpace(cols[stepCol]));
                    if (missingRequired) missing++;
                }

                if (cols.Any(c => c.Contains("NaN") || c.Contains("Infinity")))
                {
                    invalid++;
                }

                if (!seenRows.Add(line))
                {
                    duplicates++;
                }

                if (timestampCol >= 0 && timestampCol < cols.Length &&
                    double.TryParse(cols[timestampCol], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double ts))
                {
                    if (!seenTimestamps.Add(cols[timestampCol] + "_" + (stepCol >= 0 && stepCol < cols.Length ? cols[stepCol] : "")))
                    {
                        // duplicate timestamp+step combo already counted via duplicates check generally
                    }
                    string episodeKey = cols[scenarioCol] + ":" + cols[episodeCol];
                    if (lastTimestampByEpisode.TryGetValue(episodeKey, out double lastTimestamp) && ts < lastTimestamp)
                    {
                        timestampErrors++;
                    }
                    lastTimestampByEpisode[episodeKey] = ts;
                }
                else if (timestampCol >= 0)
                {
                    timestampErrors++;
                }
            }

            record.totalRows = total;
            record.missingValues = missing;
            record.duplicateRows = duplicates;
            record.invalidValues = invalid;
            record.timestampErrors = timestampErrors;
            record.validationStatus = (missing == 0 && invalid == 0 && duplicates == 0 && timestampErrors == 0) ? "PASS" : "FAIL_VALIDATION_ERRORS";

            _records.Add(record);
            return record;
        }

        private static string[] SplitCsvRespectingQuotes(string line)
        {
            var result = new List<string>();
            bool inQuotes = false;
            var current = new System.Text.StringBuilder();

            foreach (char c in line)
            {
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
            result.Add(current.ToString());
            return result.ToArray();
        }

        public void WriteReport()
        {
            var logger = new CSVLogger();
            logger.Open(Path.Combine(_datasetRoot, "Dataset_Validation_Report.csv"), ValidationRecord.CsvHeader());
            foreach (var r in _records)
            {
                logger.WriteRow(r.ToCsvRow());
            }
            logger.Close();
        }
    }
}
