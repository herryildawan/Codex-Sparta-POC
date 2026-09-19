using Microsoft.EntityFrameworkCore;
using Sparta.SharedKernel;
namespace Sparta.Modules.Sales;

public class SalesDbContext(DbContextOptions<SalesDbContext> options) : BusinessDbContext(options) {
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<SalesOrder> SalesOrders => Set<SalesOrder>();
    public DbSet<SalesOrderLine> SalesOrderLines => Set<SalesOrderLine>();
    protected override void OnModelCreating(ModelBuilder model) {
        model.ConfigureBusinessModel();
        model.Entity<Customer>().HasIndex(x => x.Code).IsUnique();
        model.Entity<SalesOrder>().HasIndex(x => x.OrderNumber).IsUnique();
        model.Entity<SalesOrder>().HasOne(x => x.Customer).WithMany(x => x.Orders).HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<SalesOrderLine>().HasOne(x => x.SalesOrder).WithMany(x => x.Lines).HasForeignKey(x => x.SalesOrderId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<SalesOrderLine>().Property(x => x.Quantity).HasPrecision(18, 3);
        model.Entity<SalesOrderLine>().Property(x => x.UnitPrice).HasPrecision(18, 2);
    }
}
