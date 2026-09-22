using Sparta.SharedKernel;
using System.ComponentModel.DataAnnotations;
namespace Sparta.Modules.Inventory
{
    public class StockMovement : Entity, IValidatableObject
    {
        public virtual int ProductId { get; set; }
        public virtual Product Product { get; set; } = null!;
        public virtual int WarehouseId { get; set; }
        public virtual Warehouse Warehouse { get; set; } = null!;
        public virtual decimal QuantityDelta { get; set; }
        public virtual DateTime OccurredAt { get; set; } = DateTime.UtcNow;
        [MaxLength(100)] 
        public virtual string Reference { get; set; } = "";
        
        public IEnumerable<ValidationResult> Validate(ValidationContext context)
        {
            if (QuantityDelta == 0 || QuantityDelta < -999999999999999.999m || QuantityDelta > 999999999999999.999m || decimal.Round(QuantityDelta, 3) != QuantityDelta)
                yield return new ValidationResult("Quantity must be nonzero, with at most three decimal places.", [nameof(QuantityDelta)]);
            
            if (string.IsNullOrWhiteSpace(Reference)) yield return new ValidationResult("A reference is required.", [nameof(Reference)]);
            if (OccurredAt == default || OccurredAt > DateTime.UtcNow.AddMinutes(1)) yield return new ValidationResult("Posting time must not be in the future.", [nameof(OccurredAt)]);
        }
        
        public override void OnSaving()
        {
            base.OnSaving();
            if (!ObjectSpace.IsNewObject(this)) return;
            var product = Product ?? ObjectSpace.GetObjectByKey<Product>(ProductId);
            var warehouse = Warehouse ?? ObjectSpace.GetObjectByKey<Warehouse>(WarehouseId);
            if (product == null || warehouse == null || !product.IsActive || !warehouse.IsActive)
                throw new ValidationException("Choose an available active material and warehouse.");
        }
    }
}
