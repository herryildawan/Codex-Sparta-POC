var builder = DistributedApplication.CreateBuilder(args);
var security = builder.AddConnectionString("Security");
var sales = builder.AddConnectionString("Sales");
var inventory = builder.AddConnectionString("Inventory");
var audit = builder.AddConnectionString("Audit");
var api = builder.AddProject<Projects.Sparta_Api>("sparta-api")
    .WithReference(security).WithReference(sales).WithReference(inventory).WithReference(audit);
var webProject = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "../../../Build Sparta Web/Sparta.Mockups.csproj"));
if(File.Exists(webProject)) builder.AddProject("sparta-web", webProject)
    .WithEnvironment("SpartaApi__BaseUrl", api.GetEndpoint("http"))
    .WaitFor(api);
builder.Build().Run();
