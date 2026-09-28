using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Doublehitgames.Gdd.Editor.Api;
using Doublehitgames.Gdd.Editor.Auth;
using Doublehitgames.Gdd.Editor.Links;
using Doublehitgames.Gdd.Editor.Pages;
using Doublehitgames.Gdd.Editor.Session;
using Doublehitgames.Gdd.Editor.Window;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;

namespace Doublehitgames.Gdd.Editor.Inspector
{
    /// <summary>
    /// The GDD page an asset is linked to, at the top of that asset's Inspector:
    /// where it sits in the document, its title, and its description folded
    /// under it. For an asset with no link, a button to link one.
    /// </summary>
    [InitializeOnLoad]
    internal static class GddInspectorHeader
    {
        const string DescriptionOpenPref = "GddManager.Inspector.DescriptionOpen";
        // About a screenful of Inspector; the rest is one click away in the window.
        const int ExcerptChars = 700;
        // The badge column, so what sits under the title lines up with it.
        const float BadgeWidth = 22;
        static readonly GUIContent DescriptionLabel = new GUIContent("Description");

        static readonly Dictionary<string, Rendered> RenderedPages = new Dictionary<string, Rendered>();
        static GUIStyle _trail, _title, _body, _note;
        static bool _pickerPending;

        sealed class Rendered
        {
            public string Content;
            public string Text;
            public bool Truncated;
        }

        static GddInspectorHeader()
        {
            UnityEditor.Editor.finishedDefaultHeaderGUI += OnHeader;
            GddSession.PagesChanged += Repaint;
            ProjectLinks.Changed += Repaint;
        }

        static void Repaint()
        {
            RenderedPages.Clear();
            InternalEditorUtility.RepaintAllViews();
        }

        static void OnHeader(UnityEditor.Editor editor)
        {
            if (!GddSession.Settings.IsLinked || !IsTopHeader(editor)) return;
            var paths = AssetPaths(editor, out var viaPrefab);
            if (paths == null) return;

            var links = paths.Select(ProjectLinks.ForAsset).ToList();
            var linked = links.Where(l => l != null).ToList();
            var signedIn = GddSession.Auth.Source != CredentialSource.None;
            // Unlinked and signed out, or a prefab instance whose prefab has no
            // link: nothing to say, so the Inspector stays as Unity draws it.
            if (linked.Count == 0 && (!signedIn || viaPrefab)) return;

            Styles();
            GddSession.EnsurePages();
            EditorGUILayout.Space(2);
            if (linked.Count == 0)
                DrawUnlinked(paths);
            else if (linked.Count < paths.Count || linked.Select(l => l.pageId).Distinct().Count() > 1)
                DrawMixed(paths, linked.Count);
            else
                DrawLinked(linked[0], paths, viaPrefab);
            EditorGUILayout.Space(2);
        }

        /// <summary>
        /// The project assets this Inspector shows, or null when it shows
        /// something that cannot be linked: a sub-asset, a scene object, or an
        /// asset drawn inside another Inspector (the materials under a GameObject).
        /// A prefab instance or the prefab open in Prefab Mode stands for its prefab.
        /// </summary>
        static List<string> AssetPaths(UnityEditor.Editor editor, out bool viaPrefab)
        {
            viaPrefab = false;
            var paths = new List<string>();
            foreach (var target in editor.targets)
            {
                string path;
                if (target is AssetImporter importer)
                    path = importer.assetPath;
                else if (target is GameObject go && !EditorUtility.IsPersistent(go))
                {
                    if (editor.targets.Length > 1) return null;
                    path = PrefabOf(go);
                    viaPrefab = true;
                }
                else if (EditorUtility.IsPersistent(target) && AssetDatabase.IsMainAsset(target) && (FirstInspectedEditor != null || Selection.Contains(target)))
                    path = AssetDatabase.GetAssetPath(target);
                else
                    return null;

                if (!IsLinkable(path)) return null;
                paths.Add(path);
            }
            return paths.Count > 0 ? paths : null;
        }

        /// <summary>
        /// Many assets are drawn by two editors, each with a header: the
        /// importer's and the asset's (a prefab, a script, a texture). Unity marks
        /// the topmost header of an Inspector, and only that one gets the page;
        /// that also leaves out assets drawn inside another Inspector. The mark
        /// is internal: if a future Unity drops it, fall back to showing the
        /// selected assets only.
        /// </summary>
        static bool IsTopHeader(UnityEditor.Editor editor) =>
            FirstInspectedEditor == null || (bool)FirstInspectedEditor.GetValue(editor);

        static readonly PropertyInfo FirstInspectedEditor = typeof(UnityEditor.Editor).GetProperty(
            "firstInspectedEditor", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) is PropertyInfo p && p.PropertyType == typeof(bool)
            ? p
            : null;

        static string PrefabOf(GameObject go)
        {
            var stage = PrefabStageUtility.GetPrefabStage(go);
            if (stage != null) return stage.prefabContentsRoot == go ? stage.assetPath : null;
            // Only the root of an instance: a child is not the thing the prefab describes.
            return PrefabUtility.IsAnyPrefabInstanceRoot(go) ? PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go) : null;
        }

        static bool IsLinkable(string path) =>
            !string.IsNullOrEmpty(path) && (path.StartsWith("Assets/") || path.StartsWith("Packages/"));

        // ── Rows ────────────────────────────────────────────────────────────

        static void DrawUnlinked(List<string> paths)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                Badge();
                GUILayout.Label("No GDD page", _note);
                GUILayout.FlexibleSpace();
                var ready = GddSession.Sections != null;
                var label = !ready && GddSession.IsLoading ? "Loading pages…"
                    : paths.Count > 1 ? $"Link {paths.Count} assets to a page…"
                    : "Link to a page…";
                using (new EditorGUI.DisabledScope(!ready))
                {
                    if (GUILayout.Button(label, EditorStyles.miniButton))
                        PickPage(paths, GUILayoutUtility.GetLastRect());
                }
            }
            DrawLoadError();
        }

        static void DrawMixed(List<string> paths, int linkedCount)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                Badge();
                var text = linkedCount < paths.Count
                    ? $"{linkedCount} of {paths.Count} assets linked"
                    : $"{paths.Count} assets linked to different pages";
                GUILayout.Label(text, _note);
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(GddSession.Sections == null))
                {
                    if (GUILayout.Button($"Link all {paths.Count}…", EditorStyles.miniButton))
                        PickPage(paths, GUILayoutUtility.GetLastRect());
                }
                if (GUILayout.Button("Unlink all", EditorStyles.miniButton)) Unlink(paths);
            }
            DrawLoadError();
        }

        static void DrawLinked(AssetLink link, List<string> paths, bool viaPrefab)
        {
            var loaded = GddSession.Sections != null;
            var page = GddSession.Pages.ById(link.pageId);
            var deleted = loaded && page == null;

            using (new EditorGUILayout.HorizontalScope())
            {
                Badge();
                using (new EditorGUILayout.VerticalScope())
                {
                    if (page != null)
                    {
                        var ancestors = GddSession.Pages.Ancestors(page);
                        if (ancestors.Count > 0) GUILayout.Label(string.Join(" › ", ancestors.Select(a => a.title)) + " ›", _trail);
                    }
                    GUILayout.Label(page?.title ?? link.pageTitle ?? "GDD page", _title);
                    if (deleted) GUILayout.Label("This page is no longer in the GDD.", _note);
                    if (viaPrefab && GUILayout.Button("via " + Path.GetFileName(paths[0]), _note))
                        EditorGUIUtility.PingObject(AssetDatabase.LoadMainAssetAtPath(paths[0]));
                }
                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(page == null))
                {
                    if (GUILayout.Button(new GUIContent("Open", "Show this page in the GDD window"), EditorStyles.miniButton))
                        GddWindow.ShowPage(link.pageId);
                }
                if (!viaPrefab)
                {
                    if (GUILayout.Button(EditorGUIUtility.IconContent("_Menu"), EditorStyles.iconButton))
                        LinkMenu(link, paths, page);
                    OpenPendingPicker(paths, GUILayoutUtility.GetLastRect());
                }
            }

            if (!GddSession.CanLoad)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("Sign in to read this page.", _note);
                    if (GUILayout.Button("Open the GDD window", EditorStyles.miniButton)) GddWindow.Open();
                    GUILayout.FlexibleSpace();
                }
                return;
            }
            if (!loaded && GddSession.IsLoading) GUILayout.Label("Loading the page…", _note);
            DrawLoadError();
            if (page != null) DrawDescription(page);
        }

        static void DrawDescription(GddSection page)
        {
            if (string.IsNullOrWhiteSpace(page.content)) return;

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(BadgeWidth);
                using (new EditorGUILayout.VerticalScope())
                {
                    // Laid out by hand: the layout version puts the arrow left
                    // of the header, out of sight.
                    var open = EditorPrefs.GetBool(DescriptionOpenPref, true);
                    var rect = GUILayoutUtility.GetRect(DescriptionLabel, EditorStyles.foldout);
                    var now = EditorGUI.Foldout(rect, open, DescriptionLabel, toggleOnLabelClick: true);
                    if (now != open) EditorPrefs.SetBool(DescriptionOpenPref, now);
                    if (!now) return;

                    // Rich text from the same renderer as the window. References
                    // show as links but only the window follows them.
                    var rendered = Render(page);
                    GUILayout.Label(rendered.Text, _body);
                    if (rendered.Truncated && EditorGUILayout.LinkButton("Continue reading in the GDD window"))
                        GddWindow.ShowPage(page.id);
                }
            }
        }

        static void DrawLoadError()
        {
            if (GddSession.LoadError == null || GddSession.IsLoading) return;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(GddSession.LoadError, _note);
                if (GUILayout.Button("Retry", EditorStyles.miniButton, GUILayout.ExpandWidth(false))) GddSession.RefreshInBackground();
            }
        }

        static void LinkMenu(AssetLink link, List<string> paths, GddSection page)
        {
            var menu = new GenericMenu();
            var settings = GddSession.Settings;
            if (page != null)
                menu.AddItem(new GUIContent("Open in browser"), false, () =>
                    Application.OpenURL(GddServer.SectionPageUrl(settings.Server, settings.projectId, link.pageId)));
            else
                menu.AddDisabledItem(new GUIContent("Open in browser"));
            if (GddSession.Sections != null)
                menu.AddItem(new GUIContent("Link to another page…"), false, () =>
                {
                    _pickerPending = true;
                    InternalEditorUtility.RepaintAllViews();
                });
            else
                menu.AddDisabledItem(new GUIContent("Link to another page…"));
            menu.AddItem(new GUIContent(paths.Count > 1 ? $"Unlink {paths.Count} assets" : "Unlink"), false, () => Unlink(paths));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Refresh pages"), false, GddSession.RefreshInBackground);
            menu.ShowAsContext();
        }

        // ── Actions ─────────────────────────────────────────────────────────

        static void PickPage(List<string> paths, Rect at)
        {
            if (GddSession.Sections == null) return;
            new PagePicker(PageTree.Build(GddSession.Sections), page =>
            {
                if (ProjectLinks.Link(paths, page))
                    Debug.Log($"[GDD Manager] Links between assets and GDD pages are saved in {AssetLinks.RelativePath}. Commit it so the whole team sees them.");
            }).Show(at);
        }

        /// <summary>
        /// A menu item runs outside the Inspector's GUI, where the picker cannot
        /// work out where to open. So the item only asks, and the next repaint of
        /// the header opens it under the menu button.
        /// </summary>
        static void OpenPendingPicker(List<string> paths, Rect under)
        {
            if (!_pickerPending || Event.current.type != EventType.Repaint) return;
            _pickerPending = false;
            PickPage(paths, under);
        }

        static void Unlink(List<string> paths) => ProjectLinks.Unlink(paths.Select(AssetDatabase.AssetPathToGUID));

        // ── Bits ────────────────────────────────────────────────────────────

        static Rendered Render(GddSection page)
        {
            if (RenderedPages.TryGetValue(page.id, out var cached) && cached.Content == page.content) return cached;
            var excerpt = MarkdownText.Excerpt(page.content, ExcerptChars, out var truncated);
            var rendered = new Rendered
            {
                Content = page.content,
                Text = MarkdownText.ToRichText(excerpt, GddSession.Pages, GddWindow.LinkColor),
                Truncated = truncated,
            };
            RenderedPages[page.id] = rendered;
            return rendered;
        }

        static void Badge()
        {
            var icon = EditorGUIUtility.IconContent("d_TextAsset Icon").image;
            GUILayout.Label(new GUIContent(icon, "GDD Manager"), GUILayout.Width(BadgeWidth - 4), GUILayout.Height(18));
        }

        static void Styles()
        {
            if (_title != null) return;
            _trail = new GUIStyle(EditorStyles.miniLabel) { richText = false, wordWrap = true };
            _title = new GUIStyle(EditorStyles.boldLabel) { richText = false, wordWrap = true };
            _body = new GUIStyle(EditorStyles.wordWrappedLabel) { richText = true };
            _note = new GUIStyle(EditorStyles.miniLabel) { richText = false, wordWrap = true };
        }
    }
}
