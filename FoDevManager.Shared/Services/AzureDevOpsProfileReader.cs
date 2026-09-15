using System.Text;
using System.Text.Json;
using FODevManager.Models;
using FODevManager.Operations;

namespace FODevManager.Services;

// Validate data shapes only; never resolve paths, import, clone, or instantiate deployment services
internal static class AzureDevOpsProfileReader
{
    public static AzureDevOpsProfileCandidate Read(string path, string content)
    {
        var errors = new List<string>();
        if (Encoding.UTF8.GetByteCount(content) > 1024 * 1024)
            return new(path, false, null, null, [], ["Profile JSON exceeds the 1 MiB inspection limit"]);
        try
        {
            using var document = JsonDocument.Parse(content.TrimStart('\uFEFF'));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("ProfileName", out _)
                || (!root.TryGetProperty("Repositories", out _) && !root.TryGetProperty("StandaloneModels", out _)))
                return new(path, false, null, null, [], ["JSON is not a recognized FODev profile definition"]);

            var portable = false;
            foreach (var discriminator in new[] { "ExportFormatVersion", "ExportProfileVersion" })
                if (root.TryGetProperty(discriminator, out var version))
                {
                    portable = true;
                    if (!version.TryGetInt32(out var number) || number != 1)
                        return new(path, false, "portable", null, [], ["Unsupported profile export format version"]);
                }
            ValidateCollection(root, "Repositories", errors, repositories: true);
            ValidateCollection(root, "StandaloneModels", errors);
            if (errors.Count != 0) return new(path, false, portable ? "portable" : "local", null, [], errors);

            var profile = portable
                ? ExportProfileMapper.FromExport(root.Deserialize<ExportProfileModel>()!)
                : root.Deserialize<ProfileModel>()!;
            ValidateName(profile.ProfileName, "Profile name", errors);
            var models = new List<AzureDevOpsProfileModelSummary>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in profile.AllModelEntries)
            {
                var model = entry.Model;
                ValidateName(model.ModelName, "Model name", errors);
                if (!names.Add(model.ModelName)) errors.Add("Duplicate model names are not supported within one profile");
                if (!Enum.IsDefined(model.ModelType)) errors.Add("Unknown model type");
                if (model.ModelType == ModelType.CompiledNuget && (string.IsNullOrWhiteSpace(model.PackageId) || string.IsNullOrWhiteSpace(model.PackageVersion)))
                    errors.Add("Compiled NuGet models require a package ID and version");
                models.Add(new(model.ModelName, model.ModelType.ToString(), entry.Repository?.DisplayName,
                    model.IsMainFOModel, model.PackageId, model.PackageVersion));
            }
            return new(path, errors.Count == 0, portable ? "portable" : "local", profile.ProfileName, models, errors.Distinct().ToArray());
        }
        catch (JsonException) { return new(path, false, null, null, [], ["Malformed JSON or unsupported profile field types"]); }
        catch (InvalidOperationException) { return new(path, false, null, null, [], ["Unsupported profile field types"]); }
    }

    private static void ValidateCollection(JsonElement parent, string property, List<string> errors, bool repositories = false)
    {
        if (!parent.TryGetProperty(property, out var items)) return;
        if (items.ValueKind != JsonValueKind.Array) { errors.Add(property + " must be an array"); return; }
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) { errors.Add(property + " entries must be objects"); continue; }
            if (repositories) ValidateCollection(item, "Models", errors);
        }
    }

    private static void ValidateName(string name, string label, List<string> errors)
    {
        try { WorkflowContext.ValidateName(name); }
        catch (ArgumentException) { errors.Add(label + " must be a valid single profile/model name"); }
    }
}
