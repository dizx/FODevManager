using FODevManager.Models;
using FODevManager.Services;
using System.Text.Json;

namespace FODevManager.Tests;

public class ProfileChangeDetectorTests
{
    [Test]
    public void RepositoryRemovalAndReplacementWithNugetAreDetectedEvenWithSameModelName()
    {
        var current = CreateProfile("tools", ModelType.Source);
        var imported = CreateProfile("main", ModelType.CompiledNuget);

        var result = ProfileChangeDetector.Compare(current, imported);

        Assert.That(result.RemovedModels, Has.Count.EqualTo(1));
        Assert.That(result.AddedModels, Has.Count.EqualTo(1));
        Assert.That(result.RemovedModels[0], Does.Contain("tools"));
        Assert.That(result.AddedModels[0], Does.Contain("main"));
    }

    [Test]
    public void NugetVersionChangeIncludesOldAndNewVersions()
    {
        var current = CreateProfile("main", ModelType.CompiledNuget, "1.0.0");
        var imported = CreateProfile("main", ModelType.CompiledNuget, "2.0.0");

        var result = ProfileChangeDetector.Compare(current, imported);

        Assert.That(result.UpdatedModels, Has.Count.EqualTo(1));
        Assert.That(result.UpdatedModels[0], Does.Contain("1.0.0").And.Contain("2.0.0"));
        Assert.That(result.AddedModels, Is.Empty);
        Assert.That(result.RemovedModels, Is.Empty);
    }

    [Test]
    public void StandaloneRemovalAndModelTypeChangeAreDetected()
    {
        var current = CreateProfile("main", ModelType.Source);
        current.StandaloneModels.Add(new() { ModelName = "Removed", ModelType = ModelType.Compiled });
        var imported = CreateProfile("main", ModelType.CompiledNuget);

        var result = ProfileChangeDetector.Compare(current, imported);

        Assert.That(result.RemovedModels, Has.Count.EqualTo(1));
        Assert.That(result.UpdatedModels, Has.Count.EqualTo(1));
    }

    [Test]
    public void DismissedDefinitionSurvivesSerializationButNewVersionPromptsAgain()
    {
        var current = CreateProfile("main", ModelType.CompiledNuget, "1.0.0");
        var imported = CreateProfile("main", ModelType.CompiledNuget, "2.0.0");
        current.DismissedProfileDefinitionRevision = ProfileChangeDetector.Compare(current, imported).DefinitionRevision;
        current = JsonSerializer.Deserialize<ProfileModel>(JsonSerializer.Serialize(current))!;

        Assert.That(ProfileChangeDetector.Compare(current, imported).HasChanges, Is.False);
        imported.Repositories[0].Models[0].PackageVersion = "3.0.0";
        Assert.That(ProfileChangeDetector.Compare(current, imported).HasChanges, Is.True);
    }

    [Test]
    public void LocalPathsAndDeploymentStateDoNotCountAsDefinitionChanges()
    {
        var current = CreateProfile("main", ModelType.CompiledNuget);
        var imported = CreateProfile("main", ModelType.CompiledNuget);
        current.Repositories[0].RepoRootFolder = @"C:\LocalRepo";
        current.Repositories[0].Models[0].CompiledModelFolder = @"C:\Packages\Tools";
        current.Repositories[0].Models[0].IsDeployed = true;

        Assert.That(ProfileChangeDetector.Compare(current, imported).HasChanges, Is.False);
    }

    private static ProfileModel CreateProfile(string repository, ModelType type, string version = "1.0.0")
        => new()
        {
            ProfileName = "YM",
            Repositories = [new()
            {
                RepoId = repository, DisplayName = repository,
                Models = [new() { ModelName = "PtsTools", ModelType = type, PackageId = "PtsTools", PackageVersion = version }]
            }]
        };
}
