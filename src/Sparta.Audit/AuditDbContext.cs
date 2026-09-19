using System.Diagnostics;
using DevExpress.Persistent.BaseImpl.EFCore.AuditTrail;
using Microsoft.EntityFrameworkCore;
namespace Sparta.Audit;

// Optional, DI-provided audit-only save observers (including integration fault injection).
public interface IAuditSaveInterceptor : Microsoft.EntityFrameworkCore.Diagnostics.ISaveChangesInterceptor { }

public class AuditDbContext(DbContextOptions<AuditDbContext> options) : DbContext(options) {
    public DbSet<AuditDataItemPersistent> AuditData => Set<AuditDataItemPersistent>();
    public DbSet<AuditEFCoreWeakReference> AuditReferences => Set<AuditEFCoreWeakReference>();
    protected override void OnModelCreating(ModelBuilder model) {
        model.UseDeferredDeletion(this);
        model.HasChangeTrackingStrategy(ChangeTrackingStrategy.ChangingAndChangedNotificationsWithOriginalValues);
        model.Entity<AuditEFCoreWeakReference>().HasMany(p => p.AuditItems).WithOne(p => p.AuditedObject);
        model.Entity<AuditEFCoreWeakReference>().HasMany(p => p.OldItems).WithOne(p => p.OldObject);
        model.Entity<AuditEFCoreWeakReference>().HasMany(p => p.NewItems).WithOne(p => p.NewObject);
        model.Entity<AuditEFCoreWeakReference>().HasMany(p => p.UserItems).WithOne(p => p.UserObject);
        // Extend the built-in audit entity with shadow metadata, retaining its native XAF key.
        model.Entity<AuditDataItemPersistent>().Property<string>("TraceId").HasMaxLength(32);
    }
    private void Stamp() {
        foreach(var entry in ChangeTracker.Entries<AuditDataItemPersistent>().Where(x => x.State == EntityState.Added)) {
            entry.Property("TraceId").CurrentValue = Activity.Current?.TraceId.ToString();
            entry.Entity.ModifiedOn = DateTime.UtcNow;
        }
    }
    public override int SaveChanges(bool acceptAllChangesOnSuccess) { Stamp(); return base.SaveChanges(acceptAllChangesOnSuccess); }
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) {
        Stamp(); return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}

public class SafeAuditFilter : IAuditFilterDataProvider {
    public bool NeedToSave(IAuditDataItemPersistent item) =>
        !(item.PropertyName?.Contains("Password", StringComparison.OrdinalIgnoreCase) == true
          || item.PropertyName?.Contains("Token", StringComparison.OrdinalIgnoreCase) == true);
}
