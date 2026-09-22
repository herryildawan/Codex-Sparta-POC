using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using Sparta.SharedKernel;
namespace Sparta.Modules.Sales;

public class Customer : Entity 
{
    [Required, MaxLength(32)] 
    public virtual string Code { get; set; } = "";
    
    [Required, MaxLength(200)] 
    public virtual string Name { get; set; } = "";
    
    [MaxLength(256)] 
    public virtual string? Email { get; set; }
    
    public virtual bool IsActive { get; set; } = true;
    
    public virtual IList<SalesOrder> Orders { get; set; } = new ObservableCollection<SalesOrder>();
}
