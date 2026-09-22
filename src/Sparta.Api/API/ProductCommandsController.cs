using System.ComponentModel.DataAnnotations;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Security;
using DevExpress.Persistent.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sparta.Modules.Inventory.BusinessObjects;

namespace Sparta.WebApi;

// Commands enforce the version the UI actually edited; ordinary reads/creation remain OData.
[Authorize, ApiController]
public class ProductCommandsController(IObjectSpaceFactory factory, ISecurityStrategyBase security, IValidator validator) : ControllerBase {
    [HttpPut("api/inventory/products/{id:int}")]
    public IActionResult Update(int id, ProductUpdate request) {
        using var os = factory.CreateObjectSpace<Product>();
        var product = os.GetObjectByKey<Product>(id);
        var permissions = (IRequestSecurityStrategy)security;
        if(product == null || !permissions.CanRead(os, product)) return NotFound();
        var members = new[] { nameof(Product.Code), nameof(Product.Name), nameof(Product.UnitOfMeasure), nameof(Product.IsActive) };
        if(members.Any(member => !permissions.CanWrite(os, product, member)) ||
            (request.StandardCost.HasValue && !permissions.CanWrite(os, product, nameof(Product.StandardCost)))) return Forbid();
        if(Convert.ToBase64String(product.RowVersion) != request.RowVersion)
            return Conflict(new { detail = "This product changed since you opened it. Refresh and reapply your changes." });
        product.Code = request.Code; product.Name = request.Name;
        product.UnitOfMeasure = request.UnitOfMeasure; product.IsActive = request.IsActive;
        if(request.StandardCost.HasValue) product.StandardCost = request.StandardCost.Value;
        os.ValidateAndCommit(validator);
        return NoContent();
    }
    [HttpDelete("api/inventory/products/{id:int}")]
    public IActionResult Delete(int id, [FromHeader(Name = "If-Match")] string version) {
        using var os = factory.CreateObjectSpace<Product>();
        var product = os.GetObjectByKey<Product>(id);
        var permissions = (IRequestSecurityStrategy)security;
        if(product == null || !permissions.CanRead(os, product)) return NotFound();
        if(!permissions.CanDelete(os, product)) return Forbid();
        if(Convert.ToBase64String(product.RowVersion) != version.Trim('"')) return Conflict();
        // Preserve movement history; products already used in stock should be deactivated.
        if(os.GetObjectsQuery<StockMovement>().Any(x => x.ProductId == id))
            return Conflict(new { detail = "This product has stock movements. Mark it inactive instead." });
        os.Delete(product); os.ValidateAndCommit(validator);
        return NoContent();
    }
}
public record ProductUpdate([Required, MaxLength(32)] string Code, [Required, MaxLength(200)] string Name,
    [Required, MaxLength(16)] string UnitOfMeasure, bool IsActive, decimal? StandardCost, [Required] string RowVersion);
