# Movement reference authorization regression

Run the focused integration suite:

```powershell
dotnet run --project tests/Sparta.IntegrationTests -- --movement-security-only
```

The suite uses the configured SQL Server connections from User Secrets/environment variables, but replaces every database name with a unique `SpartaMovementTest_<run>_<module>` catalog. The SQL identity needs permission to create/drop these temporary databases. All four schemas are migrated from the current migrations. The suite creates its own user, password, JWT key, roles, products and warehouses; existing seeded accounts and configured databases are not modified. Temporary catalogs are removed in `finally`. If the process is forcibly terminated, run-owned catalogs may require manual cleanup.

The test user has movement Create/Read/Write permissions, object-level read access to one Product and one Warehouse, and member-write permission for their movement collections. A second active Product and Warehouse are unreadable. All requests otherwise contain valid data so inactive references or quantity validation cannot mask a permission defect.

Each scenario is sent to both `POST /api/inventory/movements` and `POST /api/odata/StockMovement`:

| References | HTTP expectation | Authoritative SQL expectation |
|---|---|---|
| Both readable | 201 | Exactly one movement with the requested references and quantity |
| Product unreadable | 400/403/404 | No insert |
| Warehouse unreadable | 400/403/404 | No insert |
| Both unreadable | 400/403/404 | No insert |

The suite first verifies the user's OData reference visibility. It does not accept 500 or 409 as authorization evidence. Persisted-state checks use an independent DbContext without user filtering, and compare both a unique request reference and the total movement count. Assertions continue across the matrix and any failure makes the process exit nonzero.

This focused suite is opt-in; it does not run the existing broad suite that mutates seeded POC records. It covers scalar foreign-key POST payloads. OData navigation bindings, nested creates, batches, field-permission combinations, and the broader material-isolation scenarios remain separate coverage.

## Execution: 2026-09-21

Build succeeded with zero warnings/errors. The focused suite passed all 18 assertions and exited with code 0. Both endpoints returned 201 for readable references and 400 for each unreadable-reference combination. Independent SQL inspection confirmed exactly one row per allowed request and no inserts for denied requests. All four temporary databases were removed.

The fixture uses a timestamp one day in the past. During test development, native OData rejected a timestamp one minute in the past as future while the custom endpoint accepted it. Timestamp conversion/parity requires separate investigation; this result establishes reference authorization for the historical timestamp payload, not time-handling parity.
