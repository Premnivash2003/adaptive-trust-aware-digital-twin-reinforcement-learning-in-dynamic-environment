using System;
using System.IO;
using UnityEngine;

namespace ATADTRL.Logging
{
    /// <summary>
    /// Publishes flushed runtime datasets into StreamingAssets/Dataset.
    /// Logging remains outside Assets while an episode is active, preventing
    /// Unity's importer from reading a CSV whose size changes mid-import.
    /// </summary>
    public static class DatasetPublisher
    {
        public static string RuntimeRoot => Path.Combine(Application.persistentDataPath, "ATADTRL_Dataset");
        public static string PublishedRoot => Path.Combine(Application.streamingAssetsPath, "Dataset");

        public static int PublishCompletedFiles()
        {
            if (!Directory.Exists(RuntimeRoot)) return 0;
            Directory.CreateDirectory(PublishedRoot);
            int count = 0;
            foreach (string source in Directory.GetFiles(RuntimeRoot, "*", SearchOption.AllDirectories))
            {
                string extension = Path.GetExtension(source);
                if (!extension.Equals(".csv", StringComparison.OrdinalIgnoreCase) &&
                    !extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)) continue;
                string relative = source.Substring(RuntimeRoot.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string destination = Path.Combine(PublishedRoot, relative);
                string directory = Path.GetDirectoryName(destination);
                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                try
                {
                    File.Copy(source, destination, true);
                    count++;
                }
                catch (IOException exception)
                {
                    Debug.LogWarning($"ATADTRL DATASET: Could not publish '{relative}' yet: {exception.Message}");
                }
            }
#if UNITY_EDITOR
            UnityEditor.AssetDatabase.Refresh();
#endif
            Debug.Log($"ATADTRL DATASET: Published {count} completed files to '{PublishedRoot}'.");
            return count;
        }
    }
}
