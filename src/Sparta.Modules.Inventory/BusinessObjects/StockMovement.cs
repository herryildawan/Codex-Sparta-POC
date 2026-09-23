using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DevExpress.Persistent.Validation;
using Sparta.SharedKernel.Abstracts;

namespace Sparta.Modules.Inventory.BusinessObjects;

public class StockMovement : Entity, IValidatableObject
{
    public virtual int ProductId { get; set; }
    [RuleRequiredField(DefaultContexts.Save)]
    public virtual Product Product { get; set; } = null!;
    public virtual int WarehouseId { get; set; }
    [RuleRequiredField(DefaultContexts.Save)]
    public virtual Warehouse Warehouse { get; set; } = null!;
    public virtual decimal QuantityDelta { get; set; }
    public virtual DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    [MaxLength(100), RuleRequiredField(DefaultContexts.Save)]
    public virtual string Reference { get; set; } = "";

    [NotMapped, Browsable(false)]
    [RuleFromBoolProperty("StockMovementQuantity", DefaultContexts.Save,
        "Quantity must be nonzero, with at most three decimal places.")]
    public bool IsQuantityValid => QuantityDelta != 0
        && QuantityDelta >= -999999999999999.999m
        && QuantityDelta <= 999999999999999.999m
        && decimal.Round(QuantityDelta, 3) == QuantityDelta;

    [NotMapped, Browsable(false)]
    [RuleFromBoolProperty("StockMovementPostingTime", DefaultContexts.Save,
        "Posting time must not be in the future.")]
    public bool IsPostingTimeValid => OccurredAt != default && OccurredAt <= DateTime.UtcNow.AddMinutes(1);

    [NotMapped, Browsable(false)]
    [RuleFromBoolProperty("StockMovementReferences", DefaultContexts.Save,
        "Choose an available active material and warehouse.")]
    public bool AreReferencesValid => Product != null && Warehouse != null && Product.IsActive && Warehouse.IsActive;

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
            throw new System.ComponentModel.DataAnnotations.ValidationException("Choose an available active material and warehouse.");
    }
}
