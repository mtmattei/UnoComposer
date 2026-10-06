# Composer

An Uno Platform desktop app that walks you through specifying a new Uno app layer by layer (intent, UX, architecture, design, interactions, data, implementation, scaffold) and exports the result as a bundle of Markdown files for a coding agent. Each layer can be refined with Anthropic's Messages API.

## What's in it

The app has one page, `CompositionPage`, with a layer rail, a preview canvas per layer, and a composer footer for prompts.

- **Eight layers**, each producing one file (`Models/LayerStatus.cs`):
  `intent.md`, `ux-flow.md`, `architecture.md`, `design-system.md`, `interaction-states.md`, `data-contracts.md`, `implementation-plan.md`, `scaffold-command.md`.
- **Intent** - app type, primary user, workflow, target platforms (Web, Windows, Android, iOS, Desktop) and runtime (.NET 9, 10, 11). Locking intent seeds the downstream layers.
- **Design System** - editable color and font tokens, a swatch list, and the generated `ColorPaletteOverride.xaml`.
- **Architecture** and **Interactions** layers render diagrams (`Presentation/Diagrams/`).
- **Per-layer workflow** - preview or edit the Markdown, generate a refinement from a prompt or suggestion chip, accept and lock, discard, regenerate.
- **Section coverage** - each layer declares required headings (`Models/LayerSectionSchema.cs`); a badge shows `N / N sections covered` and offers refine chips for missing sections. Design notes in `docs/SPEC-section-schema.md`.
- **Reference screenshots** - attach images; the UX and design layers send them through the Anthropic vision endpoint.
- **Export** - saves all layer files plus `README.md` and `prompt-context.md` as a ZIP (`Services/IBundleExporter.cs`).

Without an API key, layers use the built-in Markdown generators and default records.

## Tech

- Uno Platform single project, Uno.Sdk 6.5.31, `net10.0-desktop`, Skia renderer.
- MVUX (`CompositionModel` with `IState`/`IFeed`/`IListState`), Uno.Extensions Hosting, Navigation, Configuration, HTTP (Refit), Serialization; Material theme and Uno Toolkit.
- Anthropic Messages API via a Refit client (`Services/IAnthropicClient.cs`), default model `claude-sonnet-4-6` (`Services/AnthropicConfig.cs`).
- `Uno.CommunityToolkit.WinUI.UI.Controls.Markdown` for Markdown previews, `Uno.Toolkit.Skia.WinUI` for `ShadowContainer`.

## Run it

```bash
dotnet user-secrets set "Anthropic:ApiKey" "<your key>" --project Composer/Composer.csproj   # optional
dotnet run --project Composer/Composer.csproj -f net10.0-desktop
```

On startup the console prints whether the Anthropic key was found.

## Status

Prototype. The full eight-layer flow and ZIP export are implemented; there is no test project.
