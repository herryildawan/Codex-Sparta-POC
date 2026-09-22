using DevExpress.Persistent.BaseImpl.EF.PermissionPolicy;
using Microsoft.EntityFrameworkCore;
using Sparta.Security.BusinessObject;

namespace Sparta.Security;
public class SecurityDbContext(DbContextOptions<SecurityDbContext> options) : DbContext(options)
{
    public DbSet<ApplicationUser> Users => Set<ApplicationUser>();
    public DbSet<ApplicationUserLoginInfo> UserLogins => Set<ApplicationUserLoginInfo>();
    public DbSet<PermissionPolicyRole> Roles => Set<PermissionPolicyRole>();
    
    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);
        
        model.UseDeferredDeletion(this);
        model.UseOptimisticLock();
        model.SetOneToManyAssociationDeleteBehavior(DeleteBehavior.SetNull, DeleteBehavior.Cascade);
        model.HasChangeTrackingStrategy(ChangeTrackingStrategy.ChangingAndChangedNotificationsWithOriginalValues);
        model.UsePropertyAccessMode(PropertyAccessMode.PreferFieldDuringConstruction);
        
        model.Entity<ApplicationUserLoginInfo>().HasIndex(x => new { x.LoginProviderName, x.ProviderUserKey }).IsUnique();
        model.Entity<ApplicationUser>().HasIndex(x => x.UserName).IsUnique();
    }
}
