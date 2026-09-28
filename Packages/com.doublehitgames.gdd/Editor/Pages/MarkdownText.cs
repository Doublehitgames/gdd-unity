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

        /// <param name="isKnownPage">Whether a $[Title] names a page that exists, so only those become links.</param>
        /// <param name="linkColor">Hex colour for links, e.g. "#4C9AFF".</param>
        public static string ToRichText(string markdown, Func<string, bool> isKnownPage, string linkColor)
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
                    sb.Append($"<size={size}%><b>").Append(Inline(heading.Groups[2].Value, isKnownPage, linkColor)).Append("</b></size>");
                    continue;
                }

                var bullet = Bullet.Match(raw);
                if (bullet.Success)
                {
                    sb.Append(bullet.Groups[1].Value).Append("• ").Append(Inline(bullet.Groups[2].Value, isKnownPage, linkColor));
                    continue;
                }

                var quote = Quote.Match(raw);
                if (quote.Success)
                {
                    sb.Append("<i>│ ").Append(Inline(quote.Groups[1].Value, isKnownPage, linkColor)).Append("</i>");
                    continue;
                }

                sb.Append(Inline(raw, isKnownPage, linkColor));
            }
            return sb.ToString();
        }

        static string Inline(string text, Func<string, bool> isKnownPage, string linkColor)
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
                var title = m.Groups[1].Value;
                return isKnownPage(title)
                    ? $"<link=\"{RefLinkPrefix}{title}\"><color={linkColor}><u>{title}</u></color></link>"
                    : title;
            });
            text = Bold.Replace(text, "<b>$1</b>");
            text = Italic.Replace(text, "<i>$1</i>");
            return text;
        }
    }
}
