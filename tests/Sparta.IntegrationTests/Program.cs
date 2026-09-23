using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Sparta.Audit;

var config = new ConfigurationBuilder().AddUserSecrets<Sparta.WebApi.Program>().AddEnvironmentVariables().Build();
var activities = new ConcurrentBag<Activity>();
using var listener = new ActivityListener {
    ShouldListenTo = source => source.Name.Contains("SqlClient") || source.Name == "Sparta.Business" || source.Name.Contains("AspNetCore"),
    Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity => activities.Add(activity)
};
ActivitySource.AddActivityListener(listener);
long operationMeasurements = 0;
using var metrics = new MeterListener {
    InstrumentPublished = (instrument, listener) => { if(instrument.Meter.Name == "Sparta.Business") listener.EnableMeasurementEvents(instrument); }
};
metrics.SetMeasurementEventCallback<long>((instrument, value, tags, state) => Interlocked.Add(ref operationMeasurements, value));
metrics.Start();
var root = new DirectoryInfo(AppContext.BaseDirectory);
while(root != null && !File.Exists(Path.Combine(root.FullName, "Sparta.sln"))) root = root.Parent;
var contentRoot = Path.Combine(root!.FullName, "src", "Sparta.Api");
if(args.Contains("--catalog-only")) {
    try { await ProductCatalogTests.Run(contentRoot, config); return 0; }
    catch(Exception error) { Console.Error.WriteLine(error); return 1; }
}
if(args.Contains("--movement-security-only")) {
    try { await MovementSecurityTests.Run(contentRoot, config); return 0; }
    catch(Exception error) { Console.Error.WriteLine(error); return 1; }
}
var password = config["Seed:Password"] ?? throw new Exception("Seed:Password is required.");
using var application = new WebApplicationFactory<Sparta.WebApi.Program>().WithWebHostBuilder(b => b.UseEnvironment("Development").UseContentRoot(contentRoot));
using var client = application.CreateClient();
var assertions = 0;
void Check(bool success, string description) {
    if(!success) throw new Exception("FAIL: " + description);
    assertions++; Console.WriteLine("PASS: " + description);
}
async Task<string> Login(string user, HttpClient? target = null) {
    using var response = await (target ?? client).PostAsJsonAsync("/api/Authentication/Authenticate", new { userName = user, password });
    Check(response.StatusCode == HttpStatusCode.OK, "Login " + user);
    return (await response.Content.ReadAsStringAsync()).Trim('"');
}
async Task<(int Status, JsonNode? Json)> Call(string path, string? token = null, HttpMethod? method = null, object? body = null, HttpClient? target = null) {
    using var request = new HttpRequestMessage(method ?? HttpMethod.Get, path);
    if(token != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    if(body != null) request.Content = JsonContent.Create(body);
    using var response = await (target ?? client).SendAsync(request);
    var text = await response.Content.ReadAsStringAsync();
    JsonNode? json = null;
    if(!string.IsNullOrWhiteSpace(text)) {
        try { json = JsonNode.Parse(text); } catch(System.Text.Json.JsonException) { json = JsonValue.Create(text); }
    }
    return ((int)response.StatusCode, json);
}
try {
    if(args.Contains("--entra-only")) {
        await EntraTests.Run(contentRoot, Check);
        Console.WriteLine($"SUCCESS: {assertions} assertions passed.");
        return 0;
    }
    Check((await client.GetAsync("/health")).IsSuccessStatusCode, "All four database health checks");
    Check((await Call("/api/odata/Product")).Status == 401, "Anonymous API access rejected");
    var badLogin = await client.PostAsJsonAsync("/api/Authentication/Authenticate", new { userName = "sales.reader", password = "not-the-password" });
    Check(badLogin.StatusCode == HttpStatusCode.Unauthorized, "Invalid password rejected");
    var sales = await Login("sales.clerk");
    var other = await Login("sales.other");
    var reader = await Login("sales.reader");
    var inventory = await Login("inventory.reader");
    var inventoryManager = await Login("inventory.manager");
    var both = await Login("both.reader");
    var manager = await Login("sales.manager");
    var salesOperator = await Login("sales.operator");
    var admin = await Login("admin");
    var duplicateProduct = await Call("/api/odata/Product", inventoryManager, HttpMethod.Post,
        new { Code = "PROD-001", Name = "Duplicate", UnitOfMeasure = "PCS", IsActive = true, StandardCost = 1m });
    Check(duplicateProduct.Status == 400, "XAF RuleUniqueValue rejects duplicate product code before database commit");
    var invalidMovement = await Call("/api/odata/StockMovement", inventoryManager, HttpMethod.Post,
        new { ProductId = 1, WarehouseId = 1, QuantityDelta = 0m, OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-1), Reference = "XAF-INVALID" });
    Check(invalidMovement.Status == 400, "XAF RuleFromBoolProperty rejects invalid stock quantity");
    var orderNumber = "TEST-" + Guid.NewGuid().ToString("N")[..12];
    var created = await Call("/api/sales/orders", sales, HttpMethod.Post, new { orderNumber, customerId = 1 });
    Check(created.Status == 201, "Custom endpoint creates an audited order");
    int orderId = created.Json!["id"]!.GetValue<int>();
    Check(created.Json["createByUserName"]!.GetValue<string>() == "sales.clerk", "Server stamps username snapshot");
    Check(created.Json["createdByUserId"]!.GetValue<Guid>() != Guid.Empty, "Server stamps stable XAF user ID");
    var managerCreated = await Call("/api/sales/orders", manager, HttpMethod.Post, new { orderNumber = "MGR-" + orderNumber, customerId = 1 });
    Check(managerCreated.Status == 201, "Manager can create with server-assigned protected attribution");
    var operatorCreated = await Call("/api/sales/orders", salesOperator, HttpMethod.Post, new { orderNumber = "OP-" + orderNumber, customerId = 1 });
    Check(operatorCreated.Status == 201, "Sales operator creates order");
    var operatorOrderId = operatorCreated.Json!["id"]!.GetValue<int>();
    var line = await Call("/api/odata/SalesOrderLine", salesOperator, HttpMethod.Post, new { SalesOrderId = operatorOrderId, ProductId = 1, Quantity = 2m, UnitPrice = 15000m });
    Check(line.Status == 201, "Order line validates product through Inventory contract");
    Check(line.Json!["ProductCodeSnapshot"]!.GetValue<string>() == "PROD-001", "Product snapshot is populated server-side");
    var lineId = line.Json["Id"]!.GetValue<int>();
    Check((await Call($"/api/odata/SalesOrderLine({lineId})", salesOperator, HttpMethod.Patch,
        new { ProductCodeSnapshot = "FORGED" })).Status == 400, "Historical product snapshot cannot be forged");
    var foreignLine = await Call("/api/odata/SalesOrderLine", salesOperator, HttpMethod.Post,
        new { SalesOrderId = orderId, ProductId = 1, Quantity = 2m, UnitPrice = 15000m });
    Check(foreignLine.Status is 400 or 403 or 404, "Clerk cannot add a line to another user's order");
    var badLine = await Call("/api/odata/SalesOrderLine", salesOperator, HttpMethod.Post, new { SalesOrderId = operatorOrderId, ProductId = int.MaxValue, Quantity = 2m, UnitPrice = 15000m });
    Check(badLine.Status == 400, "Nonexistent cross-database product is rejected");
    Check((await Call($"/api/sales/orders/{orderId}", sales)).Status == 200, "Owner reads custom endpoint");
    Check((await Call($"/api/sales/orders/{orderId}", other)).Status == 404, "Other clerk cannot read custom endpoint");
    var ownList = await Call($"/api/odata/SalesOrder?$filter=Id eq {orderId}", sales);
    Check(ownList.Json!["value"]!.AsArray().Count == 1, "Owner reads generated endpoint");
    var otherList = await Call($"/api/odata/SalesOrder?$filter=Id eq {orderId}", other);
    Check(otherList.Json!["value"]!.AsArray().Count == 0, "Other clerk cannot read generated endpoint");
    var expanded = await Call("/api/odata/Customer(1)?$expand=Orders", other);
    Check(expanded.Status == 200 && expanded.Json!["Orders"]!.AsArray().All(x => x!["Id"]!.GetValue<int>() != orderId), "Customer navigation does not bypass order ownership");
    Check((await Call("/api/odata/Product", sales)).Json!["value"]!.AsArray().Count == 0, "Sales role cannot read Inventory data");
    Check((await Call("/api/odata/SalesOrder", inventory)).Json!["value"]!.AsArray().Count == 0, "Inventory role cannot read Sales data");
    Check((await Call("/api/odata/Product", both)).Json!["value"]!.AsArray().Count > 0, "Shared user reads Inventory");
    Check((await Call($"/api/sales/orders/{orderId}", both)).Status == 200, "Same shared user reads Sales");
    var cost = await Call("/api/odata/Product(1)", inventory);
    Check(cost.Status == 200 && (cost.Json!["StandardCost"] == null || cost.Json["StandardCost"]!.GetValue<decimal>() == 0), "Restricted cost hidden by generated endpoint");
    Check((await Call("/api/odata/Product(1)", inventoryManager)).Json!["StandardCost"]!.GetValue<decimal>() > 0, "Inventory manager reads cost");
    var changedCost = await Call("/api/odata/Product(1)", inventoryManager, HttpMethod.Patch, new { StandardCost = 12501m });
    Check(changedCost.Status is 200 or 204, "Generated endpoint audits a product update");
    var note = "PRIVATE-" + Guid.NewGuid().ToString("N");
    var updated = await Call($"/api/odata/SalesOrder({orderId})", manager, HttpMethod.Patch, new { InternalNotes = note });
    Check(updated.Status is 200 or 204, "Manager updates restricted order field");
    Check((await Call($"/api/sales/orders/{orderId}", reader)).Json!["internalNotes"] == null, "Restricted notes hidden by custom endpoint");
    var audit = await Call($"/api/Sales/audit?entityType=SalesOrder&entityId={orderId}", sales);
    Check(audit.Status == 200 && audit.Json!.AsArray().Count > 0, "Owner reads central audit history");
    Check(!audit.Json!.ToJsonString().Contains(note), "Audit history does not reveal restricted notes");
    Check(audit.Json.AsArray().Any(x => x!["traceId"]?.GetValue<string>()?.Length == 32), "Audit has distributed TraceId");
    var managerAudit = await Call($"/api/Sales/audit?entityType=SalesOrder&entityId={orderId}", manager);
    Check(managerAudit.Json!.ToJsonString().Contains(note), "Manager reads authorized note history");
    Check((await Call($"/api/Sales/audit?entityType=SalesOrder&entityId={orderId}", other)).Status == 404, "Audit enforces record ownership");
    Check((await Call($"/api/Sales/audit?entityType=SalesOrder&entityId={orderId}", inventory)).Status == 403, "Audit enforces module scope");
    var invAudit = await Call("/api/Inventory/audit?entityType=Product&entityId=1", inventory);
    Check(invAudit.Json!.AsArray().All(x => x!["propertyName"]?.GetValue<string>() != "StandardCost"), "Audit hides restricted cost changes");
    var spoof = await Call($"/api/odata/SalesOrder({orderId})", sales, HttpMethod.Patch, new { CreateByUserName = "admin" });
    Check(spoof.Status is 400 or 403, "Changing creation attribution is rejected");
    var deniedWrite = await Call("/api/sales/orders", inventory, HttpMethod.Post, new { orderNumber = "DENIED", customerId = 1 });
    Check(deniedWrite.Status == 403, "Cross-module write rejected");
    foreach(var db in new[] { "Security", "Sales", "Inventory", "Audit" }) {
        await using var connection = new SqlConnection(config.GetConnectionString(db)); await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sys.tables WHERE name LIKE 'PermissionPolicy%' OR name = 'Users'";
        var securityTables = Convert.ToInt32(await command.ExecuteScalarAsync());
        Check(db == "Security" ? securityTables > 0 : securityTables == 0, db + " database has correct security-table ownership");
        if(db is "Sales" or "Inventory") {
            command.CommandText = "SELECT COUNT(*) FROM sys.columns c JOIN sys.tables t ON c.object_id=t.object_id WHERE c.name='Id' AND (TYPE_NAME(c.user_type_id)<>'int' OR c.is_identity<>1)";
            Check(Convert.ToInt32(await command.ExecuteScalarAsync()) == 0, db + " custom keys are int IDENTITY");
        }
    }
    Check(activities.Any(a => a.Source.Name == "Sparta.Business"), "Business operation trace emitted");
    Check(activities.Any(a => a.Source.Name.Contains("SqlClient")), "SQL dependency trace emitted");
    Check(activities.Where(a => a.Source.Name.Contains("SqlClient")).All(a => a.GetTagItem("db.query.text") == null && a.GetTagItem("db.statement") == null), "SQL telemetry excludes statement text");
    Check(operationMeasurements > 0, "Business operation metric emitted");

    // A separate host points only its audit writer at a nonexistent database. Existing databases are never stopped or altered.
    var unavailable = new SqlConnectionStringBuilder(config.GetConnectionString("Audit")) {
        InitialCatalog = "SpartaAuditUnavailable_" + Guid.NewGuid().ToString("N"), ConnectTimeout = 2
    };
    using var failureApp = new WebApplicationFactory<Sparta.WebApi.Program>().WithWebHostBuilder(b => b
        .UseEnvironment("Development").UseContentRoot(contentRoot)
        .ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Audit"] = unavailable.ConnectionString })));
    using var failureClient = failureApp.CreateClient();
    var failureToken = await Login("sales.manager", failureClient);
    var failureNumber = "AUDITFAIL-" + Guid.NewGuid().ToString("N")[..12];
    var failure = await Call("/api/sales/orders", failureToken, HttpMethod.Post, new { orderNumber = failureNumber, customerId = 1 }, failureClient);
    Check(failure.Status >= 400, "Audit outage does not report successful operation");
    await using(var connection = new SqlConnection(config.GetConnectionString("Sales"))) {
        await connection.OpenAsync(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM SalesOrders WHERE OrderNumber=@number";
        command.Parameters.AddWithValue("@number", failureNumber);
        var committed = Convert.ToInt32(await command.ExecuteScalarAsync());
        Console.WriteLine($"AUDIT FAILURE OBSERVATION: business rows committed = {committed}; HTTP status = {failure.Status}. Separate databases are not an atomic transaction.");
    }
    Check((await failureClient.GetAsync("/health")).StatusCode == HttpStatusCode.ServiceUnavailable, "Readiness reports audit database outage");

    using var writeFailureApp = new WebApplicationFactory<Sparta.WebApi.Program>().WithWebHostBuilder(b => b
        .UseEnvironment("Development").UseContentRoot(contentRoot)
        .ConfigureServices(s => s.AddSingleton<IAuditSaveInterceptor, FaultingAuditInterceptor>()));
    using var writeFailureClient = writeFailureApp.CreateClient();
    var writeFailureToken = await Login("sales.manager", writeFailureClient);
    var writeFailureNumber = "SAVEFAIL-" + Guid.NewGuid().ToString("N")[..12];
    var writeFailure = await Call("/api/sales/orders", writeFailureToken, HttpMethod.Post,
        new { orderNumber = writeFailureNumber, customerId = 1 }, writeFailureClient);
    Check(writeFailure.Status == 500 && FaultingAuditInterceptor.Calls > 0, "Injected audit save failure is reached and does not report success");
    await using(var connection = new SqlConnection(config.GetConnectionString("Sales"))) {
        await connection.OpenAsync(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM SalesOrders WHERE OrderNumber=@number";
        command.Parameters.AddWithValue("@number", writeFailureNumber);
        Console.WriteLine($"AUDIT SAVE FAILURE OBSERVATION: business rows committed = {await command.ExecuteScalarAsync()}; HTTP status = {writeFailure.Status}.");
    }
    var readerProfile = await Call("/api/session", inventory);
    Check(readerProfile.Status == 200 && !readerProfile.Json!["inventory"]!["create"]!.GetValue<bool>(), "UI profile reports reader cannot create");
    var managerProfile = await Call("/api/session", inventoryManager);
    Check(managerProfile.Status == 200 && managerProfile.Json!["inventory"]!["create"]!.GetValue<bool>(), "UI profile reports manager can create");
    var productCreated = await Call("/api/odata/Product", inventoryManager, HttpMethod.Post,
        new { Code = "UI-" + Guid.NewGuid().ToString("N")[..12], Name = "UI integration product", UnitOfMeasure = "PCS", IsActive = true, StandardCost = 10m });
    Check(productCreated.Status == 201, "UI creates product through OData");
    var productId = productCreated.Json!["Id"]!.GetValue<int>();
    var productCurrent = await Call($"/api/odata/Product({productId})", inventoryManager);
    var productVersion = productCurrent.Json!["RowVersion"]!.GetValue<string>();
    var productUpdateCommand = new { Code = productCreated.Json["Code"]!.GetValue<string>(), Name = "UI edited product", UnitOfMeasure = "PCS", IsActive = true, StandardCost = 12m, RowVersion = productVersion };
    Check((await Call($"/api/inventory/products/{productId}", inventory, HttpMethod.Put, productUpdateCommand)).Status == 403, "Reader cannot call product update command");
    Check((await Call($"/api/inventory/products/{productId}", inventoryManager, HttpMethod.Put, productUpdateCommand)).Status == 204, "Manager updates product with original version");
    Check((await Call($"/api/inventory/products/{productId}", inventoryManager, HttpMethod.Put, productUpdateCommand)).Status == 409, "Stale product edit is rejected without overwrite");
    var productAudit = await Call($"/api/Inventory/audit?entityType=Product&entityId={productId}", inventoryManager);
    Check(productAudit.Status == 200 && productAudit.Json!.AsArray().Any(), "UI product edits appear in shared audit history");
    var productPermissions = await Call($"/api/inventory/products/{productId}/permissions", inventory);
    Check(productPermissions.Status == 200 && !productPermissions.Json!["write"]!.GetValue<bool>(), "Selected product exposes effective reader permissions");
    await EntraTests.Run(contentRoot, Check);
    Console.WriteLine($"SUCCESS: {assertions} assertions passed.");
    return 0;
} catch(Exception error) { Console.Error.WriteLine(error); return 1; }

public sealed class FaultingAuditInterceptor : SaveChangesInterceptor, IAuditSaveInterceptor {
    public static int Calls;
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        { Interlocked.Increment(ref Calls); throw new IOException("Integration test: audit writer unavailable during save."); }
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Interlocked.Increment(ref Calls); throw new IOException("Integration test: audit writer unavailable during save."); }
}

