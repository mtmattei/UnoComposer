using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Composer.Services;

/// <summary>
/// Saves the composition's per-layer files (plus the synthesized README.md /
/// prompt-context.md companions) as a ZIP archive — one file per dictionary
/// entry, picker filtered to .zip. File order in the archive follows the
/// iteration order of the supplied dictionary (the model builds it in
/// canonical layer order).
/// </summary>
public interface IBundleExporter
{
    Task SaveBundleAsync(string appName, IDictionary<string, string> filesByName, CancellationToken ct = default);
}

public sealed class BundleExporter : IBundleExporter
{
    public async Task SaveBundleAsync(string appName, IDictionary<string, string> filesByName, CancellationToken ct = default)
    {
        var picker = new FileSavePicker
        {
            SuggestedFileName = $"{Slug(appName)}-bundle",
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeChoices.Add("ZIP archive", new List<string> { ".zip" });

#if HAS_UNO
        if (Microsoft.UI.Xaml.Window.Current is { } window)
        {
            WinRT.Interop.InitializeWithWindow.Initialize(picker,
                WinRT.Interop.WindowNative.GetWindowHandle(window));
        }
#endif

        var file = await picker.PickSaveFileAsync();
        if (file is null) return;

        var bytes = BuildZip(filesByName);
        await FileIO.WriteBytesAsync(file, bytes);
    }

    public static byte[] BuildZip(IDictionary<string, string> filesByName)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in filesByName)
            {
                var zipEntry = archive.CreateEntry(entry.Key, CompressionLevel.Optimal);
                using var entryStream = zipEntry.Open();
                using var writer = new StreamWriter(entryStream, Encoding.UTF8);
                writer.Write(entry.Value ?? string.Empty);
            }
        }
        return ms.ToArray();
    }

    private static string Slug(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "composer";
        var sb = new StringBuilder();
        foreach (var c in name.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c)) sb.Append(c);
            else if (sb.Length > 0 && sb[sb.Length - 1] != '-') sb.Append('-');
        }
        var s = sb.ToString().Trim('-');
        return s.Length == 0 ? "composer" : s;
    }
}
