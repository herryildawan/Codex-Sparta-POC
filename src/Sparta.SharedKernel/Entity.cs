using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Security;
using DevExpress.ExpressApp.EFCore;
using Microsoft.EntityFrameworkCore;

namespace Sparta.SharedKernel;

// Plain integer-key business entities. XAF's built-in security/audit classes keep their native keys.
public abstract class Entity : IXafEntityObject, IObjectSpaceLink {
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public virtual int Id { get; set; }
    public virtual Guid CreatedByUserId { get; set; }
    [MaxLength(256)] public virtual string CreateByUserName { get; set; } = "";
    public virtual DateTime CreatedAtUtc { get; set; }
    [Timestamp] public virtual byte[] RowVersion { get; set; } = [];
    [NotMapped] public IObjectSpace ObjectSpace { get; set; } = null!;
    public void OnCreated() {
        var security = ObjectSpace.ServiceProvider.GetService(typeof(ISecurityStrategyBase)) as ISecurityStrategyBase;
        SecuredPropertySetter.SetPropertyValueWithSecurityBypass(this, nameof(CreatedByUserId), security?.UserId is Guid id ? id : Guid.Empty);
        SecuredPropertySetter.SetPropertyValueWithSecurityBypass(this, nameof(CreateByUserName), security?.UserName ?? "");
        SecuredPropertySetter.SetPropertyValueWithSecurityBypass(this, nameof(CreatedAtUtc), DateTime.UtcNow);
    }
    public void OnLoaded() { }
    public virtual void OnSaving() {
        // The authenticated identity is authoritative even when a client sends attribution fields.
        if(ObjectSpace.IsNewObject(this)) OnCreated();
    }
}

public record ProductSnapshot(int Id, string Code, string Name);
public interface IProductCatalog { ProductSnapshot? FindActiveProduct(int id); }
[AttributeUsage(AttributeTargets.Property)]
public sealed class CreationOnlyAttribute : Attribute { }

public abstract class BusinessDbContext(DbContextOptions options) : DbContext(options) {
    private void ValidateChanges() {
        foreach(var entry in ChangeTracker.Entries<Entity>().Where(e => e.State is EntityState.Added or EntityState.Modified)) {
            if(entry.State == EntityState.Modified) {
                foreach(var property in new[] { "CreatedByUserId", "CreateByUserName", "CreatedAtUtc" }) {
                    if(!Equals(entry.Property(property).OriginalValue, entry.Property(property).CurrentValue))
                        throw new ValidationException("Creation attribution cannot be changed.");
                }
                foreach(var property in entry.Properties.Where(p => p.Metadata.PropertyInfo?.IsDefined(typeof(CreationOnlyAttribute), true) == true))
                    if(!Equals(property.OriginalValue, property.CurrentValue))
                        throw new ValidationException("Historical line references and snapshots cannot be changed; create a replacement line.");
            }
            Validator.ValidateObject(entry.Entity, new ValidationContext(entry.Entity), validateAllProperties: true);
        }
    }
    public override int SaveChanges(bool acceptAllChangesOnSuccess) { ValidateChanges(); return base.SaveChanges(acceptAllChangesOnSuccess); }
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) {
        ValidateChanges(); return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}

public static class ModelConfiguration {
    public static void ConfigureBusinessModel(this ModelBuilder modelBuilder) {
        modelBuilder.HasChangeTrackingStrategy(ChangeTrackingStrategy.ChangingAndChangedNotificationsWithOriginalValues);
        modelBuilder.UsePropertyAccessMode(PropertyAccessMode.PreferFieldDuringConstruction);
        foreach(var entity in modelBuilder.Model.GetEntityTypes())
            foreach(var property in entity.GetProperties().Where(p => p.ClrType == typeof(DateTime)))
                property.SetValueConverter(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
                    value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc)));
    }
}
