using FODevManager.Models;
using FODevManager.Services;
using FODevManager.Utils;
using System.Diagnostics;

namespace FODevManager.Tests
{
    [TestFixture]
    public class VisualStudioSolutionServiceTests
    {
        private string _baseDir = string.Empty;

        [SetUp]
        public void Setup()
        {
            _baseDir = Path.Combine(Path.GetTempPath(), "FODevManagerTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_baseDir);
        }

        [TearDown]
        public void Teardown()
        {
            if (Directory.Exists(_baseDir))
                Directory.Delete(_baseDir, true);
        }

        [Test]
        public void AddProjectMatchesExactPathRatherThanSubstringOrDisplayName()
        {
            var service = CreateService();
            var path = Path.Combine(_baseDir, "Foo.rnrproj");
            File.WriteAllText(path, "<Project />");
            var profile = new ProfileModel { ProfileName = "Test", SolutionFilePath = Path.Combine(_baseDir, "Test.sln") };
            File.WriteAllText(profile.SolutionFilePath,
                "Project(\"{FC65038C-1B2F-41E1-A629-BED71D161FFF}\") = \"FooExtensions\", \"FooExtensions.rnrproj\", \"{11111111-1111-1111-1111-111111111111}\"\r\nEndProject\r\n" +
                "Project(\"{FC65038C-1B2F-41E1-A629-BED71D161FFF}\") = \"Foo\", \"OtherFoo.rnrproj\", \"{22222222-2222-2222-2222-222222222222}\"\r\nEndProject\r\n");
            var model = new ProfileEnvironmentModel { ModelName = "Foo", ProjectFilePath = path };
            service.AddProjectToSolution(profile, model);
            var solution = File.ReadAllText(profile.SolutionFilePath);
            Assert.That(solution, Does.Contain("\"Foo.rnrproj\""));
            model.ModelName = "RenamedFoo";
            service.AddProjectToSolution(profile, model);
            Assert.That(File.ReadAllText(profile.SolutionFilePath), Is.EqualTo(solution));
        }

        [Test]
        public void CreateBuildSolution_Should_Include_ProjectReferences_And_Filter_Invalid_Nesting()
        {
            var repoRoot = Path.Combine(_baseDir, "Peritus ZTL");
            var modelProjectDirectory = Path.Combine(repoRoot, "project", "PTSZTL");
            var coreProjectDirectory = Path.Combine(repoRoot, "Lib", "Peritus.ZTL.Core");
            var foProjectDirectory = Path.Combine(repoRoot, "Lib", "Peritus.ZTL.FO");
            Directory.CreateDirectory(modelProjectDirectory);
            Directory.CreateDirectory(coreProjectDirectory);
            Directory.CreateDirectory(foProjectDirectory);

            var modelProjectPath = Path.Combine(modelProjectDirectory, "PTSZTL.rnrproj");
            var coreProjectPath = Path.Combine(coreProjectDirectory, "Peritus.ZTL.Core.csproj");
            var foProjectPath = Path.Combine(foProjectDirectory, "Peritus.ZTL.FO.csproj");
            var solutionPath = Path.Combine(repoRoot, "Peritus ZTL.sln");

            File.WriteAllText(coreProjectPath, "<Project />");
            File.WriteAllText(foProjectPath, "<Project />");
            File.WriteAllText(modelProjectPath,
                """
                <Project>
                  <ItemGroup>
                    <ProjectReference Include="..\..\Lib\Peritus.ZTL.Core\Peritus.ZTL.Core.csproj">
                      <Project>{B853DC9F-A460-5FB0-B0BE-B9621A48AD0E}</Project>
                      <Name>Peritus.ZTL.Core</Name>
                      <Private>True</Private>
                    </ProjectReference>
                    <ProjectReference Include="..\..\Lib\Peritus.ZTL.FO\Peritus.ZTL.FO.csproj">
                      <Project>{4FBBC607-345D-59EC-6E4B-EABD2D69B381}</Project>
                      <Name>Peritus.ZTL.FO</Name>
                      <Private>True</Private>
                    </ProjectReference>
                  </ItemGroup>
                </Project>
                """);

            File.WriteAllText(solutionPath,
                """
                Microsoft Visual Studio Solution File, Format Version 12.00
                # Visual Studio Version 17
                VisualStudioVersion = 17.12.35527.113
                Project("{FC65038C-1B2F-41E1-A629-BED71D161FFF}") = "PTSZTL", "project\PTSZTL\PTSZTL.rnrproj", "{11111111-1111-1111-1111-111111111111}"
                	ProjectSection(ProjectDependencies) = postProject
                		{4FBBC607-345D-59EC-6E4B-EABD2D69B381} = {4FBBC607-345D-59EC-6E4B-EABD2D69B381}
                		{99999999-9999-9999-9999-999999999999} = {99999999-9999-9999-9999-999999999999}
                	EndProjectSection
                EndProject
                Project("{2150E333-8FDC-42A3-9474-1A3956D46DE8}") = "lib", "lib", "{02EA681E-C7D8-13C7-8484-4AC65E1B71E8}"
                EndProject
                Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Peritus.ZTL.Core", "Lib\Peritus.ZTL.Core\Peritus.ZTL.Core.csproj", "{89109846-08B3-489D-8C54-2AE9F94AE919}"
                EndProject
                Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Peritus.ZTL.FO", "Lib\Peritus.ZTL.FO\Peritus.ZTL.FO.csproj", "{4FBBC607-345D-59EC-6E4B-EABD2D69B381}"
                EndProject
                Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Unrelated", "Lib\Unrelated\Unrelated.csproj", "{99999999-9999-9999-9999-999999999999}"
                EndProject
                Global
                	GlobalSection(NestedProjects) = preSolution
                		{89109846-08B3-489D-8C54-2AE9F94AE919} = {02EA681E-C7D8-13C7-8484-4AC65E1B71E8}
                		{4FBBC607-345D-59EC-6E4B-EABD2D69B381} = {02EA681E-C7D8-13C7-8484-4AC65E1B71E8}
                		{99999999-9999-9999-9999-999999999999} = {27EBDC92-A52C-4194-B0B6-5DB32EF5C97B}
                	EndGlobalSection
                EndGlobal
                """);

            var service = CreateService();
            var profile = new ProfileModel
            {
                ProfileName = "Peritus ZTL",
                SolutionFilePath = solutionPath
            };
            var model = new ProfileEnvironmentModel
            {
                ModelName = "PTSZTL",
                ModelType = ModelType.Source,
                ProjectFilePath = modelProjectPath
            };

            var buildSolutionPath = service.CreateBuildSolution(profile, model);
            var buildSolution = File.ReadAllText(buildSolutionPath);
            var sourceSolution = File.ReadAllText(solutionPath);

            Assert.That(buildSolution, Does.Contain("Project(\"{FC65038C-1B2F-41E1-A629-BED71D161FFF}\") = \"PTSZTL\", \"PTSZTL.rnrproj\""));
            Assert.That(buildSolution, Does.Contain("Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"Peritus.ZTL.Core\", \"..\\..\\Lib\\Peritus.ZTL.Core\\Peritus.ZTL.Core.csproj\", \"{B853DC9F-A460-5FB0-B0BE-B9621A48AD0E}\""));
            Assert.That(buildSolution, Does.Contain("Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"Peritus.ZTL.FO\", \"..\\..\\Lib\\Peritus.ZTL.FO\\Peritus.ZTL.FO.csproj\", \"{4FBBC607-345D-59EC-6E4B-EABD2D69B381}\""));
            Assert.That(buildSolution, Does.Contain("Project(\"{2150E333-8FDC-42A3-9474-1A3956D46DE8}\") = \"lib\", \"lib\", \"{02EA681E-C7D8-13C7-8484-4AC65E1B71E8}\""));
            Assert.That(buildSolution, Does.Contain("{B853DC9F-A460-5FB0-B0BE-B9621A48AD0E} = {02EA681E-C7D8-13C7-8484-4AC65E1B71E8}"));
            Assert.That(buildSolution, Does.Contain("{4FBBC607-345D-59EC-6E4B-EABD2D69B381} = {4FBBC607-345D-59EC-6E4B-EABD2D69B381}"));
            Assert.That(buildSolution, Does.Not.Contain("{99999999-9999-9999-9999-999999999999} = {27EBDC92-A52C-4194-B0B6-5DB32EF5C97B}"));
            Assert.That(buildSolution, Does.Not.Contain("{99999999-9999-9999-9999-999999999999} = {99999999-9999-9999-9999-999999999999}"));
            Assert.That(buildSolution, Does.Not.Contain("Unrelated"));
            Assert.That(sourceSolution, Does.Contain("Peritus.ZTL.Core\", \"Lib\\Peritus.ZTL.Core\\Peritus.ZTL.Core.csproj\", \"{B853DC9F-A460-5FB0-B0BE-B9621A48AD0E}\""));
            Assert.That(sourceSolution, Does.Not.Contain("{89109846-08B3-489D-8C54-2AE9F94AE919}"));
        }

        [Test]
        [Category("Integration")]
        public void CreateBuildSolution_Should_Produce_Solution_That_MSBuild_Can_Build_With_CSharp_ProjectReferences()
        {
            var msbuildPath = ResolveMsBuildPath();
            if (msbuildPath == null)
                Assert.Ignore("MSBuild.exe was not found");

            var repoRoot = Path.Combine(_baseDir, "Peritus ZTL");
            var modelProjectDirectory = Path.Combine(repoRoot, "project", "PTSZTL");
            var coreProjectDirectory = Path.Combine(repoRoot, "Lib", "Peritus.ZTL.Core");
            var foProjectDirectory = Path.Combine(repoRoot, "Lib", "Peritus.ZTL.FO");
            Directory.CreateDirectory(modelProjectDirectory);
            Directory.CreateDirectory(coreProjectDirectory);
            Directory.CreateDirectory(foProjectDirectory);

            var modelProjectPath = Path.Combine(modelProjectDirectory, "PTSZTL.rnrproj");
            var coreProjectPath = Path.Combine(coreProjectDirectory, "Peritus.ZTL.Core.csproj");
            var foProjectPath = Path.Combine(foProjectDirectory, "Peritus.ZTL.FO.csproj");
            var solutionPath = Path.Combine(repoRoot, "Peritus ZTL.sln");

            WriteMarkerProject(coreProjectPath, Path.Combine(_baseDir, "BuildMarkers", "Core.txt"), "Core built");
            WriteMarkerProject(foProjectPath, Path.Combine(_baseDir, "BuildMarkers", "FO.txt"), "FO built");
            File.WriteAllText(modelProjectPath,
                $$"""
                <Project DefaultTargets="Build" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
                  <ItemGroup>
                    <ProjectReference Include="..\..\Lib\Peritus.ZTL.Core\Peritus.ZTL.Core.csproj">
                      <Project>{B853DC9F-A460-5FB0-B0BE-B9621A48AD0E}</Project>
                      <Name>Peritus.ZTL.Core</Name>
                      <Private>True</Private>
                    </ProjectReference>
                    <ProjectReference Include="..\..\Lib\Peritus.ZTL.FO\Peritus.ZTL.FO.csproj">
                      <Project>{4FBBC607-345D-59EC-6E4B-EABD2D69B381}</Project>
                      <Name>Peritus.ZTL.FO</Name>
                      <Private>True</Private>
                    </ProjectReference>
                  </ItemGroup>
                  <Target Name="Build">
                    <WriteLinesToFile File="{{Path.Combine(_baseDir, "BuildMarkers", "Model.txt")}}" Lines="Model built" Overwrite="true" />
                  </Target>
                </Project>
                """);

            File.WriteAllText(solutionPath,
                """
                Microsoft Visual Studio Solution File, Format Version 12.00
                # Visual Studio Version 17
                VisualStudioVersion = 17.12.35527.113
                Project("{FC65038C-1B2F-41E1-A629-BED71D161FFF}") = "PTSZTL", "project\PTSZTL\PTSZTL.rnrproj", "{11111111-1111-1111-1111-111111111111}"
                	ProjectSection(ProjectDependencies) = postProject
                		{4FBBC607-345D-59EC-6E4B-EABD2D69B381} = {4FBBC607-345D-59EC-6E4B-EABD2D69B381}
                		{99999999-9999-9999-9999-999999999999} = {99999999-9999-9999-9999-999999999999}
                	EndProjectSection
                EndProject
                Project("{2150E333-8FDC-42A3-9474-1A3956D46DE8}") = "lib", "lib", "{02EA681E-C7D8-13C7-8484-4AC65E1B71E8}"
                EndProject
                Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Peritus.ZTL.Core", "Lib\Peritus.ZTL.Core\Peritus.ZTL.Core.csproj", "{89109846-08B3-489D-8C54-2AE9F94AE919}"
                EndProject
                Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Peritus.ZTL.FO", "Lib\Peritus.ZTL.FO\Peritus.ZTL.FO.csproj", "{4FBBC607-345D-59EC-6E4B-EABD2D69B381}"
                EndProject
                Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Unrelated", "Lib\Unrelated\Unrelated.csproj", "{99999999-9999-9999-9999-999999999999}"
                EndProject
                Global
                	GlobalSection(SolutionConfigurationPlatforms) = preSolution
                		Debug|Any CPU = Debug|Any CPU
                	EndGlobalSection
                	GlobalSection(ProjectConfigurationPlatforms) = postSolution
                		{11111111-1111-1111-1111-111111111111}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
                		{11111111-1111-1111-1111-111111111111}.Debug|Any CPU.Build.0 = Debug|Any CPU
                		{89109846-08B3-489D-8C54-2AE9F94AE919}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
                		{89109846-08B3-489D-8C54-2AE9F94AE919}.Debug|Any CPU.Build.0 = Debug|Any CPU
                		{4FBBC607-345D-59EC-6E4B-EABD2D69B381}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
                		{4FBBC607-345D-59EC-6E4B-EABD2D69B381}.Debug|Any CPU.Build.0 = Debug|Any CPU
                		{99999999-9999-9999-9999-999999999999}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
                		{99999999-9999-9999-9999-999999999999}.Debug|Any CPU.Build.0 = Debug|Any CPU
                	EndGlobalSection
                	GlobalSection(NestedProjects) = preSolution
                		{89109846-08B3-489D-8C54-2AE9F94AE919} = {02EA681E-C7D8-13C7-8484-4AC65E1B71E8}
                		{4FBBC607-345D-59EC-6E4B-EABD2D69B381} = {02EA681E-C7D8-13C7-8484-4AC65E1B71E8}
                		{99999999-9999-9999-9999-999999999999} = {27EBDC92-A52C-4194-B0B6-5DB32EF5C97B}
                	EndGlobalSection
                EndGlobal
                """);

            var service = CreateService();
            var buildSolutionPath = service.CreateBuildSolution(
                new ProfileModel
                {
                    ProfileName = "Peritus ZTL",
                    SolutionFilePath = solutionPath
                },
                new ProfileEnvironmentModel
                {
                    ModelName = "PTSZTL",
                    ModelType = ModelType.Source,
                    ProjectFilePath = modelProjectPath
                });

            var result = RunMsBuild(msbuildPath, buildSolutionPath);

            Assert.That(result.ExitCode, Is.EqualTo(0), result.Output);
            Assert.That(File.Exists(Path.Combine(_baseDir, "BuildMarkers", "Core.txt")), Is.True, result.Output);
            Assert.That(File.Exists(Path.Combine(_baseDir, "BuildMarkers", "FO.txt")), Is.True, result.Output);
            Assert.That(File.Exists(Path.Combine(_baseDir, "BuildMarkers", "Model.txt")), Is.True, result.Output);
        }

        private VisualStudioSolutionService CreateService()
        {
            return new VisualStudioSolutionService(new AppConfig
            {
                DefaultSourceDirectory = Path.Combine(_baseDir, "Source")
            });
        }

        private static void WriteMarkerProject(string projectPath, string markerPath, string markerText)
        {
            File.WriteAllText(projectPath,
                $$"""
                <Project DefaultTargets="Build" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
                  <Target Name="Build">
                    <WriteLinesToFile File="{{markerPath}}" Lines="{{markerText}}" Overwrite="true" />
                  </Target>
                </Project>
                """);
        }

        private static string? ResolveMsBuildPath()
        {
            var candidates = new[]
            {
                @"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe",
                @"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\amd64\MSBuild.exe",
                @"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
                @"C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
                @"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
            };

            return candidates.FirstOrDefault(File.Exists);
        }

        private static (int ExitCode, string Output) RunMsBuild(string msbuildPath, string solutionPath)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = msbuildPath,
                    Arguments = $"\"{solutionPath}\" /t:Build /p:Configuration=Debug /p:Platform=\"Any CPU\" /v:minimal /nologo",
                    WorkingDirectory = Path.GetDirectoryName(solutionPath)!,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();

            return (process.ExitCode, output);
        }
    }
}
