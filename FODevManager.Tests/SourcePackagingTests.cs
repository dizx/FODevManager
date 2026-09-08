using FODevManager.Messages;
using FODevManager.Models;
using FODevManager.Services;
using FODevManager.Utils;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Xml.Linq;

namespace FODevManager.Tests;

[TestFixture]
public class SourcePackagingTests
{
    private string _root = null!;

    [SetUp]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "FODevManagerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void Teardown() => Directory.Delete(_root, true);

    [TestCase(false)]
    [TestCase(true)]
    public void HintProducer_Discovery_Is_Bounded_And_Uses_AssemblyName(bool customAssemblyName)
    {
        var project = Write("repo/Libs/Peritus.Ssh/" + (customAssemblyName ? "Producer" : "Peritus.Ssh") + ".csproj",
            customAssemblyName ? "<Project><PropertyGroup><AssemblyName>Peritus.Ssh</AssemblyName></PropertyGroup></Project>" : "<Project />");
        Write("repo/Unrelated/Other.csproj", "<Project><AssemblyName>Peritus.Ssh</AssemblyName></Project>");
        Write("Outside.csproj", "<Project><AssemblyName>Peritus.Ssh</AssemblyName></Project>");
        var hint = Path.Combine(_root, "repo/Libs/Peritus.Ssh/bin/Debug/net48/Peritus.Ssh.dll");
        Assert.That(Invoke("FindHintPathProducer", hint, Path.Combine(_root, "repo")), Is.EqualTo(project));
        Assert.That(Invoke("FindHintPathProducer", hint, Path.Combine(_root, "repo/Project")), Is.Null);
    }

    [Test]
    public void Conversion_Preserves_Metadata_Conditions_And_Vendors_Without_Producer_Output()
    {
        var project = Write("repo/Project/PTSSSH/PTSSSH.rnrproj", """
            <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <ItemGroup Condition="'$(Configuration)' == 'Debug'">
                <Reference Include="Peritus.Ssh" Condition="'$(Platform)' == 'AnyCPU'">
                  <HintPath>..\..\Libs\Peritus.Ssh\bin\$(Configuration)\net48\Peritus.Ssh.dll</HintPath>
                  <Private Condition="'$(Configuration)' == 'Debug'">True</Private>
                  <Aliases>global</Aliases>
                </Reference>
                <Reference Include="Vendor"><HintPath>..\..\Libs\Vendor.dll</HintPath><Private>False</Private></Reference>
              </ItemGroup>
            </Project>
            """);
        var producer = Write("repo/Libs/Peritus.Ssh/Peritus.Ssh.csproj", "<Project Sdk='Microsoft.NET.Sdk'><PropertyGroup><TargetFramework>net48</TargetFramework></PropertyGroup></Project>");
        var originalProducer = File.ReadAllBytes(producer);
        var before = XDocument.Load(project);
        var vendor = before.Descendants().Single(element => element.Attribute("Include")?.Value == "Vendor");
        Assert.That(ConvertReferences(project), Is.True);
        var after = XDocument.Load(project);
        var reference = after.Descendants().Single(element => element.Name.LocalName == "ProjectReference");
        Assert.That(reference.Attribute("Include")!.Value, Is.EqualTo(@"..\..\Libs\Peritus.Ssh\Peritus.Ssh.csproj"));
        Assert.That(reference.Attribute("Condition")!.Value, Is.EqualTo("'$(Platform)' == 'AnyCPU'"));
        Assert.That(reference.Parent!.Attribute("Condition")!.Value, Is.EqualTo("'$(Configuration)' == 'Debug'"));
        Assert.That(XNode.DeepEquals(reference.Elements().Single(element => element.Name.LocalName == "Private"),
            before.Descendants().First(element => element.Name.LocalName == "Private")), Is.True);
        Assert.That(reference.Elements().Single(element => element.Name.LocalName == "Aliases").Value, Is.EqualTo("global"));
        Assert.That(reference.Elements().Single(element => element.Name.LocalName == "Name").Value, Is.EqualTo("Peritus.Ssh"));
        Assert.That(Guid.TryParse(reference.Elements().Single(element => element.Name.LocalName == "Project").Value, out _), Is.True);
        Assert.That(XNode.DeepEquals(vendor, after.Descendants().Single(element => element.Attribute("Include")?.Value == "Vendor")), Is.True);
        Assert.That(File.ReadAllText(project), Does.Not.Match("(?<!\\r)\\n"));
        Assert.That(File.ReadAllText(project), Does.Contain("\r\n      <Name>Peritus.Ssh</Name>\r\n      <Project>"));
        Assert.That(File.ReadAllText(project), Does.Not.Contain("\r\n      \r\n"));
        var converted = File.ReadAllBytes(project);
        Assert.That(ConvertReferences(project), Is.True);
        Assert.That(File.ReadAllBytes(project), Is.EqualTo(converted));
        Assert.That(File.ReadAllBytes(producer), Is.EqualTo(originalProducer));
        Assert.That(Directory.Exists(Path.Combine(Path.GetDirectoryName(producer)!, "bin")), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Conversion_Deduplicates_Equivalent_ProjectReferences_But_Preserves_Different_Conditions(bool differentCondition)
    {
        var condition = differentCondition ? " Condition=\"'$(Configuration)' == 'Release'\"" : "";
        var project = CreateSourceProject($"<ProjectReference Include='../../Libs/Peritus.Ssh/./Peritus.Ssh.csproj'{condition}><Project>{{11111111-1111-1111-1111-111111111111}}</Project></ProjectReference>" +
            "<Reference Include='Peritus.Ssh'><HintPath>../../Libs/Peritus.Ssh/bin/Debug/net48/Peritus.Ssh.dll</HintPath><Private>True</Private></Reference>");
        Write("repo/Libs/Peritus.Ssh/Peritus.Ssh.csproj", "<Project Sdk='Microsoft.NET.Sdk' />");
        Assert.That(ConvertReferences(project), Is.True);
        var references = XDocument.Load(project).Descendants("ProjectReference").ToList();
        Assert.That(references, Has.Count.EqualTo(differentCondition ? 2 : 1));
        Assert.That(references.Single(reference => reference.Attribute("Condition") == null).Element("Private")!.Value, Is.EqualTo("True"));
        Assert.That(references.Select(reference => reference.Element("Project")!.Value).Distinct(),
            Is.EquivalentTo(new[] { "{11111111-1111-1111-1111-111111111111}" }));
        var converted = File.ReadAllBytes(project);
        Assert.That(ConvertReferences(project), Is.True);
        Assert.That(File.ReadAllBytes(project), Is.EqualTo(converted));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Conversion_Failure_Leaves_All_References_Unchanged(bool metadataConflict)
    {
        var project = CreateSourceProject("<Reference Include='First'><HintPath>../../Libs/First/bin/First.dll</HintPath></Reference>" +
            "<Reference Include='Second'><HintPath>../../Libs/Second/bin/Second.dll</HintPath><Private>True</Private></Reference>" +
            (metadataConflict ? "<ProjectReference Include='../../Libs/Second/Second.csproj'><Private>False</Private></ProjectReference>" : ""));
        Write("repo/Libs/First/First.csproj", "<Project />");
        Write("repo/Libs/Second/Second.csproj", "<Project />");
        if (!metadataConflict)
            Write("repo/Libs/Second/Ambiguous.csproj", "<Project><AssemblyName>Second</AssemblyName></Project>");
        var original = File.ReadAllBytes(project);
        Assert.That(ConvertReferences(project), Is.False);
        Assert.That(File.ReadAllBytes(project), Is.EqualTo(original));
        Assert.That(Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories), Is.Empty);
    }

    [Test]
    public void Conversion_Does_Not_Discard_HintPath_Conditions()
    {
        var project = CreateSourceProject("<Reference Include='Peritus.Ssh'><HintPath Condition=\"'$(Configuration)' == 'Debug'\">../../Libs/Peritus.Ssh/bin/Debug/net48/Peritus.Ssh.dll</HintPath></Reference>");
        Write("repo/Libs/Peritus.Ssh/Peritus.Ssh.csproj", "<Project />");
        var original = File.ReadAllBytes(project);
        Assert.That(ConvertReferences(project), Is.False);
        Assert.That(File.ReadAllBytes(project), Is.EqualTo(original));
    }

    [Test]
    public void HintProducer_Ambiguity_Fails_Even_When_Old_Assembly_Exists()
    {
        var project = CreateSourceProject("<Reference Include='Peritus.Ssh'><HintPath>../../Libs/Peritus.Ssh/bin/$(Configuration)/net48/Peritus.Ssh.dll</HintPath></Reference>");
        Write("repo/Libs/Peritus.Ssh/First.csproj", "<Project><AssemblyName>Peritus.Ssh</AssemblyName></Project>");
        Write("repo/Libs/Second.csproj", "<Project><AssemblyName>Peritus.Ssh</AssemblyName></Project>");
        CopyAssembly("repo/Libs/Peritus.Ssh/bin/Debug/net48/Peritus.Ssh.dll");
        var messages = new List<string>();
        using var subscription = MessageBus.Subscribe(message => messages.Add(message.Content));
        Assert.That(Prepare("unused", project, out _), Is.False);
        Assert.That(string.Join("\n", messages), Does.Contain("Ambiguous producers").And.Contain("ProjectReference"));
    }

    [TestCase("<ProjectReference Include='missing.csproj' />", "missing.csproj")]
    [TestCase("<Reference Include='Missing'><HintPath>missing.dll</HintPath></Reference>", "Missing")]
    [TestCase("<Reference Include='Missing'><HintPath>$(Unknown)/missing.dll</HintPath></Reference>", "Missing")]
    public void Missing_Or_Unresolved_References_Fail_With_Diagnostics(string reference, string error)
    {
        var project = CreateSourceProject(reference);
        var messages = new List<string>();
        using var subscription = MessageBus.Subscribe(message => messages.Add(message.Content));
        Assert.That(Prepare("unused", project, out _), Is.False);
        Assert.That(string.Join("\n", messages), Does.Contain(error));
    }

    [Test]
    public void FileOnly_Reference_Stages_Evaluated_Reference_Not_Unrelated_Sibling_Files()
    {
        var project = CreateSourceProject("<Reference Include='Vendor'><HintPath>../../Libs/Vendor.dll</HintPath></Reference>");
        CopyAssembly("repo/Libs/Vendor.dll");
        CopyAssembly("repo/Libs/Dependency.dll");
        Write("repo/Libs/runtimes/win-x64/native/support.dll", "native");
        Write("repo/Libs/fr-FR/Vendor.resources.dll", "satellite");
        Write("repo/Libs/bin/Release/stale.dll", "stale");
        Write("repo/Libs/source.cs", "not a dependency");
        Assert.That(Prepare("unused", project, out var folders), Is.True);
        var target = Path.Combine(_root, "model/bin");
        Write("model/bin/Vendor.dll", "main build replaced dependency");
        Invoke("CopyStagedReferences", folders, target);
        Assert.That(Directory.GetFiles(target, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(target, path).Replace('\\', '/')),
            Does.Contain("Vendor.dll").And.Not.Contain("Dependency.dll").And.Not.Contain("bin/Release/stale.dll"));
        Assert.That(File.ReadAllBytes(Path.Combine(target, "Vendor.dll")), Is.EqualTo(File.ReadAllBytes(typeof(DeployablePackageService).Assembly.Location)));
    }

    [TestCase("empty")]
    [TestCase("xref")]
    [TestCase("invalid-dll")]
    [TestCase("valid-dll")]
    public void Source_Payload_Requires_Current_Run_Managed_Model_Assembly(string payload)
    {
        var deployed = CopyAssembly("deployed/TestModel/bin/Dynamics.AX.TestModel.dll");
        var output = Path.Combine(_root, "run/BuildOutput");
        Directory.CreateDirectory(output);
        if (payload != "empty")
            Write("run/BuildOutput/Bin/TestModel/TestModel.xref", "xref");
        if (payload == "invalid-dll")
            Write("run/BuildOutput/Bin/TestModel/bin/Dynamics.AX.TestModel.dll", "not managed");
        if (payload == "valid-dll")
            CopyAssembly("run/BuildOutput/Bin/TestModel/bin/Dynamics.AX.TestModel.dll");
        var service = new DeployablePackageService(new AppConfig { DeployablePackages = string.Empty, DeploymentBasePath = Path.Combine(_root, "deployed") });
        object?[] args = [output, "TestModel", null];
        var result = typeof(DeployablePackageService).GetMethod("TryResolveBuiltPayloadRoot", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(service, args);
        Assert.That(result, Is.EqualTo(payload == "valid-dll"));
        Assert.That(File.Exists(deployed), Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Sdk_Net48_Producer_Rebuild_Stages_Evaluated_Output_And_CopyLocal_Not_Old_Bin(bool explicitProjectReference)
    {
        var msbuild = (string)Invoke("ResolveMsBuildExecutable")!;
        if (string.IsNullOrEmpty(msbuild))
            Assert.Ignore("Visual Studio MSBuild is required for the synthetic net48 build");
        var project = CreateSourceProject(explicitProjectReference
            ? "<ProjectReference Include='../../Libs/Peritus.Ssh/Producer.csproj' />"
            : "<Reference Include='Peritus.Ssh'><HintPath>../../Libs/Peritus.Ssh/bin/$(Configuration)/$(TargetFramework)/Peritus.Ssh.dll</HintPath></Reference>");
        var producer = Write("repo/Libs/Peritus.Ssh/Producer.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net48</TargetFramework>
                <AssemblyName>Peritus.Ssh</AssemblyName>
                <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
                <OutputPath>custom-output/$(Configuration)/</OutputPath>
              </PropertyGroup>
              <ItemGroup><ProjectReference Include="../Dependency/Dependency.csproj" /></ItemGroup>
            </Project>
            """);
        Write("repo/Libs/Peritus.Ssh/Source.cs", "public class Producer { public Dependency Value; }");
        Write("repo/Libs/Dependency/Dependency.csproj", "<Project Sdk='Microsoft.NET.Sdk'><PropertyGroup><TargetFramework>net48</TargetFramework></PropertyGroup></Project>");
        Write("repo/Libs/Dependency/Source.cs", "public class Dependency { }");
        Write("repo/nuget.config", "<configuration><packageSources><clear /></packageSources></configuration>");
        Write("repo/Libs/Peritus.Ssh/bin/Debug/net48/Peritus.Ssh.dll", "old assembly");
        Write("repo/Libs/Peritus.Ssh/bin/Debug/net48/Stale.dll", "not a resolved dependency");
        var original = File.ReadAllBytes(producer);
        var messages = new List<string>();
        using var subscription = MessageBus.Subscribe(message => messages.Add(message.Content));
        Assert.That(Prepare(msbuild, project, out var folders), Is.True, string.Join("\n", messages));
        Assert.That(folders, Has.Count.EqualTo(1));
        Assert.That(AssemblyName.GetAssemblyName(Path.Combine(folders[0], "Peritus.Ssh.dll")).Name, Is.EqualTo("Peritus.Ssh"));
        Assert.That(File.Exists(Path.Combine(folders[0], "Dependency.dll")), Is.True);
        Assert.That(File.Exists(Path.Combine(folders[0], "Stale.dll")), Is.False);
        Assert.That(File.ReadAllBytes(producer), Is.EqualTo(original));
        Assert.That(File.ReadAllText(Path.Combine(_root, "repo/Libs/Peritus.Ssh/bin/Debug/net48/Peritus.Ssh.dll")), Is.EqualTo("old assembly"));
        Assert.That(Prepare(msbuild, project, out var secondFolders), Is.True, string.Join("\n", messages));
        Assert.That(secondFolders[0], Is.Not.EqualTo(folders[0]));

        // Exercise PackageReference copy-local resolution without a live package feed
        var packagePath = Path.Combine(_root, "feed", "Fixture.Dependency.1.0.0.nupkg");
        Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);
        using (var package = ZipFile.Open(packagePath, ZipArchiveMode.Create))
        {
            package.CreateEntryFromFile(Path.Combine(folders[0], "Dependency.dll"), "lib/net48/Dependency.dll");
            using var writer = new StreamWriter(package.CreateEntry("Fixture.Dependency.nuspec").Open());
            writer.Write("<package><metadata><id>Fixture.Dependency</id><version>1.0.0</version><authors>Tests</authors><description>Local test dependency</description></metadata></package>");
        }
        var document = XDocument.Load(producer);
        document.Descendants("ProjectReference").Single().ReplaceWith(new XElement("PackageReference",
            new XAttribute("Include", "Fixture.Dependency"), new XAttribute("Version", "1.0.0")));
        document.Descendants("PropertyGroup").Single().Add(new XElement("RestorePackagesPath", Path.Combine(_root, "packages")));
        document.Save(producer);
        new XDocument(new XElement("configuration", new XElement("packageSources", new XElement("clear"),
            new XElement("add", new XAttribute("key", "fixture"), new XAttribute("value", Path.Combine(_root, "feed"))))))
            .Save(Path.Combine(_root, "repo/nuget.config"));
        Assert.That(Prepare(msbuild, project, out var packageFolders), Is.True, string.Join("\n", messages));
        Assert.That(File.ReadAllBytes(Path.Combine(packageFolders[0], "Dependency.dll")),
            Is.EqualTo(File.ReadAllBytes(Path.Combine(folders[0], "Dependency.dll"))));
    }

    [TestCase(true, false)]
    [TestCase(false, false)]
    [TestCase(true, true)]
    public void Main_Rebuild_Requires_New_Model_Even_When_Old_Binaries_Are_Available(bool emitModel, bool referenceContainsOldModel)
    {
        if (string.IsNullOrEmpty((string)Invoke("ResolveMsBuildExecutable")!))
            Assert.Ignore("Visual Studio MSBuild is required");
        var project = CreateSourceProject("<Reference Include='Vendor'><HintPath>../../Libs/Vendor.dll</HintPath></Reference>");
        CopyAssembly("repo/Libs/Vendor.dll");
        Write("repo/Libs/runtimes/win-x64/native/support.dll", "native");
        if (referenceContainsOldModel)
            CopyAssembly("repo/Libs/Dynamics.AX.TestModel.dll");
        var assembly = CopyAssembly("fixture/Dynamics.AX.TestModel.dll");
        CopyAssembly("deployed/TestModel/bin/Dynamics.AX.TestModel.dll");
        var document = XDocument.Load(project);
        document.Root!.Add(new XElement("PropertyGroup", new XElement("EmitModel", emitModel.ToString().ToLowerInvariant())));
        document.Save(project);
        Assert.That(Prepare("unused", project, out var folders), Is.EqualTo(emitModel));
        if (emitModel)
            Assert.That(File.Exists(Path.Combine(folders[0], "Vendor.dll")), Is.True);
        Assert.That(File.Exists(assembly), Is.True);
    }

    [Test]
    public void CopyStagedReferences_Rejects_Conflicting_Dependencies()
    {
        var project = CreateSourceProject("<Reference Include='First'><HintPath>../../Libs/First/First.dll</HintPath></Reference><Reference Include='Second'><HintPath>../../Libs/Second/Second.dll</HintPath></Reference>");
        CopyAssembly("repo/Libs/First/First.dll");
        CopyAssembly("repo/Libs/Second/Second.dll");
        Write("repo/Libs/First/Dependency.dll", "first version");
        Write("repo/Libs/Second/Dependency.dll", "second version");
        var method = typeof(DeployablePackageService).GetMethod("CopyStagedReferences", BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.That(() => method.Invoke(null, new object[] { new[] { Path.Combine(_root, "repo/Libs/First"), Path.Combine(_root, "repo/Libs/Second") }, Path.Combine(_root, "output") }),
            Throws.TypeOf<TargetInvocationException>().With.InnerException.TypeOf<InvalidOperationException>());
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Producer_Failure_Or_No_Output_Does_Not_Use_Old_Hint(bool failBuild)
    {
        var msbuild = (string)Invoke("ResolveMsBuildExecutable")!;
        if (string.IsNullOrEmpty(msbuild))
            Assert.Ignore("Visual Studio MSBuild is required");
        var project = CreateSourceProject("<Reference Include='Producer'><HintPath>../../Libs/bin/Debug/net48/Producer.dll</HintPath></Reference>");
        Write("repo/Libs/Producer.csproj", $"<Project><Target Name='Restore' /><Target Name='Rebuild'>{(failBuild ? "<Error Text='synthetic failure' />" : "")}</Target></Project>");
        CopyAssembly("repo/Libs/bin/Debug/net48/Producer.dll");
        Assert.That(Prepare(msbuild, project, out _), Is.False);
    }

    [Test]
    public void Actual_Solution_Mapping_Stages_Release_X64_Content_Native_And_Own_Satellites()
    {
        var project = CreateSourceProject("<ProjectReference Include='../../Libs/Peritus.Ssh/Producer.csproj' />");
        Write("repo/Libs/Peritus.Ssh/Producer.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net48</TargetFramework><AssemblyName>Peritus.Ssh</AssemblyName></PropertyGroup>
              <ItemGroup>
                <None Update="settings.json" CopyToOutputDirectory="PreserveNewest" TargetPath="config/settings.json" />
                <None Update="bridge.dat" CopyToOutputDirectory="Always" TargetPath="runtimes/win-x64/native/bridge.dll" />
              </ItemGroup>
              <Target Name="RecordBuild" AfterTargets="Build">
                <WriteLinesToFile File="$(MSBuildProjectDirectory)/builds.log" Lines="$(Configuration)|$(Platform)" />
              </Target>
            </Project>
            """);
        Write("repo/Libs/Peritus.Ssh/Source.cs", "public class Producer { }");
        Write("repo/Libs/Peritus.Ssh/settings.json", "{\"current\":true}");
        Write("repo/Libs/Peritus.Ssh/bridge.dat", "current native content");
        Write("repo/Libs/Peritus.Ssh/Labels.fr.resx", """
            <root>
              <resheader name="resmimetype"><value>text/microsoft-resx</value></resheader>
              <resheader name="version"><value>2.0</value></resheader>
              <resheader name="reader"><value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
              <resheader name="writer"><value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
              <data name="Greeting" xml:space="preserve"><value>Bonjour</value></data>
            </root>
            """);
        Write("repo/Libs/Peritus.Ssh/bin/Release/net48/Stale.dll", "not an output of this build");
        Write("repo/nuget.config", "<configuration><packageSources><clear /></packageSources></configuration>");
        var messages = new List<string>();
        using var subscription = MessageBus.Subscribe(message => messages.Add(message.Content));
        Assert.That(Prepare("unused", project, out var folders, "Release|x64"), Is.True, string.Join("\n", messages));
        Assert.That(File.ReadAllLines(Path.Combine(_root, "repo/Libs/Peritus.Ssh/builds.log")), Is.EqualTo(new[] { "Release|x64" }));
        Assert.That(File.ReadAllText(Path.Combine(folders[0], "config/settings.json")), Is.EqualTo("{\"current\":true}"));
        Assert.That(File.ReadAllText(Path.Combine(folders[0], "runtimes/win-x64/native/bridge.dll")), Is.EqualTo("current native content"));
        Assert.That(File.Exists(Path.Combine(folders[0], "fr/Peritus.Ssh.resources.dll")), Is.True);
        Assert.That(File.Exists(Path.Combine(folders[0], "Stale.dll")), Is.False);
        Assert.That(File.ReadAllBytes(Path.Combine(folders[0], "Peritus.Ssh.dll")),
            Is.EqualTo(File.ReadAllBytes(Path.Combine(_root, "repo/Libs/Peritus.Ssh/bin/x64/Release/net48/Peritus.Ssh.dll"))));
    }

    [Test]
    public void Inactive_Missing_And_Existing_References_Are_Not_Built_Or_Packaged()
    {
        var project = CreateSourceProject("""
            <ProjectReference Include="missing.csproj" Condition="'$(Configuration)' == 'Release'" />
            <ProjectReference Include="../../Libs/Inactive.csproj" Condition="'$(Configuration)' == 'Release'" />
            <Reference Include="Missing" Condition="'$(Configuration)' == 'Release'"><HintPath>missing.dll</HintPath></Reference>
            <Reference Include="Unknown" Condition="'$(Configuration)' == 'Release'"><HintPath>$(Unknown)/missing.dll</HintPath></Reference>
            """);
        Write("repo/Libs/Inactive.csproj", "<Project><Target Name='Restore'/><Target Name='Rebuild'><Error Text='Inactive producer built'/></Target></Project>");
        var messages = new List<string>();
        using var subscription = MessageBus.Subscribe(message => messages.Add(message.Content));
        Assert.That(Prepare("unused", project, out var folders), Is.True, string.Join("\n", messages));
        Assert.That(File.ReadAllText(Path.Combine(Path.GetDirectoryName(project)!, "TestModel.packagebuild.sln")), Does.Not.Contain("Inactive.csproj").And.Not.Contain("missing.csproj"));
        Assert.That(Directory.GetFiles(folders[0], "*.dll").Select(Path.GetFileName), Is.EqualTo(new[] { "Dynamics.AX.TestModel.dll" }));
    }

    [Test]
    public void Reference_Assemblies_Are_Excluded_By_Metadata_Not_System_Or_Microsoft_Name()
    {
        var referenceAssembly = @"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\System.Core.dll";
        Assert.That(Invoke("IsReferenceOnlyAssembly", referenceAssembly), Is.True);
        Assert.That(Invoke("IsReferenceOnlyAssembly", @"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Core.dll"), Is.False);
        var native = Write("Microsoft.Native.dll", "native library content");
        Assert.That(Invoke("IsReferenceOnlyAssembly", native), Is.False);
        var project = CreateSourceProject("<Reference Include='System.Core'/><Reference Include='System.Custom'><HintPath>../../Libs/System.Custom.dll</HintPath></Reference>");
        CopyAssembly("repo/Libs/System.Custom.dll");
        var messages = new List<string>();
        using var subscription = MessageBus.Subscribe(message => messages.Add(message.Content));
        Assert.That(Prepare("unused", project, out var folders), Is.True, string.Join("\n", messages));
        Assert.That(File.Exists(Path.Combine(folders[0], "System.Core.dll")), Is.False);
        Assert.That(File.Exists(Path.Combine(folders[0], "System.Custom.dll")), Is.True);
    }

    [Test]
    public void RunProcess_Drains_Both_Pipes_And_Reports_Exit_Code()
    {
        var start = new ProcessStartInfo("powershell.exe",
            "-NoProfile -NonInteractive -Command \"[Console]::Error.Write(('e' * 200000)); [Console]::Out.Write(('o' * 200000)); exit 7\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        object?[] args = [start, null];
        var task = Task.Run(() => Invoke("RunProcess", args));
        Assert.That(task.Wait(TimeSpan.FromSeconds(30)), Is.True, "Process pipes failed to drain");
        Assert.That(task.Result, Is.False);
        Assert.That((string)args[1]!, Does.Contain(new string('e', 200000)).And.Contain(new string('o', 200000)));
    }

    private bool Prepare(string msbuild, string project, out IReadOnlyList<string> folders, string? producerMapping = null)
    {
        folders = [];
        if (!ConvertReferences(project))
            return false;
        var modelAssembly = CopyAssembly("repo/Metadata/TestModel/bin/Dynamics.AX.TestModel.dll");
        var originalModel = File.ReadAllBytes(modelAssembly);
        var fakeTargets = Write("repo/FakeFo.targets", $$"""
            <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup><TargetFrameworkVersion>v4.8</TargetFrameworkVersion><OutputPath>bin</OutputPath></PropertyGroup>
              <Import Project="$(MSBuildBinPath)\Microsoft.Common.targets" />
              <Target Name="CopyReferences" DependsOnTargets="ResolveProjectReferences;ResolveAssemblyReferences">
                <Copy SourceFiles="@(ReferencePath)" DestinationFolder="$(OutputDirectory)\TestModel\bin" />
                <Copy SourceFiles="{{modelAssembly}}" DestinationFolder="$(OutputDirectory)\TestModel\bin" />
              </Target>
              <Target Name="Build" DependsOnTargets="CopyReferences">
                <Error Condition="Exists('$(OutputDirectory)\TestModel\bin\Dynamics.AX.TestModel.dll')" Text="Copied old model assembly was not removed before compilation" />
                <Copy Condition="'$(EmitModel)' != 'false'" SourceFiles="{{modelAssembly}}" DestinationFolder="$(OutputDirectory)\TestModel\bin" />
              </Target>
            </Project>
            """);
        var document = XDocument.Load(project);
        if (!document.Descendants("Import").Any())
        {
            document.Root!.Add(new XElement("Import", new XAttribute("Project", fakeTargets)));
            document.Save(project);
        }
        var source = Write("repo/Source.sln", "Microsoft Visual Studio Solution File, Format Version 12.00\r\n" +
            $"Project(\"{{FC65038C-1B2F-41E1-A629-BED71D161FFF}}\") = \"TestModel\", \"{Path.GetRelativePath(Path.Combine(_root, "repo"), project)}\", \"{{11111111-1111-1111-1111-111111111111}}\"\r\nEndProject\r\n");
        if (File.Exists(Path.Combine(_root, "repo/Libs/Inactive.csproj")))
            File.AppendAllText(source, "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"Inactive\", \"Libs\\Inactive.csproj\", \"{22222222-2222-2222-2222-222222222222}\"\r\nEndProject\r\n");
        if (producerMapping != null)
        {
            var producer = Path.Combine(_root, "repo/Libs/Peritus.Ssh/Producer.csproj");
            var guid = typeof(VisualStudioSolutionService).GetMethod("ResolveProjectReferenceGuid", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [producer]);
            File.AppendAllText(source, $"Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = \"Producer\", \"Libs\\Peritus.Ssh\\Producer.csproj\", \"{guid}\"\r\nEndProject\r\n" +
                $"Global\r\n\tGlobalSection(ProjectConfigurationPlatforms) = postSolution\r\n\t\t{guid}.Debug|Any CPU.ActiveCfg = {producerMapping}\r\n\t\t{guid}.Debug|Any CPU.Build.0 = {producerMapping}\r\n\tEndGlobalSection\r\nEndGlobal\r\n");
        }
        var solution = new VisualStudioSolutionService(new AppConfig()).CreateBuildSolution(
            new ProfileModel { SolutionFilePath = source }, new ProfileEnvironmentModel { ModelName = "TestModel", ProjectFilePath = project });
        var contextType = typeof(DeployablePackageService).GetNestedType("MsBuildContext", BindingFlags.NonPublic)!;
        var context = Activator.CreateInstance(contextType, _root, _root, _root, _root);
        var runRoot = Path.Combine(_root, "run", Guid.NewGuid().ToString("N"));
        var output = Path.Combine(runRoot, "BuildOutput");
        var service = new DeployablePackageService(new AppConfig { DeployablePackages = string.Empty });
        var result = (bool)typeof(DeployablePackageService).GetMethod("RunMsBuild", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(service, [solution, project, "TestModel", context, output, runRoot])!;
        Assert.That(File.ReadAllBytes(modelAssembly), Is.EqualTo(originalModel), "Original metadata model bin must never be changed by cleanup");
        folders = [Path.Combine(output, "Bin/TestModel/bin")];
        return result;
    }

    private bool ConvertReferences(string project) =>
        new DeployablePackageService(new AppConfig { DeployablePackages = string.Empty }).ConvertSourceHintPathReferences(
            new ProfileModel(), new ProfileEnvironmentModel
            {
                ModelType = ModelType.Source,
                ModelRootFolder = Path.Combine(_root, "repo"),
                ProjectFilePath = project
            });

    private string CreateSourceProject(string reference) => Write("repo/Project/PTSSSH/PTSSSH.rnrproj", $"<Project><ItemGroup>{reference}</ItemGroup></Project>");

    private string Write(string relativePath, string contents)
    {
        var path = Path.GetFullPath(Path.Combine(_root, relativePath));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
        return path;
    }

    private string CopyAssembly(string relativePath)
    {
        var path = Write(relativePath, "");
        File.Copy(typeof(DeployablePackageService).Assembly.Location, path, overwrite: true);
        return path;
    }

    private static object? Invoke(string name, params object?[] args) =>
        typeof(DeployablePackageService).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args);
}
