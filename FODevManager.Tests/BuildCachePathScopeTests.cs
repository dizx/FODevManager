using FODevManager.Utils;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Reflection;

namespace FODevManager.Tests
{
    [TestFixture]
    [NonParallelizable]
    public class BuildCachePathScopeTests
    {
        private string _root = null!;

        [SetUp]
        public void Setup()
        {
            _root = Path.Combine(Path.GetTempPath(), "FODevManagerTests", Guid.NewGuid().ToString("N"), "BuildPackages");
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void Teardown() => Directory.Delete(Path.GetDirectoryName(_root)!, true);

        [Test]
        public void Allocation_Should_Skip_Logical_Drives_And_Existing_Dos_Mappings()
        {
            var provider = new FakeDevices();
            provider.LogicalDrives.Add("Z:");
            provider.Definitions["Y:"] = [@"\??\missing-target"];
            using var scope = CreateFake(provider);

            Assert.That(scope.RootPath, Is.EqualTo(@"X:\"));
            Assert.That(provider.Calls.Single(), Is.EqualTo((9u, "X:", @"\??\" + _root)));
            Assert.That(scope.MapPath(Path.Combine(_root, "Package", "ref", "net40")),
                Is.EqualTo(@"X:\Package\ref\net40"));
            Assert.That(scope.MapPath(_root), Is.EqualTo(@"X:\"));
            foreach (var outside in new[] { _root + "-other", Path.Combine(_root, "..", "outside") })
                Assert.That(scope.MapPath(outside), Is.EqualTo(outside));

            scope.Dispose();
            scope.Dispose();
            Assert.That(provider.Calls.Last(), Is.EqualTo((15u, "X:", @"\??\" + _root)));
            Assert.That(provider.Calls, Has.Count.EqualTo(2));
            Assert.That(provider.Definitions.Keys, Is.EquivalentTo(new[] { "Y:" }));
            Assert.That(provider.LogicalDrives, Is.EquivalentTo(new[] { "Z:" }));
        }

        [TestCase("success")]
        [TestCase("failure")]
        [TestCase("exception")]
        public void Scope_Should_Clean_Up_After_Build_Exit(string outcome)
        {
            var provider = new FakeDevices();
            bool Build()
            {
                using var scope = CreateFake(provider);
                Assert.That(provider.Definitions, Has.Count.EqualTo(1));
                if (outcome == "exception")
                    throw new InvalidOperationException("compiler failed");
                return outcome == "success";
            }

            if (outcome == "exception")
                Assert.Throws<InvalidOperationException>(() => Build());
            else
                Assert.That(Build(), Is.EqualTo(outcome == "success"));
            Assert.That(provider.Definitions, Is.Empty);
            Assert.That(provider.Calls.Select(call => call.Flags), Is.EqualTo(new[] { 9u, 15u }));
        }

        [Test]
        public void Cleanup_Should_Remove_Only_Its_Exact_Target_When_Letter_Is_Reused()
        {
            var provider = new FakeDevices();
            using var scope = CreateFake(provider);
            provider.Definitions["Z:"].Insert(0, @"\??\someone-else");

            scope.Dispose();

            Assert.That(provider.Definitions["Z:"], Is.EqualTo(new[] { @"\??\someone-else" }));
            Assert.That(provider.Calls.Last(), Is.EqualTo((15u, "Z:", @"\??\" + _root)));
        }

        [Test]
        public void Allocation_Should_Fail_Without_Changing_Any_Occupied_Letter()
        {
            var provider = new FakeDevices();
            for (var letter = 'D'; letter <= 'Z'; letter++)
                provider.LogicalDrives.Add($"{letter}:");

            var exception = Assert.Throws<TargetInvocationException>(() => CreateFake(provider));

            Assert.That(exception!.InnerException, Is.TypeOf<IOException>());
            Assert.That(exception.InnerException!.Message, Does.Contain("DeployablePackages"));
            Assert.That(provider.Calls, Is.Empty);
            Assert.That(provider.LogicalDrives, Has.Count.EqualTo(23));
        }

        [Test]
        public void Allocation_Failure_Should_Release_Mutex_And_Not_Attempt_Removal()
        {
            var provider = new FakeDevices { FailAllocation = true };
            var exception = Assert.Throws<TargetInvocationException>(() => CreateFake(provider));
            Assert.That(exception!.InnerException, Is.TypeOf<Win32Exception>());
            Assert.That(provider.Definitions, Is.Empty);
            Assert.That(provider.Calls.Select(call => call.Flags), Is.EqualTo(new[] { 9u }));

            provider.FailAllocation = false;
            Task.Run(() => { using var scope = CreateFake(provider); }).GetAwaiter().GetResult();
            Assert.That(provider.Definitions, Is.Empty);
        }

        [Test]
        public void Cleanup_Failure_Should_Be_Explicit_And_Allow_Exact_Retry()
        {
            var provider = new FakeDevices();
            using var scope = CreateFake(provider);
            try
            {
                provider.FailRemoval = true;
                Assert.Throws<Win32Exception>(() => scope.Dispose());
                Assert.That(provider.Definitions, Has.Count.EqualTo(1));
            }
            finally
            {
                provider.FailRemoval = false;
                scope.Dispose();
            }

            Assert.That(provider.Definitions, Is.Empty);
            Assert.That(provider.Calls.Skip(1), Is.All.EqualTo((15u, "Z:", @"\??\" + _root)));
        }

        [Test]
        public void Concurrent_Windows_Scopes_Should_Map_Long_Files_And_Remove_Their_Devices()
        {
            if (!OperatingSystem.IsWindows())
            {
                using var scope = BuildCachePathScope.Create(_root);
                Assert.That(scope.MapPath(_root), Is.EqualTo(_root));
                return;
            }

            var relativeFile = Path.Combine(new string('p', 70), new string('m', 70), new string('f', 70) + ".xml");
            var originalFile = Path.Combine(_root, relativeFile);
            Directory.CreateDirectory(Path.GetDirectoryName(originalFile)!);
            File.WriteAllText(originalFile, "long-path metadata");
            Assert.That(originalFile.Length, Is.GreaterThanOrEqualTo(260));
            var scopes = new ConcurrentBag<BuildCachePathScope>();
            var mappedRoots = new List<string>();
            try
            {
                Parallel.For(0, 2, _ => scopes.Add(BuildCachePathScope.Create(_root)));
                mappedRoots.AddRange(scopes.Select(scope => scope.RootPath));
                Assert.That(mappedRoots.Distinct().Count(), Is.EqualTo(2));
                foreach (var scope in scopes)
                {
                    Assert.That(File.ReadAllText(scope.MapPath(originalFile)), Is.EqualTo("long-path metadata"));
                    Assert.That(scope.MapPath(originalFile).Length, Is.LessThan(260));
                }
                File.WriteAllText(scopes.First().MapPath(originalFile), "same file");
                Assert.That(File.ReadAllText(originalFile), Is.EqualTo("same file"));
            }
            finally
            {
                foreach (var scope in scopes)
                    scope.Dispose();
            }

            var isDriveUsed = typeof(BuildCachePathScope).GetMethod("IsDriveUsed", BindingFlags.NonPublic | BindingFlags.Static)!;
            foreach (var mappedRoot in mappedRoots)
                Assert.That((bool)isDriveUsed.Invoke(null, [mappedRoot[..2]])!, Is.False, mappedRoot);
            Assert.That(File.ReadAllText(originalFile), Is.EqualTo("same file"));
        }

        private BuildCachePathScope CreateFake(FakeDevices provider)
        {
            return (BuildCachePathScope)Activator.CreateInstance(typeof(BuildCachePathScope),
                BindingFlags.NonPublic | BindingFlags.Instance, null,
                [_root, new Func<string, bool>(provider.IsDriveUsed), new Action<uint, string, string>(provider.Define)], null)!;
        }

        private sealed class FakeDevices
        {
            public HashSet<string> LogicalDrives { get; } = [];
            public Dictionary<string, List<string>> Definitions { get; } = [];
            public List<(uint Flags, string Device, string Target)> Calls { get; } = [];
            public bool FailAllocation { get; set; }
            public bool FailRemoval { get; set; }

            public bool IsDriveUsed(string device) => LogicalDrives.Contains(device) || Definitions.ContainsKey(device);

            public void Define(uint flags, string device, string target)
            {
                Calls.Add((flags, device, target));
                if (flags == 9)
                {
                    if (FailAllocation)
                        throw new Win32Exception(5);
                    Assert.That(IsDriveUsed(device), Is.False, "Must never replace an occupied drive");
                    Definitions.Add(device, [target]);
                }
                else
                {
                    Assert.That(flags, Is.EqualTo(15u));
                    if (FailRemoval)
                        throw new Win32Exception(5);
                    if (Definitions.TryGetValue(device, out var targets))
                    {
                        targets.Remove(target);
                        if (targets.Count == 0)
                            Definitions.Remove(device);
                    }
                }
            }
        }
    }
}
