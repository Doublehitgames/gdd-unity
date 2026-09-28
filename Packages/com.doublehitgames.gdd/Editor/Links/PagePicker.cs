using System;
using System.Collections.Generic;
using Doublehitgames.Gdd.Editor.Api;
using Doublehitgames.Gdd.Editor.Pages;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Doublehitgames.Gdd.Editor.Links
{
    /// <summary>
    /// Pick a GDD page: the document's tree to walk, or type to search every
    /// title, as in Unity's Add Component menu.
    /// </summary>
    internal sealed class PagePicker : AdvancedDropdown
    {
        readonly List<PageNode> _roots;
        readonly Action<GddSection> _picked;
        readonly Dictionary<int, GddSection> _byItem = new Dictionary<int, GddSection>();

        public PagePicker(List<PageNode> roots, Action<GddSection> picked) : base(new AdvancedDropdownState())
        {
            _roots = roots;
            _picked = picked;
            minimumSize = new Vector2(300, 360);
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            _byItem.Clear();
            var root = new AdvancedDropdownItem("GDD pages");
            Add(root, _roots);
            return root;
        }

        void Add(AdvancedDropdownItem parent, List<PageNode> nodes)
        {
            foreach (var node in nodes)
            {
                var page = Item(node.Section);
                if (node.Children.Count == 0)
                {
                    parent.AddChild(page);
                    continue;
                }
                // A page with pages under it opens as a submenu, which cannot be
                // picked itself: the page comes first inside its own submenu.
                var group = new AdvancedDropdownItem(TitleOf(node.Section));
                group.AddChild(page);
                Add(group, node.Children);
                parent.AddChild(group);
            }
        }

        AdvancedDropdownItem Item(GddSection section)
        {
            var item = new AdvancedDropdownItem(TitleOf(section)) { id = _byItem.Count + 1 };
            _byItem[item.id] = section;
            return item;
        }

        static string TitleOf(GddSection section) => string.IsNullOrWhiteSpace(section.title) ? "(untitled)" : section.title;

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            if (_byItem.TryGetValue(item.id, out var section)) _picked(section);
        }
    }
}
