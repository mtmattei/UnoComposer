namespace Composer.Services;

/// <summary>
/// Anthropic API binding. Properties use <c>set</c> (not <c>init</c>) so that
/// <c>services.PostConfigure&lt;AnthropicConfig&gt;</c> can stamp the API key
/// in from user secrets when the bound section is empty — Uno's
/// UseConfiguration chain doesn't compose with the standard
/// IHostBuilder.ConfigureAppConfiguration user-secrets layer cleanly, so a
/// post-configure pass is the most reliable bridge.
/// </summary>
public partial record AnthropicConfig
{
    public string BaseUrl { get; set; } = "https://api.anthropic.com";
    public string Model { get; set; } = "claude-sonnet-4-6";
    public string ApiKey { get; set; } = string.Empty;
    public string Version { get; set; } = "2023-06-01";
}
