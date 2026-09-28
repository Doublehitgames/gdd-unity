using System;
using System.Collections.Generic;
using Doublehitgames.Gdd.Editor.Api;

namespace Doublehitgames.Gdd.Editor.Pages
{
    /// <summary>
    /// Resolves the body of a page reference the way GDD Manager does
    /// (utils/sectionReferences.ts): <c>$[#id]</c> by id, <c>$[Title]</c> by
    /// title, trimmed and case-insensitive. The app stores references as ids on
    /// save so they survive renames, which makes <c>#id</c> the common case.
    /// </summary>
    internal sealed class PageIndex
    {
        readonly Dictionary<string, GddSection> _byId = new Dictionary<string, GddSection>(StringComparer.Ordinal);
        readonly Dictionary<string, GddSection> _byTitle = new Dictionary<string, GddSection>(StringComparer.OrdinalIgnoreCase);

        public PageIndex(IEnumerable<GddSection> sections)
        {
            foreach (var section in sections)
            {
                if (string.IsNullOrEmpty(section?.id)) continue;
                _byId[section.id] = section;
                var title = section.title?.Trim();
                // First one wins, like the app's find().
                if (!string.IsNullOrEmpty(title) && !_byTitle.ContainsKey(title)) _byTitle[title] = section;
            }
        }

        public static readonly PageIndex Empty = new PageIndex(Array.Empty<GddSection>());

        public GddSection ById(string id) => id != null && _byId.TryGetValue(id, out var s) ? s : null;

        /// <param name="reference">What sits between <c>$[</c> and <c>]</c>.</param>
        public GddSection Resolve(string reference)
        {
            var value = reference?.Trim();
            if (string.IsNullOrEmpty(value)) return null;
            if (value[0] == '#') return ById(value.Substring(1).Trim());
            return _byTitle.TryGetValue(value, out var s) ? s : null;
        }
    }
}
