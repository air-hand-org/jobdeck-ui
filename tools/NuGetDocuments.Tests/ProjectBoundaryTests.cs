public class ProjectBoundaryTests
{
    [Fact]
    public void CollectorTypesArePublicWithoutFriendAssemblyAccess()
    {
        var assembly = typeof(Collector).Assembly;
        Assert.Empty(assembly.GetCustomAttributes(typeof(System.Runtime.CompilerServices.InternalsVisibleToAttribute), false));
        foreach (var type in new[] { typeof(Collector), typeof(ConsoleProgress), typeof(GitHubClient), typeof(ManifestWriter), typeof(PackageInventory), typeof(Archive), typeof(PackageReader), typeof(PackageResult), typeof(SourceResolver), typeof(PackageInventoryDocument), typeof(ProjectInventory), typeof(FrameworkInventory), typeof(InventoryPackage) })
        {
            Assert.True(type.IsPublic, type.Name);
        }
    }
}
