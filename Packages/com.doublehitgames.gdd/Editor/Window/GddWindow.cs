using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Doublehitgames.Gdd.Editor.Api;
using Doublehitgames.Gdd.Editor.Auth;
using Doublehitgames.Gdd.Editor.Links;
using Doublehitgames.Gdd.Editor.Pages;
using Doublehitgames.Gdd.Editor.Session;
using Doublehitgames.Gdd.Editor.Settings;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
// PointerUpLinkTagEvent (clicks on <link> in rich text) is still Experimental in 6000.0.
using UnityEngine.UIElements.Experimental;

namespace Doublehitgames.Gdd.Editor.Window
{
    /// <summary>
    /// The game design document, read from inside Unity: sign in, link this
    /// project to its GDD once, then browse the page tree.
    /// </summary>
    internal sealed class GddWindow : EditorWindow
    {
        // As long as GDD Manager keeps an authorization code alive.
        static readonly TimeSpan SignInTimeout = TimeSpan.FromMinutes(10);

        [SerializeField] string _selectedId;
        [SerializeField] string _search;
        [SerializeField] NavigationHistory _history = new NavigationHistory();

        GddMe _me;
        List<PageNode> _roots = new List<PageNode>();
        CancellationTokenSource _signIn;
        // Bumped on every reload, so a slow answer to an old request is dropped.
        int _generation;

        VisualElement _body;
        HelpBox _status;
        TreeView _tree;
        ScrollView _detailScroll;
        VisualElement _detail;
        VisualElement _preview;
        ToolbarButton _backButton;
        ToolbarButton _forwardButton;
        GddSection _shown;
        VisualElement _linkedAssets;

        // Shared with the Inspector, which shows the same pages.
        static GddProjectSettings Settings => GddSession.Settings;
        static GddAuth Auth => GddSession.Auth;
        static GddApiClient Api => GddSession.Api;
        static PageIndex Pages => GddSession.Pages;

        [MenuItem("Window/GDD Manager")]
        static void OpenFromMenu() => Open();

        internal static GddWindow Open()
        {
            var window = GetWindow<GddWindow>();
            window.titleContent = new GUIContent("GDD", EditorGUIUtility.IconContent("d_TextAsset Icon").image);
            window.Show();
            return window;
        }

        /// <summary>Opens the window on a page, as following a link would.</summary>
        internal static void ShowPage(string id)
        {
            var window = Open();
            if (window._tree != null && Pages.ById(id) != null) window.GoTo(id);
            // Not showing the pages yet: the tree selects it once they are in.
            else window._selectedId = id;
            window.Focus();
        }

        void OnEnable()
        {
            GddSession.PagesChanged += OnPagesChanged;
            ProjectLinks.Changed += FillLinkedAssets;
        }

        void OnDisable()
        {
            GddSession.PagesChanged -= OnPagesChanged;
            ProjectLinks.Changed -= FillLinkedAssets;
        }

        void CreateGUI()
        {
            rootVisualElement.style.flexGrow = 1;
            _body = new VisualElement { style = { flexGrow = 1 } };
            _status = new HelpBox("", HelpBoxMessageType.Info) { style = { display = DisplayStyle.None, marginTop = 4 } };
            rootVisualElement.Add(_body);
            rootVisualElement.Add(_status);
            // Trickle-down so back/forward work wherever the focus is in the window.
            rootVisualElement.focusable = true;
            rootVisualElement.RegisterCallback<KeyDownEvent>(OnNavigationKey, TrickleDown.TrickleDown);
            rootVisualElement.RegisterCallback<PointerDownEvent>(OnMouseSideButton, TrickleDown.TrickleDown);
            Reload();
        }

        // ── Flow ────────────────────────────────────────────────────────────

        void Reload()
        {
            GddSession.ReloadSettings();
            _ = LoadAsync();
        }

        async Task LoadAsync(string notice = null)
        {
            var generation = ++_generation;
            ClearStatus();
            if (Auth.Source == CredentialSource.None)
            {
                ShowSignIn();
                if (notice != null) SetStatus(notice, HelpBoxMessageType.Info);
                return;
            }

            ShowMessage("Connecting to GDD Manager…");
            try
            {
                _me = await Api.GetMeAsync();
                if (generation != _generation) return;

                if (!Settings.IsLinked)
                {
                    var projects = await Api.ListProjectsAsync();
                    if (generation != _generation) return;
                    ShowProjectPicker(projects);
                }
                else
                {
                    // The pages come in through OnPagesChanged.
                    await GddSession.LoadPagesAsync();
                    if (generation != _generation) return;
                    ShowPages();
                }
                if (notice != null) SetStatus(notice, HelpBoxMessageType.Info);
            }
            catch (GddApiException e) when (generation == _generation)
            {
                HandleError(e);
            }
            catch (Exception e) when (generation == _generation)
            {
                Debug.LogException(e);
                ShowMessage("Something went wrong talking to GDD Manager. The details are in the Console.", ("Retry", Reload));
            }
        }

        void HandleError(GddApiException e)
        {
            switch (e.Kind)
            {
                case GddErrorKind.Unauthorized when Auth.Source == CredentialSource.Environment:
                    ShowMessage($"The key in {GddAuth.EnvironmentVariable} was refused by GDD Manager.", ("Retry", Reload));
                    break;
                case GddErrorKind.Unauthorized:
                    Auth.SignOut();
                    GddSession.ClearPages();
                    ShowSignIn();
                    SetStatus("Your GDD Manager session ended. Sign in again.", HelpBoxMessageType.Warning);
                    break;
                case GddErrorKind.Forbidden:
                case GddErrorKind.NotFound:
                    ShowMessage(
                        $"This project is linked to “{Settings.projectTitle ?? Settings.projectId}”, which {_me?.Label ?? "this account"} cannot open. " +
                        "Ask its owner to share it with you, or link this project to another GDD.",
                        ("Link to another GDD…", PickAnotherProject), ("Sign out", SignOut));
                    break;
                default:
                    ShowMessage(e.Message, ("Retry", Reload));
                    break;
            }
        }

        async void SignInWithBrowser()
        {
            _signIn?.Cancel();
            var signIn = _signIn = new CancellationTokenSource(SignInTimeout);
            var cancelledByUser = false;
            ShowMessage("Finish signing in in your browser…\n\nScripts will not recompile until this is done.", ("Cancel", () =>
            {
                cancelledByUser = true;
                signIn.Cancel();
            }));
            // A domain reload while the browser is out would take the loopback
            // listener with it, and the consent would come back to a closed port.
            // Closing the window, on the other hand, does not stop the sign-in.
            EditorApplication.LockReloadAssemblies();
            try
            {
                await Auth.SignInWithBrowserAsync(Application.OpenURL, signIn.Token);
                Debug.Log("[GDD Manager] Signed in through the browser.");
                if (this == null) return;
                await LoadAsync();
            }
            catch (OperationCanceledException)
            {
                if (!cancelledByUser) Debug.LogWarning("[GDD Manager] Browser sign-in timed out.");
                if (this == null) return;
                ShowSignIn();
                if (!cancelledByUser) SetStatus("Sign-in timed out. Try again.", HelpBoxMessageType.Warning);
            }
            catch (Exception e) when (e is OAuthException || e is GddApiException)
            {
                Debug.LogWarning("[GDD Manager] Browser sign-in failed: " + e.Message);
                if (this == null) return;
                ShowSignIn();
                SetStatus(e.Message, HelpBoxMessageType.Error);
            }
            finally
            {
                EditorApplication.UnlockReloadAssemblies();
                if (_signIn == signIn) _signIn = null;
                signIn.Dispose();
            }
        }

        void UseApiKey(string key)
        {
            try
            {
                Auth.UseApiKey(key);
            }
            catch (ArgumentException e)
            {
                SetStatus(e.Message, HelpBoxMessageType.Error);
                return;
            }
            _ = LoadAsync();
        }

        void SignOut()
        {
            Auth.SignOut();
            GddSession.ClearPages();
            _me = null;
            _ = LoadAsync(Auth.Source == CredentialSource.Environment
                ? $"Signed out. Still connected through {GddAuth.EnvironmentVariable}."
                : "Signed out. To revoke this editor's access too, use Connected apps in GDD Manager's API keys settings.");
        }

        void Link(GddProject project)
        {
            Settings.projectId = project.id;
            Settings.projectTitle = project.title;
            Settings.Save();
            GddSession.ClearPages();
            _selectedId = null;
            _ = LoadAsync($"Linked to “{project.title}”. Commit {GddProjectSettings.RelativePath} so the whole team gets the same link.");
        }

        async void PickAnotherProject()
        {
            var generation = ++_generation;
            ShowMessage("Loading your GDDs…");
            try
            {
                var projects = await Api.ListProjectsAsync();
                if (generation == _generation) ShowProjectPicker(projects);
            }
            catch (GddApiException e) when (generation == _generation)
            {
                HandleError(e);
            }
        }

        // ── Views ───────────────────────────────────────────────────────────

        void ShowSignIn()
        {
            _body.Clear();
            _tree = null;
            var panel = Centered();
            panel.Add(Title("GDD Manager"));
            panel.Add(Paragraph("Read this project's game design document without leaving Unity."));

            var signIn = new Button(SignInWithBrowser) { text = "Sign in with browser" };
            signIn.style.height = 28;
            signIn.style.marginTop = 8;
            panel.Add(signIn);
            if (Auth.Source == CredentialSource.Environment)
                panel.Add(new Button(() => _ = LoadAsync()) { text = $"Keep using {GddAuth.EnvironmentVariable}" });

            var other =new Foldout { text = "Other ways to connect", value = false };
            other.style.marginTop = 16;

            var server = new TextField("Server") { value = Settings.Server, isDelayed = true };
            server.SetEnabled(!Settings.IsLinked);
            server.tooltip = Settings.IsLinked
                ? $"Set by {GddProjectSettings.RelativePath}, which this project shares with the team."
                : "Change only for a self-hosted GDD Manager.";
            server.RegisterValueChangedCallback(e =>
            {
                Settings.serverUrl = GddServer.Normalize(e.newValue);
                GddSession.Connect(Settings.Server);
            });
            other.Add(server);

            var key = new TextField("API key") { isPasswordField = true };
            other.Add(key);
            other.Add(new Button(() => UseApiKey(key.value)) { text = "Use this key" });
            other.Add(Paragraph(
                "Create a key in GDD Manager under Settings → API keys. " +
                $"For builds on CI, set the {GddAuth.EnvironmentVariable} environment variable instead.", small: true));
            panel.Add(other);

            _body.Add(panel);
        }

        void ShowProjectPicker(GddProject[] projects)
        {
            _body.Clear();
            var panel = Centered();
            panel.Add(Title("Link this project"));
            panel.Add(Paragraph($"Signed in as {_me?.Label}. Which GDD does this Unity project belong to?"));

            if (projects.Length == 0)
            {
                panel.Add(Paragraph("This account has no GDD yet. Create one in GDD Manager, then refresh."));
                panel.Add(new Button(() => Application.OpenURL(Settings.Server)) { text = "Open GDD Manager" });
                panel.Add(new Button(Reload) { text = "Refresh" });
            }
            else
            {
                var list = new ScrollView { style = { maxHeight = 320, marginTop = 8 } };
                foreach (var project in projects)
                {
                    var row = new Button(() => Link(project)) { text = project.title };
                    row.style.unityTextAlign = TextAnchor.MiddleLeft;
                    row.style.height = 26;
                    if (project.access != "owner") row.text += $"   ({project.access})";
                    list.Add(row);
                }
                panel.Add(list);
            }

            panel.Add(Paragraph($"The link is saved in {GddProjectSettings.RelativePath}, to be committed with the project.", small: true));
            var footer = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 8 } };
            if (Auth.Source == CredentialSource.Environment)
                footer.Add(new Button(ShowSignIn) { text = "Sign in as yourself", tooltip = $"Now connected through {GddAuth.EnvironmentVariable}." });
            else
                footer.Add(new Button(SignOut) { text = "Sign out" });
            panel.Add(footer);
            _body.Add(panel);
        }

        void ShowPages()
        {
            _body.Clear();
            _shown = null;
            _linkedAssets = null;

            var toolbar = new Toolbar();
            _backButton = NavButton("tab_prev", "‹", "Back (Alt+←)", GoBack);
            _forwardButton = NavButton("tab_next", "›", "Forward (Alt+→)", GoForward);
            toolbar.Add(_backButton);
            toolbar.Add(_forwardButton);
            UpdateNavButtons();

            var menu = new ToolbarMenu { text = Settings.projectTitle ?? "GDD" };
            menu.menu.AppendAction("Open in browser", _ => Application.OpenURL(GddServer.ProjectPageUrl(Settings.Server, Settings.projectId)));
            menu.menu.AppendAction("Link to another GDD…", _ => PickAnotherProject());
            menu.menu.AppendSeparator();
            menu.menu.AppendAction($"Signed in as {_me?.Label}", _ => { }, DropdownMenuAction.Status.Disabled);
            if (Auth.Source != CredentialSource.Environment)
                menu.menu.AppendAction("Sign out", _ => SignOut());
            else
                menu.menu.AppendAction($"Sign in as yourself (now using {GddAuth.EnvironmentVariable})…", _ => ShowSignIn());
            toolbar.Add(menu);

            var search = new ToolbarSearchField { value = _search ?? "" };
            search.style.flexGrow = 1;
            search.style.width = StyleKeyword.Auto;
            search.RegisterValueChangedCallback(e =>
            {
                _search = e.newValue;
                FillTree();
            });
            toolbar.Add(search);
            toolbar.Add(new ToolbarButton(() => _ = LoadAsync()) { text = "Refresh", tooltip = "Fetch the pages again" });
            _body.Add(toolbar);

            var split = new TwoPaneSplitView(0, 240, TwoPaneSplitViewOrientation.Horizontal);
            _tree = new TreeView
            {
                makeItem = () => new Label { style = { unityTextAlign = TextAnchor.MiddleLeft } },
                bindItem = (element, index) => ((Label)element).text = _tree.GetItemDataForIndex<GddSection>(index).title,
                selectionType = SelectionType.Single,
                fixedItemHeight = 20,
            };
            _tree.selectionChanged += items =>
            {
                var section = items.OfType<GddSection>().FirstOrDefault();
                if (section == null) return;
                _selectedId = section.id;
                // Back/forward already moved the history; revisiting the current page is a no-op.
                _history.Visit(section.id);
                UpdateNavButtons();
                ShowDetail(section);
            };
            // Each side sits in a plain pane: the split view sizes its fixed pane
            // through `width`, which TreeView's own flex-basis: 0 would override
            // and collapse the tree to nothing.
            _tree.style.flexGrow = 1;
            split.Add(Pane(_tree));

            _detailScroll = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1 } };
            _detail = new VisualElement { style = { paddingLeft = 12, paddingRight = 12, paddingTop = 8, paddingBottom = 12 } };
            _detailScroll.Add(_detail);
            RegisterAssetDrop(_detailScroll);
            split.Add(Pane(_detailScroll));
            _body.Add(split);

            FillTree();
        }

        void FillTree()
        {
            if (_tree == null) return;
            var roots = PageTree.Filter(_roots, _search);
            var ids = new Dictionary<string, int>();
            _tree.SetRootItems(ToItems(roots, ids));
            _tree.Rebuild();

            if (!string.IsNullOrEmpty(_search)) _tree.ExpandAll();
            if (_roots.Count == 0)
            {
                _detail.Clear();
                _detail.Add(Paragraph("This GDD has no pages yet."));
            }
            else if (_selectedId != null && ids.TryGetValue(_selectedId, out var id))
            {
                _tree.SetSelectionById(id);
                _tree.ScrollToItemById(id);
            }
            else if (_detail.childCount == 0)
                _detail.Add(Paragraph("Pick a page on the left.", small: true));
        }

        static List<TreeViewItemData<GddSection>> ToItems(IEnumerable<PageNode> nodes, Dictionary<string, int> ids) =>
            nodes.Select(n =>
            {
                var id = ids[n.Section.id] = ids.Count;
                return new TreeViewItemData<GddSection>(id, n.Section, ToItems(n.Children, ids));
            }).ToList();

        void ShowDetail(GddSection section)
        {
            _detail.Clear();
            _detailScroll.scrollOffset = Vector2.zero;
            ClosePreview();

            var trail = Breadcrumb(section);
            if (trail != null) _detail.Add(trail);

            var title = Title(section.title);
            title.style.marginBottom = 2;
            _detail.Add(title);

            var meta = new List<string>();
            if (!string.IsNullOrEmpty(section.status)) meta.Add(section.status);
            if (!string.IsNullOrEmpty(section.dataId)) meta.Add("dataId: " + section.dataId);
            if (!string.IsNullOrEmpty(section.updatedByName)) meta.Add("last edited by " + section.updatedByName);
            if (meta.Count > 0) _detail.Add(Paragraph(string.Join("  ·  ", meta), small: true));

            var open = new Button(() => Application.OpenURL(GddServer.SectionPageUrl(Settings.Server, Settings.projectId, section.id)))
            {
                text = "Open in browser",
            };
            open.style.alignSelf = Align.FlexStart;
            open.style.marginLeft = 0;
            open.style.marginBottom = 10;
            _detail.Add(open);

            if (string.IsNullOrWhiteSpace(section.content))
                _detail.Add(Paragraph("This page has no description yet.", small: true));
            else
                _detail.Add(PageBody(section.content));

            _shown = section;
            _linkedAssets = new VisualElement { style = { marginTop = 16 } };
            _detail.Add(_linkedAssets);
            FillLinkedAssets();
        }

        // ── Linked assets ───────────────────────────────────────────────────

        /// <summary>The assets that implement the page on show, from the project's links file.</summary>
        void FillLinkedAssets()
        {
            if (_linkedAssets == null || _shown == null) return;
            _linkedAssets.Clear();
            var links = ProjectLinks.Current.ForPage(_shown.id);

            if (links.Count > 0)
            {
                var heading = new Label("Linked assets") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 4 } };
                _linkedAssets.Add(heading);
            }
            foreach (var link in links) _linkedAssets.Add(LinkedAssetRow(link));
            _linkedAssets.Add(Paragraph(
                links.Count == 0
                    ? "No assets are linked to this page. Drag them here from the Project window, or link them from their Inspector."
                    : "Drag more assets here from the Project window to link them.",
                small: true));
        }

        VisualElement LinkedAssetRow(AssetLink link)
        {
            var exists = ProjectLinks.Exists(link);
            var path = exists ? AssetDatabase.GUIDToAssetPath(link.guid) : link.path;

            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, height = 20 } };
            row.tooltip = path;
            var icon = new Image { image = exists ? AssetDatabase.GetCachedIcon(path) : null };
            icon.style.width = icon.style.height = 16;
            icon.style.marginRight = 4;
            row.Add(icon);

            var name = new Label(exists ? System.IO.Path.GetFileName(path) : $"{path}  (missing)") { enableRichText = false };
            name.style.flexGrow = 1;
            name.style.flexShrink = 1;
            name.style.overflow = Overflow.Hidden;
            name.style.textOverflow = TextOverflow.Ellipsis;
            if (!exists) name.style.opacity = 0.6f;
            row.Add(name);

            if (exists)
            {
                // A click shows the asset in the Project window; the Inspector then
                // shows it with this page on top.
                name.RegisterCallback<ClickEvent>(_ =>
                {
                    var asset = AssetDatabase.LoadMainAssetAtPath(path);
                    Selection.activeObject = asset;
                    EditorGUIUtility.PingObject(asset);
                });
            }
            var unlink = new Button(() => ProjectLinks.Unlink(new[] { link.guid })) { text = "×", tooltip = "Unlink from this page" };
            unlink.style.width = 20;
            row.Add(unlink);
            return row;
        }

        /// <summary>Assets dragged from the Project window onto the page get linked to it.</summary>
        void RegisterAssetDrop(VisualElement target)
        {
            target.RegisterCallback<DragUpdatedEvent>(_ =>
            {
                if (_shown == null || DraggedAssetPaths().Count == 0) return;
                DragAndDrop.visualMode = DragAndDropVisualMode.Link;
                SetDropHighlight(target, true);
            });
            target.RegisterCallback<DragLeaveEvent>(_ => SetDropHighlight(target, false));
            target.RegisterCallback<DragExitedEvent>(_ => SetDropHighlight(target, false));
            target.RegisterCallback<DragPerformEvent>(_ =>
            {
                SetDropHighlight(target, false);
                var paths = DraggedAssetPaths();
                if (_shown == null || paths.Count == 0) return;
                DragAndDrop.AcceptDrag();

                var moved = paths.Count(p => ProjectLinks.ForAsset(p) is AssetLink l && l.pageId != _shown.id);
                var created = ProjectLinks.Link(paths, _shown);
                var what = paths.Count == 1 ? System.IO.Path.GetFileName(paths[0]) : $"{paths.Count} assets";
                var message = $"Linked {what} to “{_shown.title}”.";
                if (moved > 0) message += paths.Count == 1 ? " It was linked to another page before." : $" {moved} of them were linked to another page before.";
                if (created) message += $" Commit {AssetLinks.RelativePath} so the whole team sees the links.";
                SetStatus(message, HelpBoxMessageType.Info);
            });
        }

        static List<string> DraggedAssetPaths() =>
            DragAndDrop.paths.Where(p => p.StartsWith("Assets/") || p.StartsWith("Packages/")).Distinct().ToList();

        static void SetDropHighlight(VisualElement target, bool on) =>
            target.style.backgroundColor = on ? new StyleColor(new Color(0.3f, 0.55f, 1f, 0.12f)) : new StyleColor(StyleKeyword.Null);

        Label PageBody(string content)
        {
            var body = new Label(MarkdownText.ToRichText(content, Pages, LinkColor))
            {
                enableRichText = true,
                style = { whiteSpace = WhiteSpace.Normal },
            };
            body.selection.isSelectable = true;
            body.RegisterCallback<PointerUpLinkTagEvent>(e => FollowLink(e.linkID, e.position, e.actionKey));
            return body;
        }

        internal static string LinkColor => EditorGUIUtility.isProSkin ? "#6CB4FF" : "#0B63CE";

        /// <summary>
        /// A page reference opens a preview first, as on the web: most of the
        /// time the reader only wants to know what the page is, and jumping
        /// away from 250 pages is how people get lost. Ctrl/Cmd+click jumps.
        /// </summary>
        void FollowLink(string link, Vector2 at, bool jump)
        {
            if (link.StartsWith(MarkdownText.RefLinkPrefix, StringComparison.Ordinal))
            {
                var target = Pages.ById(link.Substring(MarkdownText.RefLinkPrefix.Length));
                if (target == null) return;
                if (jump) GoTo(target.id);
                else ShowPreview(target, at);
                return;
            }
            if (Uri.TryCreate(link, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.Scheme == "http"))
                Application.OpenURL(link);
        }

        // ── Navigation ──────────────────────────────────────────────────────

        /// <summary>
        /// Opens a page by jumping to it (a link, the trail, back/forward). The
        /// tree folds up to the path of the new page, so a few jumps do not leave
        /// every branch of the document open.
        /// </summary>
        void GoTo(string id)
        {
            if (_tree == null || Pages.ById(id) == null) return;
            ClosePreview();
            _selectedId = id;
            if (!string.IsNullOrEmpty(_search))
            {
                _search = "";
                ShowPages();
                return;
            }
            _tree.CollapseAll();
            FillTree();
        }

        void GoBack()
        {
            var id = _history.GoBack();
            if (id != null) GoTo(id);
            UpdateNavButtons();
        }

        void GoForward()
        {
            var id = _history.GoForward();
            if (id != null) GoTo(id);
            UpdateNavButtons();
        }

        void UpdateNavButtons()
        {
            _backButton?.SetEnabled(_history.CanGoBack);
            _forwardButton?.SetEnabled(_history.CanGoForward);
        }

        void OnNavigationKey(KeyDownEvent e)
        {
            if (e.keyCode == KeyCode.Escape && _preview != null)
            {
                ClosePreview();
                e.StopPropagation();
            }
            else if (e.altKey && e.keyCode == KeyCode.LeftArrow)
            {
                GoBack();
                e.StopPropagation();
            }
            else if (e.altKey && e.keyCode == KeyCode.RightArrow)
            {
                GoForward();
                e.StopPropagation();
            }
        }

        void OnMouseSideButton(PointerDownEvent e)
        {
            // The side buttons of the mouse, as in a browser.
            if (e.button == 3) GoBack();
            else if (e.button == 4) GoForward();
            else return;
            e.StopPropagation();
        }

        /// <summary>“Mecânicas › Animais ›” above the title: where in the document this page sits.</summary>
        VisualElement Breadcrumb(GddSection section)
        {
            var ancestors = Pages.Ancestors(section);
            if (ancestors.Count == 0) return null;

            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, marginBottom = 2 } };
            foreach (var ancestor in ancestors)
            {
                var crumb = new Label(ancestor.title) { enableRichText = false, tooltip = "Go to " + ancestor.title };
                crumb.style.fontSize = 11;
                crumb.style.color = new StyleColor(ColorUtility.TryParseHtmlString(LinkColor, out var c) ? c : Color.gray);
                crumb.RegisterCallback<ClickEvent>(_ => GoTo(ancestor.id));
                crumb.RegisterCallback<PointerEnterEvent>(_ => crumb.style.unityFontStyleAndWeight = FontStyle.Bold);
                crumb.RegisterCallback<PointerLeaveEvent>(_ => crumb.style.unityFontStyleAndWeight = FontStyle.Normal);
                row.Add(crumb);

                var separator = new Label("›") { style = { fontSize = 11, opacity = 0.5f, marginLeft = 3, marginRight = 3 } };
                row.Add(separator);
            }
            return row;
        }

        // ── Preview ─────────────────────────────────────────────────────────

        void ShowPreview(GddSection section, Vector2 at)
        {
            ClosePreview();

            // A transparent layer over the window: a click outside the card closes it.
            _preview = new VisualElement { style = { position = Position.Absolute, left = 0, top = 0, right = 0, bottom = 0 } };
            _preview.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.target == _preview) ClosePreview();
            });

            var dark = EditorGUIUtility.isProSkin;
            var card = new VisualElement();
            card.style.position = Position.Absolute;
            card.style.width = Mathf.Min(400, position.width - 16);
            card.style.maxHeight = Mathf.Min(380, position.height - 16);
            card.style.backgroundColor = dark ? new Color(0.20f, 0.20f, 0.20f) : new Color(0.94f, 0.94f, 0.94f);
            var border = dark ? new Color(0.09f, 0.09f, 0.09f) : new Color(0.6f, 0.6f, 0.6f);
            card.style.borderTopColor = card.style.borderBottomColor = card.style.borderLeftColor = card.style.borderRightColor = border;
            card.style.borderTopWidth = card.style.borderBottomWidth = card.style.borderLeftWidth = card.style.borderRightWidth = 1;
            card.style.borderTopLeftRadius = card.style.borderTopRightRadius = card.style.borderBottomLeftRadius = card.style.borderBottomRightRadius = 6;
            card.style.paddingLeft = card.style.paddingRight = 10;
            card.style.paddingTop = card.style.paddingBottom = 8;

            var path = Pages.Ancestors(section);
            if (path.Count > 0)
                card.Add(Paragraph(string.Join(" › ", path.Select(p => p.title)), small: true));
            var title = Title(section.title);
            title.style.fontSize = 14;
            card.Add(title);

            var scroll = new ScrollView(ScrollViewMode.Vertical) { style = { flexShrink = 1 } };
            if (string.IsNullOrWhiteSpace(section.content))
                scroll.Add(Paragraph("This page has no description yet.", small: true));
            else
                scroll.Add(PageBody(section.content));
            card.Add(scroll);

            var buttons = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.FlexEnd, marginTop = 8 } };
            buttons.Add(new Button(ClosePreview) { text = "Close" });
            var go = new Button(() => GoTo(section.id)) { text = "Go to page" };
            go.style.unityFontStyleAndWeight = FontStyle.Bold;
            buttons.Add(go);
            card.Add(buttons);

            // Placed once its size is known: below the click, or above it when
            // there is no room, and always inside the window.
            card.style.visibility = Visibility.Hidden;
            card.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                var size = card.layout.size;
                var area = rootVisualElement.layout.size;
                var x = Mathf.Clamp(at.x - 24, 8, Mathf.Max(8, area.x - size.x - 8));
                var y = at.y + 14;
                if (y + size.y > area.y - 8) y = at.y - 14 - size.y;
                card.style.left = x;
                card.style.top = Mathf.Clamp(y, 8, Mathf.Max(8, area.y - size.y - 8));
                card.style.visibility = Visibility.Visible;
            });

            _preview.Add(card);
            rootVisualElement.Add(_preview);
            rootVisualElement.Focus();
        }

        void ClosePreview()
        {
            _preview?.RemoveFromHierarchy();
            _preview = null;
        }

        void OnPagesChanged()
        {
            _roots = PageTree.Build(GddSession.Sections ?? Array.Empty<GddSection>());
            if (GddSession.Sections == null) return;
            _history.Prune(id => Pages.ById(id) != null);
            UpdateNavButtons();
            // Fetched again (for the Inspector, or by Refresh) while the window shows them.
            if (_tree != null) FillTree();
        }

        void ShowMessage(string message, params (string label, Action action)[] actions)
        {
            _body.Clear();
            _tree = null;
            var panel = Centered();
            panel.Add(Paragraph(message));
            foreach (var (label, action) in actions)
            {
                var button = new Button(action) { text = label };
                button.style.marginTop = 6;
                panel.Add(button);
            }
            _body.Add(panel);
        }

        // ── Bits ────────────────────────────────────────────────────────────

        void SetStatus(string message, HelpBoxMessageType type)
        {
            _status.text = message;
            _status.messageType = type;
            _status.style.display = DisplayStyle.Flex;
        }

        void ClearStatus() => _status.style.display = DisplayStyle.None;

        static ToolbarButton NavButton(string icon, string fallbackText, string tooltip, Action action)
        {
            var button = new ToolbarButton(action) { tooltip = tooltip };
            var image = EditorGUIUtility.FindTexture((EditorGUIUtility.isProSkin ? "d_" : "") + icon);
            if (image != null) button.iconImage = Background.FromTexture2D(image);
            else button.text = fallbackText;
            return button;
        }

        static VisualElement Pane(VisualElement content)
        {
            var pane = new VisualElement { style = { minWidth = 120 } };
            pane.Add(content);
            return pane;
        }

        static VisualElement Centered()
        {
            var panel = new VisualElement();
            panel.style.maxWidth = 420;
            panel.style.width = Length.Percent(100);
            panel.style.alignSelf = Align.Center;
            panel.style.paddingLeft = 16;
            panel.style.paddingRight = 16;
            panel.style.paddingTop = 32;
            return panel;
        }

        static Label Title(string text)
        {
            var label = new Label(text) { enableRichText = false };
            label.style.fontSize = 16;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginBottom = 6;
            return label;
        }

        static Label Paragraph(string text, bool small = false)
        {
            var label = new Label(text) { enableRichText = false };
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginBottom = 6;
            if (small)
            {
                label.style.fontSize = 11;
                label.style.opacity = 0.7f;
            }
            return label;
        }
    }
}
