# XAF validation for Web API writes

Sparta uses the DevExpress XAF Validation Module as the business-validation layer for both generated OData endpoints and custom command endpoints. Database constraints and .NET DataAnnotations remain as defense-in-depth; they are not the primary API validation pipeline.

## Why an explicit commit hook is required

Registering `builder.Modules.AddValidation()` makes XAF rules and `IValidator` available, but DevExpress Web API CRUD endpoints do not invoke validation automatically. Sparta replaces the default scoped `IDataService` with `ValidatedDataService`. Each Object Space created by that service subscribes to `Committing` and evaluates all modified objects in `DefaultContexts.Save`.

Registration is performed after `AddXafWebApi`:

```csharp
services.AddScoped<IDataService, ValidatedDataService>();
```

The implementation follows the DevExpress guidance in [Validate Data Sent to Web API Endpoints](https://docs.devexpress.com/eXpressAppFramework/404223/backend-web-api-service/validate-data-sent-to-web-api-endpoints).

## Write paths

| Write path | Validation mechanism |
|---|---|
| Generated OData POST/PATCH/DELETE | `ValidatedDataService` validates `IObjectSpace.ModifiedObjects` in the `Committing` event |
| Custom Sales and Inventory commands | Controllers call `objectSpace.ValidateAndCommit(validator)` |
| Direct EF Core writes | Existing DataAnnotations, `IValidatableObject`, EF constraints, and database constraints provide fallback protection |

Custom endpoints must not call `CommitChanges()` directly for business writes. Inject `IValidator` and use:

```csharp
objectSpace.ValidateAndCommit(validator);
```

This keeps custom endpoints consistent with generated OData endpoints.

## Rules currently applied

### Inventory

- `Product.Code`: required and unique.
- `Product.Name` and `Product.UnitOfMeasure`: required.
- `Warehouse.Code`: required and unique.
- `Warehouse.Name`: required.
- `StockMovement.Product`, `Warehouse`, and `Reference`: required.
- Stock movement quantity must be nonzero, remain within `decimal(18,3)`, and contain no more than three decimal places.
- Posting time must be set and cannot be more than one minute in the future.
- Referenced product and warehouse must be present and active.

Complex stock movement rules use public, non-persistent Boolean properties decorated with `RuleFromBoolProperty`. The properties are marked `NotMapped` and `Browsable(false)` so they do not become database columns or application UI fields.

### Sales

- `Customer.Code`: required and unique.
- `Customer.Name`: required.
- `SalesOrder.OrderNumber`: required and unique.
- `SalesOrder.Customer`: required.
- `SalesOrderLine.Quantity`: range `0.001` through `1,000,000`.
- `SalesOrderLine.UnitPrice`: range `0` through `1,000,000,000`.

The existing product availability lookup in `SalesOrderLine.OnSaving` remains because it crosses the Sales and Inventory database boundary through `IProductCatalog` and also enforces secured product visibility.

## Error contract

An XAF rule failure throws `DevExpress.Persistent.Validation.ValidationException`. `ApiExceptionHandler` maps this exception to HTTP `400 Bad Request`, with the XAF validation message in the Problem Details `detail` field and the request trace ID in `extensions.traceId`.

Constraint conflicts that reach SQL Server remain HTTP `409 Conflict`. For example, a unique database index is still the final authority under concurrent writes even though `RuleUniqueValue` normally rejects a duplicate before commit.

## Adding a rule

Add `DevExpress.Persistent.Base` to a module project and import `DevExpress.Persistent.Validation`. Prefer a built-in rule where possible:

```csharp
[RuleRequiredField(DefaultContexts.Save)]
public virtual string Name { get; set; } = "";

[RuleUniqueValue(DefaultContexts.Save)]
public virtual string Code { get; set; } = "";

[RuleRange(DefaultContexts.Save, 0.001, 1000000)]
public virtual decimal Quantity { get; set; }
```

For a complex in-object condition, expose a non-persistent Boolean rule property:

```csharp
[NotMapped, Browsable(false)]
[RuleFromBoolProperty("ExampleRule", DefaultContexts.Save, "The value is invalid.")]
public bool IsValid => /* business condition */;
```

Rules that require another module should use a narrow secured service contract rather than accessing that module's `DbContext` directly.

## Verification

Build the solution and run the focused real-SQL integration suite:

```powershell
dotnet build Sparta.sln --no-restore
dotnet run --project tests/Sparta.IntegrationTests --no-build -- --movement-security-only
```

The full integration runner also verifies that `RuleUniqueValue` rejects a duplicate product code and that `RuleFromBoolProperty` rejects a zero stock movement quantity:

```powershell
dotnet run --project tests/Sparta.IntegrationTests
```

The full runner uses the configured POC databases and leaves test business/audit history. Do not run it against production.

## Operational considerations

- Validation uses the secured Object Space. It does not replace XAF type, object, or member permissions.
- Database constraints must remain in place because application-level uniqueness checks cannot eliminate write races.
- Validation should not perform external side effects. A rule may execute more than once during a request.
- New custom write endpoints must use `ValidateAndCommit`; otherwise they bypass the XAF rule pipeline.
- `IValidatableObject` and DataAnnotations currently remain for direct EF Core callers and migration/schema metadata.
