namespace Composer.Models;

/// <summary>
/// Result of a layer-preview AI call. ProposedValues is either a typed record
/// (Intent / DesignTokens) for typed layers or a string (refined markdown body)
/// for markdown layers. Summary is a one-line headline suitable for italic
/// serif display above the primary action in the composer footer.
/// </summary>
public record LayerPreviewResult(object ProposedValues, string Summary);
