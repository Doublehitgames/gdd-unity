using Doublehitgames.Gdd.Editor.Pages;
using NUnit.Framework;

namespace Doublehitgames.Gdd.Editor.Tests
{
    public class MarkdownTextTests
    {
        const string Blue = "#00F";

        static string Render(string markdown) =>
            MarkdownText.ToRichText(markdown, title => title == "🌽Corn" || title == "Mill", Blue);

        [Test]
        public void KnownReferencesBecomeLinks()
        {
            Assert.AreEqual(
                "Ground in the <link=\"ref:Mill\"><color=#00F><u>Mill</u></color></link>.",
                Render("Ground in the $[Mill]."));
        }

        [Test]
        public void EmojiArePartOfTheReferencedTitle()
        {
            StringAssert.Contains("<link=\"ref:🌽Corn\">", Render("Plant $[🌽Corn]"));
        }

        [Test]
        public void UnknownReferencesStayPlainText()
        {
            Assert.AreEqual("See Nowhere", Render("See $[Nowhere]"));
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
