using System.IO;
using System.Linq;
using Doublehitgames.Gdd.Editor.Links;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Doublehitgames.Gdd.Editor.Tests
{
    public class AssetLinksTests
    {
        string _path;

        [SetUp]
        public void SetUp() => _path = Path.Combine(Path.GetTempPath(), "gdd-links-" + System.Guid.NewGuid().ToString("N"), "GddLinks.json");

        [TearDown]
        public void TearDown()
        {
            var dir = Path.GetDirectoryName(_path);
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }

        [Test]
        public void AMissingFileMeansNoLinks()
        {
            Assert.AreEqual(0, AssetLinks.Load(_path).Count);
        }

        [Test]
        public void LinksRoundTrip()
        {
            var links = new AssetLinks();
            links.Set("g-chicken", "Assets/Data/Chicken.asset", "p-hen", "Galinha");
            links.Set("g-prefab", "Assets/Prefabs/Chicken.prefab", "p-hen", "Galinha");
            links.Save(_path);

            var loaded = AssetLinks.Load(_path);
            Assert.AreEqual(2, loaded.Count);
            var link = loaded.Get("g-chicken");
            Assert.AreEqual("p-hen", link.pageId);
            Assert.AreEqual("Assets/Data/Chicken.asset", link.path);
            Assert.AreEqual("Galinha", link.pageTitle);
        }

        [Test]
        public void EachLinkIsOneLineInPathOrder()
        {
            var links = new AssetLinks();
            links.Set("g-2", "Assets/Z.asset", "p", "Page");
            links.Set("g-1", "Assets/A.asset", "p", "Page");

            var lines = links.ToJson().Split('\n').Where(l => l.Contains("\"guid\"")).ToList();
            Assert.AreEqual(2, lines.Count);
            StringAssert.Contains("Assets/A.asset", lines[0]);
            StringAssert.Contains("Assets/Z.asset", lines[1]);
        }

        [Test]
        public void TheFileIsValidJsonWhateverThePathHolds()
        {
            var links = new AssetLinks();
            links.Set("g", "Assets/Odd \"name\" \\ here.asset", "p", "Título com \"aspas\"");
            links.Save(_path);

            var link = AssetLinks.Load(_path).Get("g");
            Assert.AreEqual("Assets/Odd \"name\" \\ here.asset", link.path);
            Assert.AreEqual("Título com \"aspas\"", link.pageTitle);
        }

        [Test]
        public void NoLinksStillWritesAReadableFile()
        {
            new AssetLinks().Save(_path);
            Assert.AreEqual(0, AssetLinks.Load(_path).Count);
        }

        [Test]
        public void LinkingAgainMovesTheAssetToTheNewPage()
        {
            var links = new AssetLinks();
            Assert.IsNull(links.Set("g", "Assets/A.asset", "p-1", "One"));
            Assert.IsNull(links.Set("g", "Assets/A.asset", "p-1", "One"), "Same page again is not a move");
            Assert.AreEqual("p-1", links.Set("g", "Assets/A.asset", "p-2", "Two"));

            Assert.AreEqual(1, links.Count);
            Assert.AreEqual(0, links.ForPage("p-1").Count);
            Assert.AreEqual(1, links.ForPage("p-2").Count);
        }

        [Test]
        public void APageListsItsAssetsByPath()
        {
            var links = new AssetLinks();
            links.Set("g-3", "Assets/C.asset", "p", "Page");
            links.Set("g-1", "Assets/A.asset", "p", "Page");
            links.Set("g-2", "Assets/B.asset", "other", "Other");

            CollectionAssert.AreEqual(new[] { "Assets/A.asset", "Assets/C.asset" }, links.ForPage("p").Select(l => l.path).ToArray());
        }

        [Test]
        public void RemoveUnlinks()
        {
            var links = new AssetLinks();
            links.Set("g", "Assets/A.asset", "p", "Page");
            Assert.IsTrue(links.Remove("g"));
            Assert.IsFalse(links.Remove("g"));
            Assert.IsNull(links.Get("g"));
        }

        [Test]
        public void EntriesWithoutGuidOrPageAreSkipped()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            File.WriteAllText(_path, "{ \"links\": [ { \"guid\": \"g\", \"pageId\": \"p\" }, { \"guid\": \"\", \"pageId\": \"p\" }, { \"guid\": \"h\" } ] }");

            var links = AssetLinks.Load(_path);
            Assert.AreEqual(1, links.Count);
            Assert.IsNotNull(links.Get("g"));
        }

        [Test]
        public void ABrokenFileIsIgnoredWithAWarning()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            File.WriteAllText(_path, "{ not json");

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("not valid JSON"));
            Assert.AreEqual(0, AssetLinks.Load(_path).Count);
        }
    }
}
