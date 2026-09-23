using Sparta.SharedKernel;
using Sparta.SharedKernel.Contracts.Inventory;
using System.ComponentModel.DataAnnotations;
using DevExpress.Persistent.Validation;
using Sparta.SharedKernel.Abstracts;
namespace Sparta.Modules.Sales.BusinessObject
{
    public class SalesOrderLine : Entity
    {
        [CreationOnly] public virtual int SalesOrderId { get; set; }
        public virtual SalesOrder SalesOrder { get; set; } = null!;
        
        [CreationOnly]
        public virtual int ProductId { get; set; }
        
        [CreationOnly, MaxLength(32)]
        public virtual string ProductCodeSnapshot { get; set; } = "";
        
        [CreationOnly, MaxLength(200)]
        public virtual string ProductNameSnapshot { get; set; } = "";
        
        [Range(0.001, 1000000), RuleRange(DefaultContexts.Save, 0.001, 1000000)]
        public virtual decimal Quantity { get; set; }
        
        [Range(0, 1000000000), RuleRange(DefaultContexts.Save, 0, 1000000000)]
        public virtual decimal UnitPrice { get; set; }
        
        public override void OnSaving()
        {
            base.OnSaving();
            // Existing lines retain their historical reference and snapshots. CreationOnly
            // is enforced by BusinessDbContext even when Inventory is no longer readable.
            if (!ObjectSpace.IsNewObject(this)) return;

            var catalog = ObjectSpace.ServiceProvider.GetService(typeof(IProductCatalog)) as IProductCatalog
                ?? throw new InvalidOperationException("IProductCatalog is not registered.");
            var product = catalog.FindActiveProduct(ProductId)
                ?? throw new System.ComponentModel.DataAnnotations.ValidationException("Product is unavailable or access is denied.");
            ProductCodeSnapshot = product.Code;
            ProductNameSnapshot = product.Name;
        }
    }
}
