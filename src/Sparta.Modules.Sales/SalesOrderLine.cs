using Sparta.SharedKernel;
using System.ComponentModel.DataAnnotations;
using DevExpress.Persistent.Validation;
namespace Sparta.Modules.Sales
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
            var catalog = (IProductCatalog?)ObjectSpace.ServiceProvider.GetService(typeof(IProductCatalog));
            var product = catalog?.FindActiveProduct(ProductId)
                ?? throw new System.ComponentModel.DataAnnotations.ValidationException("Product is unavailable or access is denied.");
            if (ObjectSpace.IsNewObject(this))
            {
                ProductCodeSnapshot = product.Code;
                ProductNameSnapshot = product.Name;
            }
        }
    }
}
