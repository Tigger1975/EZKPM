using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using EZKPM.Shared.Contracts;

namespace EZKPM.Client.Core.Services
{
    public interface IPasswordDbImporter
    {
        /// <summary>
        /// Importiert Daten aus einer Passwort-Datenbank.
        /// </summary>
        /// <param name="fileStream">Stream der Import-Datei</param>
        /// <returns>Eine flache Liste von Payload-Objekten mit korrekten ParentFolderIds (falls unterstützt)</returns>
        Task<List<VaultAssetPayload>> ImportAsync(Stream fileStream);
    }
}
