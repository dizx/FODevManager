using FODevManager.Models;
using FODevManager.Shared.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FODevManager.Services
{
    public static class ProfileChangeDetector
    {
        public static ModelSyncResult Compare(ProfileModel current, ProfileModel imported)
        {
            var currentModels = DescribeModels(current);
            var importedModels = DescribeModels(imported);
            var revision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(importedModels))));
            var result = new ModelSyncResult { DefinitionRevision = revision };

            if (current.DismissedProfileDefinitionRevision == revision)
                return result;

            foreach (var model in importedModels)
            {
                var previous = currentModels.FirstOrDefault(item => item.Key == model.Key);
                if (previous == null)
                    result.AddAdded(model.DisplayName);
                else if (previous != model)
                    result.AddUpdated($"{model.DisplayName}: {previous.Details} → {model.Details}");
            }

            foreach (var model in currentModels.Where(model => !importedModels.Any(item => item.Key == model.Key)))
                result.AddRemoved(model.DisplayName);

            return result;
        }

        private static List<ModelDefinition> DescribeModels(ProfileModel profile)
            => profile.AllModelEntries.Select(entry =>
            {
                var model = entry.Model;
                var repository = entry.Repository;
                var repositoryKey = repository == null ? "standalone"
                    : !string.IsNullOrWhiteSpace(repository.RepoId) ? repository.RepoId : repository.GetRepoKey();
                var name = model.ModelName.Trim();
                var displayName = $"{name} ({repository?.DisplayName ?? "Standalone"})";
                var details = model.ModelType == ModelType.CompiledNuget
                    ? $"NuGet {model.PackageId} {model.PackageVersion}"
                    : model.ModelType.ToString();
                return new ModelDefinition(
                    $"{repositoryKey}|{name}".ToUpperInvariant(), displayName,
                    model.ModelType, model.PackageId ?? "", model.PackageVersion ?? "",
                    model.PackageUrl ?? "", model.IsMainFOModel, details);
            }).OrderBy(model => model.Key, StringComparer.Ordinal).ThenBy(model => model.Details, StringComparer.Ordinal).ToList();

        private sealed record ModelDefinition(string Key, string DisplayName, ModelType Type,
            string PackageId, string PackageVersion, string PackageUrl, bool IsMainFOModel, string Details);
    }
}
