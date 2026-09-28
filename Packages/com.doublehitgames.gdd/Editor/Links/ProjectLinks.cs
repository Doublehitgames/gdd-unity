using System;
using System.Collections.Generic;
using System.IO;
using Doublehitgames.Gdd.Editor.Api;
using Doublehitgames.Gdd.Editor.Session;
using UnityEditor;

namespace Doublehitgames.Gdd.Editor.Links
{
    /// <summary>
    /// This project's <see cref="AssetLinks"/>, kept in step with the file: a
    /// pull that brings teammates' links is picked up without a restart.
    /// </summary>
    internal static class ProjectLinks
    {
        static AssetLinks _links;
        static DateTime _stamp;
        static double _checkedAt;

        /// <summary>Raised when links are added, removed or reloaded from the file.</summary>
        public static event Action Changed;

        static string FilePath => Path.Combine(Directory.GetCurrentDirectory(), AssetLinks.RelativePath);

        public static AssetLinks Current
        {
            get
            {
                CheckFile();
                return _links;
            }
        }

        static void CheckFile()
        {
            // Drawn on every Inspector repaint: look at the disk once a second at most.
            var now = EditorApplication.timeSinceStartup;
            if (_links != null && now - _checkedAt < 1) return;
            _checkedAt = now;

            var stamp = File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : DateTime.MinValue;
            if (_links != null && stamp == _stamp) return;
            var reloaded = _links != null;
            _links = AssetLinks.Load(FilePath);
            _stamp = stamp;
            if (reloaded) Changed?.Invoke();
        }

        public static AssetLink ForAsset(string assetPath) => Current.Get(AssetDatabase.AssetPathToGUID(assetPath));

        /// <summary>Whether the linked asset is still in the project.</summary>
        public static bool Exists(AssetLink link)
        {
            var path = AssetDatabase.GUIDToAssetPath(link.guid);
            return !string.IsNullOrEmpty(path) && AssetDatabase.GetMainAssetTypeAtPath(path) != null;
        }

        /// <returns>Whether this created the links file, which then needs committing.</returns>
        public static bool Link(IEnumerable<string> assetPaths, GddSection page)
        {
            var created = !File.Exists(FilePath);
            foreach (var path in assetPaths)
            {
                var guid = AssetDatabase.AssetPathToGUID(path);
                if (!string.IsNullOrEmpty(guid)) Current.Set(guid, path, page.id, page.title);
            }
            Save();
            return created;
        }

        public static void Unlink(IEnumerable<string> guids)
        {
            var removed = false;
            foreach (var guid in guids) removed |= Current.Remove(guid);
            if (removed) Save();
        }

        static void Save()
        {
            // Bring the parts that are only there to be read up to date too.
            foreach (var link in _links.All)
            {
                var path = AssetDatabase.GUIDToAssetPath(link.guid);
                if (!string.IsNullOrEmpty(path)) link.path = path;
                var page = GddSession.Pages.ById(link.pageId);
                if (page != null) link.pageTitle = page.title;
            }
            _links.Save(FilePath);
            _stamp = File.GetLastWriteTimeUtc(FilePath);
            Changed?.Invoke();
        }

        /// <summary>Keeps the paths in the file readable when linked assets move.</summary>
        internal sealed class Moves : AssetPostprocessor
        {
            static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            {
                if (moved.Length == 0) return;
                var any = false;
                foreach (var path in moved)
                {
                    var link = ForAsset(path);
                    if (link == null || link.path == path) continue;
                    link.path = path;
                    any = true;
                }
                if (any) Save();
            }
        }
    }
}
