using DevExpress.ExpressApp;
using Sparta.SharedKernel;
namespace Sparta.Modules.Inventory.BusinessObjects;

// Cross-module contract: secured access, no Sales dependency or cross-database navigation.
public sealed class ProductCatalog(IObjectSpaceFactory factory) : IProductCatalog {
    public ProductSnapshot? FindActiveProduct(int id) {
        using var os = factory.CreateObjectSpace<Product>();
        var product = os.GetObjectsQuery<Product>().FirstOrDefault(x => x.Id == id && x.IsActive);
        return product == null ? null : new(product.Id, product.Code, product.Name);
    }
}
