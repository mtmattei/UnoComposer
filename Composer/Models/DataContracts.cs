using System.Collections.Immutable;

namespace Composer.Models;

/// <summary>
/// Layer 6 — Data. Typed list of entity records, each with explicit fields.
/// This is the source of truth for the data layer: the canvas renders from it,
/// the markdown is generated from it, and AI refinements mutate it (same typed
/// pattern the Design layer already uses with <see cref="DesignTokens"/>).
///
/// <see cref="FromContext"/> reproduces the prior vibe-aware entity shapes
/// (formerly hardcoded across DataLayerView + MarkdownGenerators) so the default
/// is identical to before this layer became typed.
/// </summary>
public record DataContracts(ImmutableArray<EntityDef> Entities)
{
    public static DataContracts Empty { get; } = new(ImmutableArray<EntityDef>.Empty);

    /// <summary>Derive the default typed contracts from the intent context —
    /// vibe-aware entity, user, and (when applicable) schedule records.</summary>
    public static DataContracts FromContext(IntentContext ctx)
    {
        var entity = BuildEntity(ctx);
        var user = BuildUser(ctx);
        var builder = ImmutableArray.CreateBuilder<EntityDef>();
        builder.Add(entity);
        builder.Add(user);
        if (NeedsSchedule(ctx))
            builder.Add(BuildSchedule(ctx));
        return new DataContracts(builder.ToImmutable());
    }

    // Habits, notes, workouts, and trades are date-stamped inline rather than
    // booked into a day's calendar — no Schedule entity. Mirrors the prior
    // MarkdownGenerators.NeedsSchedule rule.
    private static bool NeedsSchedule(IntentContext ctx) =>
        ctx.EntityNoun is not ("habit" or "note" or "workout" or "trade");

    private static EntityDef BuildEntity(IntentContext ctx)
    {
        var e = ctx.EntityTitle;
        var userT = ctx.UserSingularTitle;

        if (ctx.IsFieldService)
            return new EntityDef(e, EntityKind.Record, ImmutableArray.Create(
                new FieldDef("Id", "string"),
                new FieldDef("Title", "string"),
                new FieldDef("Address", "string"),
                new FieldDef("ScheduledAt", "DateTime?"),
                new FieldDef($"{userT}Id", "string?"),
                new FieldDef("Status", $"{e}Status"),
                new FieldDef("Notes", "string?"),
                new FieldDef("SyncState", "SyncState")));

        return ctx.Vibe switch
        {
            Vibe.Editorial when ctx.EntityNoun == "habit" => new EntityDef(e, EntityKind.Record, ImmutableArray.Create(
                new FieldDef("Id", "string"),
                new FieldDef("Title", "string"),
                new FieldDef("LoggedOn", "DateOnly"),
                new FieldDef("Streak", "int"),
                new FieldDef("Notes", "string?"),
                new FieldDef("SyncState", "SyncState"))),

            Vibe.Editorial => new EntityDef(e, EntityKind.Record, ImmutableArray.Create(
                new FieldDef("Id", "string"),
                new FieldDef("Title", "string"),
                new FieldDef("CreatedAt", "DateTime"),
                new FieldDef("Body", "string"),
                new FieldDef("SyncState", "SyncState"))),

            Vibe.Financial => new EntityDef(e, EntityKind.Record, ImmutableArray.Create(
                new FieldDef("Id", "string"),
                new FieldDef("Symbol", "string"),
                new FieldDef("Quantity", "decimal"),
                new FieldDef("Price", "decimal"),
                new FieldDef("ExecutedAt", "DateTime"),
                new FieldDef("Status", $"{e}Status"),
                new FieldDef("SyncState", "SyncState"))),

            Vibe.Clinical => new EntityDef(e, EntityKind.Record, ImmutableArray.Create(
                new FieldDef("Id", "string"),
                new FieldDef($"{userT}Id", "string"),
                new FieldDef("VisitedAt", "DateTime"),
                new FieldDef("Notes", "string?"),
                new FieldDef("Status", $"{e}Status"),
                new FieldDef("SyncState", "SyncState"))),

            _ => new EntityDef(e, EntityKind.Record, ImmutableArray.Create(
                new FieldDef("Id", "string"),
                new FieldDef("Title", "string"),
                new FieldDef("ScheduledAt", "DateTime?"),
                new FieldDef($"{userT}Id", "string?"),
                new FieldDef("Status", $"{e}Status"),
                new FieldDef("Notes", "string?"),
                new FieldDef("SyncState", "SyncState"))),
        };
    }

    private static EntityDef BuildUser(IntentContext ctx)
        => new(ctx.UserSingularTitle, EntityKind.Record, ImmutableArray.Create(
            new FieldDef("Id", "string"),
            new FieldDef("Name", "string"),
            new FieldDef("Phone", "string?"),
            new FieldDef("Available", "bool")));

    private static EntityDef BuildSchedule(IntentContext ctx)
        => new("Schedule", EntityKind.Record, ImmutableArray.Create(
            new FieldDef("Day", "DateOnly"),
            new FieldDef(ctx.EntityPlural, $"{ctx.EntityTitle}[]")));
}

public record EntityDef(string Name, EntityKind Kind, ImmutableArray<FieldDef> Fields);

public record FieldDef(string Name, string TypeText);

public enum EntityKind { Record, Class, Struct }
