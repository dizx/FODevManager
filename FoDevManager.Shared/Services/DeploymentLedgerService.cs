using FODevManager.Messages;
using FODevManager.Models;
using FODevManager.Shared.Models;
using FODevManager.Utils;
using System.Text.Json;

namespace FODevManager.Services
{
    public sealed class DeploymentLedgerService : IDeploymentLedgerService
    {
        private readonly FileService _fileService;
        private readonly string _deploymentBasePath;
        private readonly string _ledgerPath;
        private readonly IDirectoryLinkService _directoryLinkService;
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public DeploymentLedgerService(AppConfig config, FileService fileService, IDirectoryLinkService? directoryLinkService = null)
        {
            _fileService = fileService;
            _deploymentBasePath = config.DeploymentBasePath;
            _ledgerPath = Path.Combine(config.ProfileStoragePath, "deployed-models.json");
            _directoryLinkService = directoryLinkService ?? new DirectoryLinkService();
            Directory.CreateDirectory(config.ProfileStoragePath);
        }

        public string LedgerPath => _ledgerPath;

        public IReadOnlyList<DeployedModelRecord> LoadRecords()
        {
            if (!File.Exists(_ledgerPath))
                return [];

            try
            {
                var json = File.ReadAllText(_ledgerPath);
                return JsonSerializer.Deserialize<List<DeployedModelRecord>>(json) ?? [];
            }
            catch (Exception exception)
            {
                MessageLogger.Warning($"⚠️ Could not load deployment ledger '{_ledgerPath}': {exception.Message}");
                return [];
            }
        }

        public bool CanDeploy(string profileName, string modelName, string sourcePath, out DeployedModelRecord? blocker)
        {
            blocker = null;

            var normalizedSourcePath = NormalizePath(sourcePath);
            var existing = LoadRecords()
                .FirstOrDefault(record => record.ModelName.SameAs(modelName));

            if (existing == null)
                return true;

            if (NormalizePath(existing.SourcePath).SameAs(normalizedSourcePath))
                return true;

            blocker = existing;
            return false;
        }

        public void RecordDeployment(DeployedModelRecord record)
        {
            if (record.ModelName.IsNullOrEmpty())
                throw new ArgumentException("Model name is required", nameof(record));

            record.SourcePath = NormalizePath(record.SourcePath);
            record.DeployedAtUtc = record.DeployedAtUtc == default ? DateTime.UtcNow : record.DeployedAtUtc;

            var records = LoadRecords().ToList();
            records.RemoveAll(existing => existing.ModelName.SameAs(record.ModelName));
            records.Add(record);
            SaveRecords(records);
        }

        public void RemoveDeployment(string modelName)
        {
            var records = LoadRecords().ToList();
            records.RemoveAll(existing => existing.ModelName.SameAs(modelName));
            SaveRecords(records);
        }

        public void Clear()
        {
            SaveRecords([]);
        }

        public void SelfHeal()
        {
            if (!Directory.Exists(_deploymentBasePath))
                return;

            var records = new List<DeployedModelRecord>();
            var existingRecords = LoadRecords();
            var profiles = _fileService.GetAllProfiles();

            foreach (var deploymentPath in Directory.GetDirectories(_deploymentBasePath))
            {
                var modelName = Path.GetFileName(deploymentPath);
                var sourcePath = ResolveDeploymentSourcePath(deploymentPath);

                if (sourcePath.IsNullOrEmpty())
                    sourcePath = deploymentPath;

                var match = FindProfileModelBySourcePath(profiles, sourcePath);
                if (match == null)
                {
                    var existingRecord = existingRecords.FirstOrDefault(record =>
                        record.ModelName.SameAs(modelName)
                        && NormalizePath(record.SourcePath).SameAs(NormalizePath(sourcePath)));
                    if (existingRecord != null)
                    {
                        records.Add(existingRecord);
                        continue;
                    }

                    records.Add(new DeployedModelRecord
                    {
                        ModelName = modelName,
                        SourcePath = sourcePath,
                        IsUnmanaged = true,
                        DeployedAtUtc = DateTime.UtcNow
                    });
                    continue;
                }

                records.Add(new DeployedModelRecord
                {
                    ModelName = match.Value.Model.ModelName,
                    ProfileName = match.Value.Profile.ProfileName,
                    ModelType = match.Value.Model.ModelType,
                    SourcePath = sourcePath,
                    PackageId = match.Value.Model.PackageId ?? string.Empty,
                    PackageVersion = match.Value.Model.PackageVersion ?? string.Empty,
                    IsUnmanaged = false,
                    DeployedAtUtc = DateTime.UtcNow
                });
            }

            SaveRecords(records);
        }

        public static string NormalizePath(string path)
        {
            if (path.IsNullOrEmpty())
                return string.Empty;

            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private string ResolveDeploymentSourcePath(string deploymentPath)
        {
            try
            {
                return NormalizePath(_directoryLinkService.ResolveLinkTarget(deploymentPath) ?? deploymentPath);
            }
            catch (Exception exception)
            {
                MessageLogger.Warning($"⚠️ Could not resolve deployment path '{deploymentPath}': {exception.Message}");
                return NormalizePath(deploymentPath);
            }
        }

        private static (ProfileModel Profile, ProfileEnvironmentModel Model)? FindProfileModelBySourcePath(IEnumerable<ProfileModel> profiles, string sourcePath)
        {
            var normalizedSourcePath = NormalizePath(sourcePath);

            foreach (var profile in profiles)
            {
                foreach (var model in profile.AllModels)
                {
                    var modelSourcePath = model.ModelType == ModelType.Source ? model.MetadataFolder : model.CompiledModelFolder;
                    if (NormalizePath(modelSourcePath).SameAs(normalizedSourcePath))
                        return (profile, model);
                }
            }

            return null;
        }

        private void SaveRecords(IReadOnlyCollection<DeployedModelRecord> records)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_ledgerPath)!);
            File.WriteAllText(_ledgerPath, JsonSerializer.Serialize(records, JsonOptions));
        }
    }
}
