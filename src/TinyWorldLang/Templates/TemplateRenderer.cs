using System;
using System.Text;
using TinyWorldLang.Values;

namespace TinyWorldLang.Templates
{
    /// <summary>
    /// Renders a string value's Mustache template against its owning entity. A subset:
    /// <c>{{rel}}</c> (escaped), <c>{{{rel}}}</c> / <c>{{&amp; rel}}</c> (raw),
    /// <c>{{# rel}}...{{/}}</c> (section over a set), and <c>{{.}}</c> (current value).
    /// </summary>
    /// <remarks>
    /// The escaper is host-supplied (TWL is a source of truth, not a renderer, so it
    /// does not assume HTML). Names resolve to the owning entity's relations; a
    /// multi-value relation renders only its canonical-first value unless inside a
    /// <c>{{# }}</c> section.
    /// </remarks>
    public sealed class TemplateRenderer
    {
        private readonly Func<string, string, ValueSet> _resolve; // (entity, relation) -> set
        private readonly Func<string, string> _escape;

        public TemplateRenderer(Func<string, string, ValueSet> resolveRelation, Func<string, string>? escaper = null)
        {
            _resolve = resolveRelation ?? throw new ArgumentNullException(nameof(resolveRelation));
            _escape = escaper ?? (s => s); // default: no escaping (plain text / JSON target)
        }

        /// <summary>Render <paramref name="source"/> (raw, with escapes intact) against <paramref name="owner"/>.</summary>
        public string Render(string source, string owner)
        {
            var sb = new StringBuilder();
            RenderInto(sb, source, 0, source.Length, owner, Value.Null);
            return sb.ToString();
        }

        private void RenderInto(StringBuilder sb, string s, int start, int end, string owner, Value current)
        {
            int i = start;
            while (i < end)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < end)
                {
                    // Lexical escapes kept raw by the parser: decode here.
                    char n = s[i + 1];
                    switch (n)
                    {
                        case '{': sb.Append('{'); i += 2; continue; // literal '{', never a tag
                        case '"': sb.Append('"'); i += 2; continue;
                        case '\\': sb.Append('\\'); i += 2; continue;
                        case 'n': sb.Append('\n'); i += 2; continue;
                        case 't': sb.Append('\t'); i += 2; continue;
                    }
                }

                if (c == '{' && i + 1 < end && s[i + 1] == '{')
                {
                    i = RenderTag(sb, s, i, end, owner, current);
                    continue;
                }

                sb.Append(c);
                i++;
            }
        }

        private int RenderTag(StringBuilder sb, string s, int i, int end, string owner, Value current)
        {
            // i points at '{{'
            bool tripleRaw = i + 2 < end && s[i + 2] == '{';
            int tagStart = i + (tripleRaw ? 3 : 2);
            string close = tripleRaw ? "}}}" : "}}";
            int tagEnd = s.IndexOf(close, tagStart, StringComparison.Ordinal);
            if (tagEnd < 0) throw new TwlException("unterminated template tag '{{'");
            string body = s.Substring(tagStart, tagEnd - tagStart).Trim();
            int afterTag = tagEnd + close.Length;

            if (body.StartsWith("#"))
            {
                string rel = body.Substring(1).Trim();
                return RenderSection(sb, s, afterTag, end, owner, rel);
            }
            if (body.StartsWith("&"))
            {
                string rel = body.Substring(1).Trim();
                sb.Append(RenderName(rel, owner, current, escape: false));
                return afterTag;
            }
            if (tripleRaw)
            {
                sb.Append(RenderName(body, owner, current, escape: false));
                return afterTag;
            }
            sb.Append(RenderName(body, owner, current, escape: true));
            return afterTag;
        }

        private int RenderSection(StringBuilder sb, string s, int blockStart, int end, string owner, string rel)
        {
            // Find the matching {{/}} (supporting nesting of same-or-other sections).
            int depth = 1;
            int scan = blockStart;
            int blockEnd = -1;
            while (scan < end)
            {
                int open = s.IndexOf("{{", scan, StringComparison.Ordinal);
                if (open < 0 || open >= end) break;
                int tagClose = s.IndexOf("}}", open + 2, StringComparison.Ordinal);
                if (tagClose < 0) break;
                string body = s.Substring(open + 2, tagClose - (open + 2)).Trim();
                if (body.StartsWith("#")) depth++;
                else if (body == "/" || body.StartsWith("/")) { depth--; if (depth == 0) { blockEnd = open; scan = tagClose + 2; break; } }
                scan = tagClose + 2;
            }
            if (blockEnd < 0) throw new TwlException($"unclosed section '{{{{# {rel}}}}}'");

            var set = _resolve(owner, rel);
            foreach (var v in set)
            {
                string childOwner = v.Kind == ValueKind.Entity ? v.AsEntity : owner;
                RenderInto(sb, s, blockStart, blockEnd, childOwner, v);
            }
            return scan; // position after the closing {{/}}
        }

        private string RenderName(string name, string owner, Value current, bool escape)
        {
            string text;
            if (name == ".")
            {
                text = RenderValue(current, owner);
            }
            else
            {
                var set = _resolve(owner, name);
                text = set.IsEmpty ? "" : RenderValue(set.One(), owner);
            }
            return escape ? _escape(text) : text;
        }

        /// <summary>Render a single value to text; a string value is itself a template, rendered against its owner.</summary>
        private string RenderValue(Value v, string owner)
        {
            switch (v.Kind)
            {
                case ValueKind.Null: return "";
                case ValueKind.String: return Render(v.AsString, owner);
                case ValueKind.Entity: return v.AsEntity;
                default: return v.ToString();
            }
        }
    }
}
