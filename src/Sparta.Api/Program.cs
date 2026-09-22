using DevExpress.ExpressApp;
using Microsoft.EntityFrameworkCore;
using Sparta.WebApi.DatabaseUpdate;

namespace Sparta.WebApi;
public class Program
{
    public static async Task<int> Main(string[] args)
    {
        FrameworkSettings.DefaultSettingsCompatibilityMode = FrameworkSettingsCompatibilityMode.Latest;
        DevExpress.ExpressApp.Security.SecurityStrategy.AutoAssociationReferencePropertyMode = DevExpress.ExpressApp.Security.ReferenceWithoutAssociationPermissionsMode.None;

        var builder = WebApplication.CreateBuilder(args);

        if (args.Length > 0 && args[0] == "--link-entra")
        {
            if (args.Length != 4) throw new ArgumentException("Usage: --link-entra <Sparta username> <tenant GUID> <Entra user object GUID>");
            var key = JWT.EntraAuthentication.Key(args[2], args[3]);
            await using var db = new SecurityFactory().CreateDbContext([]);
            var user = await db.Users.SingleAsync(x => x.UserName == args[1] && x.IsActive);
            var existing = await db.UserLogins.SingleOrDefaultAsync(x => x.LoginProviderName == JWT.EntraAuthentication.Scheme && x.ProviderUserKey == key);
            if (existing != null && existing.UserForeignKey != user.ID)
                throw new InvalidOperationException("Identity is already linked to another user; refusing reassignment.");
            if (existing == null)
            {
                var login = db.CreateProxy<Sparta.Security.ApplicationUserLoginInfo>();
                login.LoginProviderName = JWT.EntraAuthentication.Scheme;
                login.ProviderUserKey = key;
                login.User = user;
                db.UserLogins.Add(login);
                await db.SaveChangesAsync();
            }
            Console.WriteLine("Entra identity linked. Existing XAF roles are unchanged.");
            return 0;
        }

        if (args.Contains("--migrate"))
        {
            if (builder.Environment.IsDevelopment())
                throw new InvalidOperationException("Do not apply EF Core migrations to a Development database. Development schema is managed automatically by XAF.");

            // Migrate all four databases in order: Security, Sales, Inventory, Audit
            await using var security = new SecurityFactory().CreateDbContext([]);
            await security.Database.MigrateAsync();
            
            await using var sales = new SalesFactory().CreateDbContext([]);
            await sales.Database.MigrateAsync();
            
            await using var inventory = new InventoryFactory().CreateDbContext([]);
            await inventory.Database.MigrateAsync();
            
            await using var audit = new AuditFactory().CreateDbContext([]);
            await audit.Database.MigrateAsync();
            
            Console.WriteLine("All four database migrations completed.");
            return 0;
        }

        builder.AddServiceDefaults();
        
        var startup = new Startup(builder.Configuration, builder.Environment);
        startup.ConfigureServices(builder.Services);
        
        var app = builder.Build();
        if (args.Contains("--seed"))
        {
            if (!app.Environment.IsDevelopment()) throw new InvalidOperationException("POC seeding is Development-only.");
            PocSeeder.Seed(app.Services, app.Configuration, args.Contains("--reset-seed-passwords"));
            Console.WriteLine("POC seed completed. Seed:Password defaults to empty in Development; existing passwords change only with --reset-seed-passwords.");
            return 0;
        }
        
        startup.Configure(app, app.Environment);
        app.MapDefaultEndpoints();
        
        await app.RunAsync();
        
        return 0;
    }
}
