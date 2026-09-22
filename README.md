# Sparta architecture POC

Start with [How to implement Sparta.Api](How%20To%20Implement.md) for the architecture walkthrough, setup, authentication, hands-on API lab and entity implementation exercise.

Standalone ASP.NET Core **9** on the `net9` branch + DevExpress XAF Web API **26.1.4**, using EF Core **8.0.28**. One deployable API, modular class libraries, central identity and separate business/audit databases. No XAF Blazor, WinForms, or Middle Tier projects or packages. See [Visual Studio 2022 compatibility](docs/NET9-VS2022.md) for package decisions and verification.

Business writes use the XAF Validation Module for generated OData and custom command endpoints. See [XAF validation for Web API writes](docs/XAF-VALIDATION.md) for the commit pipeline, current rules, error contract, extension guidance, and verification commands.

## Projects

Microsoft Entra ID authentication and Scalar authorization code + PKCE are implemented. See [API documentation with Scalar](docs/API-DOCUMENTATION.md) and [Entra setup and account linking](docs/ENTRA.md). Local POC password login can be disabled through configuration; XAF remains the source of business roles and permissions for both providers.

| Project | Responsibility |
|---|---|
| `src/Sparta.Api` | Composition root, JWT, generated OData endpoints, custom endpoints, migrations/seeding |
| `src/Sparta.Security` | XAF users, login info, roles and permission database |
| `src/Sparta.Modules.Sales` | Customers, sales orders and lines; SalesDbContext |
| `src/Sparta.Modules.Inventory` | Products, warehouses, stock movements; InventoryDbContext and product lookup contract implementation |
| `src/Sparta.SharedKernel` | Integer-key entity lifecycle, attribution integrity and narrow cross-module contracts |
| `src/Sparta.Audit` | XAF audit storage, secret filtering and trace correlation |
| `src/Sparta.ServiceDefaults` | OpenTelemetry, service discovery and health defaults |
| `src/Sparta.AppHost` | Aspire development orchestration/dashboard; not a business service |
| `tests/Sparta.IntegrationTests` | Executable integration suite using an in-process API and real SQL databases |

## Local setup

Prerequisites: .NET SDK 9.0.312 or a later 9.0.3xx patch, Visual Studio 2022 17.14 for IDE development, an authorized DevExpress NuGet source/license, PowerShell 7, and reachable SQL Server. Aspire AppHost uses SDK/package 9.5.2 and hosts only the API on this branch. No SQL container is required.

On this development machine, connection strings and JWT secrets are stored in .NET User Secrets under ID `sparta-architecture-poc`. `Seed:Password` is now an empty string, and the nine existing seeded users (including `admin`) use an empty password. User Secrets is development storage, not an encrypted production vault.

For another Windows development machine:

```powershell
./scripts/Initialize-Local.ps1 -Server BGALT-NAP02 -UserName sa
dotnet tool restore
dotnet build Sparta.sln
dotnet run --project src/Sparta.Api -- --migrate
dotnet run --project src/Sparta.Api -- --seed
```

The setup script securely prompts for the SQL password. Migrations create only the four configured databases; no database is dropped. Seeding is explicitly Development-only. The seed password defaults to empty unless `Seed:Password` is configured. Ordinary `--seed` preserves existing passwords; use `--seed --reset-seed-passwords` to explicitly apply the configured seed password to the nine named seed accounts already in the database. Roles, non-seed accounts and existing business data are preserved. The application never migrates databases automatically during normal startup.

Start the API independently:

```powershell
dotnet run --project src/Sparta.Api
```

API: `http://localhost:5180`. Scalar API reference: `http://localhost:5180/scalar/`. OpenAPI JSON: `http://localhost:5180/swagger/v1/swagger.json`. Development health endpoints: `/alive` and `/health`.

Or start Aspire (stop the standalone API first):

```powershell
dotnet run --project src/Sparta.AppHost --launch-profile http
```

Use the authenticated dashboard URL printed by AppHost. The API address appears in its Resources page. Aspire may assign a proxy endpoint; use that URL with `-BaseUrl` in the request script.

## Databases

| Database | Tables owned |
|---|---|
| SpartaSecurity | Users, login information, roles and type/object/member permissions |
| SpartaSales | Customers, SalesOrders, SalesOrderLines |
| SpartaInventory | Products, Warehouses, StockMovements |
| SpartaAudit | AuditData, AuditReferences |

All custom business keys are SQL `int IDENTITY`, with `int` business foreign keys. XAF security/audit types keep their GUID keys. Business entities store `CreatedByUserId` (the XAF GUID), `CreateByUserName` (creation snapshot), and `CreatedAtUtc`. Attribution is server-assigned and immutable. Each business row has SQL rowversion concurrency support.

Sales order lines hold logical `ProductId` references without cross-database foreign keys or EF navigation. Product lookup uses Inventory's secured service contract. The caller needs Inventory read permission to create a line. Product code/name snapshots and order/product references are fixed at line creation; use a replacement line to change these references. An order does not automatically issue stock. Stock on hand is the sum of movement quantities per product/warehouse.

## Authentication and permission samples

JWT issuer `Sparta`, audience `Sparta.Api`, lifetime 30 minutes. XAF permissions reload without cross-request caching. Role definitions and assignments are stored only in Security. Roles are seeded as deny-by-default. Automatic association grants are disabled; this prevents readable customer associations from revealing another clerk's orders.

| POC user | Access |
|---|---|
| admin | Explicit platform administrator and platform audit access |
| sales.reader | Sales read; internal notes hidden |
| sales.clerk / sales.other | Customer read; create and read/update their own orders |
| sales.operator | Clerk permissions plus Inventory read, for product lookup |
| sales.manager | Sales CRUD and internal notes |
| inventory.reader | Inventory read; StandardCost hidden |
| inventory.manager | Inventory CRUD including StandardCost |
| both.reader | Sales and Inventory read, using one account/token |

Relevant audit-read roles are assigned to the sample users. They do not grant business access on their own. The generated password is stored as `Seed:Password`; do not reuse these POC accounts in production.

Make authenticated requests without printing credentials or tokens:

```powershell
./scripts/Invoke-PocRequest.ps1 -UserName inventory.reader -Path '/api/odata/Product'
./scripts/Invoke-PocRequest.ps1 -UserName sales.clerk -Path '/api/sales/orders' -Method POST -Body '{"orderNumber":"DEMO-001","customerId":1}'
./scripts/Invoke-PocRequest.ps1 -UserName sales.clerk -Path '/api/Sales/audit?entityType=SalesOrder&entityId=1'
```

Generated OData entity sets are singular: `Customer`, `SalesOrder`, `SalesOrderLine`, `Product`, `Warehouse`, `StockMovement`, under `/api/odata`. A forbidden collection read normally returns an empty list; absence of HTTP 403 is not evidence of data access. Custom order reads return 404 for missing/inaccessible records.

Custom endpoints:

- `POST /api/Authentication/Authenticate`
- `POST /api/sales/orders`
- `GET /api/sales/orders/{id}`
- `GET /api/inventory/stock`
- `GET /api/{Sales|Inventory}/audit?entityType=...&entityId=...&skip=0&take=50`

## Audit and telemetry

XAF writes Sales and Inventory changes into the separate audit database. Fully qualified entity type plus key identifies the source module. Audit responses derive the application name from an allowlisted type mapping, not caller-supplied SQL/type expressions. Page size is capped at 100; field filtering may make a returned page shorter.

Audit queries require the module audit role plus current record/member permissions. Only an explicitly assigned `Platform.Audit.Read` role can inspect deleted/inaccessible records. Password/token fields are excluded. No generic audit CRUD endpoint is exposed. The audit reader intentionally uses a read-only projection after explicit authorization; arbitrary audit records are never exposed through a non-secured ObjectSpace.

New audit writes stamp UTC time and `TraceId`. Aspire receives OpenTelemetry HTTP, SQL, runtime and business telemetry; SQL query text is removed and parameters are not enabled. `Sparta.Business` emits custom operation spans and counters. Health checks probe all four database connections. Audit and telemetry have different retention and access requirements.

## Verification

Focused movement-reference authorization regression (custom API and native OData, fresh temporary databases):

```powershell
dotnet run --project tests/Sparta.IntegrationTests -- --movement-security-only
```

See [movement reference security testing](docs/QA-MOVEMENT-REFERENCE-SECURITY.md) for fixtures, database permissions, coverage and results.

Planned QA: [Material access restricted by role (SEC-MAT-001)](docs/QA-MATERIAL-ROLE-ISOLATION.md). Covers Role X/Material A and Role Y/Material B; execution is pending.

```powershell
dotnet run --project tests/Sparta.IntegrationTests
# Or build, migrate, seed and verify in Release:
./scripts/Verify.ps1
```

This suite uses real configured POC databases, creates uniquely named test orders, updates the seeded product cost, and leaves test history for inspection. Do not point it at production. It checks JWT access, role isolation, ownership, generated/custom endpoints, field restrictions, audit authorization, trace/metric emission, integer SQL identities, table placement and audit connectivity failure behavior.

## Boundaries before production

- Separate database audit persistence is not a distributed atomic transaction. **Verified:** when the audit database is unreachable at initialization, the request fails with no business insert. When an interceptor fails the audit save itself, the request returns HTTP 500 but the business row has already committed. Reliable production audit delivery requires an outbox/reconciliation design. Do not blindly retry failed writes; use business keys/idempotency.
- This POC audits business changes; login/security-administration audit events are a separate future addition.
- No user-administration UI is included; sample users/roles are provisioned by the explicit seed command.
- Integer entity IDs do not replace authorization. Avoid exposing raw DbContexts outside module infrastructure.
- Configure real JWT key management/rotation, restricted runtime SQL identities, trusted database TLS, production health endpoint access and a durable telemetry backend.
- Runtime and migration identities must be separate in production. The supplied development SQL administrator login is not a production deployment identity.
- Decide audit retention, deleted-record visibility, backup consistency and recovery targets before deployment.
- AI implementation, production provisioning and deployment are subsequent milestones.

See `docs/DEVOPS.md` for the deployment roadmap.

See [Microservices roadmap and delivery plan](docs/MICROSERVICES-ROADMAP.md) for the phased Inventory-first extraction plan, dependencies, QA acceptance gates, and rollout approach.

## Connected warehouse and movement milestone

The sibling `Build Sparta Web` UI now connects Warehouses and Stock Movements using the existing secured object spaces and audit store. `/api/session` includes `warehouses` and `movements` read/create capabilities. Warehouse record permissions are available at `GET /api/inventory/warehouses/{id}/permissions`; updates use `PUT /api/inventory/warehouses/{id}` with `RowVersion`, and deletes require `If-Match`. Creation and reads remain OData. Warehouses referenced by movements must be deactivated.

`POST /api/inventory/movements` accepts ProductId, WarehouseId, QuantityDelta, OccurredAt (offset-aware timestamp) and Reference. It rechecks creation/member permissions and active, readable references. Quantities are nonzero decimal(18,3), reference is required, and future times are rejected. Movement history is append-only, including generated OData PATCH/DELETE requests; corrections require another signed posting. Negative stock is allowed. Stock aggregation also requires read access to its source members.

There is no schema migration or role reseeding for this milestone. Regression checks live in the sibling UI's `scripts/inventory-api-check.cjs` and `scripts/inventory-browser-check.cjs`, alongside its README and VERIFICATION notes. The existing separate-database audit commit gap and lack of movement idempotency remain POC limitations: inspect history after an ambiguous failure before retrying.
