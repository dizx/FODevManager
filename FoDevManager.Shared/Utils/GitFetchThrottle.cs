using FODevManager.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FODevManager.Shared.Utils
{
    public sealed class GitFetchThrottle
    {
        private readonly Dictionary<string, DateTime> _lastFetchUtcByRepo = new(StringComparer.OrdinalIgnoreCase);

        private readonly TimeSpan _fetchInterval = TimeSpan.FromMinutes(15);
        private readonly object _lock = new();

        public bool ShouldFetch(string repositoryRootFolder)
        {
            if (repositoryRootFolder.IsNullOrEmpty())
                return false;

            var now = DateTime.UtcNow;

            lock (_lock)
            {
                if (!_lastFetchUtcByRepo.TryGetValue(repositoryRootFolder, out var lastFetchUtc))
                {
                    _lastFetchUtcByRepo[repositoryRootFolder] = now;
                    return true;
                }

                if (now - lastFetchUtc >= _fetchInterval)
                {
                    _lastFetchUtcByRepo[repositoryRootFolder] = now;
                    return true;
                }

                return false;
            }
        }

        public void Reset(string repositoryRootFolder)
        {
            if (repositoryRootFolder.IsNullOrEmpty())
                return;

            lock (_lock)
            {
                _lastFetchUtcByRepo.Remove(repositoryRootFolder);
            }
        }
    }

}
