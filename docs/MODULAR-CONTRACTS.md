# Inventory contract and Sales line lifecycle

Decision date: 2026-09-22. Scope: backend modular-monolith implementation. Blazor work and service extraction are tracked separately; neither is implemented by this change.

Tracking: [microservices roadmap #4](https://github.com/herryildawan/Codex-Sparta-POC/issues/4), [Blazor OData lookup #5](https://github.com/herryildawan/Codex-Sparta-POC/issues/5).

## Ownership and dependencies

Keep `IProductCatalog` and `ProductSnapshot` in `Sparta.SharedKernel/Contracts/Inventory`, namespace `Sparta.SharedKernel.Contracts.Inventory`. No additional project is needed at this stage. Sales and Inventory already reference SharedKernel. The folder is an organizational boundary, not a compiler-enforced assembly boundary: SharedKernel still references EF Core and XAF.

Contracts contain only interfaces and plain DTOs. Do not expose Inventory entities, DbContext, ObjectSpace, IQueryable, or persistence attributes. Inventory owns `BusinessObjects/ProductCatalog.cs`, its secured query and database. Sales stores a logical ProductId and historical code/name snapshots. DI is composed in Sparta.Api.

## SalesOrderLine behavior

| Operation | Behavior |
|---|---|
| Create | Resolve IProductCatalog, require an active readable product, replace client snapshot values with server values |
| Update quantity/price | Preserve existing permission and validation rules; do not re-query Inventory |
| Change ProductId, SalesOrderId, or snapshots | Reject through existing CreationOnly enforcement in BusinessDbContext |
| Read historical line | Use stored snapshots even if the product is renamed, inactive or no longer readable |

Missing DI registration is an infrastructure error, distinct from unavailable/unreadable products. Existing lines are not repriced or renamed when the catalog changes. This change does not add order-status transitions or confirmation validation; SalesOrder currently exposes Status without a dedicated confirmation workflow. Nor does it make the product read and Sales commit atomic.

Validation uses XAF OnSaving. Direct EF writes do not automatically execute this catalog lookup or populate snapshots and are not a supported substitute for the secured application write path. Future write entry points must enforce the same rules. Historical snapshots remain Sales-owned data subject to Sales permissions; revoking Inventory access does not erase them.

## Blazor lookup: planned only

Use the existing `GET /api/odata/Product` endpoint (singular Product), selecting Id, Code and Name, filtering active products and searching Code/Name. Use stable ordering Code,Id and bounded paging. Client code must escape OData literals and URL-encode query parameters. `$select` is not authorization: secured ObjectSpace permissions remain authoritative. Current sales.operator has Inventory.Reader; Sales-only access does not grant catalog access.

Bind the selected ID to ProductId; POST the line to `/api/odata/SalesOrderLine`. Server-side creation validation is required even after a successful lookup. Display snapshots read-only for persisted lines. The future UI adapter must handle debounce, cancellation, loading/errors and OData response mapping; a DevExpress CustomData LoadResult is not the OData response envelope.

No custom lookup controller, search operation on IProductCatalog, or XAF non-persistent object is required for this UI design.

## Extraction to microservices: planned only

Keep the present folder structure until extraction requires a separate distribution boundary. A C# interface is an internal port, not a network contract. At extraction, choose a versioned transport contract/client (for example OpenAPI-generated DTOs) without dragging SharedKernel's persistence base classes into all services.

An HTTP adapter may initially implement the Sales catalog port, but remote calls need asynchronous application orchestration; do not block on HTTP in synchronous OnSaving. Preserve validation across generated CRUD and custom commands. Distinguish invalid/forbidden products from dependency outages. Define caller identity, authorization, timeouts and compatibility explicitly. Consider a local catalog projection only after deciding acceptable staleness and event delivery/reconciliation requirements. Preserve the existing OData route through Inventory or a gateway if compatibility requires it.

See [microservices roadmap](MICROSERVICES-ROADMAP.md) for extraction, security, audit, data ownership and rollout work.

## Verification

Run `dotnet build Sparta.sln -c Release` and `dotnet run --project tests/Sparta.IntegrationTests -c Release -- --catalog-only`.

The catalog suite creates four uniquely named SpartaCatalogTest databases using the configured SQL servers, seeds isolated users/data, exercises real HTTP/XAF/OData writes and verifies persisted rows. It removes only its own generated catalogs. It requires SQL create/drop permissions and never targets the configured business catalogs for writes. It tests active/inactive/missing/unreadable products, forged snapshots, historical edits, immutable references and secured OData lookup. No schema migration is needed for the namespace/lifecycle changes.
