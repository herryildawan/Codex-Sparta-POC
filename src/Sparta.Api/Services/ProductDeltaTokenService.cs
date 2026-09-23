using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Sparta.Api.Services;

public sealed class ProductDeltaTokenService(IDataProtectionProvider provider)
{
    private readonly IDataProtector protector = provider.CreateProtector("Sparta.Inventory.ProductDelta.v1");

    public string Encode(ProductDeltaToken token) => protector.Protect(JsonSerializer.Serialize(token));

    public bool TryDecode(string value, out ProductDeltaToken token)
    {
        try
        {
            token = JsonSerializer.Deserialize<ProductDeltaToken>(protector.Unprotect(value))
                ?? throw new JsonException("The delta token is empty.");
            return token.Version == 1 && Enum.IsDefined(token.Phase)
                && token.FromVersion >= 0 && token.ThroughVersion >= 0
                && token.AfterVersion >= 0 && token.AfterId >= 0;
        }
        catch (Exception exception) when (exception is System.Security.Cryptography.CryptographicException or JsonException)
        {
            token = default!;
            return false;
        }
    }
}

public sealed record ProductDeltaToken(
    int Version,
    ProductDeltaPhase Phase,
    long FromVersion,
    long ThroughVersion,
    long AfterVersion,
    int AfterId);

public enum ProductDeltaPhase
{
    Snapshot,
    Delta
}
