using System.IO;
using UnityEditor;
using UnityEngine;
using ATADTRL.Scenarios;

namespace ATADTRL.EditorTools
{
    /// <summary>
    /// Exports a completed runtime dataset into StreamingAssets only after
    /// logging has stopped.  This deliberately avoids live writes beneath
    /// Assets, which the Unity Asset Database may import mid-write.
    /// </summary>
    public static class ATADTRLDatasetExporter
    {
        [MenuItem("ATADTRL/Export Completed Dataset to StreamingAssets")]
        public static void ExportCompletedDataset()
        {
            var manager = Object.FindAnyObjectByType<ScenarioManager>();
            if (manager != null && manager.IsRunning)
            {
                EditorUtility.DisplayDialog("ATADTRL Dataset Export", "Stop or finish the scenario before exporting the CSV files.", "OK");
                return;
            }

            string source = Path.Combine(Application.persistentDataPath, "ATADTRL_Dataset");
            if (!Directory.Exists(source))
            {
                EditorUtility.DisplayDialog("ATADTRL Dataset Export", $"No completed runtime dataset was found at:\n{source}", "OK");
                return;
            }

            string destination = Path.Combine(Application.streamingAssetsPath, "Dataset");
            Directory.CreateDirectory(destination);
            int fileCount = 0;
            foreach (string sourceFile in Directory.GetFiles(source, "*.csv", SearchOption.AllDirectories))
            {
                string relativePath = sourceFile.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string destinationFile = Path.Combine(destination, relativePath);
                string destinationDirectory = Path.GetDirectoryName(destinationFile);
                if (!string.IsNullOrEmpty(destinationDirectory)) Directory.CreateDirectory(destinationDirectory);
                File.Copy(sourceFile, destinationFile, true);
                fileCount++;
            }

            AssetDatabase.Refresh();
            Debug.Log($"ATADTRL: Exported {fileCount} completed CSV files from '{source}' to '{destination}'.");
            EditorUtility.DisplayDialog("ATADTRL Dataset Export", $"Exported {fileCount} completed CSV files to StreamingAssets/Dataset.", "OK");
        }

        [MenuItem("ATADTRL/Export Completed Dataset to StreamingAssets", true)]
        private static bool CanExportCompletedDataset()
        {
            var manager = Object.FindAnyObjectByType<ScenarioManager>();
            return manager == null || !manager.IsRunning;
        }
    }
}
