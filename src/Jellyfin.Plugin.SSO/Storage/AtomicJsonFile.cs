using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SSO.Storage;

/// <summary>
/// Small atomic JSON file helper used by plugin-owned stores.
/// </summary>
internal static class AtomicJsonFile
{
    public static async Task<T?> ReadAsync<T>(
        string path,
        JsonSerializerOptions options,
        ILogger logger,
        CancellationToken cancellationToken)
        where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(stream, options, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unable to read SSO data file {Path}.", path);
            throw new InvalidOperationException($"The SSO data file '{path}' could not be read.", ex);
        }
    }

    public static async Task WriteAsync<T>(
        string path,
        T value,
        JsonSerializerOptions options,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var tempPath = path + ".tmp";

        await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, value, options, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        if (File.Exists(path))
        {
            File.Copy(path, path + ".bak", true);
        }

        File.Move(tempPath, path, true);
    }
}
