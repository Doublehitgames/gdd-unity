using System;
using System.IO;
using Doublehitgames.Gdd.Editor.Api;
using UnityEngine;

namespace Doublehitgames.Gdd.Editor.Settings
{
    /// <summary>
    /// Which GDD this Unity project belongs to. It lives in ProjectSettings/ and
    /// goes into version control with the rest of the project, so the whole team
    /// shares the link. Credentials are never here — see <see cref="Auth.GddAuth"/>.
    /// </summary>
    [Serializable]
    internal sealed class GddProjectSettings
    {
        internal const string RelativePath = "ProjectSettings/GddManager.json";

        public string serverUrl = GddServer.DefaultUrl;
        public string projectId;
        /// <summary>Only so the file reads well in a diff; the id is what counts.</summary>
        public string projectTitle;

        public bool IsLinked => !string.IsNullOrEmpty(projectId);
        public string Server => GddServer.Normalize(serverUrl);

        static string DefaultPath => Path.Combine(Directory.GetCurrentDirectory(), RelativePath);

        public static GddProjectSettings Load() => Load(DefaultPath);

        public static GddProjectSettings Load(string path)
        {
            if (!File.Exists(path)) return new GddProjectSettings();
            try
            {
                return JsonUtility.FromJson<GddProjectSettings>(File.ReadAllText(path)) ?? new GddProjectSettings();
            }
            catch (ArgumentException e)
            {
                Debug.LogWarning($"[GDD Manager] {RelativePath} is not valid JSON and was ignored: {e.Message}");
                return new GddProjectSettings();
            }
        }

        public void Save() => Save(DefaultPath);

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(this, prettyPrint: true) + "\n");
        }
    }
}
