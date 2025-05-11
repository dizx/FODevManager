using FODevManager.Utils;
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
