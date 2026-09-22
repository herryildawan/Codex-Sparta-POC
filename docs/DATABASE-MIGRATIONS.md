# Database Schema and Migration Operations

Sparta uses different schema-update strategies for development and controlled environments:

| Environment | Schema owner | Automatic update at API startup | `--migrate` |
|---|---|---:|---:|
| Development | XAF schema updater | Yes | Rejected |
| QAS | EF Core migrations | No | Required as a deployment step |
| Production | EF Core migrations | No | Required as a deployment step |

The environment is selected at runtime through `ASPNETCORE_ENVIRONMENT`. A Debug or Release build does not select the schema strategy. Only the exact ASP.NET Core environment name `Development` enables automatic schema updates; `QAS`, `Staging`, `Production`, an unset environment, and every other name disable them.

## Development workflow

Use separate, disposable development databases. Configure the four development connection strings (`Security`, `Sales`, `Inventory`, and `Audit`) and start the API with the Development environment:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project src/Sparta.Api --launch-profile http
```

At startup, XAF checks the business model and automatically creates or updates the development schemas. This shortens the edit-run cycle, but it does not add entries to EF Core's `__EFMigrationsHistory` table.

Do not run the following command in Development:

```powershell
dotnet run --project src/Sparta.Api -- --migrate
```

The application rejects it intentionally. Mixing XAF automatic changes and EF migrations in the same database can make a later migration try to create a table, column, or index that XAF already created. If a development schema becomes inconsistent, recreate the development database rather than promoting or repairing it as a QAS/Production database.

Development seeding remains explicit and is allowed only in Development:

```powershell
dotnet run --project src/Sparta.Api -- --seed
```

## Create and review migrations

Automatic development updates do not replace migration source files. After changing an entity model, create a migration for every affected `DbContext`. Run commands from the repository root and use a unique, descriptive migration name.

```powershell
dotnet ef migrations add <MigrationName> --project src/Sparta.Api --startup-project src/Sparta.Api --context SecurityDbContext --output-dir Migrations/Security
dotnet ef migrations add <MigrationName> --project src/Sparta.Api --startup-project src/Sparta.Api --context SalesDbContext --output-dir Migrations/Sales
dotnet ef migrations add <MigrationName> --project src/Sparta.Api --startup-project src/Sparta.Api --context InventoryDbContext --output-dir Migrations/Inventory
dotnet ef migrations add <MigrationName> --project src/Sparta.Api --startup-project src/Sparta.Api --context AuditDbContext --output-dir Migrations/Audit
```

Only generate migrations for contexts whose model changed. Before committing:

1. Review the generated `Up` and `Down` operations and the model snapshot.
2. Check for unintended drops, destructive type changes, table rebuilds, and ownership changes.
3. Build and test the solution.
4. Validate the migrations on a temporary database whose starting schema represents the currently deployed QAS/Production version, not on an automatically updated development database.
5. Commit the migration and snapshot together with the model change.

Migration generation compares the current EF model to its committed model snapshot. It does not infer a migration from changes XAF made to a development database.

## QAS deployment

Configure QAS secrets and connection strings outside the repository, then set the environment explicitly:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'QAS'
dotnet run --project src/Sparta.Api -- --migrate
```

The command applies pending EF migrations in this order:

1. Security
2. Sales
3. Inventory
4. Audit

It exits after migration and does not start the API. Run it as a dedicated deployment job before starting or switching traffic to the new API version. Use a migration database identity with only the permissions needed for schema deployment; the normal API runtime identity should not require schema-change permissions.

After migration, start the API normally with `ASPNETCORE_ENVIRONMENT=QAS`. XAF checks schema compatibility but cannot change the schema because `DisableUpdateSchema` is enabled and `DatabaseUpdateMode` is `Never`.

## Production deployment

Use the same immutable application artifact and reviewed migration files that passed QAS. Back up the affected databases and verify the restore procedure before applying a destructive or long-running migration.

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Production'
dotnet run --project src/Sparta.Api -- --migrate
```

Do not use XAF's `-updateDatabase`, `-silent`, or `-forceUpdate` switches for the QAS/Production workflow. Those switches invoke the XAF schema updater; Sparta uses EF Core migrations as the only schema authority outside Development.

The four migrations are applied sequentially, not as one distributed transaction. If one context fails, previously completed contexts may already be upgraded. Stop the deployment, record which contexts completed, correct the failure, and rerun the idempotent EF migration command. Do not manually mark a migration as applied without verifying its complete schema effects.

Application rollback does not automatically roll back database schema. Prefer backward-compatible, additive migrations and deploy breaking changes in phases so both the old and new application versions can operate during rollout and rollback.

## Operational safeguards

- Never point a Development instance at QAS or Production connection strings.
- Keep distinct credentials and database names for every environment.
- Never promote or restore an automatically updated Development database into QAS or Production.
- Do not grant the QAS/Production runtime identity schema modification permissions.
- Back up and rehearse restoration before risky production migrations.
- Inspect `__EFMigrationsHistory` independently in all four databases when diagnosing partial deployments.
- Treat an unexpected schema mismatch outside Development as a failed deployment; do not enable automatic update to bypass it.

## References

- [DevExpress: Update Database Schema and Migrations in EF Core](https://docs.devexpress.com/eXpressAppFramework/405418/business-model-design-orm/business-model-design-with-entity-framework-core/update-database-and-migrations-in-ef-core)
- [DevExpress: Production Database and Application Updates](https://docs.devexpress.com/eXpressAppFramework/113239/deployment/production-database-and-application-updates)
