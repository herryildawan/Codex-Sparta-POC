using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Sparta.WebApi.Documentation;

public sealed class ScalarOperationTagsFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var controller = context.ApiDescription.ActionDescriptor.RouteValues["controller"];
        var path = context.ApiDescription.RelativePath ?? string.Empty;

        var tag = controller switch
        {
            "Business" when path.StartsWith("api/sales/", StringComparison.OrdinalIgnoreCase) => "Sales Operations",
            "Business" when path.StartsWith("api/inventory/", StringComparison.OrdinalIgnoreCase) => "Inventory Operations",
            "InventoryCommands" => "Inventory Commands",
            "ProductCommands" => "Product Commands",
            "Session" when path.StartsWith("api/inventory/", StringComparison.OrdinalIgnoreCase) => "Inventory Operations",
            _ => null
        };

        if (tag is not null)
            operation.Tags = [new OpenApiTag { Name = tag }];
    }
}

public sealed class ScalarTagGroupsFilter : IDocumentFilter
{
    private static readonly (string Name, string[] Tags)[] Groups =
    [
        ("Platform", ["Authentication", "Session", "Audit", "Localization", "MediaFile", "Metadata"]),
        ("Sales", ["Customer", "SalesOrder", "SalesOrderLine", "Sales Operations"]),
        ("Inventory", ["Product", "Warehouse", "StockMovement", "Product Commands", "Inventory Commands", "Inventory Operations"])
    ];

    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        var documentTags = swaggerDoc.Paths.Values
            .SelectMany(path => path.Operations.Values)
            .SelectMany(operation => operation.Tags)
            .Select(tag => tag.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        var tagGroups = new OpenApiArray();
        var orderedTags = new List<string>();

        foreach (var (name, tags) in Groups)
        {
            var existingTags = tags.Where(documentTags.Contains).ToArray();
            if (existingTags.Length == 0) continue;

            tagGroups.Add(new OpenApiObject
            {
                ["name"] = new OpenApiString(name),
                ["tags"] = ToOpenApiArray(existingTags)
            });
            orderedTags.AddRange(existingTags);
        }

        var otherTags = documentTags.Except(orderedTags, StringComparer.Ordinal).OrderBy(tag => tag).ToArray();
        if (otherTags.Length > 0)
        {
            tagGroups.Add(new OpenApiObject
            {
                ["name"] = new OpenApiString("Other"),
                ["tags"] = ToOpenApiArray(otherTags)
            });
            orderedTags.AddRange(otherTags);
        }

        swaggerDoc.Tags = orderedTags.Select(name => new OpenApiTag { Name = name }).ToList();
        swaggerDoc.Extensions["x-tagGroups"] = tagGroups;
    }

    private static OpenApiArray ToOpenApiArray(IEnumerable<string> values)
    {
        var array = new OpenApiArray();
        foreach (var value in values) array.Add(new OpenApiString(value));
        return array;
    }
}
