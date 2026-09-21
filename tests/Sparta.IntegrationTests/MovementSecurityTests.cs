using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Security;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.EF.PermissionPolicy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sparta.Audit;
using Sparta.Modules.Inventory;
using Sparta.Modules.Sales;
using Sparta.Security;

// Real HTTP/XAF/SQL tests. All writes target fresh databases, never the configured catalogs.
static class MovementSecurityTests {
    public static async Task Run(string contentRoot, IConfiguration configuration) {
        var prefix = "SpartaMovementTest_" + Guid.NewGuid().ToString("N");
        var password = Guid.NewGuid().ToString("N") + "aA!1";
        var settings = new Dictionary<string, string?> {
            ["Authentication:Local:Enabled"] = "true",
            ["Authentication:Entra:Enabled"] = "false",
            ["Authentication:Jwt:IssuerSigningKey"] = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")
        };
        foreach(var name in new[] { "Security", "Sales", "Inventory", "Audit" }) {
            var connection = new SqlConnectionStringBuilder(configuration.GetConnectionString(name)
                ?? throw new InvalidOperationException($"Missing {name} connection configuration.")) {
                InitialCatalog = prefix + "_" + name,
                // XAF clones connections for secured queries; retain SQL credentials in this test-only configuration.
                PersistSecurityInfo = true
            };
            settings["ConnectionStrings:" + name] = connection.ConnectionString;
        }
        T Context<T>(string name) where T : DbContext {
            var options = new DbContextOptionsBuilder<T>()
                .UseSqlServer(settings["ConnectionStrings:" + name], sql => sql.MigrationsAssembly(typeof(Sparta.WebApi.Program).Assembly.FullName))
                .UseChangeTrackingProxies().UseObjectSpaceLinkProxies().UseLazyLoadingProxies();
            return (T)Activator.CreateInstance(typeof(T), options.Options)!;
        }
        var databases = new DbContext[] { Context<SecurityDbContext>("Security"), Context<SalesDbContext>("Sales"),
            Context<InventoryDbContext>("Inventory"), Context<AuditDbContext>("Audit") };
        var failures = new List<string>();
        var assertions = 0;
        void Check(bool success, string description) {
            assertions++;
            Console.WriteLine($"{(success ? "PASS" : "FAIL")}: {description}");
            if(!success) failures.Add(description);
        }
        try {
            foreach(var db in databases) await db.Database.MigrateAsync();
            using var app = new WebApplicationFactory<Sparta.WebApi.Program>().WithWebHostBuilder(b => b
                .UseEnvironment("Development").UseContentRoot(contentRoot)
                .ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(settings)));
            using var client = app.CreateClient();
            int allowedProduct, deniedProduct, allowedWarehouse, deniedWarehouse;
            using(var scope = app.Services.CreateScope()) {
                var factory = scope.ServiceProvider.GetRequiredService<INonSecuredObjectSpaceFactory>();
                using(var inventory = factory.CreateNonSecuredObjectSpace<Product>()) {
                    Product Product(string code) {
                        var row = inventory.CreateObject<Product>(); row.Code = code; row.Name = code; row.IsActive = true; return row;
                    }
                    Warehouse Warehouse(string code) {
                        var row = inventory.CreateObject<Warehouse>(); row.Code = code; row.Name = code; row.IsActive = true; return row;
                    }
                    var a = Product("ALLOWED"); var d = Product("DENIED");
                    var w = Warehouse("ALLOWED"); var x = Warehouse("DENIED");
                    inventory.CommitChanges();
                    allowedProduct = a.Id; deniedProduct = d.Id; allowedWarehouse = w.Id; deniedWarehouse = x.Id;
                }
                using var security = factory.CreateNonSecuredObjectSpace<ApplicationUser>();
                var role = security.CreateObject<PermissionPolicyRole>();
                role.Name = "Movement poster with scoped reference reads";
                role.PermissionPolicy = SecurityPermissionPolicy.DenyAllByDefault;
                role.AddTypePermission<StockMovement>(SecurityOperations.Create + ";" + SecurityOperations.ReadWriteAccess, SecurityPermissionState.Allow);
                role.AddObjectPermission<Product>(SecurityOperations.Read, $"Id = {allowedProduct}", SecurityPermissionState.Allow);
                role.AddObjectPermission<Warehouse>(SecurityOperations.Read, $"Id = {allowedWarehouse}", SecurityPermissionState.Allow);
                // Relationship maintenance must not mask the reference-read test with unrelated write denial.
                role.AddMemberPermission<Product>(SecurityOperations.Write, nameof(Product.Movements), null, SecurityPermissionState.Allow);
                role.AddMemberPermission<Warehouse>(SecurityOperations.Write, nameof(Warehouse.Movements), null, SecurityPermissionState.Allow);
                security.CommitChanges();
                scope.ServiceProvider.GetRequiredService<UserManager>().CreateUser<ApplicationUser>(security,
                    "movement.scoped", password, user => user.Roles.Add(role));
                security.CommitChanges();
            }
            using var login = await client.PostAsJsonAsync("/api/Authentication/Authenticate", new { userName = "movement.scoped", password });
            login.EnsureSuccessStatusCode();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await login.Content.ReadAsStringAsync()).Trim('"'));
            foreach(var (entity, allowed, denied) in new[] { ("Product", allowedProduct, deniedProduct), ("Warehouse", allowedWarehouse, deniedWarehouse) }) {
                var json = JsonNode.Parse(await client.GetStringAsync($"/api/odata/{entity}?$select=Id"))!;
                var ids = json["value"]!.AsArray().Select(x => x!["Id"]!.GetValue<int>()).ToArray();
                Check(ids.SequenceEqual(new[] { allowed }), $"Fixture: scoped user reads only allowed {entity}; denied ID {denied} is hidden");
            }
            var inspection = (InventoryDbContext)databases[2];
            foreach(var route in new[] { "/api/inventory/movements", "/api/odata/StockMovement" }) {
                foreach(var (label, product, warehouse, permitted) in new[] {
                    ("allowed references", allowedProduct, allowedWarehouse, true),
                    ("unreadable Product", deniedProduct, allowedWarehouse, false),
                    ("unreadable Warehouse", allowedProduct, deniedWarehouse, false),
                    ("both references unreadable", deniedProduct, deniedWarehouse, false)
                }) {
                    var reference = Guid.NewGuid().ToString("N");
                    var before = await inspection.StockMovements.AsNoTracking().CountAsync();
                    using var response = await client.PostAsJsonAsync(route, new {
                        ProductId = product, WarehouseId = warehouse, QuantityDelta = 1.125m,
                        // A historical instant keeps timezone conversion separate from reference authorization.
                        OccurredAt = DateTimeOffset.UtcNow.AddDays(-1), Reference = reference
                    });
                    var status = (int)response.StatusCode;
                    // A 500 or unrelated conflict is never accepted as authorization evidence.
                    Check(permitted ? status == 201 : status is 400 or 403 or 404,
                        $"{route}: {label}, HTTP {status}, expected {(permitted ? "201" : "400/403/404")}");
                    var saved = await inspection.StockMovements.AsNoTracking().Where(x => x.Reference == reference)
                        .Select(x => new { x.ProductId, x.WarehouseId, x.QuantityDelta }).ToArrayAsync();
                    var after = await inspection.StockMovements.AsNoTracking().CountAsync();
                    Check(permitted
                        ? saved.Length == 1 && saved[0].ProductId == product && saved[0].WarehouseId == warehouse
                            && saved[0].QuantityDelta == 1.125m && after == before + 1
                        : saved.Length == 0 && after == before,
                        $"{route}: {label}, authoritative database confirms {(permitted ? "one exact movement" : "no insert")}");
                }
            }
            if(failures.Count > 0) throw new Exception($"Movement security: {failures.Count}/{assertions} assertions failed. See FAIL lines above.");
            Console.WriteLine($"SUCCESS: {assertions} movement security assertions passed.");
        } finally {
            // Strict ownership guard: only this run's generated catalogs can be deleted.
            foreach(var db in databases) {
                try {
                    if(!db.Database.GetDbConnection().Database.StartsWith(prefix + "_", StringComparison.Ordinal))
                        throw new InvalidOperationException("Refusing cleanup of a database not owned by this run.");
                    await db.Database.EnsureDeletedAsync();
                } finally { await db.DisposeAsync(); }
            }
            Console.WriteLine("Temporary movement-security databases removed.");
        }
    }
}
