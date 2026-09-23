namespace Sparta.SharedKernel.Contracts.Inventory
{
    public interface IProductCatalog
    {
        ProductSnapshot? FindActiveProduct(int id);
    }
}
