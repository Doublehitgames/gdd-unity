using System;

// Shapes of the GDD Manager REST API (/api/v1). JsonUtility ignores fields the
// server sends and these do not declare, so only what the plugin reads is here.
namespace Doublehitgames.Gdd.Editor.Api
{
    [Serializable]
    internal sealed class GddMe
    {
        public string id;
        public string email;
        public string displayName;
        public string authSource;

        public string Label => !string.IsNullOrEmpty(displayName) ? displayName : email;
    }

    [Serializable]
    internal sealed class GddProject
    {
        public string id;
        public string title;
        public string description;
        /// <summary>owner, editor or viewer.</summary>
        public string access;
        public string updatedAt;
    }

    [Serializable]
    internal sealed class GddSection
    {
        public string id;
        public string projectId;
        /// <summary>Empty for a top-level page.</summary>
        public string parentId;
        public string title;
        /// <summary>The page description as markdown.</summary>
        public string content;
        public int order;
        public string color;
        public string status;
        public string dataId;
        public string updatedAt;
        public string updatedByName;
    }

    // JsonUtility cannot read a top-level array or a generic envelope, hence one
    // wrapper per response shape.
    [Serializable] internal sealed class MeEnvelope { public GddMe data; }
    [Serializable] internal sealed class ProjectListEnvelope { public GddProject[] data; }
    [Serializable] internal sealed class SectionListEnvelope { public GddSection[] data; }
    [Serializable] internal sealed class SectionEnvelope { public GddSection data; }

    /// <summary>/api/v1 error body: <c>{ error: message, code }</c>.</summary>
    [Serializable]
    internal sealed class ApiErrorBody
    {
        public string error;
        public string code;
    }
}
