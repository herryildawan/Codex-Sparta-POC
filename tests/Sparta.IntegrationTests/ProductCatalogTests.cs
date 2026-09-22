using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Sparta.Audit;
using Sparta.Modules.Inventory;
using Sparta.Modules.Sales;
using Sparta.Security;
using Sparta.WebApi.DatabaseUpdate;

// Generated catalogs isolate all fixture writes and permission changes from configured databases.
static class ProductCatalogTests {
    public static async Task Run(string contentRoot, IConfiguration configuration) {
        var prefix = "SpartaCatalogTest_" + Guid.NewGuid().ToString("N");
        var password = Guid.NewGuid().ToString("N") + "aA!1";
        var settings = new Dictionary<string, string?> {
            ["Authentication:Local:Enabled"] = "true",
            ["Authentication:Entra:Enabled"] = "false",
            ["Seed:Password"] = password,
            ["Authentication:Jwt:IssuerSigningKey"] = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")
        };
        foreach(var name in new[] { "Security", "Sales", "Inventory", "Audit" }) {
            var connection = new SqlConnectionStringBuilder(configuration.GetConnectionString(name)
                ?? throw new InvalidOperationException($"Missing {name} connection configuration.")) {
                InitialCatalog = prefix + "_" + name,
                PersistSecurityInfo = true
            };
            settings["ConnectionStrings:" + name] = connection.ConnectionString;
        }
        T Context<T>(string name) where T : DbContext => (T)Activator.CreateInstance(typeof(T),
            new DbContextOptionsBuilder<T>()
                .UseSqlServer(settings["ConnectionStrings:" + name], sql => sql.MigrationsAssembly(typeof(Sparta.WebApi.Program).Assembly.FullName))
                .UseChangeTrackingProxies().UseObjectSpaceLinkProxies().UseLazyLoadingProxies().Options)!;
        var databases = new DbContext[] { Context<SecurityDbContext>("Security"), Context<SalesDbContext>("Sales"),
            Context<InventoryDbContext>("Inventory"), Context<AuditDbContext>("Audit") };
        var assertions = 0;
        void Check(bool success, string description) {
            if(!success) throw new Exception("FAIL: " + description);
            assertions++;
            Console.WriteLine("PASS: " + description);
        }
        try {
            foreach(var db in databases) await db.Database.MigrateAsync();
            using var app = new WebApplicationFactory<Sparta.WebApi.Program>().WithWebHostBuilder(b => b
                .UseEnvironment("Development").UseContentRoot(contentRoot)
                .ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(settings)));
            using var client = app.CreateClient();
            PocSeeder.Seed(app.Services, new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
            async Task<string> Login(string user) {
                using var response = await client.PostAsJsonAsync("/api/Authentication/Authenticate", new { userName = user, password });
                response.EnsureSuccessStatusCode();
                return (await response.Content.ReadAsStringAsync()).Trim('"');
            }
            async Task<(int Status, JsonNode? Json)> Call(string path, string token, HttpMethod? method = null, object? body = null) {
                using var request = new HttpRequestMessage(method ?? HttpMethod.Get, path);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                if(body != null) request.Content = JsonContent.Create(body);
                using var response = await client.SendAsync(request);
                var text = await response.Content.ReadAsStringAsync();
                JsonNode? json = null;
                try { if(!string.IsNullOrWhiteSpace(text)) json = JsonNode.Parse(text); } catch(System.Text.Json.JsonException) { }
                return ((int)response.StatusCode, json);
            }
            var sales = await Login("sales.operator");
            var clerk = await Login("sales.clerk");
            var inventory = await Login("inventory.manager");
            var product = await Call("/api/odata/Product", inventory, HttpMethod.Post,
                new { Code = "CAT-TEST", Name = "Catalog Original", UnitOfMeasure = "PCS", IsActive = true });
            Check(product.Status == 201, "Create isolated catalog product");
            var productId = product.Json!["Id"]!.GetValue<int>();
            async Task<int> Order(string token, string number) {
                var result = await Call("/api/sales/orders", token, HttpMethod.Post, new { orderNumber = number, customerId = 1 });
                Check(result.Status == 201, "Create order " + number);
                return result.Json!["id"]!.GetValue<int>();
            }
            var orderId = await Order(sales, "CAT-OPERATOR");
            var clerkOrderId = await Order(clerk, "CAT-CLERK");
            object NewLine(int order, int id) => new { SalesOrderId = order, ProductId = id, Quantity = 2m, UnitPrice = 15m,
                ProductCodeSnapshot = "FORGED", ProductNameSnapshot = "FORGED" };
            var line = await Call("/api/odata/SalesOrderLine", sales, HttpMethod.Post, NewLine(orderId, productId));
            Check(line.Status == 201 && line.Json!["ProductCodeSnapshot"]!.GetValue<string>() == "CAT-TEST"
                && line.Json["ProductNameSnapshot"]!.GetValue<string>() == "Catalog Original", "New line replaces client snapshots with authoritative product values");
            var lineId = line.Json!["Id"]!.GetValue<int>();
            var count = await ((SalesDbContext)databases[1]).SalesOrderLines.CountAsync();
            Check((await Call("/api/odata/SalesOrderLine", clerk, HttpMethod.Post, NewLine(clerkOrderId, productId))).Status == 400,
                "New line rejects a product the caller cannot read");
            Check((await Call("/api/odata/SalesOrderLine", sales, HttpMethod.Post, NewLine(orderId, int.MaxValue))).Status == 400,
                "New line rejects nonexistent product");
            var filter = Uri.EscapeDataString("IsActive eq true and contains(Code,'CAT-')");
            var query = "/api/odata/Product?$select=Id,Code,Name&$filter=" + filter + "&$orderby=Code,Id&$top=1&$skip=0";
            var lookup = await Call(query, sales);
            Check(lookup.Status == 200 && lookup.Json!["value"]!.AsArray().Count == 1
                && lookup.Json["value"]![0]!["Id"]!.GetValue<int>() == productId, "Existing OData endpoint supports active product lookup");
            var deniedLookup = await Call(query, clerk);
            Check(deniedLookup.Status == 200 && deniedLookup.Json!["value"]!.AsArray().Count == 0, "OData lookup preserves Inventory permissions");
            Check((await Call($"/api/odata/Product({productId})", inventory, HttpMethod.Patch,
                new { IsActive = false, Name = "Catalog Renamed" })).Status is 200 or 204, "Deactivate and rename selected product before next submit");
            Check((await Call("/api/odata/SalesOrderLine", sales, HttpMethod.Post, NewLine(orderId, productId))).Status == 400,
                "New line revalidates product after lookup and rejects inactive product");
            Check(await ((SalesDbContext)databases[1]).SalesOrderLines.CountAsync() == count, "Rejected creates did not persist lines");
            Check((await Call($"/api/odata/SalesOrderLine({lineId})", sales, HttpMethod.Patch,
                new { Quantity = 3m, UnitPrice = 20m })).Status is 200 or 204, "Historical line remains editable after product deactivation");
            foreach(var update in new object[] { new { ProductId = 1 }, new { ProductCodeSnapshot = "CHANGED" },
                new { ProductNameSnapshot = "CHANGED" }, new { SalesOrderId = clerkOrderId } }) {
                Check((await Call($"/api/odata/SalesOrderLine({lineId})", sales, HttpMethod.Patch, update)).Status is 400 or 403 or 404,
                    "Historical reference or snapshot mutation rejected");
            }
            var saved = await ((SalesDbContext)databases[1]).SalesOrderLines.AsNoTracking().SingleAsync(x => x.Id == lineId);
            Check(saved.ProductId == productId && saved.SalesOrderId == orderId && saved.ProductCodeSnapshot == "CAT-TEST"
                && saved.ProductNameSnapshot == "Catalog Original" && saved.Quantity == 3m && saved.UnitPrice == 20m,
                "Database preserves original references/snapshots while quantity and price change");
            var inactiveLookup = await Call(query, sales);
            Check(inactiveLookup.Status == 200 && inactiveLookup.Json!["value"]!.AsArray().Count == 0, "Active lookup excludes deactivated product");
            Console.WriteLine($"SUCCESS: {assertions} product catalog assertions passed.");
        } finally {
            foreach(var db in databases) {
                try {
                    if(!db.Database.GetDbConnection().Database.StartsWith(prefix + "_", StringComparison.Ordinal))
                        throw new InvalidOperationException("Refusing cleanup of database not owned by this test.");
                    await db.Database.EnsureDeletedAsync();
                } finally { await db.DisposeAsync(); }
            }
            Console.WriteLine("Temporary product-catalog databases removed.");
        }
    }
}
