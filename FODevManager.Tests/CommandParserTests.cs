using FODevManager.Utils;
using FODevManager.Messages;
using NUnit.Framework;

namespace FODevManager.Tests
{
    [TestFixture]
    public class CommandParserTests
    {
        [Test]
        public void Should_Parse_Profile_Create_Command()
        {
            string[] args = { "-profile", "YM", "create" };
            var parser = new CommandParser(args);

            Assert.That(parser.IsValid, Is.True);
            Assert.That(parser.ProfileName, Is.EqualTo("YM"));
            Assert.That(parser.ModelName, Is.Null);
            Assert.That(parser.Command, Is.EqualTo("create"));
        }

        [Test]
        public void Should_Parse_Profile_Import_Command_With_FilePath()
        {
            string[] args = { "-profile", "import", "C:\\path\\to\\profile.json" };
            var parser = new CommandParser(args);

            Assert.That(parser.IsValid, Is.True);
            Assert.That(parser.Command, Is.EqualTo("import"));
            Assert.That(parser.FilePath, Is.EqualTo("C:\\path\\to\\profile.json"));
            Assert.That(parser.ProfileName, Is.Null);  // Import doesn't set profile name
            Assert.That(parser.ModelName, Is.Null);
        }


        [Test]
        public void Should_Parse_Profile_List_Command()
        {
            string[] args = { "-profile", "list" };
            var parser = new CommandParser(args);

            Assert.That(parser.IsValid, Is.True);
            Assert.That(parser.Command, Is.EqualTo("list"));
        }

        [TestFixture]
        public class CommandParserPeriTests
        {
            [Test]
            public void Should_Parse_Model_Peri_Command()
            {
                string[] args = { "-profile", "MyProfile", "-model", "MyModel", "peri", "Task1234" };
                var parser = new CommandParser(args);

                Assert.That(parser.IsValid, Is.True);
                Assert.That(parser.ProfileName, Is.EqualTo("MyProfile"));
                Assert.That(parser.ModelName, Is.EqualTo("MyModel"));
                Assert.That(parser.Command, Is.EqualTo("peri"));
                Assert.That(parser.FilePath, Is.EqualTo("Task1234")); // Reused as Task holder
            }
        }


        [Test]
        public void Should_Parse_Model_Add_Command()
        {
            string[] args = { "-profile", "YM", "-model", "PtsTools", "add", "C:\\Path\\to\\project.rnrproj" };
            var parser = new CommandParser(args);

            Assert.That(parser.IsValid, Is.True);
            Assert.That(parser.ProfileName, Is.EqualTo("YM"));
            Assert.That(parser.ModelName, Is.EqualTo("PtsTools"));
            Assert.That(parser.Command, Is.EqualTo("add"));
            Assert.That(parser.FilePath, Is.EqualTo("C:\\Path\\to\\project.rnrproj"));
        }

        [Test]
        public void Should_Parse_Db_Set_Command()
        {
            string[] args = { "-profile", "YM", "db-set", "AxDB_Test" };
            var parser = new CommandParser(args);

            Assert.That(parser.IsValid, Is.True);
            Assert.That(parser.Command, Is.EqualTo("db-set"));
            Assert.That(parser.ProfileName, Is.EqualTo("YM"));
            Assert.That(parser.DatabaseName, Is.EqualTo("AxDB_Test"));
        }

        [Test]
        public void Should_Parse_Simplified_Model_Add_Command()
        {
            string[] args = { "-profile", "MyProfile", "-model", "add", "C:\\source\\mymodule" };
            var parser = new CommandParser(args);

            Assert.That(parser.IsValid, Is.True);
            Assert.That(parser.ProfileName, Is.EqualTo("MyProfile"));
            Assert.That(parser.Command, Is.EqualTo("add"));
            Assert.That(parser.ModelName, Is.Null); // To be resolved in service
            Assert.That(parser.FilePath, Is.EqualTo("C:\\source\\mymodule"));
        }


        [Test]
        public void Should_Parse_Switch_Profile_Command()
        {
            string[] args = { "switch", "-profile", "YM" };
            var parser = new CommandParser(args);

            Assert.That(parser.IsValid, Is.True);
            Assert.That(parser.Command, Is.EqualTo("switch"));
            Assert.That(parser.ProfileName, Is.EqualTo("YM"));
        }

        [Test]
        public void Should_Fail_When_Profile_Missing()
        {
            string[] args = { "create" };
            var parser = new CommandParser(args);

            Assert.That(parser.IsValid, Is.False);
        }

        [Test]
        public void Should_Parse_DeployAll_Command()
        {
            string[] args = { "-profile", "MyProfile", "deploy" };
            var parser = new CommandParser(args);

            Assert.That(parser.IsValid, Is.True);
            Assert.That(parser.Command, Is.EqualTo("deploy"));
        }

        [Test]
        public void Should_Fail_When_Command_Missing()
        {
            string[] args = { "-profile", "YM" };
            var parser = new CommandParser(args);

            Assert.That(parser.IsValid, Is.False);
        }

        private static IEnumerable<TestCaseData> CommandCases()
        {
            foreach (string command in new[] { "create", "delete", "check", "list", "deploy", "undeploy", "open-vs", "git-fetch", "switch", "db-apply", "show", "solution-path", "solution-ensure", "repos" })
                yield return new TestCaseData((object)new[] { "-profile", "MyProfile", command });
            foreach (string command in new[] { "remove", "check", "deploy", "undeploy", "git-status", "git-check", "git-open", "show", "package-build" })
                yield return new TestCaseData((object)new[] { "-profile", "MyProfile", "-model", "MyModel", command });
            foreach (string command in new[] { "add", "peri" })
                yield return new TestCaseData((object)new[] { "-profile", "MyProfile", "-model", "MyModel", command, "Value" });
            foreach (string command in new[] { "export", "db-set" })
                yield return new TestCaseData((object)new[] { "-profile", "MyProfile", command, "Value" });
            yield return new TestCaseData((object)new[] { "import", "Profile.json" });
            yield return new TestCaseData((object)new[] { "list" });
        }

        [TestCaseSource(nameof(CommandCases))]
        public void Should_Accept_All_Supported_Command_Forms(string[] args)
        {
            var parser = CommandParser.Parse(args);
            Assert.That(parser.IsValid, Is.True);
            Assert.That(parser.IsHelpRequested, Is.False);
        }

        [TestCaseSource(nameof(CommandCases))]
        public void Should_Reject_Extra_Arguments_For_Every_Command(string[] args)
        {
            var parser = CommandParser.Parse(args.Concat(new[] { "Unexpected" }).ToArray());
            Assert.That(parser.IsValid, Is.False);
            Assert.That(parser.IsHelpRequested, Is.False);
        }

        [TestCase("create")]
        [TestCase("delete")]
        [TestCase("check")]
        [TestCase("add")]
        [TestCase("remove")]
        [TestCase("deploy")]
        [TestCase("undeploy")]
        [TestCase("git-status")]
        [TestCase("git-open")]
        [TestCase("git-fetch")]
        [TestCase("open-vs")]
        [TestCase("switch")]
        [TestCase("db-set")]
        [TestCase("db-apply")]
        [TestCase("peri")]
        [TestCase("show")]
        [TestCase("export")]
        [TestCase("solution-path")]
        [TestCase("solution-ensure")]
        [TestCase("package-build")]
        [TestCase("repos")]
        public void Should_Require_Profile(string command)
        {
            Assert.That(CommandParser.Parse(new[] { command }).IsValid, Is.False);
        }

        [TestCase("remove")]
        [TestCase("git-status")]
        [TestCase("git-check")]
        [TestCase("git-open")]
        [TestCase("package-build")]
        public void Should_Require_Model(string command)
        {
            Assert.That(CommandParser.Parse(new[] { "-profile", "P", command }).IsValid, Is.False);
        }

        [TestCase((object)new string[] { "import" })]
        [TestCase((object)new string[] { "-profile", "P", "add" })]
        [TestCase((object)new string[] { "-profile", "P", "export" })]
        [TestCase((object)new string[] { "-profile", "P", "db-set" })]
        [TestCase((object)new string[] { "-profile", "P", "-model", "M", "peri" })]
        [TestCase((object)new string[] { "-profile", "P", "peri", "Task" })]
        [TestCase((object)new string[] { "-model", "M", "peri", "Task" })]
        [TestCase((object)new string[] { "-profile" })]
        [TestCase((object)new string[] { "--model" })]
        [TestCase((object)new string[] { "-profile", "-model", "M", "show" })]
        [TestCase((object)new string[] { "-profile", "P", "show", "--model" })]
        [TestCase((object)new string[] { "-profile", "", "show" })]
        [TestCase((object)new string[] { "-profile", "P", "-model", " ", "show" })]
        [TestCase((object)new string[] { "-profile", "P", "export", " " })]
        [TestCase((object)new string[] { "-profile", "P", "unknown" })]
        [TestCase((object)new string[] { "--json", "list" })]
        [TestCase((object)new string[] { "list", "--unknown" })]
        [TestCase((object)new string[] { "list", "list" })]
        [TestCase((object)new string[] { "-profile", "P", "show", "delete" })]
        [TestCase((object)new string[] { "-profile", "P", "--PROFILE", "Other", "show" })]
        [TestCase((object)new string[] { "-profile", "P", "-model", "M", "--MODEL", "M", "show" })]
        [TestCase((object)new string[] { "-profile", "list", "--profile", "P" })]
        [TestCase((object)new string[] { "-profile", "P", "-model", "add", "Path", "--model", "M" })]
        [TestCase((object)new string[] { "-profile", "P", "import", "Profile.json" })]
        [TestCase((object)new string[] { "-profile", "P", "-model", "M", "export", "Out.json" })]
        [TestCase((object)new string[] { "-profile", "P", "-model", "M", "solution-path" })]
        [TestCase((object)new string[] { "-profile", "P", "-model", "M", "solution-ensure" })]
        [TestCase((object)new string[] { "-profile", "P", "-model", "M", "repos" })]
        [TestCase((object)new string[] { "-profile", "P", "-model", "M", "list" })]
        [TestCase((object)new string[] { "-profile", "P", "-model", "M", "package-build", "--push" })]
        public void Should_Reject_Invalid_Syntax(string[] args)
        {
            var parser = CommandParser.Parse(args);
            Assert.That(parser.IsValid, Is.False);
            Assert.That(parser.IsHelpRequested, Is.False);
        }

        [TestCase("-profile", "-model")]
        [TestCase("--PROFILE", "--MODEL")]
        public void Should_Normalize_Commands_And_Options_Without_Changing_Values(string profileOption, string modelOption)
        {
            var parser = CommandParser.Parse(new[] { "PeRi", modelOption, "MyModel", "Task/AbC", profileOption, "MyProfile" });
            Assert.That(parser.IsValid, Is.True);
            Assert.That(parser.Command, Is.EqualTo("peri"));
            Assert.That(parser.ProfileName, Is.EqualTo("MyProfile"));
            Assert.That(parser.ModelName, Is.EqualTo("MyModel"));
            Assert.That(parser.FilePath, Is.EqualTo("Task/AbC"));
        }

        [Test]
        public void Should_Canonicalize_Git_Check_Alias()
        {
            var parser = CommandParser.Parse(new[] { "GIT-CHECK", "--profile", "P", "--model", "M" });
            Assert.That(parser.IsValid, Is.True);
            Assert.That(parser.Command, Is.EqualTo("git-status"));
        }

        [TestCase(new[] { "--PROFILE", "LIST" }, "list")]
        [TestCase(new[] { "--PROFILE", "IMPORT", "MixedCase.json" }, "import")]
        [TestCase(new[] { "--PROFILE", "P", "--MODEL", "ADD", "MixedCasePath" }, "add")]
        public void Should_Support_Legacy_Shorthand_With_Aliases(string[] args, string command)
        {
            var parser = CommandParser.Parse(args);
            Assert.That(parser.IsValid, Is.True);
            Assert.That(parser.Command, Is.EqualTo(command));
        }

        [TestCase("help")]
        [TestCase("HELP")]
        [TestCase("-h")]
        [TestCase("--help")]
        [TestCase("?")]
        public void Should_Distinguish_Help_From_Executable_Commands(string help)
        {
            foreach (var args in new[] { new[] { help }, new[] { help, "package-build" }, new[] { "package-build", help }, new[] { "--profile", "P", "show", help } })
            {
                var parser = CommandParser.Parse(args);
                Assert.That(parser.IsHelpRequested, Is.True);
                Assert.That(parser.IsValid, Is.False);
            }
        }

        [Test]
        public void Should_Show_General_Help_For_No_Arguments()
        {
            var parser = CommandParser.Parse(Array.Empty<string>());
            Assert.That(parser.IsHelpRequested, Is.True);
            Assert.That(parser.IsValid, Is.False);
        }

        [TestCase((object)new string[] { "help", "unknown" })]
        [TestCase((object)new string[] { "help", "list", "extra" })]
        [TestCase((object)new string[] { "help", "--json" })]
        [TestCase((object)new string[] { "help", "--help" })]
        public void Should_Not_Treat_Malformed_Help_As_Success(string[] args)
        {
            var parser = CommandParser.Parse(args);
            Assert.That(parser.IsHelpRequested, Is.False);
            Assert.That(parser.IsValid, Is.False);
        }

        [Test]
        public void Should_Preserve_Output_And_Database_Values()
        {
            var export = CommandParser.Parse(new[] { "EXPORT", "C:\\Output Folder\\MixedCase.json", "--profile", "P" });
            Assert.That(export.IsValid, Is.True);
            Assert.That(export.FilePath, Is.EqualTo("C:\\Output Folder\\MixedCase.json"));
            var database = CommandParser.Parse(new[] { "DB-SET", "AxDB_MixedCase", "--profile", "P" });
            Assert.That(database.IsValid, Is.True);
            Assert.That(database.DatabaseName, Is.EqualTo("AxDB_MixedCase"));
            Assert.That(database.FilePath, Is.Null);
        }

        [Test]
        public void Should_Not_Log_Raw_Arguments()
        {
            var messages = new List<string>();
            using var subscription = MessageBus.Subscribe(message => messages.Add(message.Content));
            CommandParser.Parse(new[] { "import", "https://user:SecretToken@example.com/Profile.json" });
            CommandParser.Parse(new[] { "--SecretToken", "list" });
            CommandParser.Parse(new[] { "SecretToken" });
            Assert.That(string.Join("\n", messages), Does.Not.Contain("SecretToken"));
        }

        [TestCase("check", "clone")]
        [TestCase("switch", "stash")]
        [TestCase("db-set", "active")]
        [TestCase("peri", "does not check out")]
        [TestCase("package-build", "never pushes or publishes")]
        [TestCase("export", "refuses to overwrite")]
        [TestCase("git-check", "git-status")]
        public void Should_Document_Command_Side_Effects(string command, string expected)
        {
            var messages = new List<string>();
            using var subscription = MessageBus.Subscribe(message => messages.Add(message.Content));
            var parser = CommandParser.Parse(new[] { "help", command });
            Assert.That(parser.IsHelpRequested, Is.True);
            Assert.That(string.Join("\n", messages), Does.Contain(expected));
        }

        [TestCase(new string[] { "-profile", "YM", "-model", "PtsTools", "add", "C:\\Path\\to\\project.rnrproj" }, "YM", "PtsTools", "add", "C:\\Path\\to\\project.rnrproj")]
        public void Should_Parse_Valid_Commands(string[] args, string expectedProfile, string expectedModel, string expectedCommand, string expectedFilePath)
        {
            var parser = new CommandParser(args);

            Assert.That(parser.IsValid, Is.True);
            Assert.That(parser.ProfileName, Is.EqualTo(expectedProfile));
            Assert.That(parser.ModelName, Is.EqualTo(expectedModel));
            Assert.That(parser.Command, Is.EqualTo(expectedCommand));
            Assert.That(parser.FilePath, Is.EqualTo(expectedFilePath));
        }
    }
}
