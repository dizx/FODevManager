using System.Net;
using System.Text;
using System.Text.Json;
using FODevManager.Models;
using FODevManager.Operations;
using FODevManager.Services;
using FODevManager.Utils;

namespace FODevManager.Tests;

[TestFixture]
public class AzureDevOpsDiscoveryTests
{
    private const string RepositoryId = "278d5cd2-584d-4b63-824a-2ba458937249";
    private const string ProjectId = "6ce954b1-ce1f-45d1-b94d-e6bf2464ba2c";
    private const string Commit = "23d0bc5b128a10056dc68afece360d8a0fabb014";
    private AppConfig config = null!;
    private string root = null!;

    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), "fodev-discovery-" + Guid.NewGuid().ToString("N"));
        config = new AppConfig { AzureArtifactsPat = "private-pat", ProfileStoragePath = Path.Combine(root, "profiles"),
            DefaultSourceDirectory = Path.Combine(root, "source"), DeploymentBasePath = Path.Combine(root, "deployment") };
    }

    [TearDown]
    public void TearDown() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    [Test]
    public async Task ProjectsFollowContinuationWithoutExposingCredentialsOrUsingResponseUrls()
    {
        var requests = new List<string>();
        using var client = Client(request =>
        {
            requests.Add(request.RequestUri!.AbsoluteUri);
            Assert.That(request.Method, Is.EqualTo(HttpMethod.Get));
            Assert.That(request.Headers.Authorization!.Scheme, Is.EqualTo("Basic"));
            Assert.That(Encoding.UTF8.GetString(Convert.FromBase64String(request.Headers.Authorization.Parameter!)), Is.EqualTo(":private-pat"));
            var response = Json(new { count = 1, value = new[] { new { id = ProjectId, name = requests.Count == 1 ? "First" : "Second", state = "wellFormed", url = "https://foreign.invalid/credentials" } } });
            if (requests.Count == 1) response.Headers.Add("x-ms-continuationtoken", "100 +/&");
            return response;
        });
        var result = await new AzureDevOpsService(config, client).ListProjectsAsync("peritus-no", CancellationToken.None);
        Assert.That(result.Select(p => p.Name), Is.EqualTo(new[] { "First", "Second" }));
        Assert.That(requests, Has.Count.EqualTo(2));
        Assert.That(requests[1], Does.Contain("continuationToken=100%20%2B%2F%26"));
        Assert.That(requests.All(u => u.StartsWith("https://dev.azure.com/peritus-no/_apis/projects?")), Is.True);
        Assert.That(JsonSerializer.Serialize(result), Does.Not.Contain("private-pat").And.Not.Contain("foreign.invalid"));
        Assert.That(Directory.Exists(root), Is.False);
    }

    [TestCase(401, "authentication")]
    [TestCase(403, "permission")]
    [TestCase(404, "not found")]
    [TestCase(429, "rate limit")]
    [TestCase(500, "500")]
    [TestCase(302, "redirect")]
    public void HttpFailuresAreErrorsNotEmptyListsAndDoNotEchoBodies(int status, string expected)
    {
        using var client = Client(_ => new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("private-pat internal-server-detail") });
        var error = Assert.ThrowsAsync<InvalidOperationException>(() => new AzureDevOpsService(config, client).ListProjectsAsync("peritus-no", CancellationToken.None));
        Assert.That(error!.Message.ToLowerInvariant(), Does.Contain(expected));
        Assert.That(error.ToString(), Does.Not.Contain("private-pat").And.Not.Contain("internal-server-detail"));
    }

    [TestCase("https://evil.invalid/org")]
    [TestCase("../other")]
    [TestCase("org?secret=1")]
    [TestCase("")]
    public void InvalidOrganizationNeverSendsCredentials(string organization)
    {
        using var client = Client(_ => throw new AssertionException("Invalid input must not send HTTP"));
        Assert.ThrowsAsync<ArgumentException>(() => new AzureDevOpsService(config, client).ListProjectsAsync(organization, CancellationToken.None));
    }

    [Test]
    public void MissingPatFailsBeforeNetwork()
    {
        config.AzureArtifactsPat = "";
        using var client = Client(_ => throw new AssertionException("Missing credentials must not send HTTP"));
        var error = Assert.ThrowsAsync<InvalidOperationException>(() => new AzureDevOpsService(config, client).ListProjectsAsync("peritus-no", CancellationToken.None));
        Assert.That(error!.Message, Does.Contain("AzureArtifactsPat"));
    }

    [Test]
    public void RepeatedContinuationAndMalformedResponsesAreNotSilentlyAccepted()
    {
        using var loop = Client(_ => { var response = Json(new { value = Array.Empty<object>() }); response.Headers.Add("x-ms-continuationtoken", "repeat"); return response; });
        Assert.ThrowsAsync<InvalidOperationException>(() => new AzureDevOpsService(config, loop).ListProjectsAsync("peritus-no", CancellationToken.None));
        using var malformed = Client(_ => Json(new { message = "not a collection" }));
        Assert.ThrowsAsync<InvalidOperationException>(() => new AzureDevOpsService(config, malformed).ListProjectsAsync("peritus-no", CancellationToken.None));
    }

    [Test]
    public void CancellationInterruptsHttpAndIsNotAnEmptySuccess()
    {
        using var cancellation = new CancellationTokenSource();
        using var client = new HttpClient(new AsyncHandler(async (_, token) => { cancellation.Cancel(); await Task.Delay(Timeout.Infinite, token); return Json(new { }); }));
        Assert.That(async () => await new AzureDevOpsService(config, client).ListProjectsAsync("peritus-no", cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public void RequestTimeoutAlsoCoversAStalledResponseBody()
    {
        using var safety = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        using var client = Client(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StalledStream()) });
        client.Timeout = TimeSpan.FromMilliseconds(50);
        var error = Assert.ThrowsAsync<InvalidOperationException>(() => new AzureDevOpsService(config, client).ListProjectsAsync("peritus-no", safety.Token));
        Assert.That(error!.Message, Does.Contain("timed out"));
        Assert.That(safety.IsCancellationRequested, Is.False);
    }

    [Test]
    public async Task UnfilteredRepositoriesFollowPaginationAndDoNotCreateLocalProfileStore()
    {
        var requests = 0;
        using var client = Client(request =>
        {
            requests++;
            Assert.That(request.RequestUri!.AbsolutePath, Is.EqualTo("/peritus-no/_apis/git/repositories"));
            var response = Json(new { value = requests == 1 ? new[] { Repository() } : Array.Empty<object>() });
            if (requests == 1) response.Headers.Add("x-ms-continuationtoken", "next");
            return response;
        });
        var result = await new AzureDevOpsOperations(new WorkflowContext(config), new AzureDevOpsService(config, client)).RepositoriesAsync("peritus-no", null, CancellationToken.None);
        Assert.That(result.Single().AlreadyRegistered, Is.False);
        Assert.That(requests, Is.EqualTo(2));
        Assert.That(Directory.Exists(root), Is.False);
    }

    [Test]
    public async Task MissingArtifactFoldersAreASuccessfulInspectionWithNoCandidates()
    {
        using var client = Client(request => request.RequestUri!.AbsolutePath.EndsWith("/items")
            ? Json(new { value = new[] { new { path = "/Metadata", isFolder = true }, new { path = "/ArtifactsElsewhere", isFolder = true } } })
            : InspectionResponse(request, []));
        var result = await new AzureDevOpsService(config, client).InspectRepositoryAsync("peritus-no", RepositoryId, null, CancellationToken.None);
        Assert.That(result.Status, Is.EqualTo("inspected"));
        Assert.That(result.CommitId, Is.EqualTo(Commit));
        Assert.That(result.ProfileDefinitions, Is.Empty);
    }

    [Test]
    public void OversizedResponseFailsInsteadOfTruncatingDiscovery()
    {
        using var client = Client(_ => { var response = Json(new { value = Array.Empty<object>() }); response.Content.Headers.ContentLength = 9 * 1024 * 1024; return response; });
        Assert.ThrowsAsync<InvalidOperationException>(() => new AzureDevOpsService(config, client).ListProjectsAsync("peritus-no", CancellationToken.None));
    }

    [Test]
    public async Task OversizedProfileAndSymlinkAreExplicitInvalidCandidates()
    {
        using var large = Client(request => InspectionResponse(request, [], new string(' ', 1024 * 1024 + 1)));
        var result = await new AzureDevOpsService(config, large).InspectRepositoryAsync("peritus-no", RepositoryId, null, CancellationToken.None);
        Assert.That(result.ProfileDefinitions.Single(c => c.Path.EndsWith("team.json")).ValidationErrors, Does.Contain("Profile JSON exceeds the 1 MiB inspection limit"));
        using var symlink = Client(request => System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query)["path"] is string path
            ? Json(new { path, isSymLink = true, commitId = Commit }) : InspectionResponse(request, []));
        result = await new AzureDevOpsService(config, symlink).InspectRepositoryAsync("peritus-no", RepositoryId, null, CancellationToken.None);
        Assert.That(result.ProfileDefinitions.All(c => !c.IsValid), Is.True);
    }

    [TestCase("https://user@dev.azure.com/peritus-no/Bank%20Filer/_git/Repo/")]
    [TestCase("https://peritus-no.visualstudio.com/Bank%20Filer/_git/Repo.git")]
    [TestCase("https://peritus-no.visualstudio.com/DefaultCollection/Bank%20Filer/_git/Repo")]
    [TestCase("git@ssh.dev.azure.com:v3/peritus-no/Bank%20Filer/Repo")]
    [TestCase("ssh://git@ssh.dev.azure.com/v3/peritus-no/Bank%20Filer/Repo")]
    public async Task RepositoryDiscoveryMatchesSavedProfileUrlsAcrossAzureFormats(string localUrl)
    {
        var files = new FileService(config);
        files.SaveProfile(new ProfileModel { ProfileName = "Bank", Repositories = [new RepositoryModel { RepoId = "local-repo", GitUrl = localUrl, RepoRootFolder = "C:\\Dev\\Bank" }] }, skipExistCheck: true);
        files.SaveProfile(new ProfileModel { ProfileName = "OtherOrg", Repositories = [new RepositoryModel { GitUrl = "https://dev.azure.com/other/Bank%20Filer/_git/Repo" }] }, skipExistCheck: true);
        var original = File.ReadAllBytes(Path.Combine(config.ProfileStoragePath, "Bank.json"));
        using var client = Client(request =>
        {
            Assert.That(request.RequestUri!.AbsolutePath, Is.EqualTo("/peritus-no/Bank%20Filer/_apis/git/repositories"));
            return Json(new { value = new[] { Repository() } });
        });
        var result = await new AzureDevOpsOperations(new WorkflowContext(config), new AzureDevOpsService(config, client))
            .RepositoriesAsync("peritus-no", "Bank Filer", CancellationToken.None);
        Assert.That(result.Single().AlreadyRegistered, Is.True);
        Assert.That(result.Single().LocalRegistrations.Select(p => p.ProfileName), Is.EqualTo(new[] { "Bank" }));
        Assert.That(result.Single().LocalRegistrations.Single().RepoId, Is.EqualTo("local-repo"));
        Assert.That(result.Single().CloneUrl, Is.EqualTo("https://dev.azure.com/peritus-no/Bank%20Filer/_git/Repo"));
        Assert.That(File.ReadAllBytes(Path.Combine(config.ProfileStoragePath, "Bank.json")), Is.EqualTo(original));
        Assert.That(Directory.Exists(config.DefaultSourceDirectory), Is.False);
        Assert.That(Directory.Exists(config.DeploymentBasePath), Is.False);
    }

    [TestCase("https://dev.azure.com/peritus-no/_git/Bank%20Filer")]
    [TestCase("https://peritus-no.visualstudio.com/_git/Bank%20Filer")]
    [TestCase("https://peritus-no.visualstudio.com/DefaultCollection/_git/Bank%20Filer")]
    [TestCase("https://dev.azure.com/peritus-no/_git/278d5cd2-584d-4b63-824a-2ba458937249")]
    public async Task ProjectOmittedCloneUrlsMatchDefaultRepositoryOrUniqueGuid(string url)
    {
        new FileService(config).SaveProfile(new ProfileModel { ProfileName = "Existing", Repositories = [new RepositoryModel { RepoId = "local", GitUrl = url }] }, skipExistCheck: true);
        using var client = Client(_ => Json(new { value = new[] { new { id = RepositoryId, name = "Bank Filer", project = new { id = ProjectId, name = "Bank Filer" } } } }));
        var result = await new AzureDevOpsOperations(new WorkflowContext(config), new AzureDevOpsService(config, client)).RepositoriesAsync("peritus-no", null, CancellationToken.None);
        Assert.That(result.Single().LocalRegistrations.Select(p => p.ProfileName), Is.EqualTo(new[] { "Existing" }));
    }

    [Test]
    public async Task ProjectOmittedNameDoesNotMatchSameNameInAnotherProject()
    {
        new FileService(config).SaveProfile(new ProfileModel { ProfileName = "Existing", Repositories = [new RepositoryModel { GitUrl = "https://dev.azure.com/peritus-no/_git/Repo" }] }, skipExistCheck: true);
        using var client = Client(_ => Json(new { value = new[] { Repository() } }));
        var result = await new AzureDevOpsOperations(new WorkflowContext(config), new AzureDevOpsService(config, client)).RepositoriesAsync("peritus-no", null, CancellationToken.None);
        Assert.That(result.Single().AlreadyRegistered, Is.False);
    }

    [TestCase("{}")]
    [TestCase("{\"path\":\"/Artifacts\",\"isFolder\":\"true\"}")]
    [TestCase("{\"path\":123,\"isFolder\":true}")]
    [TestCase("{\"path\":\"/Artifacts\",\"gitObjectType\":42}")]
    public void MalformedItemsFailInsteadOfReportingNoProfileDefinitions(string itemJson)
    {
        using var item = JsonDocument.Parse(itemJson);
        using var client = Client(request => request.RequestUri!.AbsolutePath.EndsWith("/items")
            ? Json(new { value = new[] { item.RootElement } }) : InspectionResponse(request, []));
        Assert.ThrowsAsync<InvalidOperationException>(() => new AzureDevOpsService(config, client).InspectRepositoryAsync("peritus-no", RepositoryId, null, CancellationToken.None));
    }

    [Test]
    public void MalformedDefaultBranchIsNotAnEmptyRepository()
    {
        using var client = Client(_ => Json(new { id = RepositoryId, name = "Repo", project = new { id = ProjectId, name = "Bank Filer" }, defaultBranch = 42 }));
        Assert.ThrowsAsync<InvalidOperationException>(() => new AzureDevOpsService(config, client).InspectRepositoryAsync("peritus-no", RepositoryId, null, CancellationToken.None));
    }

    [Test]
    public async Task InspectionPinsCommitScansBothArtifactFoldersAndValidatesEveryCandidate()
    {
        var reads = new List<string>();
        using var client = Client(request => InspectionResponse(request, reads));
        var result = await new AzureDevOpsService(config, client).InspectRepositoryAsync("peritus-no", RepositoryId, "feature/discovery", CancellationToken.None);
        Assert.That(result.Branch, Is.EqualTo("refs/heads/feature/discovery"));
        Assert.That(result.CommitId, Is.EqualTo(Commit));
        Assert.That(result.ProfileDefinitions.Select(c => c.Path), Is.EqualTo(new[] { "/Artifact/legacy.json", "/Artifacts/bad.json", "/Artifacts/nested/team.json" }));
        Assert.That(result.ValidProfileCount, Is.EqualTo(2));
        Assert.That(result.ProfileDefinitions.Single(c => c.Path.EndsWith("team.json")).Models.Select(m => m.ModelName), Is.EqualTo(new[] { "Source", "Compiled", "Package" }));
        Assert.That(result.ProfileDefinitions.Single(c => c.Path.EndsWith("bad.json")).ValidationErrors, Is.Not.Empty);
        Assert.That(reads.Where(p => p.Contains("/items?")).All(p => p.Contains("versionDescriptor.version=" + Commit) && p.Contains("versionDescriptor.versionType=commit")), Is.True);
        Assert.That(reads.Any(p => p.Contains("foreign.invalid")), Is.False);
        Assert.That(Directory.Exists(root), Is.False);
    }

    [TestCase("{}")]
    [TestCase("[]")]
    [TestCase("{bad")]
    [TestCase("{\"ProfileName\":\"../invalid\",\"Repositories\":[]}")]
    [TestCase("{\"ProfileName\":\"Good\",\"ExportFormatVersion\":99,\"Repositories\":[]}")]
    [TestCase("{\"ProfileName\":\"Good\",\"Repositories\":null}")]
    [TestCase("{\"ProfileName\":\"Good\",\"Repositories\":[null]}")]
    [TestCase("{\"ProfileName\":\"Good\",\"StandaloneModels\":[{\"ModelName\":\"A\",\"ModelType\":99}]}")]
    [TestCase("{\"ProfileName\":\"Good\",\"StandaloneModels\":[{\"ModelName\":\"A\"},{\"ModelName\":\"a\"}]}")]
    public async Task InvalidProfilesAreCandidatesWithValidationErrors(string content)
    {
        using var client = Client(request => InspectionResponse(request, [], content));
        var result = await new AzureDevOpsService(config, client).InspectRepositoryAsync("peritus-no", RepositoryId, null, CancellationToken.None);
        var candidate = result.ProfileDefinitions.Single(c => c.Path.EndsWith("team.json"));
        Assert.That(candidate.IsValid, Is.False);
        Assert.That(candidate.ValidationErrors, Is.Not.Empty);
    }

    [Test]
    public async Task EmptyRepositoryIsDistinguishedFromMissingBranchAndPermissionFailure()
    {
        using var empty = Client(_ => Json(Repository(null)));
        var result = await new AzureDevOpsService(config, empty).InspectRepositoryAsync("peritus-no", RepositoryId, null, CancellationToken.None);
        Assert.That(result.Status, Is.EqualTo("no_default_branch"));
        Assert.That(result.ProfileDefinitions, Is.Empty);
        using var missingBranch = Client(request => request.RequestUri!.AbsolutePath.EndsWith("/refs") ? Json(new { value = Array.Empty<object>() }) : Json(Repository()));
        Assert.ThrowsAsync<InvalidOperationException>(() => new AzureDevOpsService(config, missingBranch).InspectRepositoryAsync("peritus-no", RepositoryId, "missing", CancellationToken.None));
        using var forbiddenItems = Client(request => request.RequestUri!.AbsolutePath.EndsWith("/items") ? new HttpResponseMessage(HttpStatusCode.Forbidden) : InspectionResponse(request, []));
        Assert.ThrowsAsync<InvalidOperationException>(() => new AzureDevOpsService(config, forbiddenItems).InspectRepositoryAsync("peritus-no", RepositoryId, null, CancellationToken.None));
    }

    private static object Repository(string? defaultBranch = "refs/heads/main") => new { id = RepositoryId, name = "Repo", project = new { id = ProjectId, name = "Bank Filer" },
        defaultBranch, remoteUrl = "https://user@dev.azure.com/peritus-no/Bank%20Filer/_git/Repo", webUrl = "https://foreign.invalid", isDisabled = false };

    private static HttpResponseMessage InspectionResponse(HttpRequestMessage request, List<string> reads, string? teamContent = null)
    {
        var uri = request.RequestUri!;
        reads.Add(uri.AbsoluteUri);
        if (uri.AbsolutePath.EndsWith(RepositoryId)) return Json(Repository());
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        if (uri.AbsolutePath.EndsWith("/refs")) return Json(new { value = new[] { new { name = "refs/" + query["filter"], objectId = Commit } } });
        Assert.That(uri.AbsolutePath.EndsWith("/items"), Is.True, uri.ToString());
        if (query["path"] is string path)
        {
            var content = path switch
            {
                "/Artifact/legacy.json" => "{\"ProfileName\":\"Legacy\",\"StandaloneModels\":[{\"ModelName\":\"Local\",\"ModelType\":1}]}",
                "/Artifacts/bad.json" => "{\"logging\":true}",
                "/Artifacts/nested/team.json" => teamContent ?? "{\"ExportFormatVersion\":1,\"ProfileName\":\"Team\",\"Repositories\":[{\"DisplayName\":\"Repo\",\"Models\":[{\"ModelName\":\"Source\",\"ModelType\":0},{\"ModelName\":\"Compiled\",\"ModelType\":1},{\"ModelName\":\"Package\",\"ModelType\":2,\"PackageId\":\"Pkg\",\"PackageVersion\":\"1.0.0\"}]}]}",
                _ => throw new AssertionException("Unexpected content request: " + path)
            };
            return Json(new { path, content, commitId = Commit });
        }
        object Item(string path, bool folder = false) => new { path, isFolder = folder, gitObjectType = folder ? "tree" : "blob", url = "https://foreign.invalid/item" };
        var items = query["scopePath"] switch
        {
            "/" => new[] { Item("/", true), Item("/Artifact", true), Item("/Artifacts", true), Item("/Metadata", true), Item("/unrelated.json") },
            "/Artifact" => new[] { Item("/Artifact", true), Item("/Artifact/legacy.json") },
            "/Artifacts" => new[] { Item("/Artifacts/nested/team.json"), Item("/Artifacts/bad.json"), Item("/Artifacts/notes.txt") },
            _ => throw new AssertionException("Unexpected item scope")
        };
        return Json(new { count = items.Length, value = items });
    }

    private static HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> handle) => new(new AsyncHandler((request, _) => Task.FromResult(handle(request))));
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    private sealed class AsyncHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handle(request, cancellationToken);
    }

    private sealed class StalledStream : MemoryStream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }
}
