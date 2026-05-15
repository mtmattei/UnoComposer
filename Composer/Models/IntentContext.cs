using System.Text.RegularExpressions;

namespace Composer.Models;

public enum Vibe { Standard, Utilitarian, Editorial, Playful, Clinical, Financial }
public enum Palette { Neutral, HighContrast, Warm, Cool, Conservative, Paper }
public enum Density { Standard, Compact, Spacious }
public enum SyncPosture { QueueAndSync, Optimistic, Live }

// Derived snapshot of the Intent — every downstream layer reads from this.
// Pattern-based (no LLM); see composer-delta-brief.md §4.
public sealed partial record IntentContext(
    string AppName,
    string EntityNoun,
    string EntityTitle,
    string EntityPlural,
    string UserNoun,
    string UserSingular,
    bool IsOfflineFirst,
    bool IsMobileFirst,
    bool IsFieldService,
    bool IsMulti,
    Vibe Vibe,
    Palette Palette,
    Density Density,
    bool Outdoor,
    string ActionRationale,
    SyncPosture SyncPosture)
{
    // Lazy so the static cctor doesn't call DeriveFrom before the rule
    // tables below have initialised. (Static fields run in textual order;
    // DomainNouns/VibeRules are declared after this line.)
    private static readonly Lazy<IntentContext> _default = new(() => DeriveFrom(Intent.Example));
    public static IntentContext Default => _default.Value;

    // Filename for the UX layer's flow file — matches the file the bundle
    // exporter writes for this entity (e.g. "meal-flow.md" for a calorie
    // tracker). Replaces the hardcoded "dispatch-flow.md" in the views.
    public string UxFlowFilename  => $"{EntityNoun}-flow.md";

    // 5-screen UX flow derived from Vibe + entity. The views render this as
    // a horizontal card scroller (UxLayerView) and a vertical labelled row
    // list (UxPreviewer). Each Vibe has its own natural flow shape; the
    // entity nouns thread through so a calorie tracker sees meals, a
    // financial app sees positions, a clinical app sees encounters, etc.
    public ImmutableArray<UxScreen> ScreenFlow => Vibe switch
    {
        Vibe.Editorial => ImmutableArray.Create(
            new UxScreen("Today",         $"Recent {EntityPlural}"),
            new UxScreen($"{EntityTitle} detail", $"Open {EntityNoun}"),
            new UxScreen($"Add {EntityNoun}",     "Quick entry"),
            new UxScreen("Insights",       "Trends + streaks"),
            new UxScreen("Goals",          "Targets + reminders")),

        Vibe.Financial => ImmutableArray.Create(
            new UxScreen("Dashboard",     $"{EntityTitle} balance"),
            new UxScreen($"{EntityTitle} detail", "Open position"),
            new UxScreen("Activity",       "Recent transactions"),
            new UxScreen("Watchlist",      "Track + alert"),
            new UxScreen("Settings",       "Accounts + alerts")),

        Vibe.Clinical => ImmutableArray.Create(
            new UxScreen("Roster",        $"Today's {EntityPlural}"),
            new UxScreen($"{EntityTitle} chart",   "History + vitals"),
            new UxScreen("Encounter",      "Notes + plan"),
            new UxScreen("Orders",         "Labs + meds"),
            new UxScreen("Handoff",        "Sign-out summary")),

        Vibe.Playful => ImmutableArray.Create(
            new UxScreen("Home",          $"Recent {EntityPlural}"),
            new UxScreen($"{EntityTitle}",        "Open"),
            new UxScreen($"New {EntityNoun}",     "Add"),
            new UxScreen("Collection",     "Browse"),
            new UxScreen("Settings",       "You")),

        Vibe.Utilitarian => ImmutableArray.Create(
            new UxScreen("Dashboard",     DashboardHint),
            new UxScreen(DetailLabel,     DetailHint),
            new UxScreen(ScheduleLabel,   ScheduleHint),
            new UxScreen(DispatchLabel,   DispatchHint),
            new UxScreen("Confirmation", "✓ Logged")),

        // Standard / fallback — generic CRUD-shaped flow with entity nouns.
        _ => ImmutableArray.Create(
            new UxScreen("Dashboard",     $"Today's {EntityPlural}"),
            new UxScreen($"{EntityTitle} detail", $"Open {EntityNoun}"),
            new UxScreen("Plan",           "Choose a slot"),
            new UxScreen("Commit",         "Save"),
            new UxScreen("Confirmation", "✓ Logged")),
    };

    // The "Five steps maps to the most-common dispatch model..." annotation
    // is field-service-shaped — these phrasings rotate per vibe so the
    // reasoning shown matches the flow above.
    public string UxFlowRationale => Vibe switch
    {
        Vibe.Editorial   => $"A daily-use shape: Today as the canvas, detail to focus, add as the smallest possible commit. Insights and goals support the habit without competing with the {EntityNoun}.",
        Vibe.Financial   => "Dashboard first because money apps are read-mostly. Activity is a separate surface so a single position stays clean. Watchlist keeps the unowned in view.",
        Vibe.Clinical    => "Roster anchors the shift. Chart and encounter separate \"what is\" from \"what now\". Handoff is a real terminal — never a modal.",
        Vibe.Playful     => "Home invites; detail focuses; add stays one tap away. Collection and Settings live below the fold.",
        Vibe.Utilitarian => "Five steps maps to a five-second mental model. Confirmation is a real terminal screen, not a modal — there's somewhere to come back to.",
        _                => "Five screens map a CRUD flow end to end. Confirmation is a terminal, not a modal — the in-flight case has somewhere to live.",
    };

    // Computed labels for XAML bindings — keep these here so views don't need
    // converters or per-locale formatting code.
    public string FlowLabel       => $"{EntityTitle} flow".ToUpperInvariant();
    public string FlowName        => $"{EntityTitle} flow";
    public string DashboardHint   => IsFieldService ? "Today's jobs"        : $"Today's {EntityPlural}";
    public string DetailLabel     => IsFieldService ? "Job detail"          : $"{EntityTitle} detail";
    public string DetailHint      => IsFieldService ? "Address + parts"     : $"Open {EntityNoun}";
    public string ScheduleLabel   => IsFieldService ? "Schedule"            : "Plan";
    public string ScheduleHint    => IsFieldService ? "Pick time slot"      : "Choose a slot";
    public string DispatchLabel   => IsFieldService ? "Dispatch"            : "Commit";
    public string DispatchHint    => IsFieldService ? "Notify + go"         : "Save";
    public string DefaultStateLabel => IsFieldService ? "Empty calendar; primary CTA visible." : $"Empty {EntityNoun} list; primary CTA visible.";
    public string UserSingularTitle => string.IsNullOrEmpty(UserSingular) ? "User" : char.ToUpperInvariant(UserSingular[0]) + UserSingular[1..];
    public string ServicesDescription => IsFieldService
        ? "Job, Technician, Schedule"
        : $"{EntityTitle}, {UserSingularTitle}, Schedule";
    public string SyncPostureLabel => SyncPosture switch
    {
        SyncPosture.QueueAndSync => "Local cache, offline-first",
        SyncPosture.Optimistic   => "Optimistic writes, server confirms",
        _                        => "Live, server-confirmed",
    };

    // Rationale for the Architecture layer's WHY THIS MATTERS annotation.
    // Branches on the three intent dimensions that actually change the
    // architecture: offline-first (the queue becomes load-bearing),
    // multi-role (feeds need scoping), and mobile-first (regions matter
    // because surfaces animate independently on small screens).
    public string ArchitectureRationale => (IsOfflineFirst, IsMulti, IsMobileFirst) switch
    {
        (true, true, _)       => $"Offline-first + multi-role: MVUX feeds re-derive on reconnect, and each {UserNoun} role gets its own scoped slice — nothing in the UI knows about the network or the other roles.",
        (true, false, _)      => "Offline-first means the queue is load-bearing. MVUX feeds re-derive on reconnect; the UI stays oblivious to network state.",
        (false, true, _)      => $"Multi-role MVUX: role-scoped feeds compose into the same surface so {UserNoun} never see each other's state. Navigation regions let role transitions animate without remounting.",
        (false, false, true)  => $"Mobile-first MVUX: each surface owns one feed, navigation regions let the stack animate independently. The {EntityNoun} flow is the canonical primitive.",
        _                     => "MVUX keeps each view's state derivable from a single intent. Navigation regions let surfaces animate independently — no manual state plumbing.",
    };

    public static IntentContext DeriveFrom(Intent intent)
    {
        var blob = string.Join(' ',
            new[] { intent.AppType, intent.Workflow, intent.PrimaryUser }
                .Where(s => !string.IsNullOrWhiteSpace(s)));

        var entityNoun = MatchDomainNoun(blob);
        var entityTitle = Capitalize(entityNoun);
        var entityPlural = entityNoun.EndsWith('s') ? entityNoun : entityNoun + "s";

        var appName = PascalCase(intent.AppType, fallback: "App");

        var userPlural = (intent.PrimaryUser ?? "users").Trim().ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var userNoun = userPlural.Length > 0 ? userPlural[^1] : "users";
        var userSingular = userNoun.EndsWith('s') ? userNoun[..^1] : userNoun;

        var isOfflineFirst = OfflineFirstRegex().IsMatch(blob);
        var isMobileFirst  = MobileFirstRegex().IsMatch(blob);
        var isFieldService = intent.AppType == Intent.Example.AppType;
        var isMulti        = MultiUserRegex().IsMatch(blob);

        var rule = MatchVibeRule(blob);
        var syncPosture = isOfflineFirst ? SyncPosture.QueueAndSync : SyncPosture.Live;

        return new IntentContext(
            AppName: appName,
            EntityNoun: entityNoun,
            EntityTitle: entityTitle,
            EntityPlural: entityPlural,
            UserNoun: userNoun,
            UserSingular: userSingular,
            IsOfflineFirst: isOfflineFirst,
            IsMobileFirst: isMobileFirst,
            IsFieldService: isFieldService,
            IsMulti: isMulti,
            Vibe: rule.Vibe,
            Palette: rule.Palette,
            Density: rule.Density,
            Outdoor: rule.Outdoor,
            ActionRationale: rule.Rationale,
            SyncPosture: syncPosture);
    }

    // Source-generated regex (one per noun) — zero runtime compilation cost,
    // delegate-tree match, AOT-safe. Order matches the historic precedence.
    [GeneratedRegex("habit|streak",                              RegexOptions.IgnoreCase)] private static partial Regex HabitNounRegex();
    [GeneratedRegex("recipe|cook|meal|calorie|nutrition",         RegexOptions.IgnoreCase)] private static partial Regex MealNounRegex();
    [GeneratedRegex("workout|exercise|fitness|gym|run",           RegexOptions.IgnoreCase)] private static partial Regex WorkoutNounRegex();
    [GeneratedRegex("trade|portfolio|invest|stock|finance",       RegexOptions.IgnoreCase)] private static partial Regex TradeNounRegex();
    [GeneratedRegex("task|todo|backlog",                          RegexOptions.IgnoreCase)] private static partial Regex TaskNounRegex();
    [GeneratedRegex("note|journal|diary",                         RegexOptions.IgnoreCase)] private static partial Regex NoteNounRegex();
    [GeneratedRegex("appointment|booking|reserv",                 RegexOptions.IgnoreCase)] private static partial Regex AppointmentNounRegex();
    [GeneratedRegex("patient|medical|health|clinic|nurse|doctor", RegexOptions.IgnoreCase)] private static partial Regex PatientNounRegex();
    [GeneratedRegex("invoice|billing|payment",                    RegexOptions.IgnoreCase)] private static partial Regex InvoiceNounRegex();
    [GeneratedRegex("order|purchase|cart",                        RegexOptions.IgnoreCase)] private static partial Regex OrderNounRegex();
    [GeneratedRegex("ticket|incident|issue",                      RegexOptions.IgnoreCase)] private static partial Regex TicketNounRegex();
    [GeneratedRegex("lesson|class|course",                        RegexOptions.IgnoreCase)] private static partial Regex LessonNounRegex();
    [GeneratedRegex("dispatch|field-service|job|service-call",    RegexOptions.IgnoreCase)] private static partial Regex JobNounRegex();

    private static string MatchDomainNoun(string blob)
    {
        if (HabitNounRegex().IsMatch(blob))       return "habit";
        if (MealNounRegex().IsMatch(blob))        return "meal";
        if (WorkoutNounRegex().IsMatch(blob))     return "workout";
        if (TradeNounRegex().IsMatch(blob))       return "trade";
        if (TaskNounRegex().IsMatch(blob))        return "task";
        if (NoteNounRegex().IsMatch(blob))        return "note";
        if (AppointmentNounRegex().IsMatch(blob)) return "appointment";
        if (PatientNounRegex().IsMatch(blob))     return "patient";
        if (InvoiceNounRegex().IsMatch(blob))     return "invoice";
        if (OrderNounRegex().IsMatch(blob))       return "order";
        if (TicketNounRegex().IsMatch(blob))      return "ticket";
        if (LessonNounRegex().IsMatch(blob))      return "lesson";
        if (JobNounRegex().IsMatch(blob))         return "job";
        return "item";
    }

    [GeneratedRegex("offline|local|queue|sync\\s+later|no\\s+backend",     RegexOptions.IgnoreCase)] private static partial Regex OfflineFirstRegex();
    [GeneratedRegex("mobile|phone|tablet|on-the-go",                       RegexOptions.IgnoreCase)] private static partial Regex MobileFirstRegex();
    [GeneratedRegex("team|crew|fleet|multiple|workforce|technicians|nurses", RegexOptions.IgnoreCase)] private static partial Regex MultiUserRegex();

    private sealed record VibeRule(Vibe Vibe, Palette Palette, Density Density, bool Outdoor, string Rationale);

    [GeneratedRegex("medical|patient|clinic|health|nurse|doctor|hospital", RegexOptions.IgnoreCase)] private static partial Regex ClinicalVibeRegex();
    [GeneratedRegex("trade|portfolio|invest|stock|finance|bank|wealth",    RegexOptions.IgnoreCase)] private static partial Regex FinancialVibeRegex();
    [GeneratedRegex("habit|streak|journal|meditat|mindful|wellness",       RegexOptions.IgnoreCase)] private static partial Regex EditorialVibeRegex();
    [GeneratedRegex("kid|child|playful|game|fun|silly",                    RegexOptions.IgnoreCase)] private static partial Regex PlayfulVibeRegex();
    [GeneratedRegex("field|dispatch|service-call|truck|outdoor|driver",    RegexOptions.IgnoreCase)] private static partial Regex FieldVibeRegex();
    [GeneratedRegex("recipe|cook|meal|kitchen|food|calorie",               RegexOptions.IgnoreCase)] private static partial Regex RecipeVibeRegex();
    [GeneratedRegex("workout|exercise|fitness|gym|run",                    RegexOptions.IgnoreCase)] private static partial Regex WorkoutVibeRegex();
    [GeneratedRegex("note|document|writ|essay|study",                      RegexOptions.IgnoreCase)] private static partial Regex NoteVibeRegex();

    private static VibeRule MatchVibeRule(string blob)
    {
        if (ClinicalVibeRegex().IsMatch(blob))
            return new VibeRule(Vibe.Clinical, Palette.Cool, Density.Compact, false,
                "Clinical environments need calm, low-saturation tones; the action color carries weight precisely because nothing else does.");
        if (FinancialVibeRegex().IsMatch(blob))
            return new VibeRule(Vibe.Financial, Palette.Conservative, Density.Compact, false,
                "Financial interfaces reward restraint. Saturated hues belong to motion — gain, loss. The rest of the palette stays out of the way.");
        if (EditorialVibeRegex().IsMatch(blob))
            return new VibeRule(Vibe.Editorial, Palette.Warm, Density.Spacious, false,
                "Personal-practice apps live in the morning and evening. Warm, low-chroma palettes feel like a quiet room, not a notification.");
        if (PlayfulVibeRegex().IsMatch(blob))
            return new VibeRule(Vibe.Playful, Palette.Warm, Density.Spacious, false,
                "Playful interfaces still need restraint to feel calm. One teal accent earns attention; everything else clears the way.");
        if (FieldVibeRegex().IsMatch(blob))
            return new VibeRule(Vibe.Utilitarian, Palette.HighContrast, Density.Compact, true,
                "Outdoor sun + a phone in a truck mount means high-contrast, single-accent palette. Glove-friendly hit targets.");
        if (RecipeVibeRegex().IsMatch(blob))
            return new VibeRule(Vibe.Editorial, Palette.Warm, Density.Spacious, false,
                "Personal-practice apps live in the morning and evening. Warm, low-chroma palettes feel like a quiet room, not a notification.");
        if (WorkoutVibeRegex().IsMatch(blob))
            return new VibeRule(Vibe.Utilitarian, Palette.HighContrast, Density.Standard, true,
                "Outdoor activity + a phone in a sleeve: high-contrast, single-accent palette. Glove-friendly hit targets.");
        if (NoteVibeRegex().IsMatch(blob))
            return new VibeRule(Vibe.Editorial, Palette.Paper, Density.Spacious, false,
                "A reading surface should feel like paper, not a screen. Low chroma, generous spacing, one accent only.");
        return new VibeRule(Vibe.Standard, Palette.Neutral, Density.Standard, false,
            "A quiet palette with one accent that earns attention.");
    }

    [GeneratedRegex("[^a-zA-Z0-9 ]")] private static partial Regex NonAlnumRegex();

    private static string PascalCase(string? source, string fallback)
    {
        if (string.IsNullOrWhiteSpace(source)) return fallback;
        var cleaned = NonAlnumRegex().Replace(source, "");
        var parts = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return fallback;
        return string.Concat(parts.Select(Capitalize));
    }

    private static string Capitalize(string s)
        => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];

    // Strips non-alphanumeric characters; used to make AppName safe for
    // filesystem paths and identifiers. Returns "Composer" when nothing
    // survives. Shared by the bundle writer and the bundle-tree feed so
    // both produce the same identifier for a given Intent.
    public static string SanitizeForIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name)) return "Composer";
        var clean = new string(name.Where(char.IsLetterOrDigit).ToArray());
        return string.IsNullOrEmpty(clean) ? "Composer" : clean;
    }
}

// One row in the derived UX flow. Name is the screen label; Caption is the
// supporting hint shown below or beside it. See IntentContext.ScreenFlow.
public sealed record UxScreen(string Name, string Caption);
