using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using DevExpress.Persistent.Validation;
using Sparta.SharedKernel;
namespace Sparta.Modules.Sales
{
    public class SalesOrder : Entity
    {
        [Required, MaxLength(32)]
        [RuleRequiredField(DefaultContexts.Save)]
        [RuleUniqueValue(DefaultContexts.Save)]
        public virtual string OrderNumber { get; set; } = "";
        
        public virtual int CustomerId { get; set; }
        
        [RuleRequiredField(DefaultContexts.Save)]
        public virtual Customer Customer { get; set; } = null!;
        
        public virtual DateTime OrderDate { get; set; } = DateTime.UtcNow.Date;
        
        public virtual OrderStatus Status { get; set; }
        
        [MaxLength(2000)] 
        public virtual string? InternalNotes { get; set; }
        
        public virtual IList<SalesOrderLine> Lines { get; set; } = new ObservableCollection<SalesOrderLine>();
    }
}
