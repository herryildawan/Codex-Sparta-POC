using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace Sparta.SharedKernel
{
    public abstract class BusinessDbContext(DbContextOptions options) : DbContext(options)
    {
        private void ValidateChanges()
        {
            foreach (var entry in ChangeTracker.Entries<Entity>().Where(e => e.State is EntityState.Added or EntityState.Modified))
            {
                if (entry.State == EntityState.Modified)
                {
                    foreach (var property in new[] { "CreatedByUserId", "CreateByUserName", "CreatedAtUtc" })
                    {
                        if (!Equals(entry.Property(property).OriginalValue, entry.Property(property).CurrentValue))
                            throw new ValidationException("Creation attribution cannot be changed.");
                    }
                    foreach (var property in entry.Properties.Where(p => p.Metadata.PropertyInfo?.IsDefined(typeof(CreationOnlyAttribute), true) == true))
                        if (!Equals(property.OriginalValue, property.CurrentValue))
                            throw new ValidationException("Historical line references and snapshots cannot be changed; create a replacement line.");
                }
                Validator.ValidateObject(entry.Entity, new ValidationContext(entry.Entity), validateAllProperties: true);
            }
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess) 
        { 
            ValidateChanges(); 
            return base.SaveChanges(acceptAllChangesOnSuccess); 
        }
        
        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            ValidateChanges(); return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
    }
}
