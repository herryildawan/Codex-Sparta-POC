using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Security;
using DevExpress.ExpressApp.EFCore;

namespace Sparta.SharedKernel;

// Plain integer-key business entities. XAF's built-in security/audit classes keep their native keys.
public abstract class Entity : IXafEntityObject, IObjectSpaceLink
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public virtual int Id { get; set; }
    public virtual Guid CreatedByUserId { get; set; }
    [MaxLength(256)] public virtual string CreateByUserName { get; set; } = "";
    public virtual DateTime CreatedAtUtc { get; set; }
    [Timestamp] public virtual byte[] RowVersion { get; set; } = [];
    [NotMapped] public IObjectSpace ObjectSpace { get; set; } = null!;
    public void OnCreated()
    {
        var security = ObjectSpace.ServiceProvider.GetService(typeof(ISecurityStrategyBase)) as ISecurityStrategyBase;
        SecuredPropertySetter.SetPropertyValueWithSecurityBypass(this, nameof(CreatedByUserId), security?.UserId is Guid id ? id : Guid.Empty);
        SecuredPropertySetter.SetPropertyValueWithSecurityBypass(this, nameof(CreateByUserName), security?.UserName ?? "");
        SecuredPropertySetter.SetPropertyValueWithSecurityBypass(this, nameof(CreatedAtUtc), DateTime.UtcNow);
    }
    public void OnLoaded() { }
    public virtual void OnSaving()
    {
        // The authenticated identity is authoritative even when a client sends attribution fields.
        if (ObjectSpace.IsNewObject(this)) OnCreated();
    }
}
