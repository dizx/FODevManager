using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FODevManager.Shared.Models
{
    public sealed class ModelSyncResult
    {
        public bool IsLegacy { get; set; }

        public bool HasChanges => AddedModels.Any() || RemovedModels.Any();

        public IReadOnlyList<string> AddedModels => _addedModels;
        public IReadOnlyList<string> RemovedModels => _removedModels;

        private readonly List<string> _addedModels = new();
        private readonly List<string> _removedModels = new();

        internal void AddAdded(string modelName) => _addedModels.Add(modelName);
        internal void AddRemoved(string modelName) => _removedModels.Add(modelName);
    }

}
