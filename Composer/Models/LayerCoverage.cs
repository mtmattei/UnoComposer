using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Composer.Models;

public sealed record SectionCoverage(SectionSpec Section, bool Present, bool HasContent);

// Coverage report for one layer. Total/Covered drive the right-rail counter
// and dot strip; Missing drives the section-aware refine chips in the footer
// and the +N more flyout.
public sealed record LayerCoverage(string LayerId, ImmutableArray<SectionCoverage> Sections)
{
    public int Total => Sections.Length;
    public int Covered => Sections.Count(s => s.Present && s.HasContent);

    // Layers with no required sections (currently only "scaffold") are
    // always complete — UI hides the badge entirely when Total == 0.
    public bool IsComplete => Total == 0 || Covered == Total;

    public ImmutableArray<SectionSpec> Missing => Sections
        .Where(s => !s.Present || !s.HasContent)
        .Select(s => s.Section)
        .ToImmutableArray();

    public static LayerCoverage Empty(string layerId)
        => new(layerId, ImmutableArray<SectionCoverage>.Empty);
}

// Pure parser — extracts H2 headings from rendered markdown and matches them
// against the per-layer schema. Heading match is normalized for two common
// generator quirks: numeric prefixes (design's "## 1. Imagery") and em-dash
// vs double-hyphen (implementation's "## Phase 1 — Scaffold").
public static partial class LayerCoverageAnalyzer
{
    [GeneratedRegex(@"^##\s+(.+?)\s*$", RegexOptions.Multiline)]
    private static partial Regex H2Regex();

    [GeneratedRegex(@"^\d+\.\s+")]
    private static partial Regex NumericPrefixRegex();

    public static LayerCoverage Analyze(string layerId, string? markdown)
    {
        var schema = LayerSectionSchemas.For(layerId);
        if (schema.RequiredSections.IsDefaultOrEmpty)
            return LayerCoverage.Empty(layerId);

        var doc = markdown ?? string.Empty;
        var headings = ExtractH2Headings(doc);

        var builder = ImmutableArray.CreateBuilder<SectionCoverage>(schema.RequiredSections.Length);
        foreach (var spec in schema.RequiredSections)
        {
            var (present, body) = TryFindHeading(spec, headings, doc);
            var hasContent = present && IsRealContent(body);
            builder.Add(new SectionCoverage(spec, present, hasContent));
        }
        return new LayerCoverage(layerId, builder.ToImmutable());
    }

    private readonly record struct H2Hit(string Normalized, string Original, int LineStart, int BodyStart);

    private static List<H2Hit> ExtractH2Headings(string markdown)
    {
        var hits = new List<H2Hit>();
        foreach (Match m in H2Regex().Matches(markdown))
        {
            var raw = m.Groups[1].Value.Trim();
            var normalized = NormalizeHeading(raw);
            hits.Add(new H2Hit(normalized, raw, m.Index, m.Index + m.Length));
        }
        return hits;
    }

    // Normalize headings for matching: strip leading "<n>. " numeric prefix
    // (design uses "## 1. Imagery") and collapse em-dash to "--" so
    // "Phase 1 — Scaffold" matches a schema row of "Phase 1 -- Scaffold".
    private static string NormalizeHeading(string raw)
    {
        var stripped = NumericPrefixRegex().Replace(raw, string.Empty);
        return stripped.Replace("—", "--").Replace("–", "--").Trim();
    }

    private static (bool present, string body) TryFindHeading(SectionSpec spec, List<H2Hit> headings, string markdown)
    {
        if (headings.Count == 0) return (false, string.Empty);

        int? matchIdx = null;
        var target = NormalizeHeading(spec.Heading);

        switch (spec.Match)
        {
            case SectionMatch.AnyFirst:
                matchIdx = 0;
                break;
            case SectionMatch.Prefix:
                for (int i = 0; i < headings.Count; i++)
                {
                    if (headings[i].Normalized.StartsWith(target, StringComparison.OrdinalIgnoreCase))
                    {
                        matchIdx = i; break;
                    }
                }
                break;
            default: // Exact
                for (int i = 0; i < headings.Count; i++)
                {
                    if (string.Equals(headings[i].Normalized, target, StringComparison.OrdinalIgnoreCase))
                    {
                        matchIdx = i; break;
                    }
                }
                break;
        }

        if (matchIdx is null) return (false, string.Empty);

        var hit = headings[matchIdx.Value];
        var nextStart = matchIdx.Value + 1 < headings.Count
            ? headings[matchIdx.Value + 1].LineStart
            : markdown.Length;
        var body = markdown.Substring(hit.BodyStart, Math.Max(0, nextStart - hit.BodyStart));
        return (true, body);
    }

    private static bool IsRealContent(string body)
    {
        var trimmed = (body ?? string.Empty).Trim();
        if (trimmed.Length == 0) return false;
        // Section body that is exactly the canonical placeholder (or that
        // placeholder followed by a small amount of trailing whitespace /
        // separator) doesn't count as covered — the AI / user still has to
        // write real content.
        if (trimmed.Equals(LayerSectionSchemas.PendingPlaceholder, StringComparison.Ordinal))
            return false;
        if (trimmed.StartsWith(LayerSectionSchemas.PendingPlaceholder, StringComparison.Ordinal)
            && trimmed.Length <= LayerSectionSchemas.PendingPlaceholder.Length + 8)
            return false;
        return true;
    }
}
