using FODevManager.Services;
using System.Reflection;

namespace FODevManager.Tests
{
    [TestFixture]
    public class DeployablePackageServiceTests
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
        public void ResolveCompiledModelName_Should_Use_Xref_Name_When_Package_Root_Is_Versioned()
        {
            var packageRoot = Path.Combine(_baseDir, "BluestarMotus-7.6.18");
            Directory.CreateDirectory(packageRoot);
            File.WriteAllText(Path.Combine(packageRoot, "BluestarMotus.xref"), "");

            var modelName = ResolveCompiledModelName(packageRoot, "BluestarMotus", "7.6.18");

            Assert.That(modelName, Is.EqualTo("BluestarMotus"));
        }

        [Test]
        public void ResolveCompiledModelName_Should_Use_Compiled_Assembly_Name_When_Xref_Is_Missing()
        {
            var packageRoot = Path.Combine(_baseDir, "BluestarMotus-7.6.18");
            var binRoot = Path.Combine(packageRoot, "bin");
            Directory.CreateDirectory(binRoot);
            File.WriteAllText(Path.Combine(binRoot, "Dynamics.AX.BluestarMotus.dll"), "");

            var modelName = ResolveCompiledModelName(packageRoot, "BluestarMotus", "7.6.18");

            Assert.That(modelName, Is.EqualTo("BluestarMotus"));
        }

        private static string ResolveCompiledModelName(string modelFolder, string packageId, string packageVersion)
        {
            var serviceType = typeof(DeployablePackageService);
            var packageReferenceType = serviceType.GetNestedType("PackageReference", BindingFlags.NonPublic);
            Assert.That(packageReferenceType, Is.Not.Null);

            var packageReference = Activator.CreateInstance(packageReferenceType!, packageId, packageVersion);
            var method = serviceType.GetMethod("ResolveCompiledModelName", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            return (string)method!.Invoke(null, [modelFolder, packageReference])!;
        }
    }
}
