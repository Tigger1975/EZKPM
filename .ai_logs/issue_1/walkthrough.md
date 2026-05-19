Die Analyse zeigte, dass der `500 Internal Server Error` beim Löschen des Key-Assets auftritt, wenn ein Hard-Delete durchgeführt wird (`asset.IsDeleted == true`).
Dabei wurde in der Methode `DeleteAsset` das Asset über `_db.VaultAssets.Remove(asset)` aus dem Kontext gelöscht. Anschließend wurde aber bedingungslos `AppendAuditLog(asset.Id, ...)` aufgerufen.
Dies führte zu einem Foreign-Key-Konflikt, da Entity Framework Core versucht hat, einen Audit-Log-Eintrag für ein Asset zu speichern, das in der gleichen Transaktion vollständig aus der Datenbank gelöscht werden soll.

**Lösung:**
Der Code in `EZKPM.Server.PDP/Controllers/VaultController.cs` wurde so angepasst, dass der Audit-Log-Eintrag ("AssetDeleted") nur noch erstellt wird, wenn ein Soft-Delete stattfindet. Bei einem Hard-Delete wird nun das Asset komplett entfernt, ohne dass zuvor noch ein neues Audit-Log erstellt wird.