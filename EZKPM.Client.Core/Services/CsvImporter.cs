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
        public Dictionary<string, int> Mapping { get; set; }

        public async Task<List<VaultAssetPayload>> ImportAsync(Stream fileStream, string password = null, string keyFilePath = null)
        {
            var resultList = new List<VaultAssetPayload>();
            
            System.Text.Encoding encoding = System.Text.Encoding.UTF8;
            try 
            {
                fileStream.Position = 0;
                using var testReader = new StreamReader(fileStream, new System.Text.UTF8Encoding(false, true), true, 1024, true);
                await testReader.ReadToEndAsync();
            }
            catch (System.Text.DecoderFallbackException)
            {
                System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
                encoding = System.Text.Encoding.GetEncoding(1252);
            }
            fileStream.Position = 0;

            using var reader = new StreamReader(fileStream, encoding);
            string headerLine = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(headerLine)) return resultList;

            char delimiter = headerLine.Contains(';') ? ';' : ',';
            var headers = headerLine.Split(delimiter).Select(h => h.Trim('"')).ToList();

            int titleIdx = Mapping != null && Mapping.ContainsKey("Title") ? Mapping["Title"] : -1;
            int userIdx = Mapping != null && Mapping.ContainsKey("Username") ? Mapping["Username"] : -1;
            int passIdx = Mapping != null && Mapping.ContainsKey("Password") ? Mapping["Password"] : -1;
            int urlIdx = Mapping != null && Mapping.ContainsKey("Url") ? Mapping["Url"] : -1;
            int notesIdx = Mapping != null && Mapping.ContainsKey("Notes") ? Mapping["Notes"] : -1;

            while (!reader.EndOfStream)
            {
                string line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line)) continue;

                var columns = SplitCsvLine(line, delimiter);

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

        private List<string> SplitCsvLine(string line, char delimiter)
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
                else if (line[i] == delimiter && !inQuotes)
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
            if (val.StartsWith("\"") && val.EndsWith("\"") && val.Length >= 2)
            {
                val = val.Substring(1, val.Length - 2);
            }
            return val.Replace("\"\"", "\"");
        }
    }
}
