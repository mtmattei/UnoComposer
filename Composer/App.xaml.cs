using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Uno.Resizetizer;

namespace Composer;

public partial class App : Application
{
    /// <summary>
    /// Initializes the singleton application object. This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        this.InitializeComponent();
    }

    protected Window? MainWindow { get; private set; }
    protected IHost? Host { get; private set; }

    [SuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "Uno.Extensions APIs are used in a way that is safe for trimming in this template context.")]
    protected async override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var builder = this.CreateBuilder(args)
            // Add navigation support for toolkit controls such as TabBar and NavigationView
            .UseToolkitNavigation()
            .Configure(host => host
#if DEBUG
                // Switch to Development environment when running in DEBUG
                .UseEnvironment(Environments.Development)
#endif
                .UseLogging(configure: (context, logBuilder) =>
                {
                    // Configure log levels for different categories of logging
                    logBuilder
                        .SetMinimumLevel(
                            context.HostingEnvironment.IsDevelopment() ?
                                LogLevel.Information :
                                LogLevel.Warning)

                        // Default filters for core Uno Platform namespaces
                        .CoreLogLevel(LogLevel.Warning);

                    // Uno Platform namespace filter groups
                    // Uncomment individual methods to see more detailed logging
                    //// Generic Xaml events
                    //logBuilder.XamlLogLevel(LogLevel.Debug);
                    //// Layout specific messages
                    //logBuilder.XamlLayoutLogLevel(LogLevel.Debug);
                    //// Storage messages
                    //logBuilder.StorageLogLevel(LogLevel.Debug);
                    //// Binding related messages
                    //logBuilder.XamlBindingLogLevel(LogLevel.Debug);
                    //// Binder memory references tracking
                    //logBuilder.BinderMemoryReferenceLogLevel(LogLevel.Debug);
                    //// DevServer and HotReload related
                    //logBuilder.HotReloadCoreLogLevel(LogLevel.Information);
                    //// Debug JS interop
                    //logBuilder.WebAssemblyLogLevel(LogLevel.Debug);

                }, enableUnoLogging: true)
                .UseConfiguration(configure: configBuilder =>
                    configBuilder
                        .EmbeddedSource<App>()
                        .Section<AppConfig>()
                        .Section<Composer.Services.AnthropicConfig>("Anthropic")
                )
#if DEBUG
                // User secrets layer for the Anthropic API key — set with:
                //   dotnet user-secrets set "Anthropic:ApiKey" "sk-ant-..."
                // Layered AFTER UseConfiguration so secret values win over the
                // empty default in the embedded appsettings.json. The literal
                // userSecretsId must match <UserSecretsId> in Composer.csproj.
                .ConfigureAppConfiguration((ctx, cb) =>
                {
                    if (ctx.HostingEnvironment.IsDevelopment())
                        cb.AddUserSecrets(userSecretsId: "comp-composer-anthropic-2026");
                })
#endif
                .UseHttp((context, services) => {
#if DEBUG
                // DelegatingHandler will be automatically injected
                services.AddTransient<DelegatingHandler, DebugHttpHandler>();
#endif
                    // Refit client for the Anthropic Messages API. The endpoint
                    // base URL is sourced from configuration so a proxy can be
                    // injected without rebuilding (production should use a proxy
                    // to keep the API key out of the WASM bundle).
                    services.AddRefitClient<Composer.Services.IAnthropicClient>(
                        context,
                        configure: (builder, _) => builder.ConfigureHttpClient(c =>
                        {
                            var baseUrl = context.Configuration["Anthropic:BaseUrl"] ?? "https://api.anthropic.com";
                            c.BaseAddress = new Uri(baseUrl);
                            // Bound the HTTP wait so a stalled Anthropic call surfaces as
                            // TaskCanceledException instead of locking the conversation
                            // in IsThinking=true forever.
                            c.Timeout = TimeSpan.FromSeconds(60);
                        }));
})
                .ConfigureServices((context, services) =>
                {
                    // Layer preview service — Anthropic-backed with identity
                    // fallback when the API key is empty.
                    services.AddSingleton<Composer.Services.ILayerPreviewService, Composer.Services.LayerPreviewService>();

                    // Typed HttpClient for the Uno.Sdk version chip — NuGet
                    // flat-container probe. 8s timeout; callers fall back to
                    // the hardcoded constant on failure.
                    services.AddHttpClient<Composer.Services.IUnoSdkVersionService, Composer.Services.UnoSdkVersionService>(c =>
                    {
                        c.Timeout = TimeSpan.FromSeconds(8);
                    });

                    // Bundle exporter — FileSavePicker + ZipArchive.
                    services.AddSingleton<Composer.Services.IBundleExporter, Composer.Services.BundleExporter>();

#if DEBUG
                    // PostConfigure bridge: Uno's UseConfiguration chain doesn't
                    // reliably compose with the standard
                    // ConfigureAppConfiguration/AddUserSecrets layer, so the
                    // bound AnthropicConfig.ApiKey may stay empty even when
                    // `dotnet user-secrets list` shows the key. Read the secrets
                    // file directly here and stamp it into the bound options if
                    // the section value is empty.
                    services.PostConfigure<Composer.Services.AnthropicConfig>(cfg =>
                    {
                        if (!string.IsNullOrEmpty(cfg.ApiKey)) return;
                        var fromSecrets = LoadAnthropicKeyFromUserSecrets("comp-composer-anthropic-2026");
                        if (!string.IsNullOrEmpty(fromSecrets))
                            cfg.ApiKey = fromSecrets!;
                    });
#endif
                })
                .UseNavigation(ReactiveViewModelMappings.ViewModelMappings, RegisterRoutes)
            );
        MainWindow = builder.Window;

        #if DEBUG
        MainWindow.UseStudio();
#endif
                MainWindow.SetWindowIcon();

        Host = await builder.NavigateAsync<Shell>();

#if DEBUG
        // Startup diagnostic — print whether the Anthropic key was wired so the
        // contextual-fetch silent fallback is visible from the terminal. Length
        // only, never the value.
        try
        {
            var opts = Host!.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Composer.Services.AnthropicConfig>>();
            var len = opts.Value.ApiKey?.Length ?? 0;
            System.Console.WriteLine($"[Composer] Anthropic API key configured: {(len > 0 ? "yes" : "NO (empty)")}, length={len}");
        }
        catch { /* diagnostic best-effort */ }
#endif
    }

#if DEBUG
    /// <summary>
    /// Reads <c>%APPDATA%\Microsoft\UserSecrets\{id}\secrets.json</c> directly
    /// and returns the <c>Anthropic:ApiKey</c> value if present. Mirrors what
    /// <c>Microsoft.Extensions.Configuration.UserSecrets</c> does internally,
    /// invoked from a PostConfigure hook so it survives Uno's UseConfiguration
    /// chain not propagating user-secrets sources into the bound
    /// <c>IOptions&lt;AnthropicConfig&gt;</c>.
    /// </summary>
    private static string? LoadAnthropicKeyFromUserSecrets(string userSecretsId)
    {
        try
        {
            var appData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
            var path = System.IO.Path.Combine(appData, "Microsoft", "UserSecrets", userSecretsId, "secrets.json");
            if (!System.IO.File.Exists(path)) return null;

            using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(path));
            // user-secrets stores hierarchical keys flat with ":" as separator,
            // e.g. "Anthropic:ApiKey". Try both shapes for robustness.
            if (doc.RootElement.TryGetProperty("Anthropic:ApiKey", out var flat)
                && flat.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                return flat.GetString();
            }
            if (doc.RootElement.TryGetProperty("Anthropic", out var nested)
                && nested.ValueKind == System.Text.Json.JsonValueKind.Object
                && nested.TryGetProperty("ApiKey", out var key)
                && key.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                return key.GetString();
            }
        }
        catch
        {
            // Best-effort — fall through to empty so callers see the banner.
        }
        return null;
    }
#endif

    private static void RegisterRoutes(IViewRegistry views, IRouteRegistry routes)
    {
        views.Register(
            new ViewMap(ViewModel: typeof(ShellModel)),
            new ViewMap<CompositionPage, CompositionModel>()
        );

        routes.Register(
            new RouteMap("", View: views.FindByViewModel<ShellModel>(),
                Nested:
                [
                    new ("Composition", View: views.FindByViewModel<CompositionModel>(), IsDefault: true),
                ]
            )
        );
    }
}
