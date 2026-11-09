using System.Text.Json;

public class PackageInventoryTests
{
    [Fact]
    public void InventoryModelDeserializesBothPackageGroups()
    {
        const string json = """
            {"projects":[{"frameworks":[{"topLevelPackages":[{"id":"Example","resolvedVersion":"1.0.0"}],"transitivePackages":[{"id":"Other","resolvedVersion":"2.0.0"}]}]}]}
            """;
        var inventory = JsonSerializer.Deserialize<PackageInventoryDocument>(json)!;
        var framework = Assert.Single(Assert.Single(inventory.Projects).Frameworks);
        Assert.Equal("Example", Assert.Single(framework.TopLevelPackages!).Id);
        Assert.Equal("2.0.0", Assert.Single(framework.TransitivePackages!).ResolvedVersion);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"projects\":null}")]
    [InlineData("{\"projects\":[{}]}")]
    [InlineData("{\"projects\":[{\"frameworks\":null}]}")]
    [InlineData("{\"projects\":[{\"frameworks\":[{\"topLevelPackages\":[{}]}]}]}")]
    [InlineData("{\"projects\":[{\"frameworks\":[{\"topLevelPackages\":null}]}]}")]
    public void InvalidInventoryIsRejectedBeforeArchiveCollection(string json)
    {
        var root = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? Path.GetTempPath(), "inventory-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var input = Path.Combine(root, "inventory.json");
            File.WriteAllText(input, json);
            Assert.ThrowsAny<Exception>(() => PackageInventory.Read(input, root));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
