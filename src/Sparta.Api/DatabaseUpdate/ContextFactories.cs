using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Sparta.Security;
using Sparta.Modules.Sales;
using Sparta.Modules.Inventory;
using Sparta.Audit;
namespace Sparta.WebApi.DatabaseUpdate;

public abstract class ContextFactory<T> : IDesignTimeDbContextFactory<T> where T : DbContext {
    protected abstract string DatabaseName { get; }
    public T CreateDbContext(string[] args) {
        var configuration = new ConfigurationBuilder().AddUserSecrets<Program>().AddEnvironmentVariables().Build();
        var connection = configuration.GetConnectionString(DatabaseName)
            ?? throw new InvalidOperationException($"Configure ConnectionStrings:{DatabaseName} before running migrations.");
        
        var options = new DbContextOptionsBuilder<T>();
        options.UseSqlServer(connection, sql => sql.MigrationsAssembly(typeof(Program).Assembly.FullName))
            .UseChangeTrackingProxies().UseObjectSpaceLinkProxies().UseLazyLoadingProxies();
        
        return (T)Activator.CreateInstance(typeof(T), options.Options)!;
    }
}
public sealed class SecurityFactory : ContextFactory<SecurityDbContext> { protected override string DatabaseName => "Security"; }
public sealed class SalesFactory : ContextFactory<SalesDbContext> { protected override string DatabaseName => "Sales"; }
public sealed class InventoryFactory : ContextFactory<InventoryDbContext> { protected override string DatabaseName => "Inventory"; }
public sealed class AuditFactory : ContextFactory<AuditDbContext> { protected override string DatabaseName => "Audit"; }
