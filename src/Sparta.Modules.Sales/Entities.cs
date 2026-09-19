using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using Sparta.SharedKernel;
namespace Sparta.Modules.Sales;

public class Customer : Entity {
    [Required, MaxLength(32)] public virtual string Code { get; set; } = "";
    [Required, MaxLength(200)] public virtual string Name { get; set; } = "";
    [MaxLength(256)] public virtual string? Email { get; set; }
    public virtual bool IsActive { get; set; } = true;
    public virtual IList<SalesOrder> Orders { get; set; } = new ObservableCollection<SalesOrder>();
}
public enum OrderStatus { Draft, Confirmed, Cancelled }
public class SalesOrder : Entity {
    [Required, MaxLength(32)] public virtual string OrderNumber { get; set; } = "";
    public virtual int CustomerId { get; set; }
    public virtual Customer Customer { get; set; } = null!;
    public virtual DateTime OrderDate { get; set; } = DateTime.UtcNow.Date;
    public virtual OrderStatus Status { get; set; }
    [MaxLength(2000)] public virtual string? InternalNotes { get; set; }
    public virtual IList<SalesOrderLine> Lines { get; set; } = new ObservableCollection<SalesOrderLine>();
}
public class SalesOrderLine : Entity {
    [CreationOnly] public virtual int SalesOrderId { get; set; }
    public virtual SalesOrder SalesOrder { get; set; } = null!;
    [CreationOnly] public virtual int ProductId { get; set; }
    [CreationOnly, MaxLength(32)] public virtual string ProductCodeSnapshot { get; set; } = "";
    [CreationOnly, MaxLength(200)] public virtual string ProductNameSnapshot { get; set; } = "";
    [Range(0.001, 1000000)] public virtual decimal Quantity { get; set; }
    [Range(0, 1000000000)] public virtual decimal UnitPrice { get; set; }
    public override void OnSaving() {
        base.OnSaving();
        var catalog = (IProductCatalog?)ObjectSpace.ServiceProvider.GetService(typeof(IProductCatalog));
        var product = catalog?.FindActiveProduct(ProductId)
            ?? throw new ValidationException("Product is unavailable or access is denied.");
        if(ObjectSpace.IsNewObject(this)) {
            ProductCodeSnapshot = product.Code;
            ProductNameSnapshot = product.Name;
        }
    }
}
