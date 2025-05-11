using FODevManager.Utils;
using NUnit.Framework;
using System;
using System.IO;

namespace FODevManager.Tests
{
    [TestFixture]
    public  class FileHelperTests
    {
        private string _baseDir;

        [SetUp]
        public void Setup()
        {
            _baseDir = Path.Combine(Path.GetTempPath(), "FODevManagerTest");
            Directory.CreateDirectory(_baseDir);
        }

        [TearDown]
        public void Teardown()
        {
            if (Directory.Exists(_baseDir))
                Directory.Delete(_baseDir, true);
        }

        private string CreateDummyFile(params string[] pathSegments)
        {
            var fullPath = Path.Combine(_baseDir, Path.Combine(pathSegments));
            var dir = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(fullPath, "// dummy content");
            return fullPath;
        }



        private string CreateProjectFile(params string[] pathSegments)
        {
            var fullPath = Path.Combine(_baseDir, Path.Combine(pathSegments));
            var dir = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(fullPath, "// dummy rnrproj file");
            return fullPath;
        }

        [Test]
        public void Should_Find_File_In_project_modelName_ModelNameDotRnrproj()
        {
            var model = "TestModel";
            var path = CreateProjectFile("project", model, $"{model}.rnrproj");

            var result = FileHelper.GetProjectFilePath(model, Path.Combine(_baseDir, "project"));
            Assert.That(result, Is.EqualTo(path));
        }

        [Test]
        public void Should_Find_File_In_modelName_ModelNameDotRnrproj()
        {
            var model = "AnotherModel";
            var path = CreateProjectFile(model, $"{model}.rnrproj");

            var result = FileHelper.GetProjectFilePath(model, _baseDir);
            Assert.That(result, Is.EqualTo(path));
        }

        [Test]
        public void Should_Throw_When_File_Not_Found()
        {
            var model = "MissingModel";
            var ex = Assert.Throws<FileNotFoundException>(() =>
            {
                FileHelper.GetProjectFilePath(model, _baseDir);
            });

            Assert.That(ex.Message, Does.Contain("Can't find project file"));
        }

        [Test]
        public void PathContainsDirectory_Should_Return_False_If_Not_Present()
        {
            var path = Path.Combine("C:/foo/bar", "model");
            Assert.That(FileHelper.PathContainsDirectory(path, "project"), Is.False);
        }

        [Test]
        public void GetModelRootFolder_Should_Return_Top_Level()
        {
            var model = "TestModel";
            var fullPath = CreateDummyFile("source", "project", model, $"{model}.rnrproj");

            var root = FileHelper.GetModelRootFolder(fullPath);
            Assert.That(root, Does.Contain("source"));
        }

        [Test]
        public void GetProjectFilePath_Should_Find_Various_Layouts()
        {
            var model = "DemoModel";
            var path = CreateDummyFile("project", model, $"{model}.rnrproj");

            var full = FileHelper.GetProjectFilePath(model, Path.Combine(_baseDir, "project"));
            Assert.That(full, Is.EqualTo(path));
        }

        [Test]
        public void GetMetadataFolder_Should_Locate_Metadata()
        {
            var model = "MyModel";
            var metaPath = Path.Combine(_baseDir, "Metadata", model);
            Directory.CreateDirectory(metaPath);

            var result = FileHelper.GetMetadataFolder(model, _baseDir);
            Assert.That(result, Is.EqualTo(metaPath));
        }

        [Test]
        public void GetMetadataFolder_Should_Throw_If_Not_Found()
        {
            var ex = Assert.Throws<DirectoryNotFoundException>(() =>
            {
                FileHelper.GetMetadataFolder("X", _baseDir);
            });

            Assert.That(ex.Message, Does.Contain("Can't find metadata folder in path"));
        }

    }
}
