using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using DevExpress.Persistent.Validation;
using Sparta.SharedKernel;
namespace Sparta.Modules.Sales.BusinessObject;

public class Customer : Entity 
{
    [Required, MaxLength(32), RuleRequiredField(DefaultContexts.Save), RuleUniqueValue(DefaultContexts.Save)]
    public virtual string Code { get; set; } = "";
    
    [Required, MaxLength(200), RuleRequiredField(DefaultContexts.Save)]
    public virtual string Name { get; set; } = "";
    
    [MaxLength(256)] 
    public virtual string? Email { get; set; }
    
    public virtual bool IsActive { get; set; } = true;
    
    public virtual IList<SalesOrder> Orders { get; set; } = new ObservableCollection<SalesOrder>();
}
