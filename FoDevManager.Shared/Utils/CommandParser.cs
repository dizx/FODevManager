using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FODevManager.Utils
{
    public class CommandParser
    {
        public string ProfileName { get; private set; }
        public string ModelName { get; private set; }
        public string Command { get; private set; }
        public string FilePath { get; private set; }
        public bool IsValid { get; private set; }

        public CommandParser(string[] args)
        {
            ParseArguments(args);
        }

        private void ParseArguments(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Error: Insufficient arguments provided.");
                IsValid = false;
                return;
            }

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-profile":
                        if (args.Length == 2) { Command = args[++i]; break; }
                        if (i + 1 < args.Length) ProfileName = args[++i];
                        break;

                    case "-model":
                        if (i + 1 < args.Length) ModelName = args[++i];
                        break;

                    default:
                        if (Command == null)
                            Command = args[i];
                        else
                            FilePath = args[i];
                        break;
                }
            }

            // Ensure required arguments are present
            IsValid = (!string.IsNullOrEmpty(Command) && Command.Equals("list"))
                || (!string.IsNullOrEmpty(ProfileName) && !string.IsNullOrEmpty(Command));
        }
    }
}
