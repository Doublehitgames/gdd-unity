using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Doublehitgames.Gdd.Editor.Pages
{
    /// <summary>
    /// Turns a page description (markdown) into UI Toolkit rich text. Deliberately
    /// small: headings, bold, italics, inline code, lists, quotes and page
    /// references. Anything else shows as the markdown it is, which reads fine.
    /// </summary>
    internal static class MarkdownText
    {
        public const string RefLinkPrefix = "ref:";
        const string EscapedLt = "<noparse><</noparse>";

        static readonly Regex Heading = new Regex(@"^(#{1,6})\s+(.*)$");
        static readonly Regex Bullet = new Regex(@"^(\s*)[-*+]\s+(.*)$");
        static readonly Regex Quote = new Regex(@"^>\s?(.*)$");
        static readonly Regex Bold = new Regex(@"\*\*(.+?)\*\*");
        static readonly Regex Italic = new Regex(@"(?<![\w*])\*(?!\s)(.+?)(?<!\s)\*(?![\w*])");
        static readonly Regex Code = new Regex(@"`([^`]+)`");
        static readonly Regex Image = new Regex(@"!\[([^\]]*)\]\([^)]*\)");
        static readonly Regex Link = new Regex(@"(?<!!)\[([^\]]+)\]\(([^)\s]+)\)");
        static readonly Regex PageRef = new Regex(@"\$\[([^\]]+)\]");

        static readonly int[] HeadingSizes = { 160, 140, 120, 110, 100, 100 };

        /// <param name="pages">Resolves $[Title] and $[#id]; only references it finds become links.</param>
        /// <param name="linkColor">Hex colour for links, e.g. "#4C9AFF".</param>
        public static string ToRichText(string markdown, PageIndex pages, string linkColor)
        {
            if (string.IsNullOrEmpty(markdown)) return "";

            var sb = new StringBuilder();
            var inCodeBlock = false;
            foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
            {
                if (raw.TrimStart().StartsWith("```"))
                {
                    inCodeBlock = !inCodeBlock;
                    continue;
                }
                if (sb.Length > 0) sb.Append('\n');
                if (inCodeBlock)
                {
                    sb.Append("<noparse>").Append(raw).Append("</noparse>");
                    continue;
                }

                var heading = Heading.Match(raw);
                if (heading.Success)
                {
                    var size = HeadingSizes[heading.Groups[1].Length - 1];
                    sb.Append($"<size={size}%><b>").Append(Inline(heading.Groups[2].Value, pages, linkColor)).Append("</b></size>");
                    continue;
                }

                var bullet = Bullet.Match(raw);
                if (bullet.Success)
                {
                    sb.Append(bullet.Groups[1].Value).Append("• ").Append(Inline(bullet.Groups[2].Value, pages, linkColor));
                    continue;
                }

                var quote = Quote.Match(raw);
                if (quote.Success)
                {
                    sb.Append("<i>│ ").Append(Inline(quote.Groups[1].Value, pages, linkColor)).Append("</i>");
                    continue;
                }

                sb.Append(Inline(raw, pages, linkColor));
            }
            return sb.ToString();
        }

        /// <summary>
        /// The start of a description, for places with little room: whole lines
        /// up to about <paramref name="maxChars"/>, or a line cut at a word when
        /// the first one alone is longer. Never cuts a page reference in half.
        /// </summary>
        public static string Excerpt(string markdown, int maxChars, out bool truncated)
        {
            var text = (markdown ?? "").Replace("\r\n", "\n").Trim();
            truncated = text.Length > maxChars;
            if (!truncated) return text;

            var cut = text.LastIndexOf('\n', maxChars);
            var atLine = cut >= maxChars / 2;
            if (!atLine)
            {
                cut = text.LastIndexOf(' ', maxChars);
                if (cut < maxChars / 2) cut = maxChars;
            }
            var openRef = text.LastIndexOf("$[", cut, StringComparison.Ordinal);
            if (openRef >= 0 && text.IndexOf(']', openRef) >= cut) cut = openRef;
            if (cut > 0 && char.IsHighSurrogate(text[cut - 1])) cut--;

            var excerpt = text.Substring(0, cut).TrimEnd();
            return atLine ? excerpt : excerpt + "…";
        }

        // For text that comes from elsewhere (a referenced page's title) and is
        // inserted after the line was escaped.
        static string Escape(string s) => (s ?? "").Replace("<", EscapedLt);

        static string Inline(string text, PageIndex pages, string linkColor)
        {
            // Escape first, so a literal "<b>" in the document stays text. The
            // markdown markers below contain no "<", so escaping cannot break them.
            text = text.Replace("<", EscapedLt);

            text = Image.Replace(text, m => $"<i>[image{(m.Groups[1].Length > 0 ? ": " + m.Groups[1].Value : "")}]</i>");
            // Code is already inside <noparse>, so it takes its "<" back unescaped.
            text = Code.Replace(text, m => "<mark=#8080802A><noparse>" + m.Groups[1].Value.Replace(EscapedLt, "<") + "</noparse></mark>");
            text = Link.Replace(text, m => $"<link=\"{m.Groups[2].Value}\"><color={linkColor}><u>{m.Groups[1].Value}</u></color></link>");
            text = PageRef.Replace(text, m =>
            {
                var reference = m.Groups[1].Value;
                var page = pages.Resolve(reference);
                if (page != null)
                    return $"<link=\"{RefLinkPrefix}{page.id}\"><color={linkColor}><u>{Escape(page.title)}</u></color></link>";
                // An id says nothing to a reader; a title is still worth showing.
                return reference.TrimStart().StartsWith("#") ? "<i>[missing page]</i>" : reference;
            });
            text = Bold.Replace(text, "<b>$1</b>");
            text = Italic.Replace(text, "<i>$1</i>");
            return text;
        }
    }
}
