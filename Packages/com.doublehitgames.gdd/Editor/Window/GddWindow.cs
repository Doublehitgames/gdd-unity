using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Doublehitgames.Gdd.Editor.Api;
using Doublehitgames.Gdd.Editor.Auth;
using Doublehitgames.Gdd.Editor.Pages;
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

        readonly IHttpTransport _transport = new UnityWebRequestTransport();
        GddProjectSettings _settings;
        GddAuth _auth;
        GddApiClient _api;
        GddMe _me;
        List<PageNode> _roots = new List<PageNode>();
        Dictionary<string, GddSection> _byTitle = new Dictionary<string, GddSection>(StringComparer.OrdinalIgnoreCase);
        CancellationTokenSource _signIn;
        // Bumped on every reload, so a slow answer to an old request is dropped.
        int _generation;

        VisualElement _body;
        HelpBox _status;
        TreeView _tree;
        ScrollView _detailScroll;
        VisualElement _detail;

        [MenuItem("Window/GDD Manager")]
        public static void Open()
        {
            var window = GetWindow<GddWindow>();
            window.titleContent = new GUIContent("GDD", EditorGUIUtility.IconContent("d_TextAsset Icon").image);
            window.Show();
        }

        void CreateGUI()
        {
            rootVisualElement.style.flexGrow = 1;
            _body = new VisualElement { style = { flexGrow = 1 } };
            _status = new HelpBox("", HelpBoxMessageType.Info) { style = { display = DisplayStyle.None, marginTop = 4 } };
            rootVisualElement.Add(_body);
            rootVisualElement.Add(_status);
            Reload();
        }

        // ── Flow ────────────────────────────────────────────────────────────

        void Reload()
        {
            _settings = GddProjectSettings.Load();
            Connect(_settings.Server);
            _ = LoadAsync();
        }

        void Connect(string server)
        {
            _auth = new GddAuth(server, new EditorPrefsCredentialStore(), _transport);
            _api = new GddApiClient(server, _transport, _auth);
        }

        async Task LoadAsync(string notice = null)
        {
            var generation = ++_generation;
            ClearStatus();
            if (_auth.Source == CredentialSource.None)
            {
                ShowSignIn();
                if (notice != null) SetStatus(notice, HelpBoxMessageType.Info);
                return;
            }

            ShowMessage("Connecting to GDD Manager…");
            try
            {
                _me = await _api.GetMeAsync();
                if (generation != _generation) return;

                if (!_settings.IsLinked)
                {
                    var projects = await _api.ListProjectsAsync();
                    if (generation != _generation) return;
                    ShowProjectPicker(projects);
                }
                else
                {
                    var sections = await _api.ListSectionsAsync(_settings.projectId);
                    if (generation != _generation) return;
                    SetPages(sections);
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
                case GddErrorKind.Unauthorized when _auth.Source == CredentialSource.Environment:
                    ShowMessage($"The key in {GddAuth.EnvironmentVariable} was refused by GDD Manager.", ("Retry", Reload));
                    break;
                case GddErrorKind.Unauthorized:
                    _auth.SignOut();
                    ShowSignIn();
                    SetStatus("Your GDD Manager session ended. Sign in again.", HelpBoxMessageType.Warning);
                    break;
                case GddErrorKind.Forbidden:
                case GddErrorKind.NotFound:
                    ShowMessage(
                        $"This project is linked to “{_settings.projectTitle ?? _settings.projectId}”, which {_me?.Label ?? "this account"} cannot open. " +
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
                await _auth.SignInWithBrowserAsync(Application.OpenURL, signIn.Token);
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
                _auth.UseApiKey(key);
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
            _auth.SignOut();
            _me = null;
            _ = LoadAsync(_auth.Source == CredentialSource.Environment
                ? $"Signed out. Still connected through {GddAuth.EnvironmentVariable}."
                : "Signed out. To revoke this editor's access too, use Connected apps in GDD Manager's API keys settings.");
        }

        void Link(GddProject project)
        {
            _settings.projectId = project.id;
            _settings.projectTitle = project.title;
            _settings.Save();
            _selectedId = null;
            _ = LoadAsync($"Linked to “{project.title}”. Commit {GddProjectSettings.RelativePath} so the whole team gets the same link.");
        }

        async void PickAnotherProject()
        {
            var generation = ++_generation;
            ShowMessage("Loading your GDDs…");
            try
            {
                var projects = await _api.ListProjectsAsync();
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
            if (_auth.Source == CredentialSource.Environment)
                panel.Add(new Button(() => _ = LoadAsync()) { text = $"Keep using {GddAuth.EnvironmentVariable}" });

            var other =new Foldout { text = "Other ways to connect", value = false };
            other.style.marginTop = 16;

            var server = new TextField("Server") { value = _settings.Server, isDelayed = true };
            server.SetEnabled(!_settings.IsLinked);
            server.tooltip = _settings.IsLinked
                ? $"Set by {GddProjectSettings.RelativePath}, which this project shares with the team."
                : "Change only for a self-hosted GDD Manager.";
            server.RegisterValueChangedCallback(e =>
            {
                _settings.serverUrl = GddServer.Normalize(e.newValue);
                Connect(_settings.Server);
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
                panel.Add(new Button(() => Application.OpenURL(_settings.Server)) { text = "Open GDD Manager" });
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
            if (_auth.Source == CredentialSource.Environment)
                footer.Add(new Button(ShowSignIn) { text = "Sign in as yourself", tooltip = $"Now connected through {GddAuth.EnvironmentVariable}." });
            else
                footer.Add(new Button(SignOut) { text = "Sign out" });
            panel.Add(footer);
            _body.Add(panel);
        }

        void ShowPages()
        {
            _body.Clear();

            var toolbar = new Toolbar();
            var menu = new ToolbarMenu { text = _settings.projectTitle ?? "GDD" };
            menu.menu.AppendAction("Open in browser", _ => Application.OpenURL(GddServer.ProjectPageUrl(_settings.Server, _settings.projectId)));
            menu.menu.AppendAction("Link to another GDD…", _ => PickAnotherProject());
            menu.menu.AppendSeparator();
            menu.menu.AppendAction($"Signed in as {_me?.Label}", _ => { }, DropdownMenuAction.Status.Disabled);
            if (_auth.Source != CredentialSource.Environment)
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

            var title = Title(section.title);
            title.style.marginBottom = 2;
            _detail.Add(title);

            var meta = new List<string>();
            if (!string.IsNullOrEmpty(section.status)) meta.Add(section.status);
            if (!string.IsNullOrEmpty(section.dataId)) meta.Add("dataId: " + section.dataId);
            if (!string.IsNullOrEmpty(section.updatedByName)) meta.Add("last edited by " + section.updatedByName);
            if (meta.Count > 0) _detail.Add(Paragraph(string.Join("  ·  ", meta), small: true));

            var open = new Button(() => Application.OpenURL(GddServer.SectionPageUrl(_settings.Server, _settings.projectId, section.id)))
            {
                text = "Open in browser",
            };
            open.style.alignSelf = Align.FlexStart;
            open.style.marginLeft = 0;
            open.style.marginBottom = 10;
            _detail.Add(open);

            if (string.IsNullOrWhiteSpace(section.content))
            {
                _detail.Add(Paragraph("This page has no description yet.", small: true));
                return;
            }

            var linkColor = EditorGUIUtility.isProSkin ? "#6CB4FF" : "#0B63CE";
            var body = new Label(MarkdownText.ToRichText(section.content, t => _byTitle.ContainsKey(t), linkColor))
            {
                enableRichText = true,
                style = { whiteSpace = WhiteSpace.Normal },
            };
            body.selection.isSelectable = true;
            body.RegisterCallback<PointerUpLinkTagEvent>(e => FollowLink(e.linkID));
            _detail.Add(body);
        }

        void FollowLink(string link)
        {
            if (link.StartsWith(MarkdownText.RefLinkPrefix, StringComparison.Ordinal))
            {
                var title = link.Substring(MarkdownText.RefLinkPrefix.Length);
                if (!_byTitle.TryGetValue(title, out var target)) return;
                _selectedId = target.id;
                if (!string.IsNullOrEmpty(_search))
                {
                    _search = "";
                    ShowPages();
                }
                else FillTree();
                return;
            }
            if (Uri.TryCreate(link, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.Scheme == "http"))
                Application.OpenURL(link);
        }

        void SetPages(GddSection[] sections)
        {
            _roots = PageTree.Build(sections);
            _byTitle = new Dictionary<string, GddSection>(StringComparer.OrdinalIgnoreCase);
            foreach (var section in sections)
                if (!string.IsNullOrEmpty(section.title) && !_byTitle.ContainsKey(section.title))
                    _byTitle[section.title] = section;
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
