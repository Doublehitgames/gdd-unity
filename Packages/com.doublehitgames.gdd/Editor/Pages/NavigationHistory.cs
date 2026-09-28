using System;
using System.Collections.Generic;
using UnityEngine;

namespace Doublehitgames.Gdd.Editor.Pages
{
    /// <summary>
    /// Back and forward through the pages visited, like a browser. Serializable
    /// so it survives the domain reloads Unity does on every script change.
    /// </summary>
    [Serializable]
    internal sealed class NavigationHistory
    {
        public const int Limit = 100;

        [SerializeField] List<string> _back = new List<string>();
        [SerializeField] List<string> _forward = new List<string>();
        [SerializeField] string _current;

        public string Current => string.IsNullOrEmpty(_current) ? null : _current;
        public bool CanGoBack => _back.Count > 0;
        public bool CanGoForward => _forward.Count > 0;

        /// <summary>A new page was opened by the user: it becomes current and the forward trail is dropped.</summary>
        public void Visit(string id)
        {
            if (string.IsNullOrEmpty(id) || id == Current) return;
            if (Current != null)
            {
                _back.Add(Current);
                if (_back.Count > Limit) _back.RemoveAt(0);
            }
            _forward.Clear();
            _current = id;
        }

        /// <returns>The page to show, or null when there is nowhere to go.</returns>
        public string GoBack() => Move(_back, _forward);

        public string GoForward() => Move(_forward, _back);

        /// <summary>Drops pages that no longer exist (deleted since the last load).</summary>
        public void Prune(Func<string, bool> exists)
        {
            _back.RemoveAll(id => !exists(id));
            _forward.RemoveAll(id => !exists(id));
            if (Current != null && !exists(Current)) _current = null;
            // Pruning can leave the same page twice in a row; it would take two clicks to pass.
            Collapse(_back);
            Collapse(_forward);
            if (_back.Count > 0 && _back[_back.Count - 1] == Current) _back.RemoveAt(_back.Count - 1);
            if (_forward.Count > 0 && _forward[_forward.Count - 1] == Current) _forward.RemoveAt(_forward.Count - 1);
        }

        string Move(List<string> from, List<string> to)
        {
            if (from.Count == 0) return null;
            var target = from[from.Count - 1];
            from.RemoveAt(from.Count - 1);
            if (Current != null) to.Add(Current);
            _current = target;
            return target;
        }

        static void Collapse(List<string> list)
        {
            for (var i = list.Count - 1; i > 0; i--)
                if (list[i] == list[i - 1]) list.RemoveAt(i);
        }
    }
}
