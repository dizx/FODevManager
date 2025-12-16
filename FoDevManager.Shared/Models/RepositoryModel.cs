using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FODevManager.Models
{
    public class RepositoryModel
    {
        public string RepoId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string RepoRootFolder { get; set; } = "";     // where .git lives (your current ModelRootFolder)
        public string? GitUrl { get; set; }                  // origin URL
        public string? PreferredBranch { get; set; }          // profile wants this branch
        public string? LastKnownBranch { get; set; }          // saved/observed branch
        public string? LastKnownCommit { get; set; }          // optional

        public bool AutoCheckoutOnProfileLoad { get; set; } = true;
        public bool AutoStashOnDirtyCheckout { get; set; } = false;

        // Models that belong to this repo
        public List<ProfileEnvironmentModel> Models { get; set; } = new();

        // Optional: repo-level PeriTask (you already have repo-level UI actions)
        public string PeriTask { get; set; } = "";
        public string PeriTaskComment { get; set; } = "";
    }

}
