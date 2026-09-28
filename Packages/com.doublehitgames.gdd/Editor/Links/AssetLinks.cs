using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Doublehitgames.Gdd.Editor.Links
{
    [Serializable]
    internal sealed class AssetLink
    {
        /// <summary>Only so the file reads well in a diff; the guid is what counts.</summary>
        public string path;
        public string guid;
        public string pageId;
        /// <summary>Like <see cref="path"/>, for the diff. Also what shows while the pages are not loaded.</summary>
        public string pageTitle;
    }

    /// <summary>
    /// Which asset describes which GDD page, keyed by the asset's guid so moving
    /// or renaming the asset keeps the link. Saved in one file in
    /// ProjectSettings/, committed with the project: the link belongs to the
    /// team, and the assets themselves are never touched (no .meta edit, no
    /// reimport, and packages' read-only assets can be linked too).
    /// </summary>
    internal sealed class AssetLinks
    {
        internal const string RelativePath = "ProjectSettings/GddLinks.json";

        [Serializable]
        sealed class FileShape
        {
            public AssetLink[] links;
        }

        readonly Dictionary<string, AssetLink> _byGuid = new Dictionary<string, AssetLink>(StringComparer.Ordinal);

        public int Count => _byGuid.Count;
        public IEnumerable<AssetLink> All => _byGuid.Values;

        public AssetLink Get(string guid) => guid != null && _byGuid.TryGetValue(guid, out var link) ? link : null;

        /// <summary>The assets linked to a page, by path.</summary>
        public List<AssetLink> ForPage(string pageId) =>
            _byGuid.Values.Where(l => l.pageId == pageId).OrderBy(l => l.path, StringComparer.Ordinal).ToList();

        /// <returns>The page the asset was linked to before, if another one.</returns>
        public string Set(string guid, string path, string pageId, string pageTitle)
        {
            if (string.IsNullOrEmpty(guid)) throw new ArgumentException("An asset guid is required.", nameof(guid));
            if (string.IsNullOrEmpty(pageId)) throw new ArgumentException("A page id is required.", nameof(pageId));
            var previous = Get(guid)?.pageId;
            _byGuid[guid] = new AssetLink { guid = guid, path = path, pageId = pageId, pageTitle = pageTitle };
            return previous != pageId ? previous : null;
        }

        public bool Remove(string guid) => guid != null && _byGuid.Remove(guid);

        // ── File ────────────────────────────────────────────────────────────

        public static AssetLinks Load(string file)
        {
            var links = new AssetLinks();
            if (!File.Exists(file)) return links;
            try
            {
                var shape = JsonUtility.FromJson<FileShape>(File.ReadAllText(file));
                foreach (var link in shape?.links ?? Array.Empty<AssetLink>())
                    if (!string.IsNullOrEmpty(link?.guid) && !string.IsNullOrEmpty(link.pageId))
                        links._byGuid[link.guid] = link;
            }
            catch (ArgumentException e)
            {
                Debug.LogWarning($"[GDD Manager] {RelativePath} is not valid JSON and was ignored: {e.Message}");
            }
            return links;
        }

        public void Save(string file)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file, ToJson());
        }

        /// <summary>
        /// One link per line, in path order: two people linking different assets
        /// touch different lines, and a review reads as a list of assets.
        /// </summary>
        internal string ToJson()
        {
            var sb = new StringBuilder("{\n  \"links\": [");
            var ordered = _byGuid.Values
                .OrderBy(l => l.path ?? "", StringComparer.Ordinal)
                .ThenBy(l => l.guid, StringComparer.Ordinal)
                .ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                var l = ordered[i];
                sb.Append(i == 0 ? "\n" : ",\n");
                sb.Append("    { \"path\": ").Append(Quote(l.path))
                    .Append(", \"guid\": ").Append(Quote(l.guid))
                    .Append(", \"pageId\": ").Append(Quote(l.pageId))
                    .Append(", \"pageTitle\": ").Append(Quote(l.pageTitle))
                    .Append(" }");
            }
            sb.Append(ordered.Count > 0 ? "\n  ]\n}\n" : "]\n}\n");
            return sb.ToString();
        }

        static string Quote(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (var c in s ?? "")
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }
    }
}
