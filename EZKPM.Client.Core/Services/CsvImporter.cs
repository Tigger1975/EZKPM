using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EZKPM.Shared.Contracts;

namespace EZKPM.Client.Core.Services
{
    public class CsvImporter : IPasswordDbImporter
    {
        public async Task<List<VaultAssetPayload>> ImportAsync(Stream fileStream, string password = null, string keyFilePath = null)
        {
            var resultList = new List<VaultAssetPayload>();
            
            using var reader = new StreamReader(fileStream);
            string headerLine = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(headerLine)) return resultList;

            var headers = headerLine.Split(',').Select(h => h.Trim('"')).ToList();

            // Expected basic headers (ignoring case)
            int titleIdx = headers.FindIndex(h => h.Equals("Account", StringComparison.OrdinalIgnoreCase) || h.Equals("Title", StringComparison.OrdinalIgnoreCase) || h.Equals("Name", StringComparison.OrdinalIgnoreCase));
            int userIdx = headers.FindIndex(h => h.Equals("Login Name", StringComparison.OrdinalIgnoreCase) || h.Equals("Username", StringComparison.OrdinalIgnoreCase));
            int passIdx = headers.FindIndex(h => h.Equals("Password", StringComparison.OrdinalIgnoreCase));
            int urlIdx = headers.FindIndex(h => h.Equals("Web Site", StringComparison.OrdinalIgnoreCase) || h.Equals("Url", StringComparison.OrdinalIgnoreCase));
            int notesIdx = headers.FindIndex(h => h.Equals("Comments", StringComparison.OrdinalIgnoreCase) || h.Equals("Notes", StringComparison.OrdinalIgnoreCase));

            while (!reader.EndOfStream)
            {
                string line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line)) continue;

                var columns = SplitCsvLine(line);

                var payload = new VaultAssetPayload
                {
                    TransientAssetId = Guid.NewGuid(),
                    AssetType = "Login",
                    Title = titleIdx >= 0 && titleIdx < columns.Count ? columns[titleIdx] : "Unnamed Login",
                    Username = userIdx >= 0 && userIdx < columns.Count ? columns[userIdx] : "",
                    Password = passIdx >= 0 && passIdx < columns.Count ? columns[passIdx] : "",
                    Url = urlIdx >= 0 && urlIdx < columns.Count ? columns[urlIdx] : "",
                    Notes = notesIdx >= 0 && notesIdx < columns.Count ? columns[notesIdx] : ""
                };

                resultList.Add(payload);
            }

            return resultList;
        }

        private List<string> SplitCsvLine(string line)
        {
            var result = new List<string>();
            bool inQuotes = false;
            int startIdx = 0;

            for (int i = 0; i < line.Length; i++)
            {
                if (line[i] == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (line[i] == ',' && !inQuotes)
                {
                    result.Add(CleanValue(line.Substring(startIdx, i - startIdx)));
                    startIdx = i + 1;
                }
            }

            result.Add(CleanValue(line.Substring(startIdx)));
            return result;
        }

        private string CleanValue(string val)
        {
            if (val.StartsWith("\"") && val.EndsWith("\""))
            {
                val = val.Substring(1, val.Length - 2);
            }
            return val.Replace("\"\"", "\"");
        }
    }
}
