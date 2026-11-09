using System.Text.Json.Serialization;

internal sealed record PackageInventoryDocument(
    [property: JsonPropertyName("projects")] ProjectInventory[] Projects);

internal sealed record ProjectInventory(
    [property: JsonPropertyName("frameworks")] FrameworkInventory[] Frameworks);

internal sealed record FrameworkInventory
{
    [JsonPropertyName("topLevelPackages")]
    public InventoryPackage[] TopLevelPackages { get; init; } = [];

    [JsonPropertyName("transitivePackages")]
    public InventoryPackage[] TransitivePackages { get; init; } = [];
}

internal sealed record InventoryPackage(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("resolvedVersion")] string ResolvedVersion);
