The issue persists because Entity Framework's database configuration for `AuditLog` uses `DeleteBehavior.Restrict` on the `AssetId` foreign key. Furthermore, the `EzkpmDbContext.EnforceImmutability()` method throws an `InvalidOperationException` if any `AuditLog` entity state becomes `Modified`, making it impossible to update or delete audit logs when hard-deleting a `VaultAsset`.

**Technical Plan:**
1. **Modify `EzkpmDbContext.cs`**:
   Update the `EnforceImmutability()` method to allow modifying an `AuditLog` **only** if the modified property is `AssetId` and its new value is `null`. This prevents the compliance violation exception while strictly keeping other fields immutable (WORM).
2. **Modify `VaultController.cs` (`DeleteAsset`)**:
   Before calling `_db.VaultAssets.Remove(asset)`, fetch all related `AuditLog` entries for the asset and explicitly set their `AssetId` to `null`. This detaches the logs from the asset, preventing the foreign key constraint violation.
3. **Modify `VaultController.cs` (`CleanOrphanedAssets`)**:
   Apply the same fix when removing multiple orphaned assets: set `AssetId = null` for all related logs before calling `_db.VaultAssets.RemoveRange`.