using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using DevExpress.Persistent.Validation;
using Sparta.SharedKernel;
namespace Sparta.Modules.Inventory.BusinessObjects;

public class Product : Entity
{
    [Required, MaxLength(32), RuleRequiredField(DefaultContexts.Save), RuleUniqueValue(DefaultContexts.Save)]
    public virtual string Code { get; set; } = "";
   
    [Required, MaxLength(200), RuleRequiredField(DefaultContexts.Save)]
    public virtual string Name { get; set; } = "";
    
    [Required, MaxLength(16), RuleRequiredField(DefaultContexts.Save)]
    public virtual string UnitOfMeasure { get; set; } = "PCS";
    
    public virtual decimal StandardCost { get; set; }
    
    public virtual bool IsActive { get; set; } = true;
    
    public virtual IList<StockMovement> Movements { get; set; } = new ObservableCollection<StockMovement>();
}
