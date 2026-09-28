using System.Linq;
using Doublehitgames.Gdd.Editor.Api;
using Doublehitgames.Gdd.Editor.Pages;
using NUnit.Framework;

namespace Doublehitgames.Gdd.Editor.Tests
{
    public class PageTreeTests
    {
        static GddSection Page(string id, string parent, int order, string title = null, string content = null) =>
            new GddSection { id = id, parentId = parent, order = order, title = title ?? id, content = content };

        static string Shape(System.Collections.Generic.IEnumerable<PageNode> nodes) =>
            string.Join(" ", nodes.Select(n => n.Children.Count == 0 ? n.Section.id : $"{n.Section.id}({Shape(n.Children)})"));

        [Test]
        public void NestsPagesUnderTheirParentsInOrder()
        {
            var roots = PageTree.Build(new[]
            {
                Page("crops", null, 1),
                Page("wheat", "crops", 1),
                Page("corn", "crops", 0),
                Page("loop", null, 0),
            });

            Assert.AreEqual("loop crops(corn wheat)", Shape(roots));
        }

        [Test]
        public void TiesInOrderFallBackToTitle()
        {
            var roots = PageTree.Build(new[] { Page("b", null, 0, "Beta"), Page("a", null, 0, "alpha") });
            Assert.AreEqual("a b", Shape(roots));
        }

        [Test]
        public void APageWhoseParentIsMissingSurfacesAtTheTop()
        {
            var roots = PageTree.Build(new[] { Page("a", null, 0), Page("orphan", "gone", 1) });
            Assert.AreEqual("a orphan", Shape(roots));
        }

        [Test]
        public void AParentLoopDoesNotHidePages()
        {
            var roots = PageTree.Build(new[]
            {
                Page("root", null, 0),
                Page("x", "y", 1),
                Page("y", "x", 2),
                Page("self", "self", 3),
            });

            Assert.AreEqual(4, PageTree.Flatten(roots).Count());
            Assert.AreEqual("root x(y) self", Shape(roots));
        }

        [Test]
        public void FilterKeepsTheAncestorsOfAMatch()
        {
            var roots = PageTree.Build(new[]
            {
                Page("economy", null, 0, "Economy"),
                Page("shop", "economy", 0, "Shop", "Sells $[Seeds]"),
                Page("coins", "economy", 1, "Coins"),
                Page("loop", null, 1, "Core loop"),
            });

            Assert.AreEqual("economy(shop)", Shape(PageTree.Filter(roots, "seeds")));
            Assert.AreEqual("loop", Shape(PageTree.Filter(roots, " core ")));
            Assert.AreEqual(Shape(roots), Shape(PageTree.Filter(roots, "")));
            // Filtering copies: the full tree is untouched.
            Assert.AreEqual("economy(shop coins) loop", Shape(roots));
        }
    }
}
