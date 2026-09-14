using FODevManager.Models;
using FODevManager.Services;
using FODevManager.Utils;
using FODevManager.Messages;

namespace FODevManager.Operations;

// Construct inside the operation gate so services snapshot the latest effective settings
public sealed class WorkflowContext(AppConfig config)
{
    public AppConfig Config { get; } = config;
    public bool OuterOwnsServiceControl { get; init; }
    public Action RefreshServiceState { get; init; } = () => ServiceHelper.RefreshW3SVCState();

    public void PrepareServiceControl()
    {
        if (OuterOwnsServiceControl)
        {
            Require(Singleton<FODevManager.Shared.Utils.FODevManager.WinUI.Services.W3cServiceState>.Instance.InOperation,
                "Outer service-control scope must be active");
            return;
        }
        RefreshServiceState();
    }
    private FileService? files;
    private DeployablePackageService? packages;
    private ModelDeploymentService? deployment;
    private ProfileService? profiles;
    private IDeploymentLedgerService? ledger;
    public FileService Files => files ??= new(Config);
    public IDirectoryLinkService Links { get; init; } = new DirectoryLinkService();
    public IDeploymentLedgerService Ledger => ledger ??= new DeploymentLedgerService(Config, Files, Links);
    public VisualStudioSolutionService Solutions => new(Config);
    public ModelVersionService Versions => new();
    public DeployablePackageService Packages { get => packages ??= new(Config, allowLegacyDeploymentCleanup: false); init => packages = value; }
    public ModelDeploymentService Deployment => deployment ??= new(Config, Files, Packages, Ledger, Versions, Links);
    public ProfileService Profiles => profiles ??= new(Config, Files, Solutions, Deployment, Packages, Versions, new ProfilesContainer(Files), Ledger);

    public ProfileModel Profile(string name)
    {
        ValidateName(name);
        var profile = Files.LoadProfile(name);
        if (!profile.ProfileName.SameAs(name)) throw new InvalidOperationException("Saved profile name does not match its file name");
        return profile;
    }

    public static ProfileEnvironmentModel Model(ProfileModel profile, string name)
    {
        ValidateName(name);
        return profile.FindModel(name) ?? throw new InvalidOperationException($"Model '{name}' not found in '{profile.ProfileName}'");
    }

    public static RepositoryModel Repository(ProfileModel profile, string repoId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoId);
        return profile.Repositories.SingleOrDefault(r => r.RepoId.SameAs(repoId))
            ?? throw new InvalidOperationException($"Repository '{repoId}' not found in '{profile.ProfileName}'");
    }

    public static void ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name != Path.GetFileName(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || name.EndsWith('.') || name.EndsWith(' ') || name.SameAs("deployed-models"))
            throw new ArgumentException("A single valid profile/model name is required");
    }

    public static void Require(bool succeeded, string operation)
    {
        if (!succeeded) throw new InvalidOperationException(operation + " did not complete successfully");
    }

    public static void ServiceCall(Action action)
    {
        var errors = 0;
        using var subscription = MessageBus.Subscribe(message =>
        {
            if (message.Type == MessageType.Error) Interlocked.Increment(ref errors);
        });
        action();
        Require(Volatile.Read(ref errors) == 0, "Service operation");
    }
}
