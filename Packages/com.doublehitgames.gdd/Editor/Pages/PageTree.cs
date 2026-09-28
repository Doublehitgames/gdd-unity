using System;
using System.Collections.Generic;
using System.Linq;
using Doublehitgames.Gdd.Editor.Api;

namespace Doublehitgames.Gdd.Editor.Pages
{
    internal sealed class PageNode
    {
        public readonly GddSection Section;
        public readonly List<PageNode> Children = new List<PageNode>();

        public PageNode(GddSection section) => Section = section;
    }

    /// <summary>
    /// The API lists pages flat, each pointing at its parent; the window shows
    /// them as the tree the document is.
    /// </summary>
    internal static class PageTree
    {
        public static List<PageNode> Build(IEnumerable<GddSection> sections)
        {
            var nodes = new Dictionary<string, PageNode>();
            foreach (var section in sections)
                if (!string.IsNullOrEmpty(section?.id)) nodes[section.id] = new PageNode(section);

            var roots = new List<PageNode>();
            foreach (var node in nodes.Values)
            {
                var parentId = node.Section.parentId;
                // A parent that is not in the list (deleted, or out of reach) must
                // not hide its children: they surface at the top instead.
                if (!string.IsNullOrEmpty(parentId) && parentId != node.Section.id && nodes.TryGetValue(parentId, out var parent))
                    parent.Children.Add(node);
                else
                    roots.Add(node);
            }

            // A parent loop (A under B under A) leaves pages no root reaches.
            // Break it by lifting one page of each loop to the top.
            var reached = new HashSet<PageNode>();
            foreach (var root in roots) Mark(root, reached);
            foreach (var node in nodes.Values.OrderBy(n => n.Section.order).ThenBy(n => n.Section.id, StringComparer.Ordinal))
            {
                if (reached.Contains(node)) continue;
                nodes[node.Section.parentId].Children.Remove(node);
                roots.Add(node);
                Mark(node, reached);
            }

            Sort(roots);
            return roots;
        }

        /// <summary>
        /// The pages whose title or text contains <paramref name="term"/>, plus the
        /// pages above them so each match still shows where it sits.
        /// </summary>
        public static List<PageNode> Filter(IEnumerable<PageNode> roots, string term)
        {
            var result = new List<PageNode>();
            if (string.IsNullOrWhiteSpace(term))
            {
                result.AddRange(roots);
                return result;
            }
            term = term.Trim();
            foreach (var node in roots)
            {
                var children = Filter(node.Children, term);
                if (children.Count == 0 && !Matches(node.Section, term)) continue;
                var copy = new PageNode(node.Section);
                copy.Children.AddRange(children);
                result.Add(copy);
            }
            return result;
        }

        static bool Matches(GddSection section, string term) =>
            (section.title ?? "").IndexOf(term, StringComparison.CurrentCultureIgnoreCase) >= 0
            || (section.content ?? "").IndexOf(term, StringComparison.CurrentCultureIgnoreCase) >= 0;

        public static IEnumerable<PageNode> Flatten(IEnumerable<PageNode> roots)
        {
            foreach (var node in roots)
            {
                yield return node;
                foreach (var child in Flatten(node.Children)) yield return child;
            }
        }

        static void Mark(PageNode node, HashSet<PageNode> reached)
        {
            var stack = new Stack<PageNode>();
            stack.Push(node);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (!reached.Add(current)) continue;
                foreach (var child in current.Children) stack.Push(child);
            }
        }

        static void Sort(List<PageNode> nodes)
        {
            nodes.Sort((a, b) =>
            {
                var byOrder = a.Section.order.CompareTo(b.Section.order);
                return byOrder != 0 ? byOrder : string.Compare(a.Section.title, b.Section.title, StringComparison.CurrentCultureIgnoreCase);
            });
            foreach (var node in nodes) Sort(node.Children);
        }
    }
}
