using System.Linq;
using Doublehitgames.Gdd.Editor.Api;
using Doublehitgames.Gdd.Editor.Pages;
using NUnit.Framework;
using UnityEngine;

namespace Doublehitgames.Gdd.Editor.Tests
{
    public class NavigationTests
    {
        [Test]
        public void BackAndForwardRetraceTheVisits()
        {
            var h = new NavigationHistory();
            h.Visit("egg");
            h.Visit("hen");
            h.Visit("barn");

            Assert.AreEqual("hen", h.GoBack());
            Assert.AreEqual("egg", h.GoBack());
            Assert.IsFalse(h.CanGoBack);
            Assert.IsNull(h.GoBack());
            Assert.AreEqual("egg", h.Current);

            Assert.AreEqual("hen", h.GoForward());
            Assert.AreEqual("barn", h.GoForward());
            Assert.IsFalse(h.CanGoForward);
        }

        [Test]
        public void VisitingAfterGoingBackDropsTheForwardTrail()
        {
            var h = new NavigationHistory();
            h.Visit("egg");
            h.Visit("hen");
            h.GoBack();
            h.Visit("kitchen");

            Assert.IsFalse(h.CanGoForward);
            Assert.AreEqual("egg", h.GoBack());
        }

        [Test]
        public void ReopeningTheCurrentPageIsNotAStep()
        {
            var h = new NavigationHistory();
            h.Visit("egg");
            h.Visit("egg");
            Assert.IsFalse(h.CanGoBack);
        }

        [Test]
        public void KeepsOnlyTheLastHundredSteps()
        {
            var h = new NavigationHistory();
            for (var i = 0; i <= NavigationHistory.Limit + 10; i++) h.Visit("p" + i);

            var steps = 0;
            while (h.GoBack() != null) steps++;
            Assert.AreEqual(NavigationHistory.Limit, steps);
        }

        [Test]
        public void PruningDeletedPagesLeavesNoDoubleSteps()
        {
            var h = new NavigationHistory();
            h.Visit("a");
            h.Visit("gone");
            h.Visit("a");
            h.Visit("b");

            h.Prune(id => id != "gone");

            Assert.AreEqual("a", h.GoBack());
            Assert.IsNull(h.GoBack());
        }

        [Test]
        public void SurvivesADomainReload()
        {
            var h = new NavigationHistory();
            h.Visit("egg");
            h.Visit("hen");

            var restored = JsonUtility.FromJson<NavigationHistory>(JsonUtility.ToJson(h));
            Assert.AreEqual("hen", restored.Current);
            Assert.AreEqual("egg", restored.GoBack());
        }

        [Test]
        public void AncestorsRunFromTheTopDown()
        {
            var pages = new PageIndex(new[]
            {
                new GddSection { id = "mech", title = "Mecânicas" },
                new GddSection { id = "animals", parentId = "mech", title = "Animais" },
                new GddSection { id = "hen", parentId = "animals", title = "Galinha" },
                new GddSection { id = "loop-a", parentId = "loop-b", title = "A" },
                new GddSection { id = "loop-b", parentId = "loop-a", title = "B" },
            });

            CollectionAssert.AreEqual(new[] { "Mecânicas", "Animais" }, pages.Ancestors(pages.ById("hen")).Select(s => s.title));
            Assert.IsEmpty(pages.Ancestors(pages.ById("mech")));
            CollectionAssert.AreEqual(new[] { "B" }, pages.Ancestors(pages.ById("loop-a")).Select(s => s.title));
        }
    }
}
