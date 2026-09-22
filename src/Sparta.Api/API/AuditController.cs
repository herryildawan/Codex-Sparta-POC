using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sparta.Audit;
using Sparta.WebApi.DatabaseUpdate;
using Sparta.Security.BusinessObject;
using Sparta.Modules.Sales.BusinessObject;
using Sparta.Modules.Inventory.BusinessObjects;
namespace Sparta.WebApi;

[Authorize, ApiController]
public class AuditController(IObjectSpaceFactory factory, ISecurityStrategyBase security, IConfiguration configuration) : ControllerBase
{
    private static readonly Dictionary<string, Type[]> Modules = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Sales"] = [typeof(Customer), typeof(SalesOrder), typeof(SalesOrderLine)],
        ["Inventory"] = [typeof(Product), typeof(Warehouse), typeof(StockMovement)]
    };
    [HttpGet("api/{module}/audit")]
    public async Task<IActionResult> Get(string module, string entityType, int entityId, int skip = 0, int take = 50)
    {
        if (!Modules.TryGetValue(module, out var types)) return NotFound();
        var type = types.FirstOrDefault(t => t.Name == entityType);
        if (type == null || entityId <= 0 || skip < 0 || take < 1 || take > 100) return BadRequest();
        var user = (ApplicationUser)security.User;
        var moduleName = type.Namespace!.Split('.').Last();
        var platformAuditor = user.Roles.Any(r => r.Name == "Platform.Audit.Read");
        if (!platformAuditor && !user.Roles.Any(r => r.Name == moduleName + ".Audit.Read")) return Forbid();
        using var os = factory.CreateObjectSpace(type);
        var target = os.GetObjectByKey(type, entityId);
        var permissions = (IRequestSecurityStrategy)security;
        // Deleted or inaccessible objects are visible only to explicitly assigned platform auditors.
        if (!platformAuditor && (target == null || !permissions.CanRead(os, target))) return NotFound();
        await using var audit = new AuditDbContext(new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlServer(configuration.GetConnectionString("Audit")).UseChangeTrackingProxies().Options);
        var key = entityId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var rows = await audit.AuditData.AsNoTracking()
            .Where(a => a.AuditedObject.TypeName == type.FullName && a.AuditedObject.Key == key)
            .OrderByDescending(a => a.ModifiedOn).ThenBy(a => a.ID)
            .Skip(skip).Take(take)
            .Select(a => new
            {
                a.ID,
                a.ModifiedOn,
                a.OperationType,
                a.PropertyName,
                a.OldValue,
                a.NewValue,
                UserName = a.UserObject.DefaultString,
                UserId = a.UserObject.Key,
                TraceId = EF.Property<string>(a, "TraceId")
            }).ToListAsync(HttpContext.RequestAborted);
        return Ok(rows.Where(a => platformAuditor || string.IsNullOrEmpty(a.PropertyName)
            || permissions.CanRead(os, target!, a.PropertyName)).Select(a => new
            {
                Application = moduleName,
                EntityType = type.Name,
                EntityId = entityId,
                a.ID,
                ModifiedOn = DateTime.SpecifyKind(a.ModifiedOn, DateTimeKind.Utc),
                a.OperationType,
                a.PropertyName,
                a.OldValue,
                a.NewValue,
                a.UserName,
                a.UserId,
                a.TraceId
            }).ToArray());
    }
}
