using System.Collections.Generic;
using System.IO;
using UnityEngine;
using ATADTRL.Core;
using ATADTRL.Validation;

namespace ATADTRL.TestFlow.A1.Module1
{
    /// <summary>Runs the architecture's dataset-validation stage for A1.</summary>
    public static class DatasetValidation
    {
        public static IReadOnlyList<ValidationRecord> ValidateScenario01()
        {
            string root = Path.Combine(Application.streamingAssetsPath, "Dataset");
            string scenario = Path.Combine(root, "Scenario_01");
            var validator = new DatasetValidator(root);
            var records = new List<ValidationRecord>
            {
                validator.ValidateScenarioFile(1, "GroundTruth_Environment",
                    Path.Combine(scenario, "GroundTruth_Environment.csv")),
                validator.ValidateScenarioFile(1, "Unified_Observation",
                    Path.Combine(scenario, "Unified_Observation.csv"))
            };
            validator.WriteReport();
            return records;
        }
    }
}
