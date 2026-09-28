using System;
using System.Threading.Tasks;
using Doublehitgames.Gdd.Editor.Api;
using Doublehitgames.Gdd.Editor.Auth;
using Doublehitgames.Gdd.Editor.Pages;
using Doublehitgames.Gdd.Editor.Settings;
using UnityEngine;

namespace Doublehitgames.Gdd.Editor.Session
{
    /// <summary>
    /// What the GDD window and the Inspector share within one editor session:
    /// the project's link, the sign-in, and the pages fetched once for both.
    /// Lives until the next domain reload, when it is fetched again on demand.
    /// </summary>
    internal static class GddSession
    {
        /// <summary>How old the pages may get before an Inspector asks for them again.</summary>
        static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);

        static readonly IHttpTransport Transport = new UnityWebRequestTransport();
        static GddProjectSettings _settings;
        static GddAuth _auth;
        static GddApiClient _api;
        static Task _loading;
        // Bumped whenever the pages are thrown away, so a slow answer to an old
        // request does not bring them back.
        static int _generation;
        static DateTime _loadedAtUtc;

        /// <summary>Raised on the main thread when pages arrive, fail to arrive, or are cleared.</summary>
        public static event Action PagesChanged;

        public static GddProjectSettings Settings
        {
            get
            {
                if (_settings == null) ReloadSettings();
                return _settings;
            }
        }

        public static GddAuth Auth
        {
            get
            {
                if (_auth == null) ReloadSettings();
                return _auth;
            }
        }

        public static GddApiClient Api
        {
            get
            {
                if (_api == null) ReloadSettings();
                return _api;
            }
        }

        /// <summary>Null until the pages of the linked GDD have been fetched.</summary>
        public static GddSection[] Sections { get; private set; }
        public static PageIndex Pages { get; private set; } = PageIndex.Empty;
        /// <summary>Why the last fetch failed; null after a good one.</summary>
        public static string LoadError { get; private set; }
        public static bool IsLoading => _loading != null;
        public static bool CanLoad => Settings.IsLinked && Auth.Source != CredentialSource.None;

        /// <summary>Reads ProjectSettings/GddManager.json again, where a teammate may have changed the link.</summary>
        public static void ReloadSettings()
        {
            var settings = GddProjectSettings.Load();
            var changed = _settings != null && (settings.Server != _settings.Server || settings.projectId != _settings.projectId);
            _settings = settings;
            Connect(settings.Server);
            if (changed) ClearPages();
        }

        public static void Connect(string server)
        {
            _auth = new GddAuth(server, new EditorPrefsCredentialStore(), Transport);
            _api = new GddApiClient(server, Transport, _auth);
        }

        /// <summary>
        /// Fetches the pages of the linked GDD. A fetch already on its way is
        /// shared rather than repeated. Throws what the API throws.
        /// </summary>
        public static Task LoadPagesAsync()
        {
            if (_loading != null) return _loading;
            var task = Load(Settings.projectId, _generation);
            // A fetch that finished on the spot has already cleared _loading.
            if (!task.IsCompleted) _loading = task;
            return task;
        }

        static async Task Load(string projectId, int generation)
        {
            try
            {
                var sections = await Api.ListSectionsAsync(projectId);
                if (generation != _generation) return;
                Sections = sections;
                Pages = new PageIndex(sections);
                LoadError = null;
                _loadedAtUtc = DateTime.UtcNow;
            }
            catch (Exception e) when (generation == _generation)
            {
                LoadError = e is GddApiException ? e.Message : "Could not reach GDD Manager. The details are in the Console.";
                if (!(e is GddApiException)) Debug.LogException(e);
                throw;
            }
            finally
            {
                if (generation == _generation)
                {
                    _loading = null;
                    PagesChanged?.Invoke();
                }
            }
        }

        /// <summary>
        /// For views that only read (the Inspector): fetches the pages when there
        /// are none, or when they are a few minutes old, and never throws.
        /// </summary>
        public static void EnsurePages()
        {
            if (IsLoading || !CanLoad) return;
            var missing = Sections == null && LoadError == null;
            var stale = Sections != null && DateTime.UtcNow - _loadedAtUtc > StaleAfter;
            if (missing || stale) RefreshInBackground();
        }

        /// <summary>Fetches the pages again without throwing; a failure lands in <see cref="LoadError"/>.</summary>
        public static async void RefreshInBackground()
        {
            try
            {
                await LoadPagesAsync();
            }
            catch (Exception)
            {
                // Kept in LoadError for whoever shows it.
            }
        }

        /// <summary>After signing out or linking another GDD: what was fetched no longer applies.</summary>
        public static void ClearPages()
        {
            _generation++;
            _loading = null;
            Sections = null;
            Pages = PageIndex.Empty;
            LoadError = null;
            PagesChanged?.Invoke();
        }
    }
}
