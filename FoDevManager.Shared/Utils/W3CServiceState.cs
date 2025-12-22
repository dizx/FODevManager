using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FODevManager.Shared.Utils
{
    namespace FODevManager.WinUI.Services
    {
        public sealed class W3cServiceState
        {
            public bool IsRunning { get; set; }

            public bool InOperation { get; set; }
            public object SyncRoot { get; } = new object();
        }
    }

}
