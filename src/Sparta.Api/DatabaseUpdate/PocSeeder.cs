using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Security;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.EF.PermissionPolicy;
using Sparta.Security.BusinessObject;
using Sparta.Modules.Sales.BusinessObject;
using Sparta.Modules.Inventory.BusinessObjects;
using Sparta.SharedKernel.Abstracts;
namespace Sparta.WebApi.DatabaseUpdate;

public static class PocSeeder
{
    public static void Seed(IServiceProvider services, IConfiguration configuration, bool resetSeedPasswords = false)
    {
        using var scope = services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<INonSecuredObjectSpaceFactory>();
        using var os = factory.CreateNonSecuredObjectSpace<ApplicationUser>();
        var password = configuration["Seed:Password"] ?? string.Empty;
        PermissionPolicyRole Role(string name)
        {
            var role = os.FirstOrDefault<PermissionPolicyRole>(x => x.Name == name);
            if (role != null) return role;
            role = os.CreateObject<PermissionPolicyRole>(); role.Name = name;
            role.PermissionPolicy = SecurityPermissionPolicy.DenyAllByDefault;
            return role;
        }
        var admin = Role("PlatformAdministrator"); admin.IsAdministrative = true;
        var salesReader = Role("Sales.Reader");
        var salesClerk = Role("Sales.Clerk");
        var salesManager = Role("Sales.Manager");
        var invReader = Role("Inventory.Reader");
        var invManager = Role("Inventory.Manager");
        Role("Sales.Audit.Read"); Role("Inventory.Audit.Read");
        Role("Platform.Audit.Read");
        
        // Configure once. Subsequent seeding leaves administrator changes intact.
        if (salesReader.TypePermissions.Count == 0)
        {
            Grant<Customer>(salesReader, SecurityOperations.Read);
            Grant<SalesOrder>(salesReader, SecurityOperations.Read);
            Grant<SalesOrderLine>(salesReader, SecurityOperations.Read);
            salesReader.AddMemberPermission<SalesOrder>(SecurityOperations.Read, nameof(SalesOrder.InternalNotes), null, SecurityPermissionState.Deny);
            Grant<Customer>(salesClerk, SecurityOperations.Read);
            salesClerk.AddTypePermission<SalesOrder>(SecurityOperations.Create, SecurityPermissionState.Allow);
            salesClerk.AddObjectPermission<SalesOrder>(SecurityOperations.ReadWriteAccess, "CreatedByUserId = CurrentUserId()", SecurityPermissionState.Allow);
            salesClerk.AddTypePermission<SalesOrderLine>(SecurityOperations.Create, SecurityPermissionState.Allow);
            salesClerk.AddObjectPermission<SalesOrderLine>(SecurityOperations.ReadWriteAccess, "SalesOrder.CreatedByUserId = CurrentUserId()", SecurityPermissionState.Allow);
            salesClerk.AddMemberPermission<SalesOrder>(SecurityOperations.ReadWriteAccess, nameof(SalesOrder.InternalNotes), null, SecurityPermissionState.Deny);
            Grant<Customer>(salesManager, SecurityOperations.CRUDAccess);
            Grant<SalesOrder>(salesManager, SecurityOperations.CRUDAccess);
            Grant<SalesOrderLine>(salesManager, SecurityOperations.CRUDAccess);
            Grant<Product>(invReader, SecurityOperations.Read);
            Grant<Warehouse>(invReader, SecurityOperations.Read);
            Grant<StockMovement>(invReader, SecurityOperations.Read);
            invReader.AddMemberPermission<Product>(SecurityOperations.Read, nameof(Product.StandardCost), null, SecurityPermissionState.Deny);
            Grant<Product>(invManager, SecurityOperations.CRUDAccess);
            Grant<Warehouse>(invManager, SecurityOperations.CRUDAccess);
            Grant<StockMovement>(invManager, SecurityOperations.CRUDAccess);
        }

        if (!salesClerk.TypePermissions.Any(p => p.TargetType == typeof(Customer) && p.MemberPermissions.Any(m => m.Members == nameof(Customer.Orders))))
            salesClerk.AddMemberPermission<Customer>(SecurityOperations.Write, nameof(Customer.Orders), null, SecurityPermissionState.Allow);
        
        os.CommitChanges();
        
        var manager = scope.ServiceProvider.GetRequiredService<UserManager>();
        void User(string name, params string[] roles)
        {
            var existing = manager.FindUserByName<ApplicationUser>(os, name);
            if (existing != null)
            {
                if (resetSeedPasswords) existing.SetPassword(password);
                return;
            }
            manager.CreateUser<ApplicationUser>(os, name, password, user =>
            {
                foreach (var role in roles) user.Roles.Add(Role(role));
            });
        }
        User("admin", "PlatformAdministrator", "Platform.Audit.Read");
        User("sales.reader", "Sales.Reader", "Sales.Audit.Read");
        User("sales.clerk", "Sales.Clerk", "Sales.Audit.Read");
        User("sales.other", "Sales.Clerk", "Sales.Audit.Read");
        User("sales.operator", "Sales.Clerk", "Inventory.Reader", "Sales.Audit.Read");
        User("sales.manager", "Sales.Manager", "Sales.Audit.Read");
        User("inventory.reader", "Inventory.Reader", "Inventory.Audit.Read");
        User("inventory.manager", "Inventory.Manager", "Inventory.Audit.Read");
        User("both.reader", "Sales.Reader", "Inventory.Reader", "Sales.Audit.Read", "Inventory.Audit.Read");
        os.CommitChanges();
        
        using var sales = factory.CreateNonSecuredObjectSpace<Customer>();
        if (!sales.GetObjectsQuery<Customer>().Any())
        {
            var customer = sales.CreateObject<Customer>(); customer.Code = "CUST-001"; customer.Name = "POC Customer";
            sales.CommitChanges();
        }
        using var inventory = factory.CreateNonSecuredObjectSpace<Product>();
        if (!inventory.GetObjectsQuery<Product>().Any())
        {
            var product = inventory.CreateObject<Product>(); product.Code = "PROD-001"; product.Name = "POC Product"; product.StandardCost = 12500;
            var warehouse = inventory.CreateObject<Warehouse>(); warehouse.Code = "WH-01"; warehouse.Name = "Main Warehouse";
            var movement = inventory.CreateObject<StockMovement>(); movement.Product = product; movement.Warehouse = warehouse; movement.QuantityDelta = 100; movement.Reference = "POC opening balance";
            inventory.CommitChanges();
        }
    }
    private static void Grant<T>(PermissionPolicyRole role, string operations) where T : Entity
    {
        role.AddTypePermission<T>(operations, SecurityPermissionState.Allow);
        role.AddMemberPermission<T>(SecurityOperations.Write, "CreatedByUserId;CreateByUserName;CreatedAtUtc", null, SecurityPermissionState.Deny);
    }
}
