using System.ComponentModel.DataAnnotations;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Security;
using DevExpress.Persistent.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sparta.Modules.Sales;
using Sparta.Modules.Inventory;
using Sparta.WebApi.Telemetry;
namespace Sparta.WebApi;

[Authorize, ApiController]
public class BusinessController(IObjectSpaceFactory factory, ISecurityStrategyBase strategy, IValidator validator) : ControllerBase
{
    [HttpGet("api/sales/orders/{id:int}")]
    public IActionResult Order(int id)
    {
        using var activity = SpartaTelemetry.Activities.StartActivity("Sales.GetOrder");
        activity?.SetTag("sparta.module", "Sales");
        using var os = factory.CreateObjectSpace<SalesOrder>();
        var order = os.GetObjectByKey<SalesOrder>(id);
        if (order == null || !((IRequestSecurityStrategy)strategy).CanRead(os, order)) return NotFound();
        return Ok(new
        {
            order.Id,
            order.OrderNumber,
            order.CustomerId,
            order.Status,
            order.CreatedByUserId,
            order.CreateByUserName,
            InternalNotes = ((IRequestSecurityStrategy)strategy).CanRead(os, order, nameof(SalesOrder.InternalNotes)) ? order.InternalNotes : null
        });
    }

    [HttpGet("api/inventory/stock")]
    public IActionResult Stock()
    {
        using var activity = SpartaTelemetry.Activities.StartActivity("Inventory.Stock");
        activity?.SetTag("sparta.module", "Inventory");
    
        using var os = factory.CreateObjectSpace<StockMovement>();
        if (!((IRequestSecurityStrategy)strategy).CanRead(typeof(StockMovement), os)) return Forbid();
        if (new[] { nameof(StockMovement.ProductId), nameof(StockMovement.WarehouseId), nameof(StockMovement.QuantityDelta) }
            .Any(m => !((IRequestSecurityStrategy)strategy).CanRead(typeof(StockMovement), os, m))) return Forbid();
        
        return Ok(os.GetObjectsQuery<StockMovement>().GroupBy(x => new { x.ProductId, x.WarehouseId })
            .Select(g => new { g.Key.ProductId, g.Key.WarehouseId, Quantity = g.Sum(x => x.QuantityDelta) }).ToArray());
    }

    [HttpPost("api/sales/orders")]
    public IActionResult CreateOrder(CreateOrderRequest request)
    {
        using var activity = SpartaTelemetry.Activities.StartActivity("Sales.CreateOrder");
        activity?.SetTag("sparta.module", "Sales");

        using var os = factory.CreateObjectSpace<SalesOrder>();
        if (!((IRequestSecurityStrategy)strategy).CanCreate(typeof(SalesOrder), os)) return Forbid();
        
        var customer = os.GetObjectByKey<Customer>(request.CustomerId);
        if (customer == null || !customer.IsActive) return BadRequest("Customer is unavailable.");
        
        var order = os.CreateObject<SalesOrder>();
        order.Customer = customer; 
        order.OrderNumber = request.OrderNumber;
        os.ValidateAndCommit(validator);

        SpartaTelemetry.Operations.Add(1, new KeyValuePair<string, object?>("sparta.module", "Sales"), new("operation", "create-order"));
        
        return Created($"/api/sales/orders/{order.Id}", new { order.Id, order.OrderNumber, order.CreatedByUserId, order.CreateByUserName });
    }
}
public record CreateOrderRequest([Required, MaxLength(32)] string OrderNumber, [Range(1, int.MaxValue)] int CustomerId);
