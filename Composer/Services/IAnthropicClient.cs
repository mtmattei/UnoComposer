using System.Threading;
using System.Threading.Tasks;
using Refit;

namespace Composer.Services;

/// <summary>
/// Single-endpoint Anthropic Messages API client. Concrete implementation
/// is wired in App.xaml.cs via Refit. The API key is supplied via header
/// (sourced from configuration; in production should be served by a proxy
/// rather than baked into the WASM bundle).
/// </summary>
[Headers("Content-Type: application/json")]
public interface IAnthropicClient
{
    [Post("/v1/messages")]
    Task<MessagesResponse> CreateMessageAsync(
        [Body] MessagesRequest request,
        [Header("x-api-key")] string apiKey,
        [Header("anthropic-version")] string version,
        CancellationToken ct = default);

    /// <summary>
    /// Multimodal entry point — request body is an arbitrary object so the
    /// caller can pass an anonymous structure mirroring Anthropic's
    /// content-block array shape (image + text mixed). Refit serializes it
    /// via System.Text.Json. Used by the vision route in LayerPreviewService
    /// so the screenshot's pixels reach Sonnet's vision pipeline without
    /// modelling a polymorphic content-block hierarchy in C#.
    /// </summary>
    [Post("/v1/messages")]
    Task<MessagesResponse> CreateVisionMessageAsync(
        [Body] object request,
        [Header("x-api-key")] string apiKey,
        [Header("anthropic-version")] string version,
        CancellationToken ct = default);
}

public partial record MessagesRequest(
    string model,
    int max_tokens,
    MessagesContent[] messages,
    string? system = null,
    double? temperature = null);

public partial record MessagesContent(string role, string content);

public partial record MessagesResponse(
    string id,
    string type,
    string role,
    string model,
    MessagesContentBlock[] content,
    string? stop_reason,
    string? stop_sequence);

public partial record MessagesContentBlock(string type, string text);
