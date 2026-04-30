using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BmsHostUi.Models;

namespace BmsHostUi.Services
{
    public sealed class CsvLoggerService
    {
        public void EnsureWritableFile(string path, bool overwrite)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(path) && !overwrite)
            {
                throw new IOException("CSV already exists and overwrite is disabled: " + path);
            }

            if (!File.Exists(path) || overwrite)
            {
                File.WriteAllText(path, "Timestamp,Name,Address,Value,Unit" + Environment.NewLine, Encoding.UTF8);
            }
        }

        public void AppendRows(string path, IEnumerable<LiveDataRow> rows)
        {
            using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(stream, Encoding.UTF8))
            {
                foreach (var row in rows)
                {
                    writer.Write(row.Timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                    writer.Write(',');
                    writer.Write(Escape(row.Name));
                    writer.Write(',');
                    writer.Write(row.AddressHex);
                    writer.Write(',');
                    writer.Write(Escape(row.ValueText));
                    writer.Write(',');
                    writer.Write(Escape(row.Unit));
                    writer.WriteLine();
                }
            }
        }

        private static string Escape(string source)
        {
            if (source == null)
            {
                return string.Empty;
            }

            if (source.Contains(",") || source.Contains("\"") || source.Contains("\n"))
            {
                return "\"" + source.Replace("\"", "\"\"") + "\"";
            }

            return source;
        }
    }
}
