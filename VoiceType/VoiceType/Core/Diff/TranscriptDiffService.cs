using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using VoiceType.Models;

namespace VoiceType.Core.Diff
{
    /// <summary>
    /// Produces a word/token-aware diff between the raw ("You spoke") and post-processed
    /// ("Final text") transcripts, expressed as semantic <see cref="HighlightSpan"/>s over each
    /// source string. Deliberately independent of any UI/persistence concern so it can be unit
    /// tested in isolation.
    /// </summary>
    public static class TranscriptDiffService
    {
        // Splits into "word" tokens (letters/digits/underscore runs) or single non-whitespace,
        // non-word characters (punctuation). Whitespace is skipped entirely - it is never part of
        // a highlight span, matching the "leave unchanged text plain" requirement.
        private static readonly Regex TokenPattern = new(@"\w+|[^\w\s]", RegexOptions.Compiled);

        private readonly record struct Token(string Text, int Start, int Length);

        /// <summary>
        /// Computes highlight spans for both the spoken and final text based on a token-level diff.
        /// </summary>
        public static (List<HighlightSpan> SpokenHighlights, List<HighlightSpan> FinalHighlights) BuildHighlights(
            string spokenText, string finalText)
        {
            spokenText ??= string.Empty;
            finalText ??= string.Empty;

            var spokenTokens = Tokenize(spokenText);
            var finalTokens = Tokenize(finalText);

            var ops = ComputeDiffOps(spokenTokens, finalTokens);

            var spokenHighlights = new List<HighlightSpan>();
            var finalHighlights = new List<HighlightSpan>();

            // Walk the op list, pairing up adjacent Delete/Insert runs as "Modified" (a replacement
            // or normalization) rather than separate Removed/Added tokens, per spec.
            for (int i = 0; i < ops.Count;)
            {
                var op = ops[i];
                if (op.Kind == DiffOpKind.Equal)
                {
                    // Tokens matched case-insensitively but differ in case (e.g. sentence
                    // capitalization) still represent a real post-processing change, so surface
                    // them as Modified rather than silently treating them as unchanged.
                    var spokenToken = spokenTokens[op.AIndex];
                    var finalToken = finalTokens[op.BIndex];
                    if (!string.Equals(spokenToken.Text, finalToken.Text, StringComparison.Ordinal))
                    {
                        spokenHighlights.Add(new HighlightSpan(spokenToken.Start, spokenToken.Length, HighlightKind.Modified));
                        finalHighlights.Add(new HighlightSpan(finalToken.Start, finalToken.Length, HighlightKind.Modified));
                    }

                    i++;
                    continue;
                }

                // Gather the contiguous run of Delete/Insert ops starting here. A lone single-char
                // punctuation Equal (e.g. a shared trailing '.') is "bridged" over rather than
                // treated as a run boundary when edits continue right after it, so a
                // delete+insert split only by incidental shared punctuation (see
                // docs/DESIGN_DECISIONS.md) still pairs up as Modified instead of separate
                // Removed/Added spans.
                var deletes = new List<Token>();
                var inserts = new List<Token>();
                while (i < ops.Count)
                {
                    if (ops[i].Kind == DiffOpKind.Delete)
                    {
                        deletes.Add(spokenTokens[ops[i].AIndex]);
                        i++;
                        continue;
                    }
                    if (ops[i].Kind == DiffOpKind.Insert)
                    {
                        inserts.Add(finalTokens[ops[i].BIndex]);
                        i++;
                        continue;
                    }

                    // Equal op: bridge over it only if it's a single punctuation character and
                    // more edits immediately follow; otherwise this Equal ends the run.
                    if (IsBridgeablePunctuation(ops[i], spokenTokens) && i + 1 < ops.Count && ops[i + 1].Kind != DiffOpKind.Equal)
                    {
                        i++;
                        continue;
                    }
                    break;
                }

                // A run with both deletes and inserts represents a single replacement (e.g. a
                // multi-word phrase collapsing into one punctuation mark, or vice versa) - mark
                // every token on both sides as Modified rather than only pairing them off
                // index-by-index and calling the remainder Removed/Added, which would otherwise
                // misleadingly split one replacement into "changed" + "deleted" pieces.
                if (deletes.Count > 0 && inserts.Count > 0)
                {
                    foreach (var deleted in deletes)
                        spokenHighlights.Add(new HighlightSpan(deleted.Start, deleted.Length, HighlightKind.Modified));
                    foreach (var inserted in inserts)
                        finalHighlights.Add(new HighlightSpan(inserted.Start, inserted.Length, HighlightKind.Modified));
                }
                else
                {
                    foreach (var deleted in deletes)
                        spokenHighlights.Add(new HighlightSpan(deleted.Start, deleted.Length, HighlightKind.Removed));
                    foreach (var inserted in inserts)
                        finalHighlights.Add(new HighlightSpan(inserted.Start, inserted.Length, HighlightKind.Added));
                }
            }

            return (spokenHighlights, finalHighlights);
        }

        // True when the given Equal op's matched token is a single non-word character (i.e. one
        // punctuation mark). Used to let such a token be "bridged" over instead of ending a
        // Delete/Insert run when edits continue immediately after it.
        private static bool IsBridgeablePunctuation(DiffOp equalOp, List<Token> spokenTokens)
        {
            var text = spokenTokens[equalOp.AIndex].Text;
            return text.Length == 1 && !char.IsLetterOrDigit(text[0]);
        }

        private static List<Token> Tokenize(string text)
        {
            var tokens = new List<Token>();
            foreach (Match m in TokenPattern.Matches(text))
                tokens.Add(new Token(m.Value, m.Index, m.Length));
            return tokens;
        }

        private enum DiffOpKind { Equal, Delete, Insert }

        private readonly record struct DiffOp(DiffOpKind Kind, int AIndex, int BIndex);

        // Classic LCS-based diff via a DP table, then backtrack to produce an edit script. Token
        // lists for a single dictated utterance are small, so the O(n*m) table is negligible.
        private static List<DiffOp> ComputeDiffOps(List<Token> a, List<Token> b)
        {
            int n = a.Count, m = b.Count;
            var lcs = new int[n + 1, m + 1];
            for (int i = n - 1; i >= 0; i--)
            {
                for (int j = m - 1; j >= 0; j--)
                {
                    lcs[i, j] = string.Equals(a[i].Text, b[j].Text, StringComparison.OrdinalIgnoreCase)
                        ? lcs[i + 1, j + 1] + 1
                        : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
                }
            }

            var ops = new List<DiffOp>();
            int x = 0, y = 0;
            while (x < n && y < m)
            {
                if (string.Equals(a[x].Text, b[y].Text, StringComparison.OrdinalIgnoreCase))
                {
                    ops.Add(new DiffOp(DiffOpKind.Equal, x, y));
                    x++; y++;
                }
                else if (lcs[x + 1, y] >= lcs[x, y + 1])
                {
                    ops.Add(new DiffOp(DiffOpKind.Delete, x, -1));
                    x++;
                }
                else
                {
                    ops.Add(new DiffOp(DiffOpKind.Insert, -1, y));
                    y++;
                }
            }
            while (x < n) { ops.Add(new DiffOp(DiffOpKind.Delete, x, -1)); x++; }
            while (y < m) { ops.Add(new DiffOp(DiffOpKind.Insert, -1, y)); y++; }

            return ops;
        }
    }
}
