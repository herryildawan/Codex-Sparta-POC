var builder = DistributedApplication.CreateBuilder(args);
var security = builder.AddConnectionString("Security");
var sales = builder.AddConnectionString("Sales");
var inventory = builder.AddConnectionString("Inventory");
var audit = builder.AddConnectionString("Audit");
var api = builder.AddProject<Projects.Sparta_Api>("sparta-api")
    .WithReference(security).WithReference(sales).WithReference(inventory).WithReference(audit);
// The net9 branch hosts the API independently. The sibling Web UI still targets .NET 10.
builder.Build().Run();
