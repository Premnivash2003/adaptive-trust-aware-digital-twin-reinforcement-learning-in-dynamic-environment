using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

namespace ATADTRL.Logging
{
    public interface IDataLogger
    {
        void Open(string filePath, string header);
        void WriteRow(string csvRow);
        void Close();
    }

    /// <summary>
    /// Minimal, robust CSV writer. Opens in append mode if the file already
    /// exists with the same header (so scenario data is never overwritten),
    /// otherwise creates a fresh file with the header row.
    /// </summary>
    public class CSVLogger : IDataLogger
    {
        private StreamWriter _writer;
        private string _path;
        private bool _isOpen;

        public bool IsOpen => _isOpen;
        public string FilePath => _path;

        public void Open(string filePath, string header)
        {
            // Be idempotent: a new scenario may begin after Stop/Reset or a
            // domain reload, and an old writer must never retain the file.
            Close();
            _path = filePath;
            string dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            bool writeHeader;
            try
            {
                writeHeader = RequiresNewSchema(filePath, header);
            }
            catch (IOException exception)
            {
                Debug.LogWarning($"CSVLogger: could not inspect {_path}. Close the file in Excel/another editor, then run again. {exception.Message}");
                _isOpen = false;
                return;
            }
            const int attempts = 5;
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                try
                {
                    // Read/write sharing allows the user to inspect a live CSV
                    // without locking Unity out of its own dataset file.
                    var stream = new FileStream(filePath, writeHeader ? FileMode.Create : FileMode.Append,
                        FileAccess.Write, FileShare.ReadWrite);
                    _writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
                    break;
                }
                catch (IOException) when (attempt < attempts - 1)
                {
                    Thread.Sleep(100);
                }
                catch (IOException exception)
                {
                    Debug.LogWarning($"CSVLogger: could not open {_path}. Close the file in Excel/another editor, then run again. {exception.Message}");
                    _writer = null;
                    _isOpen = false;
                    return;
                }
            }

            if (writeHeader) _writer.WriteLine(header);
            _isOpen = true;
        }

        // When the observation schema changes, preserve the old CSV beside
        // the new one instead of appending rows with a different column count.
        private static bool RequiresNewSchema(string filePath, string header)
        {
            if (!File.Exists(filePath) || new FileInfo(filePath).Length == 0) return true;

            string existingHeader;
            using (var reader = new StreamReader(new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite), Encoding.UTF8))
            {
                existingHeader = reader.ReadLine();
            }
            if (existingHeader == header) return false;

            string backupPath = filePath + ".previous_schema.csv";
            int suffix = 2;
            while (File.Exists(backupPath))
            {
                backupPath = filePath + $".previous_schema_{suffix++}.csv";
            }
            File.Copy(filePath, backupPath);
            return true;
        }

        public void WriteRow(string csvRow)
        {
            if (!_isOpen || _writer == null)
            {
                return;
            }
            _writer.WriteLine(csvRow);
        }

        public void Close()
        {
            if (_writer != null)
            {
                _writer.Flush();
                _writer.Dispose();
                _writer = null;
            }
            _isOpen = false;
        }
    }
}
