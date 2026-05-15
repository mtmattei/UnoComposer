using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Composer.Models;
using Microsoft.Extensions.Options;

namespace Composer.Services;

/// <summary>
/// Anthropic-backed layer preview + initial-seed service. Both entry points
/// dispatch by layer id to a layer-specific JSON contract or markdown contract,
/// send to Sonnet via the Refit <see cref="IAnthropicClient"/>, and parse the
/// response back into the layer's canonical value type. UX and Design route
/// through the vision endpoint when reference screenshots are supplied so
/// Sonnet can see the user's reference images.
///
/// Identity fallback (no API key, network error, parse failure): preview
/// returns the current values unchanged with "Showing your edits as proposed.";
/// initial-seed returns null so the caller falls back to the layer's hardcoded
/// default (typed) or the markdown generator (markdown layers).
/// </summary>
public sealed class LayerPreviewService : ILayerPreviewService
{
    private const int MaxVisionImages = 4;
    private const long MaxVisionImageBytes = 4 * 1024 * 1024;

    private readonly IAnthropicClient _api;
    private readonly IOptions<AnthropicConfig> _options;

    private AnthropicConfig Cfg => _options.Value;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    public LayerPreviewService(IAnthropicClient api, IOptions<AnthropicConfig> options)
    {
        _api = api;
        _options = options;
    }

    // ────────────────────────────────────────────────────────────────────
    //  GenerateInitialAsync — Intent-locked seeding
    // ────────────────────────────────────────────────────────────────────

    public async Task<object?> GenerateInitialAsync(
        string layerId,
        Intent intent,
        ImmutableArray<string> screenshotPaths,
        CancellationToken ct = default)
    {
        var key = Cfg.ApiKey;
        if (string.IsNullOrWhiteSpace(key)) return null;
        if (intent is null) return null;

        try
        {
            var (system, prompt) = BuildSeedPrompt(layerId, intent);
            if (system is null) return null;

            // For markdown-shaped seeds (everything except the design typed
            // layer and intent), prepend the schema rules so the very first
            // generation already covers every required heading.
            if (!IsTypedLayer(layerId) && layerId != "intent" && layerId != "scaffold")
            {
                var schemaBlock = BuildSchemaBlock(layerId);
                if (!string.IsNullOrEmpty(schemaBlock))
                    system = schemaBlock + system;
            }

            var groundingBlock = UnoDocsGrounding.BuildPromptBlock(layerId);
            if (!string.IsNullOrEmpty(groundingBlock))
                system = groundingBlock + system;

            var useVision = !screenshotPaths.IsDefaultOrEmpty
                            && (layerId == "ux" || layerId == "design");

            string? text;
            if (useVision)
            {
                text = await SendVisionAsync(system, prompt, screenshotPaths, key, ct).ConfigureAwait(false);
            }
            else
            {
                var req = new MessagesRequest(
                    model: Cfg.Model,
                    max_tokens: 2000,
                    messages: new[] { new MessagesContent("user", prompt) },
                    system: system,
                    temperature: 0.4);
                var resp = await _api.CreateMessageAsync(req, key, Cfg.Version, ct).ConfigureAwait(false);
                text = resp?.content?.FirstOrDefault(b => b.type == "text")?.text;
            }

            if (string.IsNullOrWhiteSpace(text)) return null;

            return ParseSeed(layerId, text);
        }
        catch
        {
            return null;
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  GeneratePreviewAsync — refine on user prompt
    // ────────────────────────────────────────────────────────────────────

    public async Task<LayerPreviewResult> GeneratePreviewAsync(
        string layerId,
        object currentValues,
        string userPrompt,
        IReadOnlyDictionary<string, string> lockedContextSummaries,
        CancellationToken ct = default)
    {
        var key = Cfg.ApiKey;
        if (string.IsNullOrWhiteSpace(key))
            return Identity(layerId, currentValues);

        try
        {
            var label = LayerLabel(layerId);
            var purpose = LayerPurpose(layerId);
            var isTyped = IsTypedLayer(layerId);

            var lockedBlock = lockedContextSummaries.Count == 0
                ? "(none yet — this is one of the earlier layers)"
                : string.Join("\n", lockedContextSummaries.Select(kv => $"- {LayerLabel(kv.Key)}: {kv.Value}"));

            // Intent layer: AI synthesizes a 2-4 sentence prose overview from
            // the current field values + optional refinement prompt. The four
            // Intent fields stay as the user typed them — the overview is a
            // separate piece of state. Returns string in ProposedValues.
            if (layerId == "intent" && currentValues is IntentPreviewInput inp)
                return await GenerateIntentOverviewAsync(inp, userPrompt, lockedBlock, key, ct).ConfigureAwait(false);

            string currentJson;
            string instructions;
            if (isTyped)
            {
                currentJson = JsonSerializer.Serialize(currentValues, currentValues.GetType(), JsonOpts);
                instructions =
                    "You will receive the current state as JSON, and a user prompt describing how they want it changed. " +
                    "Return JSON of the SAME shape with your proposed refinement, plus a one-line summary.\n\n" +
                    "Respond with EXACTLY this JSON structure and nothing else:\n" +
                    "{\"proposed\": <same shape as input>, \"summary\": \"<one-line headline>\"}";
            }
            else
            {
                currentJson = currentValues as string ?? string.Empty;
                instructions =
                    "You will receive the current markdown body for this layer and a user prompt describing how they want it changed. " +
                    "Return refined markdown of the SAME general structure, plus a one-line summary.\n\n" +
                    "Respond with EXACTLY this JSON structure and nothing else (proposed is a JSON string containing the markdown body):\n" +
                    "{\"proposed\": \"<markdown body as JSON string>\", \"summary\": \"<one-line headline>\"}";
            }

            var schemaBlock = isTyped ? string.Empty : BuildSchemaBlock(layerId);
            var groundingBlock = UnoDocsGrounding.BuildPromptBlock(layerId);

            var system =
                $"You are refining the {label} layer of a Uno Platform app composition.\n\n" +
                $"Layer purpose: {purpose}\n\n" +
                $"Locked context from prior layers:\n{lockedBlock}\n\n" +
                schemaBlock +
                groundingBlock +
                instructions;

            var prompt = isTyped
                ? $"Current state:\n{currentJson}\n\nUser prompt:\n{userPrompt}"
                : $"Current markdown:\n{currentJson}\n\nUser prompt:\n{userPrompt}";

            var req = new MessagesRequest(
                model: Cfg.Model,
                max_tokens: 2000,
                messages: new[] { new MessagesContent("user", prompt) },
                system: system,
                temperature: 0.3);

            var resp = await _api.CreateMessageAsync(req, key, Cfg.Version, ct).ConfigureAwait(false);
            var text = resp?.content?.FirstOrDefault(b => b.type == "text")?.text;
            if (string.IsNullOrWhiteSpace(text))
                return Identity(layerId, currentValues);

            var json = ExtractJsonObject(text);
            if (string.IsNullOrEmpty(json))
                return Identity(layerId, currentValues);

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("proposed", out var proposedEl))
                return Identity(layerId, currentValues);

            object? proposedObj;
            if (isTyped)
            {
                proposedObj = JsonSerializer.Deserialize(proposedEl.GetRawText(), currentValues.GetType(), JsonOpts);
            }
            else
            {
                proposedObj = proposedEl.ValueKind == JsonValueKind.String
                    ? proposedEl.GetString()
                    : proposedEl.GetRawText();
            }
            if (proposedObj is null)
                return Identity(layerId, currentValues);

            var summary = doc.RootElement.TryGetProperty("summary", out var sumEl) && sumEl.ValueKind == JsonValueKind.String
                ? sumEl.GetString() ?? "Preview ready."
                : "Preview ready.";

            return new LayerPreviewResult(proposedObj, summary);
        }
        catch
        {
            return Identity(layerId, currentValues);
        }
    }

    private async Task<LayerPreviewResult> GenerateIntentOverviewAsync(
        IntentPreviewInput inp, string userPrompt, string lockedBlock, string key, CancellationToken ct)
    {
        var system =
            "You synthesize a concise project overview for a Uno Platform app composition.\n\n" +
            "Layer purpose: capture what the app is for and who it's for in plain prose.\n\n" +
            $"Locked context from prior layers:\n{lockedBlock}\n\n" +
            "You will receive the current intent fields (app type, primary user, workflow, platforms), " +
            "the previously-accepted overview (may be empty on first generation), and an optional refinement prompt.\n\n" +
            "Return a 2-4 sentence project overview that:\n" +
            "- Names the app type, primary user, and workflow concretely\n" +
            "- Calls out one or two design implications (e.g. \"offline-first because field technicians work in trucks\")\n" +
            "- Stays under ~280 characters total\n" +
            "- Uses plain prose only — no markdown, no headings, no bullet points, no code\n\n" +
            "Respond with EXACTLY this JSON structure and nothing else:\n" +
            "{\"proposed\": \"<2-4 sentence plain-prose overview as a JSON string>\", \"summary\": \"<one-line headline>\"}";

        var fields =
            $"App type: {inp.Intent.AppType}\n" +
            $"Primary user: {inp.Intent.PrimaryUser}\n" +
            $"Workflow: {inp.Intent.Workflow}\n" +
            $"Platforms: {inp.Intent.Platforms}";
        var prior = string.IsNullOrWhiteSpace(inp.CurrentOverview)
            ? "(none yet — first generation)"
            : inp.CurrentOverview;
        var refinement = string.IsNullOrWhiteSpace(userPrompt)
            ? "(no refinement — generate from fields)"
            : userPrompt;
        var prompt =
            $"Current intent fields:\n{fields}\n\n" +
            $"Previously-accepted overview:\n{prior}\n\n" +
            $"Refinement prompt:\n{refinement}";

        var req = new MessagesRequest(
            model: Cfg.Model,
            max_tokens: 600,
            messages: new[] { new MessagesContent("user", prompt) },
            system: system,
            temperature: 0.4);

        var resp = await _api.CreateMessageAsync(req, key, Cfg.Version, ct).ConfigureAwait(false);
        var text = resp?.content?.FirstOrDefault(b => b.type == "text")?.text;
        if (string.IsNullOrWhiteSpace(text))
            return new LayerPreviewResult(inp.CurrentOverview, "Showing your edits as proposed.");

        var json = ExtractJsonObject(text);
        if (string.IsNullOrEmpty(json))
            return new LayerPreviewResult(inp.CurrentOverview, "Showing your edits as proposed.");

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("proposed", out var proposedEl))
            return new LayerPreviewResult(inp.CurrentOverview, "Showing your edits as proposed.");

        var proposed = proposedEl.ValueKind == JsonValueKind.String
            ? proposedEl.GetString() ?? string.Empty
            : proposedEl.GetRawText();
        if (string.IsNullOrWhiteSpace(proposed))
            return new LayerPreviewResult(inp.CurrentOverview, "Showing your edits as proposed.");

        var summary = doc.RootElement.TryGetProperty("summary", out var sumEl) && sumEl.ValueKind == JsonValueKind.String
            ? sumEl.GetString() ?? "Overview ready."
            : "Overview ready.";

        return new LayerPreviewResult(proposed.Trim(), summary);
    }

    private static LayerPreviewResult Identity(string layerId, object currentValues)
    {
        // Intent's currentValues is an IntentPreviewInput wrapper — unwrap so
        // ApplyLayerValues' string-match branch can write the (unchanged)
        // overview back instead of silently no-op'ing on the wrapper type.
        if (layerId == "intent" && currentValues is IntentPreviewInput inp)
            return new(inp.CurrentOverview, "Showing your edits as proposed.");
        return new(currentValues, "Showing your edits as proposed.");
    }

    // ────────────────────────────────────────────────────────────────────
    //  Vision route — base64-attached reference screenshots
    // ────────────────────────────────────────────────────────────────────

    private async Task<string?> SendVisionAsync(
        string system, string prompt, ImmutableArray<string> paths, string key, CancellationToken ct)
    {
        var content = new List<object>();
        var added = 0;
        foreach (var path in paths)
        {
            if (added >= MaxVisionImages) break;
            try
            {
                if (!File.Exists(path)) continue;
                var info = new FileInfo(path);
                if (info.Length <= 0 || info.Length > MaxVisionImageBytes) continue;
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext is not ".png" and not ".jpg" and not ".jpeg" and not ".gif" and not ".webp") continue;

                var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
                var b64 = Convert.ToBase64String(bytes);
                var mime = ext switch
                {
                    ".png"  => "image/png",
                    ".gif"  => "image/gif",
                    ".webp" => "image/webp",
                    _       => "image/jpeg",
                };
                content.Add(new
                {
                    type = "image",
                    source = new { type = "base64", media_type = mime, data = b64 },
                });
                added++;
            }
            catch
            {
                // Skip unreadable files — best-effort vision.
            }
        }
        content.Add(new { type = "text", text = prompt });

        var visionReq = new
        {
            model = Cfg.Model,
            max_tokens = 2000,
            system,
            temperature = 0.4,
            messages = new[] { new { role = "user", content = content.ToArray() } },
        };

        var resp = await _api.CreateVisionMessageAsync(visionReq, key, Cfg.Version, ct).ConfigureAwait(false);
        return resp?.content?.FirstOrDefault(b => b.type == "text")?.text;
    }

    // ────────────────────────────────────────────────────────────────────
    //  Per-layer prompt + parse dispatch
    // ────────────────────────────────────────────────────────────────────

    // Required-section preamble appended to non-typed-layer system prompts.
    // Tells the model the schema for this layer and the four contractual
    // rules: preserve every required heading, fill missing or placeholder
    // sections, never delete a required heading, mark unknowns as
    // assumptions / risks rather than dropping them.
    private static string BuildSchemaBlock(string layerId)
    {
        var schema = LayerSectionSchemas.For(layerId);
        if (schema.RequiredSections.IsDefaultOrEmpty) return string.Empty;

        var headings = string.Join(
            "\n",
            schema.RequiredSections
                .Where(s => s.Match != SectionMatch.AnyFirst)
                .Select(s => $"- ## {s.Heading} — {s.Purpose}"));

        return
            "This layer has a required section schema. The required level-2 headings are:\n\n" +
            headings + "\n\n" +
            "Rules:\n" +
            "1. Preserve every required heading exactly. Do not rename them.\n" +
            "2. Fill missing or placeholder (\"" + LayerSectionSchemas.PendingPlaceholder + "\") sections when you can — derive content from the locked context if needed.\n" +
            "3. Never delete a required heading. If you have nothing concrete, keep the heading and write \"" + LayerSectionSchemas.PendingPlaceholder + "\" or call out the missing input as an assumption / risk.\n" +
            "4. You may add additional sections beyond the schema if useful, but they must come after all required ones.\n\n";
    }

    private static (string? system, string user) BuildSeedPrompt(string layerId, Intent intent)
    {
        var ctx =
            $"App type: {intent.AppType}\n" +
            $"Primary user: {intent.PrimaryUser}\n" +
            $"Workflow: {intent.Workflow}\n" +
            $"Platforms: {intent.Platforms}";

        return layerId switch
        {
            // Intent is the source — nothing to seed from itself.
            "intent" => (null, ctx),

            "ux" => (
                """
                You design primary user flows for a Uno Platform app. Given the app's
                intent below, produce the UX-flow markdown for this app: a 5-screen
                primary flow with screen names, one-line goals per screen, and a
                short "Why this flow" rationale.

                Use this structure exactly:
                # UX Flows

                ## <Flow name>

                Five screens in sequence:

                1. **Screen** — goal
                2. **Screen** — goal
                3. **Screen** — goal
                4. **Screen** — goal
                5. **Screen** — goal

                ## Why this flow

                <2 sentences>

                Respond with ONLY the markdown body (no preamble, no code fences).
                """,
                ctx),

            "design" => (
                """
                You curate Material design tokens for a Uno Platform app. Given the
                app's intent below, choose token values whose accent reflects the
                app's tone and can support a complete design.md brief covering:
                imagery, iconography, typography, state matrix, layout, page
                background, component inventory, and palette tokens. Use hex
                strings. Pick BodyFont from: Satoshi Variable or Inter.

                Reply with ONLY this JSON (no preamble, no fences):
                {
                  "Surface":  "#RRGGBB",
                  "Action":   "#RRGGBB",
                  "Info":     "#RRGGBB",
                  "Success":  "#RRGGBB",
                  "Warn":     "#RRGGBB",
                  "Panel":    "#RRGGBB",
                  "Tag":      "#RRGGBB",
                  "Locked":   "#RRGGBB",
                  "BodyFont": "Inter"
                }
                """,
                ctx),

            "architecture" => (
                """
                You shape an MVUX Uno Platform architecture brief. Given the app's
                intent, produce the architecture markdown with modules + connections
                + a one-line agent prompt for downstream codegen.

                Use this structure:
                # Architecture

                MVUX + Uno Extensions. <one-line summary>.

                ## Modules

                | Module | Role | Files |
                |---|---|---|
                | Pages | ... | N |
                | State (MVUX) | ... | N |
                | Navigation | ... | N |
                | Services | ... | N |
                | Storage | ... | N |

                ## Connections

                | From → To | Verb |
                |---|---|
                | Pages → State (MVUX) | binds |
                | ... | ... |

                ## Agent prompt

                <one paragraph>

                Respond with ONLY the markdown body.
                """,
                ctx),

            "interactions" => (
                """
                You define the canonical 6-state interaction spec for the primary
                flow of a Uno Platform app. Given the app's intent, produce the
                interactions markdown.

                Use this structure:
                # Interaction Spec

                ## Primary flow: <flow name>

                Six canonical states per screen — all required by the VSM contract.

                | State | Visual | Copy |
                |---|---|---|
                | Default | ... | ... |
                | Loading | ... | ... |
                | Empty | ... | ... |
                | Error | ... | ... |
                | Success | ... | ... |
                | Offline | ... | ... |

                ## Agent prompt

                <one paragraph>

                Respond with ONLY the markdown body.
                """,
                ctx),

            "data" => (
                """
                You shape data contracts for a Uno Platform app. Given the app's
                intent, produce the data-contracts markdown with 3 entity records
                (the domain entity, the actor, and a related shape like Schedule)
                and a short rationale.

                Use this structure:
                # Data Contracts

                ## Entities

                ```csharp
                public partial record <Entity>(
                    ... fields ...);

                public partial record <Actor>(
                    ... fields ...);

                public partial record Schedule(
                    DateOnly Day,
                    <Entity>[] <plural>);
                ```

                ## Why these shapes

                <2 sentences>

                Respond with ONLY the markdown body.
                """,
                ctx),

            "implementation" => (
                """
                You sequence a 6-phase implementation plan for a Uno Platform app.
                Given the app's intent, produce the implementation markdown with
                short agent prompts inline per phase (Scaffold / Shell / Domain /
                Screens / States / Polish).

                Use this structure:
                # Implementation Plan

                ## Phase 1 — Scaffold
                <output description>
                > <agent prompt>

                ## Phase 2 — Shell
                <output description>
                > <agent prompt>

                ## Phase 3 — Domain
                <output description>
                > <agent prompt>

                ## Phase 4 — Screens
                <output description>
                > <agent prompt>

                ## Phase 5 — States
                <output description>
                > <agent prompt>

                ## Phase 6 — Polish
                <output description>
                > <agent prompt>

                Respond with ONLY the markdown body.
                """,
                ctx),

            "scaffold" => (
                """
                You produce the final `dotnet new unoapp` scaffold command for a
                Uno Platform app. Given the app's intent, output a sh code block
                with the right --features and a one-line closer.

                Use this structure:
                # Scaffold

                ```sh
                dotnet new unoapp \
                  -n <PascalAppName> \
                  --tfm net10.0 \
                  --platforms <comma-separated> \
                  --markup xaml --presentation mvux --theme material \
                  --features <comma-separated>
                ```

                The composition is, for now, complete.

                Respond with ONLY the markdown body.
                """,
                ctx),

            _ => (null, ctx),
        };
    }

    private static object? ParseSeed(string layerId, string text)
    {
        return layerId switch
        {
            "design" => ParseDesignTokens(text),
            "ux" or "architecture" or "interactions" or "data" or "implementation" or "scaffold"
                     => ExtractMarkdown(text),
            _        => null,
        };
    }

    private static DesignTokens? ParseDesignTokens(string text)
    {
        var json = ExtractJsonObject(text);
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var d = DesignTokens.Default;
            return new DesignTokens(
                Surface:  GetHex(root, "Surface", d.Surface),
                Action:   GetHex(root, "Action",  d.Action),
                Info:     GetHex(root, "Info",    d.Info),
                Success:  GetHex(root, "Success", d.Success),
                Warn:     GetHex(root, "Warn",    d.Warn),
                Panel:    GetHex(root, "Panel",   d.Panel),
                Tag:      GetHex(root, "Tag",     d.Tag),
                Locked:   GetHex(root, "Locked",  d.Locked),
                BodyFont: GetBodyFont(root) ?? d.BodyFont);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Strip leading/trailing markdown fences if present, return the
    /// raw body. Returns null when the result is empty.</summary>
    private static string? ExtractMarkdown(string text)
    {
        var t = text.Trim();
        if (t.StartsWith("```markdown"))
            t = t.Substring("```markdown".Length).TrimStart('\r', '\n');
        else if (t.StartsWith("```"))
            t = t.Substring(3).TrimStart('\r', '\n');
        if (t.EndsWith("```"))
            t = t.Substring(0, t.Length - 3).TrimEnd();
        return string.IsNullOrWhiteSpace(t) ? null : t;
    }

    // ────────────────────────────────────────────────────────────────────
    //  Layer info + JSON helpers
    // ────────────────────────────────────────────────────────────────────

    private static bool IsTypedLayer(string layerId) =>
        layerId is "design";

    private static string LayerLabel(string layerId) => layerId switch
    {
        "intent"         => "Intent",
        "ux"             => "UX",
        "architecture"   => "Architecture",
        "design"         => "Design",
        "interactions"   => "Interactions",
        "data"           => "Data",
        "implementation" => "Implementation",
        "scaffold"       => "Scaffold",
        _                => layerId,
    };

    private static string LayerPurpose(string layerId) => layerId switch
    {
        "intent"         => "Capture what the app is for and who it's for.",
        "ux"             => "Trace the primary screens the user moves through.",
        "architecture"   => "Name modules, their roles, and how they connect.",
        "design"         => "Pick the design tokens — colors, typography, density.",
        "interactions"   => "Define the six canonical states per screen.",
        "data"           => "Shape the entity records and their relationships.",
        "implementation" => "Sequence the build into ordered phases.",
        "scaffold"       => "Emit the final `dotnet new unoapp` command.",
        _                => string.Empty,
    };

    /// <summary>Case-insensitive property lookup — Sonnet sometimes returns
    /// camelCase even when prompted PascalCase, and vice versa.</summary>
    private static bool TryGet(JsonElement el, string name, out JsonElement value)
    {
        if (el.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in el.EnumerateObject())
            {
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = p.Value;
                    return true;
                }
            }
        }
        value = default;
        return false;
    }

    private static string GetHex(JsonElement root, string name, string fallback)
    {
        if (TryGet(root, name, out var el) && el.ValueKind == JsonValueKind.String)
        {
            var v = el.GetString();
            return string.IsNullOrEmpty(v) ? fallback : v;
        }
        return fallback;
    }

    private static string? GetBodyFont(JsonElement root)
    {
        if (TryGet(root, "BodyFont", out var el) && el.ValueKind == JsonValueKind.String)
        {
            var v = el.GetString();
            return v switch
            {
                "Satoshi Variable" or "Satoshi" or "Inter" => v,
                _ => null,
            };
        }
        return null;
    }

    /// <summary>Extract the first balanced JSON object from a text response.
    /// Robust to commentary that AI sometimes prepends despite the prompt.</summary>
    private static string ExtractJsonObject(string text)
    {
        var depth = 0;
        var start = -1;
        var inString = false;
        var escaped = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }
                continue;
            }

            if (c == '"')
            {
                inString = true;
                continue;
            }

            if (c == '{')
            {
                if (depth == 0) start = i;
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0 && start >= 0)
                    return text.Substring(start, i - start + 1);
            }
        }
        return string.Empty;
    }
}
