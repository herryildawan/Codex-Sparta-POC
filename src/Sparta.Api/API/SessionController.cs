using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sparta.Modules.Inventory.BusinessObjects;
using Sparta.Security.BusinessObject;

namespace Sparta.WebApi;

[Authorize, ApiController]
public class SessionController(IObjectSpaceFactory factory, ISecurityStrategyBase security) : ControllerBase {
    [HttpGet("api/session")]
    public IActionResult Current() {
        using var os = factory.CreateObjectSpace<Product>();
        var user = (ApplicationUser)security.User;
        var permissions = (IRequestSecurityStrategy)security;
        return Ok(new {
            userId = user.ID, userName = user.UserName,
            roles = user.Roles.Select(r => r.Name).ToArray(),
            warehouses = new { read = permissions.CanRead(typeof(Warehouse), os), create = permissions.CanCreate(typeof(Warehouse), os) },
            movements = new { read = permissions.CanRead(typeof(StockMovement), os), create = permissions.CanCreate(typeof(StockMovement), os) },
            inventory = new {
                read = permissions.CanRead(typeof(Product), os),
                create = permissions.CanCreate(typeof(Product), os),
                readCost = permissions.CanRead(typeof(Product), os, nameof(Product.StandardCost)),
                audit = user.Roles.Any(r => r.Name == "Inventory.Audit.Read" || r.Name == "Platform.Audit.Read")
            }
        });
    }
    [HttpGet("api/inventory/products/{id:int}/permissions")]
    public IActionResult ProductPermissions(int id) {
        using var os = factory.CreateObjectSpace<Product>();
        var product = os.GetObjectByKey<Product>(id);
        var permissions = (IRequestSecurityStrategy)security;
        if(product == null || !permissions.CanRead(os, product)) return NotFound();
        return Ok(new {
            write = new[] { nameof(Product.Code), nameof(Product.Name), nameof(Product.UnitOfMeasure), nameof(Product.IsActive) }
                .All(member => permissions.CanWrite(os, product, member)),
            writeCost = permissions.CanWrite(os, product, nameof(Product.StandardCost)),
            delete = permissions.CanDelete(os, product)
        });
    }
}
