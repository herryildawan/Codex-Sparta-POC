using System.ComponentModel.DataAnnotations;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Security;
using DevExpress.Persistent.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sparta.Api.Services;
using Sparta.Modules.Inventory.BusinessObjects;

namespace Sparta.WebApi;

[Authorize, ApiController]
public class InventoryCommandsController(IObjectSpaceFactory factory, ISecurityStrategyBase security, IValidator validator) : ControllerBase
{
    private static readonly string[] WarehouseMembers = [nameof(Warehouse.Code), nameof(Warehouse.Name), nameof(Warehouse.IsActive)];

    [HttpGet("api/inventory/warehouses/{id:int}/permissions")]
    public IActionResult Permissions(int id)
    {
        using var os = factory.CreateObjectSpace<Warehouse>();
        var row = os.GetObjectByKey<Warehouse>(id);
        var p = (IRequestSecurityStrategy)security;
        if (row == null || !p.CanRead(os, row)) return NotFound();
        return Ok(new { write = WarehouseMembers.All(m => p.CanWrite(os, row, m)), delete = p.CanDelete(os, row) });
    }
    
    [HttpPut("api/inventory/warehouses/{id:int}")]
    public IActionResult Update(int id, WarehouseUpdate request)
    {
        using var os = factory.CreateObjectSpace<Warehouse>();
        var row = os.GetObjectByKey<Warehouse>(id);
        var p = (IRequestSecurityStrategy)security;
        if (row == null || !p.CanRead(os, row)) return NotFound();
        if (WarehouseMembers.Any(m => !p.CanWrite(os, row, m))) return Forbid();
        if (Convert.ToBase64String(row.RowVersion) != request.RowVersion) return Conflict();
        row.Code = request.Code; row.Name = request.Name; row.IsActive = request.IsActive;
        os.ValidateAndCommit(validator);
        return NoContent();
    }
    
    [HttpDelete("api/inventory/warehouses/{id:int}")]
    public IActionResult Delete(int id, [FromHeader(Name = "If-Match")] string version)
    {
        using var os = factory.CreateObjectSpace<Warehouse>();
        var row = os.GetObjectByKey<Warehouse>(id);
        var p = (IRequestSecurityStrategy)security;
        if (row == null || !p.CanRead(os, row)) return NotFound();
        if (!p.CanDelete(os, row)) return Forbid();
        if (Convert.ToBase64String(row.RowVersion) != version.Trim('"')) return Conflict();
        
        if (os.GetObjectsQuery<StockMovement>().Any(x => x.WarehouseId == id))
            return Conflict(new { detail = "This warehouse has stock movements. Mark it inactive instead." });
        
        os.Delete(row); os.ValidateAndCommit(validator); return NoContent();
    }

    [HttpPost("api/inventory/movements")]
    public IActionResult Post(MovementPost request)
    {
        using var os = factory.CreateObjectSpace<StockMovement>();
        var p = (IRequestSecurityStrategy)security;
     
        if (!p.CanCreate(typeof(StockMovement), os)) return Forbid();
        
        var product = os.GetObjectByKey<Product>(request.ProductId);
        var warehouse = os.GetObjectByKey<Warehouse>(request.WarehouseId);
        
        if (product == null || warehouse == null || !p.CanRead(os, product) || !p.CanRead(os, warehouse)
            || !product.IsActive || !warehouse.IsActive)
            return BadRequest(new { detail = "Choose an available active material and warehouse." });

        var row = os.CreateObject<StockMovement>();
        if (new[] { nameof(StockMovement.ProductId), nameof(StockMovement.WarehouseId), nameof(StockMovement.QuantityDelta), nameof(StockMovement.OccurredAt), nameof(StockMovement.Reference) }
            .Any(m => !p.CanWrite(os, row, m))) return Forbid();
        
        row.Product = product; row.Warehouse = warehouse; row.QuantityDelta = request.QuantityDelta;
        row.OccurredAt = request.OccurredAt.UtcDateTime; row.Reference = request.Reference;
        os.ValidateAndCommit(validator);
        
        return Created($"/api/odata/StockMovement({row.Id})", new { row.Id });
    }
}
public record WarehouseUpdate
(
    [Required, MaxLength(32)] 
    string Code, 
    
    [Required, MaxLength(200)] 
    string Name,
    
    bool IsActive, 
    
    [Required] 
    string RowVersion
);

public record MovementPost
(
    [Range(1, int.MaxValue)] 
    int ProductId, 

    [Range(1, int.MaxValue)] 
    int WarehouseId,

    decimal QuantityDelta, 

    DateTimeOffset OccurredAt, 
    
    [Required, MaxLength(100)] 
    string Reference
);
