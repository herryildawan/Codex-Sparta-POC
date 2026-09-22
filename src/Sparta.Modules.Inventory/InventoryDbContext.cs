using Microsoft.EntityFrameworkCore;
using Sparta.SharedKernel;
namespace Sparta.Modules.Inventory;
public class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : BusinessDbContext(options)
{
    private void ValidateMovements()
    {
        if (ChangeTracker.Entries<StockMovement>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new System.ComponentModel.DataAnnotations.ValidationException("Posted movements cannot be changed or deleted. Post a correcting movement.");
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess) { ValidateMovements(); return base.SaveChanges(acceptAllChangesOnSuccess); }
    
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ValidateMovements(); 
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public DbSet<Product> Products => Set<Product>();    
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.ConfigureBusinessModel();
        model.Entity<Product>().HasIndex(x => x.Code).IsUnique();
        model.Entity<Warehouse>().HasIndex(x => x.Code).IsUnique();
        model.Entity<Product>().Property(x => x.StandardCost).HasPrecision(18, 2);
        model.Entity<StockMovement>().Property(x => x.QuantityDelta).HasPrecision(18, 3);
        model.Entity<StockMovement>().HasOne(x => x.Product).WithMany(x => x.Movements).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<StockMovement>().HasOne(x => x.Warehouse).WithMany(x => x.Movements).HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
    }
}
