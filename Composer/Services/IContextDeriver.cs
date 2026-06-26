using Composer.Models;

namespace Composer.Services;

/// <summary>
/// Derives the pattern-based <see cref="IntentContext"/> from an
/// <see cref="Intent"/>. Wraps the existing (rich, Vibe-archetype) static
/// derivation behind an injectable seam so consumers take a constructor
/// dependency instead of calling the static method directly — testable and
/// centrally owned. The derivation logic itself is unchanged.
/// </summary>
public interface IContextDeriver
{
    IntentContext Derive(Intent intent);
}
