using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace FODevManager.Utils
{
    public sealed class BuildCachePathScope : IDisposable
    {
        private const uint RawTargetPath = 0x1;
        private const uint RemoveDefinition = 0x2;
        private const uint ExactMatchOnRemove = 0x4;
        private const uint NoBroadcastSystem = 0x8;
        private const string AllocationAdvice = "Free an unused drive letter (D-Z), or use a shorter DeployablePackages location for a direct compiler build";
        private readonly string _cacheRoot;
        private readonly string _target;
        private readonly Action<uint, string, string>? _defineDevice;
        private string? _deviceName;

        public string RootPath => _deviceName == null ? _cacheRoot : _deviceName + @"\";

        public static BuildCachePathScope Create(string cacheRoot)
        {
            return OperatingSystem.IsWindows()
                ? new BuildCachePathScope(cacheRoot, IsDriveUsed, DefineDevice)
                : new BuildCachePathScope(cacheRoot, null, null);
        }

        private BuildCachePathScope(string cacheRoot, Func<string, bool>? isDriveUsed, Action<uint, string, string>? defineDevice)
        {
            _cacheRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(cacheRoot));
            if (!Directory.Exists(_cacheRoot))
                throw new DirectoryNotFoundException($"Build cache does not exist: '{_cacheRoot}'");
            _target = _cacheRoot.StartsWith(@"\\", StringComparison.Ordinal)
                ? @"\??\UNC\" + _cacheRoot[2..]
                : @"\??\" + _cacheRoot;
            _defineDevice = defineDevice;
            if (isDriveUsed == null || defineDevice == null)
                return;

            WithDriveLock(() =>
            {
                for (var letter = 'Z'; letter >= 'D'; letter--)
                {
                    var deviceName = $"{letter}:";
                    if (isDriveUsed(deviceName))
                        continue;

                    defineDevice(RawTargetPath | NoBroadcastSystem, deviceName, _target);
                    _deviceName = deviceName;
                    return;
                }

                throw new IOException($"No unused drive letter is available for the temporary build cache mapping. {AllocationAdvice}");
            });
        }

        public string MapPath(string path)
        {
            if (_deviceName == null)
                return path;

            var relativePath = Path.GetRelativePath(_cacheRoot, Path.GetFullPath(path));
            if (Path.IsPathRooted(relativePath) || relativePath == ".."
                || relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                return path;

            return relativePath == "." ? RootPath : Path.Combine(RootPath, relativePath);
        }

        public void Dispose()
        {
            if (_defineDevice == null)
                return;

            WithDriveLock(() =>
            {
                if (_deviceName == null)
                    return;

                // Remove only our exact target, even if another tool has since reused this letter
                _defineDevice(RemoveDefinition | ExactMatchOnRemove | RawTargetPath | NoBroadcastSystem, _deviceName, _target);
                _deviceName = null;
            });
        }

        private static void WithDriveLock(Action action)
        {
            using var mutex = new Mutex(false, @"Local\FODevManager.BuildCachePathScope");
            try
            {
                if (!mutex.WaitOne(TimeSpan.FromSeconds(30)))
                    throw new TimeoutException("Timed out coordinating temporary build cache drives");
            }
            catch (AbandonedMutexException)
            {
                // Ownership is granted when a previous process exited without releasing the mutex
            }

            try
            {
                action();
            }
            finally
            {
                mutex.ReleaseMutex();
            }
        }

        private static bool IsDriveUsed(string deviceName)
        {
            if (Environment.GetLogicalDrives().Any(drive => drive.StartsWith(deviceName, StringComparison.OrdinalIgnoreCase)))
                return true;

            // Only existence matters, including mappings whose target is currently inaccessible
            if (QueryDosDevice(deviceName, new StringBuilder(1), 1) != 0)
                return true;
            var error = Marshal.GetLastWin32Error();
            if (error == 122)
                return true;
            if (error == 2)
                return false;
            throw new Win32Exception(error, $"Could not check whether drive '{deviceName}' is free. {AllocationAdvice}");
        }

        private static void DefineDevice(uint flags, string deviceName, string target)
        {
            if (DefineDosDevice(flags, deviceName, target))
                return;
            var error = Marshal.GetLastWin32Error();
            if ((flags & RemoveDefinition) != 0 && error == 2)
                return;
            throw new Win32Exception(error, (flags & RemoveDefinition) != 0
                ? $"Could not remove temporary drive '{deviceName}' targeting '{target}'. The mapping may remain until logoff"
                : $"Could not allocate temporary drive '{deviceName}'. {AllocationAdvice}");
        }

        [DllImport("kernel32.dll", EntryPoint = "QueryDosDeviceW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern uint QueryDosDevice(string deviceName, StringBuilder targetPath, uint maxLength);

        [DllImport("kernel32.dll", EntryPoint = "DefineDosDeviceW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DefineDosDevice(uint flags, string deviceName, string targetPath);
    }
}
