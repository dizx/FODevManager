using FODevManager.Models;
using FODevManager.Services;
using FODevManager.Utils;

namespace FODevManager.Tests;

[TestFixture]
public class ProfilePackageReferenceTests
{
    private string root = null!;
    private string isvPath = null!;
    private AppConfig config = null!;
    private RepositoryModel repository = null!;
    private ProfileModel profile = null!;
    private const string BuildReferences = "<packages><package id=\"BuildOnly\" version=\"9.0.0\" /></packages>";

    [SetUp]
    public void Setup()
    {
        root = Path.Combine(Path.GetTempPath(), "FODevManagerTests", Guid.NewGuid().ToString("N"));
        var repoRoot = Path.Combine(root, "repo");
        Directory.CreateDirectory(repoRoot);
        isvPath = Path.Combine(repoRoot, "isv.config");
        File.WriteAllText(isvPath, BuildReferences);
        File.WriteAllText(Path.Combine(repoRoot, "nuget.config"), "<configuration><packageSources /></configuration>");
        config = new AppConfig
        {
            DeployablePackages = Path.Combine(root, "packages"),
            DeploymentBasePath = Path.Combine(root, "deployment"),
            NuGetExecutablePath = Environment.ProcessPath!
        };
        repository = new RepositoryModel { RepoRootFolder = repoRoot, DisplayName = "Repo" };
        profile = new ProfileModel { ProfileName = "Profile", Repositories = [repository] };
    }

    [TearDown]
    public void Teardown() => Directory.Delete(root, true);

    [TestCase(true)]
    [TestCase(false)]
    public void AddUpdateAndRemoveUseProfileAndLeaveBuildReferencesAlone(bool hasIsvConfig)
    {
        if (!hasIsvConfig) File.Delete(isvPath);
        CachePackage("1.0.0", "A", "B");
        CachePackage("2.0.0", "A", "C");
        var service = new DeployablePackageService(config, versionQueryRunner: _ => (true, "Pkg 1.0.0\nPkg 2.0.0"));
        const string url = "https://dev.azure.com/org/project/_artifacts/feed/feed/NuGet/Pkg/overview/1.0.0";

        Assert.That(service.AddOrUpdatePackageFromUrl(profile, repository, url), Is.True);
        Assert.That(repository.Models.Select(model => model.ModelName), Is.EquivalentTo(new[] { "A", "B" }));
        Assert.That(repository.Models.All(model => model.PackageUrl == url), Is.True);
        Assert.That(service.GetConfiguredPackageVersions(repository), Has.Count.EqualTo(1));

        Assert.That(service.UpdatePackageVersion(profile, repository, "Pkg", "2.0.0"), Is.True);
        Assert.That(repository.Models.Select(model => model.ModelName), Is.EquivalentTo(new[] { "A", "C" }));
        Assert.That(repository.Models.All(model => model.PackageVersion == "2.0.0"), Is.True);
        Assert.That(repository.Models.All(model => model.PackageUrl == url.Replace("1.0.0", "2.0.0")), Is.True);
        Assert.That(service.GetConfiguredPackageVersions(repository)["Pkg"], Is.EqualTo("2.0.0"));

        var newUrl = url.Replace("/feed/feed/", "/feed/another-feed/").Replace("1.0.0", "2.0.0");
        Assert.That(service.UpdatePackageFromUrl(profile, repository, "Pkg", newUrl), Is.True);
        Assert.That(repository.Models.All(model => model.PackageUrl == newUrl), Is.True);

        Assert.That(service.RemovePackage(profile, repository, "Pkg"), Is.True);
        Assert.That(repository.Models, Is.Empty);
        Assert.That(service.EnsureCompiledNugetModels(profile, repository), Is.False);
        if (hasIsvConfig) Assert.That(File.ReadAllText(isvPath), Is.EqualTo(BuildReferences));
        else Assert.That(File.Exists(isvPath), Is.False);
    }

    [Test]
    public void FailedVersionValidationPreservesExistingPackageMembership()
    {
        repository.Models = [new() { ModelName = "A", ModelType = ModelType.CompiledNuget, PackageId = "Pkg", PackageVersion = "1.0.0" }];
        var original = repository.Models.Single();
        var service = new DeployablePackageService(config, versionQueryRunner: _ => (false, "feed unavailable"));

        Assert.That(service.UpdatePackageVersion(profile, repository, "Pkg", "2.0.0"), Is.False);
        Assert.That(repository.Models.Single(), Is.SameAs(original));
        Assert.That(original.PackageVersion, Is.EqualTo("1.0.0"));
        Assert.That(File.ReadAllText(isvPath), Is.EqualTo(BuildReferences));
    }

    [Test]
    public void PreparationDoesNotDiscoverPackagesFromBuildConfiguration()
    {
        var service = new DeployablePackageService(config);
        Assert.That(service.EnsureCompiledNugetModels(profile, repository), Is.False);
        Assert.That(service.GetConfiguredPackageVersions(repository), Is.Empty);
        Assert.That(repository.Models, Is.Empty);
    }

    [Test]
    public void FailedRestoreRetainsAllModelsEvenWhenAnotherPackageIsCached()
    {
        CachePackage("1.0.0", "A");
        repository.Models =
        [
            new() { ModelName = "A", ModelType = ModelType.CompiledNuget, PackageId = "Pkg", PackageVersion = "1.0.0", CompiledModelFolder = "original-a" },
            new() { ModelName = "B", ModelType = ModelType.CompiledNuget, PackageId = "Missing", PackageVersion = "1.0.0", CompiledModelFolder = "original-b" }
        ];
        var service = new DeployablePackageService(config, versionQueryRunner: _ => (false, "feed unavailable"));

        Assert.That(service.EnsureCompiledNugetModels(profile, repository), Is.False);
        Assert.That(repository.Models.Select(model => model.CompiledModelFolder), Is.EqualTo(new[] { "original-a", "original-b" }));
        Assert.That(service.GetConfiguredPackageVersions(repository), Has.Count.EqualTo(2));
    }

    [TestCase("")]
    [TestCase("2.0.0")]
    public void InvalidOrConflictingProfileVersionsFailBeforeMutation(string version)
    {
        repository.Models =
        [
            new() { ModelName = "A", ModelType = ModelType.CompiledNuget, PackageId = "Pkg", PackageVersion = "1.0.0" },
            new() { ModelName = "B", ModelType = ModelType.CompiledNuget, PackageId = "Pkg", PackageVersion = version }
        ];
        var service = new DeployablePackageService(config);
        Assert.Throws<InvalidOperationException>(() => service.EnsureCompiledNugetModels(profile, repository));
        Assert.That(repository.Models, Has.Count.EqualTo(2));
        Assert.That(File.ReadAllText(isvPath), Is.EqualTo(BuildReferences));
    }

    private void CachePackage(string version, params string[] models)
    {
        foreach (var name in models)
        {
            var folder = Path.Combine(config.DeployablePackages, "Pkg-" + version, name);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, name + ".xref"), "cached");
        }
    }
}
