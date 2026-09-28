using Doublehitgames.Gdd.Editor.Api;
using Doublehitgames.Gdd.Editor.Pages;
using NUnit.Framework;

namespace Doublehitgames.Gdd.Editor.Tests
{
    public class MarkdownTextTests
    {
        const string Blue = "#00F";
        const string HenId = "95ef7ef6-9d0a-46f6-99ce-199a94b49019";

        static readonly PageIndex Pages = new PageIndex(new[]
        {
            new GddSection { id = "mill-id", title = "Mill" },
            new GddSection { id = "corn-id", title = "🌽Corn" },
            new GddSection { id = HenId, title = "🐔 Galinha" },
            new GddSection { id = "tag-id", title = "a<b>c" },
        });

        static string Render(string markdown) => MarkdownText.ToRichText(markdown, Pages, Blue);

        static string Link(string id, string title) => $"<link=\"ref:{id}\"><color=#00F><u>{title}</u></color></link>";

        [Test]
        public void ReferencesByTitleBecomeLinksToThePageId()
        {
            Assert.AreEqual($"Ground in the {Link("mill-id", "Mill")}.", Render("Ground in the $[Mill]."));
        }

        [Test]
        public void ReferencesByIdShowTheCurrentTitle()
        {
            // How GDD Manager stores references once a page is saved.
            Assert.AreEqual($"a {Link(HenId, "🐔 Galinha")} tem", Render($"a $[#{HenId}] tem"));
        }

        [Test]
        public void TitlesMatchTrimmedAndIgnoringCase()
        {
            Assert.AreEqual(Link("mill-id", "Mill"), Render("$[ mill ]"));
        }

        [Test]
        public void EmojiArePartOfTheReferencedTitle()
        {
            StringAssert.Contains("<link=\"ref:corn-id\">", Render("Plant $[🌽Corn]"));
            Assert.AreEqual("Plant Corn", Render("Plant $[Corn]"));
        }

        [Test]
        public void UnknownReferencesStayReadable()
        {
            Assert.AreEqual("See Nowhere", Render("See $[Nowhere]"));
            Assert.AreEqual("See <i>[missing page]</i>", Render("See $[#00000000-0000-0000-0000-000000000000]"));
        }

        [Test]
        public void AReferencedTitleIsEscapedToo()
        {
            Assert.AreEqual(Link("tag-id", "a<noparse><</noparse>b>c"), Render("$[#tag-id]"));
        }

        [Test]
        public void HeadingsBulletsAndEmphasis()
        {
            Assert.AreEqual("<size=160%><b>Loop</b></size>", Render("# Loop"));
            Assert.AreEqual("• <b>plant</b> then <i>wait</i>", Render("- **plant** then *wait*"));
            Assert.AreEqual("  • nested", Render("  * nested"));
        }

        [Test]
        public void LiteralTagsInTheTextAreNotInterpreted()
        {
            Assert.AreEqual("a <noparse><</noparse>b> tag", Render("a <b> tag"));
            Assert.AreEqual("<mark=#8080802A><noparse>List<int></noparse></mark>", Render("`List<int>`"));
        }

        [Test]
        public void CodeBlocksAreShownVerbatim()
        {
            Assert.AreEqual("<noparse>**not bold**</noparse>", Render("```\n**not bold**\n```"));
        }

        [Test]
        public void ImagesAndLinks()
        {
            Assert.AreEqual("<i>[image: map]</i>", Render("![map](https://x/map.png)"));
            Assert.AreEqual("<link=\"https://x.com\"><color=#00F><u>site</u></color></link>", Render("[site](https://x.com)"));
        }
    }
}
