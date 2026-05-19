The issue states:
`Fehler beim Löschen des Key-Assets 0006f18a-c592-41ff-9866-4ee3eda7090e: Response status code does not indicate success: 500 (Internal Server Error).`

Analysis:
The error occurs when the application attempts to delete an asset, and the server returns a 500 Internal Server Error.

Looking at `VaultController.cs`, specifically the `DeleteAsset` method:

```csharp
[HttpDelete("assets/{id}")]
public async Task<IActionResult> DeleteAsset(Guid id, [FromQuery] bool forceAdmin = false)
{
    var userSidsInfo = GetUserSids();
    var allSids = userSidsInfo.AllSids;
    
    var asset = await _db.VaultAssets
        .Include(a => a.Acls.Where(acl => allSids.Contains(acl.HashedSid)))
        .FirstOrDefaultAsync(a => a.Id == id);

    if (asset == null) return NotFound();
    
    if (!forceAdmin) 
    {
        if (!asset.Acls.Any() || asset.Acls.First().PermissionLevel < 3) return Forbid(); // Only owner can delete
    }

    if (asset.IsDeleted)
    {
        _db.VaultAssets.Remove(asset);
    }
    else
    {
        asset.IsDeleted = true;
        asset.UpdatedUtc = DateTime.UtcNow;
    }

    await AppendAuditLog(asset.Id, userSidsInfo.PrimarySid, "AssetDeleted");
    await _db.SaveChangesAsync(); // <-- Exception happens here if constraints fail
    // ...
}
```

When an asset is completely deleted (`_db.VaultAssets.Remove(asset)`), what happens to its related records?
In `EzkpmDbContext.cs`, `VaultAsset` has relationships:
- `Acls` (Collection of `AssetAcl`)
- `AuditLogs`? No, wait. Let's look at `AuditLog`.

If `VaultAsset` is deleted, `AssetAcl` will probably be deleted via cascade delete.
However, what about `AuditLog`? `AuditLog` has an `AssetId` and a navigation property `Asset`.
In `EZKPM.Server.PDP.Migrations`, there is `20260512062114_RemoveAuditLogUniqueConstraint`. It modified `AuditLog`.
If `AuditLog` has a foreign key to `VaultAsset` with `ON DELETE NO ACTION` or `RESTRICT`, deleting the asset will throw an exception because there are audit logs referencing it (we literally just called `AppendAuditLog` right before `_db.SaveChangesAsync()`).

Wait, look at `DeleteAsset`:
```csharp
    if (asset.IsDeleted)
    {
        _db.VaultAssets.Remove(asset);
    }
    else
    {
        asset.IsDeleted = true;
        asset.UpdatedUtc = DateTime.UtcNow;
    }

    await AppendAuditLog(asset.Id, userSidsInfo.PrimarySid, "AssetDeleted");
    await _db.SaveChangesAsync();
```

If `asset.IsDeleted` is true (e.g., hard delete), we remove the asset from the context: `_db.VaultAssets.Remove(asset)`.
Then we immediately call `AppendAuditLog(asset.Id, ...)`, which creates a NEW `AuditLog` pointing to `asset.Id`.
If EF tries to save this, it will fail because we are inserting an `AuditLog` with a foreign key to a `VaultAsset` that is simultaneously being deleted (and thus won't exist).
Also, if there are existing `AuditLog`s, `Remove(asset)` might fail due to FK constraints if EF doesn't cascade delete logs. Assuming `AuditLog.AssetId` is nullable (`Guid? AssetId`), EF might set it to null, but `AppendAuditLog` sets `AssetId = asset.Id`.

Let's look at `AppendAuditLog` method in `VaultController.cs`:
```csharp
    private async Task AppendAuditLog(Guid assetId, string actorHashedSid, string actionType)
    {
        // ...
        _db.AuditLogs.Add(new AuditLog
        {
            AssetId = assetId,
            ActionType = actionType,
            // ...
        });
    }
```

When hard deleting an asset (it was already in `IsDeleted` state), we shouldn't append an audit log referencing it because the asset is gone. Or we should append the log but clear the `AssetId`? But `AssetId` might be required, or we just shouldn't log an asset deletion if the asset is entirely removed. Or we can just log it without foreign key constraint issues if `AssetId` is set to null.

Wait, if we remove the asset, we can't create an `AuditLog` for it.
Let's change `DeleteAsset`:
```csharp
    bool isHardDelete = asset.IsDeleted;
    if (isHardDelete)
    {
        _db.VaultAssets.Remove(asset);
        // Do not append audit log, or set AssetId to null if we want to keep the log
    }
    else
    {
        asset.IsDeleted = true;
        asset.UpdatedUtc = DateTime.UtcNow;
        await AppendAuditLog(asset.Id, userSidsInfo.PrimarySid, "AssetDeleted");
    }
```

Wait, if we hard delete the asset, what happens to existing `AuditLog`s? If they don't cascade delete, EF Core will try to set `AssetId = null` (since it's `Guid?`) or delete them. If we just don't add a new `AuditLog`, maybe the `Remove(asset)` succeeds.

Let's check `EzkpmDbContext.cs` for `AuditLog`:
```csharp
    class AuditLog
    {
        public Guid Id { get; set; }
        public Guid? AssetId { get; set; }
        public VaultAsset Asset { get; set; }
//...
```

Since it's `Guid? AssetId`, EF Core defaults to `ClientSetNull` for optional relationships. This means existing audit logs will have `AssetId` set to `null` when the asset is deleted. But adding a *new* audit log pointing to the deleted asset's ID will definitely cause an FK violation or an `InvalidOperationException` if EF detects the conflict in the same transaction.

Plan:
Modify `DeleteAsset` in `VaultController.cs` to only append the audit log if the asset is being soft-deleted. If it is being hard-deleted, we shouldn't append a log pointing to the asset (or we can just skip it, since the asset and its history are conceptually gone).

```csharp
    if (asset.IsDeleted)
    {
        _db.VaultAssets.Remove(asset);
    }
    else
    {
        asset.IsDeleted = true;
        asset.UpdatedUtc = DateTime.UtcNow;
        await AppendAuditLog(asset.Id, userSidsInfo.PrimarySid, "AssetDeleted");
    }

    await _db.SaveChangesAsync();
```