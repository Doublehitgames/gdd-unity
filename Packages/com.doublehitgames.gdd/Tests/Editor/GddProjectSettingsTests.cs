using System.IO;
using Doublehitgames.Gdd.Editor.Api;
using Doublehitgames.Gdd.Editor.Settings;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Doublehitgames.Gdd.Editor.Tests
{
    public class GddProjectSettingsTests
    {
        string _path;

        [SetUp]
        public void SetUp() => _path = Path.Combine(Path.GetTempPath(), "gdd-settings-" + System.Guid.NewGuid().ToString("N"), "GddManager.json");

        [TearDown]
        public void TearDown()
        {
            var dir = Path.GetDirectoryName(_path);
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }

        [Test]
        public void AMissingFileMeansNotLinked()
        {
            var settings = GddProjectSettings.Load(_path);
            Assert.IsFalse(settings.IsLinked);
            Assert.AreEqual(GddServer.DefaultUrl, settings.Server);
        }

        [Test]
        public void TheLinkRoundTrips()
        {
            new GddProjectSettings { serverUrl = "https://gdd.studio.internal/", projectId = "p-1", projectTitle = "Harvest" }.Save(_path);

            var settings = GddProjectSettings.Load(_path);
            Assert.IsTrue(settings.IsLinked);
            Assert.AreEqual("p-1", settings.projectId);
            Assert.AreEqual("https://gdd.studio.internal", settings.Server);
        }

        [Test]
        public void TheFileHoldsNoCredentials()
        {
            new GddProjectSettings { projectId = "p-1" }.Save(_path);
            var json = File.ReadAllText(_path);
            StringAssert.DoesNotContain("token", json.ToLowerInvariant());
            StringAssert.DoesNotContain("key", json.ToLowerInvariant());
        }

        [Test]
        public void ABrokenFileIsIgnoredWithAWarning()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            File.WriteAllText(_path, "{ not json");

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("not valid JSON"));
            Assert.IsFalse(GddProjectSettings.Load(_path).IsLinked);
        }
    }
}
