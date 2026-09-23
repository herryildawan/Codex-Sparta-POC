using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Sparta.Api.Services;
using Sparta.Modules.Inventory.BusinessObjects;
using Sparta.Modules.Inventory.Sync;

namespace Sparta.WebApi;

[Authorize, ApiController]
public sealed class ProductDeltaController(
    IProductChangeFeed changeFeed,
    ProductDeltaTokenService tokens,
    IObjectSpaceFactory objectSpaceFactory,
    ISecurityStrategyBase security) : ControllerBase
{
    private const int DefaultPageSize = 100;
    private const int MaximumPageSize = 500;

    [HttpGet("api/odata/Product/$delta")]
    public async Task<IActionResult> Get(
        [FromQuery(Name = "$deltatoken")] string? deltaToken,
        [FromQuery(Name = "$top")] int? requestedPageSize,
        CancellationToken cancellationToken)
    {
        Response.Headers["OData-Version"] = "4.01";

        var permissions = (IRequestSecurityStrategy)security;
        using var objectSpace = objectSpaceFactory.CreateObjectSpace<Product>();
        if (!permissions.CanRead(typeof(Product), objectSpace))
            return Forbid();

        var pageSize = Math.Clamp(requestedPageSize ?? DefaultPageSize, 1, MaximumPageSize);
        ProductDeltaToken? token = null;
        if (deltaToken is not null && !tokens.TryDecode(deltaToken, out token))
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid delta token",
                Detail = "Request a new Product snapshot; the supplied delta token is invalid."
            });

        return token?.Phase == ProductDeltaPhase.Delta
            ? await ReadDelta(objectSpace, permissions, token, pageSize, cancellationToken)
            : await ReadSnapshot(objectSpace, permissions, token, pageSize, cancellationToken);
    }

    private async Task<IActionResult> ReadSnapshot(
        IObjectSpace objectSpace,
        IRequestSecurityStrategy permissions,
        ProductDeltaToken? token,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var throughVersion = token?.ThroughVersion ?? await changeFeed.CurrentVersionAsync(cancellationToken);
        var afterId = token?.AfterId ?? 0;
        var products = objectSpace.GetObjectsQuery<Product>()
            .Where(product => product.Id > afterId)
            .OrderBy(product => product.Id)
            .Take(pageSize + 1)
            .ToArray();
        var hasMore = products.Length > pageSize;
        var page = products.Take(pageSize).ToArray();
        var items = page.Select(product => ProductPayload(objectSpace, permissions, product)).ToArray();

        if (hasMore)
        {
            var next = new ProductDeltaToken(1, ProductDeltaPhase.Snapshot, 0, throughVersion, 0, page[^1].Id);
            return Ok(Envelope(items, nextLink: Link(tokens.Encode(next), pageSize), deltaLink: null));
        }

        var delta = new ProductDeltaToken(1, ProductDeltaPhase.Delta, throughVersion, 0, 0, 0);
        return Ok(Envelope(items, nextLink: null, deltaLink: Link(tokens.Encode(delta), pageSize)));
    }

    private async Task<IActionResult> ReadDelta(
        IObjectSpace objectSpace,
        IRequestSecurityStrategy permissions,
        ProductDeltaToken token,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var minimumVersion = await changeFeed.MinimumValidVersionAsync(cancellationToken);
        if (token.FromVersion < minimumVersion)
            return StatusCode(StatusCodes.Status410Gone, new ProblemDetails
            {
                Title = "Delta token expired",
                Detail = "SQL Server no longer retains all changes after this token. Request a new Product snapshot."
            });

        var throughVersion = token.ThroughVersion == 0
            ? await changeFeed.CurrentVersionAsync(cancellationToken)
            : token.ThroughVersion;
        var changes = await changeFeed.ReadAsync(
            token.FromVersion,
            throughVersion,
            token.AfterVersion,
            token.AfterId,
            pageSize + 1,
            cancellationToken);
        var hasMore = changes.Count > pageSize;
        var page = changes.Take(pageSize).ToArray();
        var ids = page.Where(change => change.Operation != ProductChangeOperation.Delete)
            .Select(change => change.ProductId).ToArray();
        var visibleProducts = objectSpace.GetObjectsQuery<Product>()
            .Where(product => ids.Contains(product.Id))
            .ToDictionary(product => product.Id);

        var items = page.Select(change =>
        {
            if (change.Operation != ProductChangeOperation.Delete
                && visibleProducts.TryGetValue(change.ProductId, out var product)
                && permissions.CanRead(objectSpace, product))
                return ProductPayload(objectSpace, permissions, product);

            return RemovedPayload(change.ProductId,
                change.Operation == ProductChangeOperation.Delete ? "deleted" : "changed");
        }).ToArray();

        if (hasMore)
        {
            var last = page[^1];
            var next = token with
            {
                ThroughVersion = throughVersion,
                AfterVersion = last.Version,
                AfterId = last.ProductId
            };
            return Ok(Envelope(items, nextLink: Link(tokens.Encode(next), pageSize), deltaLink: null));
        }

        var delta = new ProductDeltaToken(1, ProductDeltaPhase.Delta, throughVersion, 0, 0, 0);
        return Ok(Envelope(items, nextLink: null, deltaLink: Link(tokens.Encode(delta), pageSize)));
    }

    private Dictionary<string, object?> ProductPayload(
        IObjectSpace objectSpace,
        IRequestSecurityStrategy permissions,
        Product product)
    {
        var payload = new Dictionary<string, object?>
        {
            [nameof(Product.Id)] = product.Id,
            [nameof(Product.Code)] = product.Code,
            [nameof(Product.Name)] = product.Name,
            [nameof(Product.UnitOfMeasure)] = product.UnitOfMeasure,
            [nameof(Product.IsActive)] = product.IsActive,
            [nameof(Product.RowVersion)] = Convert.ToBase64String(product.RowVersion)
        };
        if (permissions.CanRead(objectSpace, product, nameof(Product.StandardCost)))
            payload[nameof(Product.StandardCost)] = product.StandardCost;
        return payload;
    }

    private static Dictionary<string, object?> RemovedPayload(int id, string reason) => new()
    {
        [nameof(Product.Id)] = id,
        ["@removed"] = new Dictionary<string, object?> { ["reason"] = reason }
    };

    private Dictionary<string, object?> Envelope(
        object[] items,
        string? nextLink,
        string? deltaLink)
    {
        var root = $"{Request.Scheme}://{Request.Host}{Request.PathBase}";
        var envelope = new Dictionary<string, object?>
        {
            ["@odata.context"] = $"{root}/api/odata/$metadata#Product/$delta",
            ["value"] = items
        };
        if (nextLink is not null) envelope["@odata.nextLink"] = nextLink;
        if (deltaLink is not null) envelope["@odata.deltaLink"] = deltaLink;
        return envelope;
    }

    private string Link(string token, int pageSize)
    {
        var root = $"{Request.Scheme}://{Request.Host}{Request.PathBase}/api/odata/Product/$delta";
        return QueryHelpers.AddQueryString(root, new Dictionary<string, string?>
        {
            ["$deltatoken"] = token,
            ["$top"] = pageSize.ToString(System.Globalization.CultureInfo.InvariantCulture)
        });
    }
}
