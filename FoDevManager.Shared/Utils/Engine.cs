using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FODevManager.Shared.Utils
{
    public interface IEngine
    {
        EnvironmentType EnvironmentType { get; set; }

    }

    public class Engine : IEngine
    {
        public EnvironmentType EnvironmentType { get; set; }

    }

    public enum EnvironmentType
    {
        Console, WinUi
    }
}
