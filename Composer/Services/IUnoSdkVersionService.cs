using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Composer.Services;

public interface IUnoSdkVersionService
{
    Task<string> GetLatestStableAsync(CancellationToken ct = default);
}

/// <summary>
/// Fetches the latest stable Uno.Sdk package version from the NuGet flat
/// container endpoint. Filters out prereleases (versions containing '-').
/// On any failure throws — callers fall back to the hardcoded constant.
/// </summary>
public sealed class UnoSdkVersionService : IUnoSdkVersionService
{
    private const string EndpointUrl = "https://api.nuget.org/v3-flatcontainer/uno.sdk/index.json";
    private readonly HttpClient _http;

    public UnoSdkVersionService(HttpClient http)
    {
        _http = http;
    }

    public async Task<string> GetLatestStableAsync(CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync(EndpointUrl, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, default, ct).ConfigureAwait(false);

        if (!doc.RootElement.TryGetProperty("versions", out var versionsElement)
            || versionsElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Unexpected NuGet response shape — no 'versions' array.");
        }

        string? lastStable = null;
        foreach (var v in versionsElement.EnumerateArray())
        {
            var s = v.GetString();
            if (string.IsNullOrEmpty(s)) continue;
            if (s.Contains('-')) continue; // prerelease
            lastStable = s;                 // keep the last stable
        }

        if (string.IsNullOrEmpty(lastStable))
            throw new InvalidOperationException("No stable Uno.Sdk versions found.");

        return lastStable;
    }
}
