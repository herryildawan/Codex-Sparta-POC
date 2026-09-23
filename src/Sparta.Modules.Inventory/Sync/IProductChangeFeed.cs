namespace Sparta.Modules.Inventory.Sync;

public interface IProductChangeFeed
{
    Task<long> CurrentVersionAsync(CancellationToken cancellationToken);
    Task<long> MinimumValidVersionAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ProductChange>> ReadAsync(
        long fromVersion,
        long throughVersion,
        long afterVersion,
        int afterId,
        int take,
        CancellationToken cancellationToken);
}

public sealed record ProductChange(int ProductId, long Version, ProductChangeOperation Operation);

public enum ProductChangeOperation
{
    Insert,
    Update,
    Delete
}
