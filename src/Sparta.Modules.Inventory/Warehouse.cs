using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using Sparta.SharedKernel;

namespace Sparta.Modules.Inventory;

public class Warehouse : Entity
{
    [Required, MaxLength(32)] 
    public virtual string Code { get; set; } = "";
    [Required, MaxLength(200)] 
    public virtual string Name { get; set; } = "";
    public virtual bool IsActive { get; set; } = true;
    public virtual IList<StockMovement> Movements { get; set; } = new ObservableCollection<StockMovement>();
}
