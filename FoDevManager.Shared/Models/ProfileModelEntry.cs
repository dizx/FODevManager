using FODevManager.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FODevManager.Shared.Models
{
    public sealed record ProfileModelEntry(RepositoryModel? Repository, ProfileEnvironmentModel Model)
    {
        public bool IsRepository => Repository != null;

        public string ModelName => Model.ModelName;

    }
}
