# Product delta synchronization

Status: prototype implemented for review. The endpoint and SQL Server integration are not yet approved for production rollout.

Tracking: [OData Product delta synchronization for mobile clients #6](https://github.com/herryildawan/Codex-Sparta-POC/issues/6).

## Objective

Allow a mobile client to keep its SQLite Product cache synchronized without downloading the full catalog on every request. The client obtains an initial snapshot, stores the returned opaque delta link, and later applies only inserted, updated, or deleted products.

The HTTP contract uses OData 4.01 delta annotations. SQL Server Change Tracking supplies the underlying database change versions. The existing `RowVersion` remains responsible for optimistic concurrency on writes; it is not used as the synchronization cursor because a deleted row no longer has a readable rowversion.

## Ownership and boundaries

| Concern | Owner |
|---|---|
| Product data and change-feed abstraction | `Sparta.Modules.Inventory` |
| SQL Server `CHANGETABLE` adapter | `Sparta.Api` infrastructure |
| HTTP delta endpoint, token protection and XAF authorization | `Sparta.Api` |
| Change Tracking schema enablement | Inventory EF migration in `Sparta.Api/Migrations/Inventory` |
| Client cache and stored delta link | Mobile application |

The regular generated XAF endpoints remain unchanged:

```text
GET /api/odata/Product
POST /api/odata/Product
PATCH /api/odata/Product({id})
```

Delta synchronization is additive:

```text
GET /api/odata/Product/$delta
```

This avoids replacing the generated XAF Product controller and preserves its existing CRUD, validation, and query behavior.

## Server flow

### Initial snapshot

The mobile client calls:

```http
GET /api/odata/Product/$delta
Authorization: Bearer {token}
```

The server captures `CHANGE_TRACKING_CURRENT_VERSION()`, reads Products through a secured XAF Object Space, and returns a page. An intermediate page contains `@odata.nextLink`; the final page contains `@odata.deltaLink`.

Example final page:

```json
{
  "@odata.context": "https://api.example/api/odata/$metadata#Product/$delta",
  "value": [
    {
      "Id": 1,
      "Code": "PROD-001",
      "Name": "Product A",
      "UnitOfMeasure": "PCS",
      "IsActive": true,
      "RowVersion": "AAAAAAAAB9E="
    }
  ],
  "@odata.deltaLink": "https://api.example/api/odata/Product/$delta?$deltatoken=...&$top=100"
}
```

The client treats `@odata.nextLink` and `@odata.deltaLink` as opaque URLs. It must not inspect, construct, or modify the token.

### Incremental synchronization

The client calls the saved `@odata.deltaLink`. The server:

1. decrypts and validates the token;
2. checks `CHANGE_TRACKING_MIN_VALID_VERSION()`;
3. captures a stable upper change version for this enumeration;
4. reads changes with `CHANGETABLE(CHANGES dbo.Products, @fromVersion)`;
5. loads current insert/update records through a secured XAF Object Space;
6. emits deleted records with `@removed`;
7. returns a next link or a new delta link.

Example response:

```json
{
  "@odata.context": "https://api.example/api/odata/$metadata#Product/$delta",
  "value": [
    {
      "Id": 1,
      "Code": "PROD-001-NEW",
      "Name": "Product A",
      "UnitOfMeasure": "PCS",
      "IsActive": true,
      "RowVersion": "AAAAAAAACAE="
    },
    {
      "Id": 7,
      "@removed": {
        "reason": "deleted"
      }
    }
  ],
  "@odata.deltaLink": "https://api.example/api/odata/Product/$delta?$deltatoken=...&$top=100"
}
```

The response includes `OData-Version: 4.01`.

## Mobile application algorithm

Store Product rows and the Product delta link in the same SQLite database. Apply a complete response page transactionally:

1. begin a SQLite transaction;
2. upsert every ordinary Product item by `Id`;
3. delete every item carrying `@removed` by `Id`;
4. commit the transaction;
5. follow `@odata.nextLink` when present;
6. replace the stored delta link only after the final page is applied successfully.

Upserts and delete-by-key make replay safe. If a page fails, retain the previous link and retry. Do not advance the saved delta position before local changes commit.

When the API returns `410 Gone`, the SQL Server retention window no longer contains a complete history for the saved token. The client must discard the expired link and perform a new initial snapshot. Local replacement semantics must also remove products that are absent from the new snapshot.

## Paging and consistency

The default page size is 100 and the maximum is 500. `$top` controls only this bounded page size; arbitrary OData `$filter`, `$select`, `$expand`, `$orderby`, and `$search` are intentionally unsupported in the prototype.

The initial snapshot captures its baseline version before reading data. Changes committed afterward may appear once in the snapshot and again in the next delta, but upsert semantics make this harmless. They are not lost. Incremental pages preserve a fixed upper version and use `(SYS_CHANGE_VERSION, Id)` as the continuation cursor.

## Security

The endpoint requires authentication and Product read permission. Current Product values are loaded through `IObjectSpaceFactory`, so XAF object criteria continue to filter visible records. Member permission is checked before `StandardCost` is included; a reader denied that member never receives it.

The SQL adapter returns only Product IDs, change versions, and operations. It does not return unrestricted Product values from the raw connection.

An inaccessible changed Product is represented as removal with reason `changed`, allowing a client that previously cached it to remove it. A future security design that grants access at a finer tenant or object scope must also decide how permission changes themselves invalidate cached data; Product Change Tracking does not observe changes made only in `SpartaSecurity`.

## Delta token

The token contains the protocol version, phase, baseline version, upper version, and paging cursor. ASP.NET Core Data Protection encrypts and authenticates it. Clients must consider it opaque.

Production requirements:

- persist the Data Protection key ring outside an individual process;
- share the key ring across API replicas;
- restrict key-ring access to the API identity;
- define key lifetime and disaster-recovery handling;
- do not reuse the JWT signing key as the delta-token key.

## SQL Server configuration

The Inventory migration enables database tracking and Product table tracking:

```sql
ALTER DATABASE [SpartaInventory]
SET CHANGE_TRACKING = ON
(
    CHANGE_RETENTION = 14 DAYS,
    AUTO_CLEANUP = ON
);

ALTER TABLE dbo.Products
ENABLE CHANGE_TRACKING
WITH (TRACK_COLUMNS_UPDATED = OFF);
```

The 14-day retention period is a prototype value. Production must choose a value based on the maximum supported offline duration, storage impact, and full-resynchronization policy.

QAS and Production receive this configuration through the Inventory EF migration. Development normally uses the XAF automatic schema updater, which does not manage SQL Server Change Tracking metadata. Before rollout, choose and document one Development approach:

- run a reviewed initialization command against disposable development databases; or
- add an explicit Development-only initializer after XAF schema creation.

Do not give the QAS or Production runtime identity schema-alteration permission merely to configure Change Tracking. Schema changes remain a migration-job responsibility.

## Error behavior

| Condition | Result |
|---|---|
| Missing Product read permission | `403 Forbidden` |
| Invalid or tampered token | `400 Bad Request` |
| Token older than retained changes | `410 Gone` |
| Change Tracking missing from `dbo.Products` | Server configuration failure |

## Current implementation

- `src/Sparta.Modules.Inventory/Sync/IProductChangeFeed.cs`
- `src/Sparta.Api/Services/SqlServerProductChangeFeed.cs`
- `src/Sparta.Api/Services/ProductDeltaTokenService.cs`
- `src/Sparta.Api/API/ProductDeltaController.cs`
- `src/Sparta.Api/Migrations/Inventory/20260922090000_EnableProductChangeTracking.cs`
- `tests/Sparta.IntegrationTests/ProductCatalogTests.cs`

The current endpoint emits an OData 4.01-compatible JSON delta envelope using MVC serialization. It does not yet use the ASP.NET Core OData `DeltaSet<T>` output formatter. Review must decide whether wire-contract compatibility is sufficient or whether formatter integration is required.

## Verification

```powershell
dotnet build Sparta.sln -c Release
dotnet run --project tests/Sparta.IntegrationTests -c Release -- --catalog-only
```

The focused integration coverage verifies:

- the initial snapshot returns an OData delta link;
- a Product inserted after the baseline appears in the next delta;
- a deleted Product appears as an `@removed` tombstone;
- `StandardCost` remains hidden from a reader without member permission;
- a caller without Inventory read permission is rejected.

## Review and production acceptance

- Confirm the endpoint path and fixed Product projection.
- Decide whether native `DeltaSet<T>` formatter integration is required.
- Approve the supported mobile offline period and SQL retention value.
- Define persisted/shared Data Protection key storage.
- Define Development database provisioning.
- Test multi-page snapshot and delta enumeration under concurrent writes.
- Test expired, tampered, replayed, and cross-environment tokens.
- Decide how role or object-permission changes invalidate an existing mobile cache.
- Add observability for delta size, latency, expired tokens, full resyncs, and failures.
- Document client-side atomic application and recovery behavior in the mobile repository.
