using System.Text.Json.Serialization;

public sealed record PackageInventoryDocument(
    [property: JsonPropertyName("projects")] ProjectInventory[] Projects);

public sealed record ProjectInventory(
    [property: JsonPropertyName("frameworks")] FrameworkInventory[] Frameworks);

public sealed record FrameworkInventory
{
    [JsonPropertyName("topLevelPackages")]
    public InventoryPackage[] TopLevelPackages { get; init; } = [];

    [JsonPropertyName("transitivePackages")]
    public InventoryPackage[] TransitivePackages { get; init; } = [];
}

public sealed record InventoryPackage(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("resolvedVersion")] string ResolvedVersion);
