using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AssettoServer.RaceControl.Core.Models;

namespace AssettoServer.RaceControl.Core.Content;

public sealed class AcContentCatalogCache
{
    private const int CurrentSchemaVersion = 3;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _cacheDirectory;

    public AcContentCatalogCache(string cacheDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        _cacheDirectory = cacheDirectory;
    }

    public AcContentCatalog? TryLoad(string assettoCorsaRoot)
    {
        var normalizedRoot = NormalizeRoot(assettoCorsaRoot);
        if (!Directory.Exists(Path.Combine(normalizedRoot, "content")))
        {
            return null;
        }

        try
        {
            var path = GetCachePath(normalizedRoot);
            if (!File.Exists(path))
            {
                return null;
            }

            var envelope = JsonSerializer.Deserialize<CacheEnvelope>(File.ReadAllText(path), JsonOptions);
            return envelope is
            {
                SchemaVersion: CurrentSchemaVersion,
                Catalog: not null,
            } && string.Equals(envelope.AssettoCorsaRoot, normalizedRoot, StringComparison.OrdinalIgnoreCase)
                ? envelope.Catalog
                : null;
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or JsonException
                                          or NotSupportedException)
        {
            return null;
        }
    }

    public void Save(string assettoCorsaRoot, AcContentCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var normalizedRoot = NormalizeRoot(assettoCorsaRoot);
        Directory.CreateDirectory(_cacheDirectory);
        var path = GetCachePath(normalizedRoot);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";

        try
        {
            var envelope = new CacheEnvelope(CurrentSchemaVersion, normalizedRoot, catalog);
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(envelope, JsonOptions));
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    internal string GetCachePath(string assettoCorsaRoot)
    {
        var normalizedRoot = NormalizeRoot(assettoCorsaRoot).ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedRoot)))[..16];
        return Path.Combine(_cacheDirectory, $"content-catalog-v{CurrentSchemaVersion}-{hash}.json");
    }

    internal static string NormalizeRoot(string assettoCorsaRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assettoCorsaRoot);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(assettoCorsaRoot));
    }

    private sealed record CacheEnvelope(
        int SchemaVersion,
        string AssettoCorsaRoot,
        AcContentCatalog Catalog);
}
