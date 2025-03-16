using FODevManager.Utils;

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
        public void Should_Fail_When_Profile_Missing()
        {
            string[] args = { "create" };
            var parser = new CommandParser(args);

            Assert.That(parser.IsValid, Is.False);
        }

        [Test]
        public void Should_Fail_When_Command_Missing()
        {
            string[] args = { "-profile", "YM" };
            var parser = new CommandParser(args);

            Assert.That(parser.IsValid, Is.False);
        }

        [TestCase(new string[] { "-profile", "YM", "-model", "PtsTools", "deploy" }, "YM", "PtsTools", "deploy", null)]
        [TestCase(new string[] { "-profile", "YM", "-model", "PtsTools", "add", "C:\\Path\\to\\project.rnrproj" }, "YM", "PtsTools", "add", "C:\\Path\\to\\project.rnrproj")]
        [TestCase(new string[] { "-profile", "DevEnv", "check" }, "DevEnv", null, "check", null)]
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
