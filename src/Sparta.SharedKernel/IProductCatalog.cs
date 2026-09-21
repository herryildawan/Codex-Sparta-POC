namespace Sparta.SharedKernel
{
    public interface IProductCatalog
    {
        ProductSnapshot? FindActiveProduct(int id);
    }
}
