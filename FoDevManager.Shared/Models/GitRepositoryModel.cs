using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FODevManager.Models
{
    public class GitRepositoryModel
    {
        public string? RemoteUrl { get; set; }

        // The branch this profile wants when activated.
        public string? PreferredBranch { get; set; }

        // Last observed branch and commit (updated on save or refresh).
        public string? LastKnownBranch { get; set; }
        public string? LastKnownCommit { get; set; }

        public bool AutoCheckoutOnProfileLoad { get; set; } = true;
        public DirtyCheckoutPolicy DirtyPolicy { get; set; } = DirtyCheckoutPolicy.Skip;

        public bool AutoApplyStashAfterCheckout { get; set; } = false;



    }
    public enum DirtyCheckoutPolicy
    {
        Skip = 0,
        AutoStash = 1
    }

}
