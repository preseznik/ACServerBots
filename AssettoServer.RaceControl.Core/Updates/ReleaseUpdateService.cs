using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using AssettoServer.RaceControl.Core.Infrastructure;
using AssettoServer.Release;

namespace AssettoServer.RaceControl.Core.Updates;

public sealed record UpdateInstaller(string Name, Uri Url, long Bytes, string Sha256);
public sealed record ReleaseUpdate(string Version, Uri Page, UpdateInstaller? Installer);

public sealed class ReleaseUpdateService(HttpClient client)
{
    public const string Repository = "https://github.com/preseznik/ACServerBots";
    public const string Feed = "https://api.github.com/repos/preseznik/ACServerBots/releases";
    private const string TagPrefix = "race-control-v";
    // The full Race Control installer already exceeds 512 MiB.
    private const long MaximumInstallerBytes = 2L * 1024 * 1024 * 1024;

    public static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RaceControl/" + ReleaseIdentity.Version);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    public async Task<ReleaseUpdate?> CheckAsync(string currentVersion, bool includePreview,
        CancellationToken cancellationToken = default)
    {
        var current = ReleaseVersion.Parse(currentVersion) ?? throw new InvalidDataException("Invalid installed version.");
        ReleaseUpdate? newest = null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        for (int page = 1; page <= 10; page++)
        {
            using var response = await client.GetAsync(Feed + $"?per_page=100&page={page}",
                HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode == HttpStatusCode.Forbidden || (int)response.StatusCode == 429)
                throw new IOException("GitHub's update limit was reached. Try again later or open GitHub releases.");
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await ReadLimitedAsync(response, 2 * 1024 * 1024, timeout.Token));
            var rows = document.RootElement;
            if (rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() > 100)
                throw new InvalidDataException("Invalid GitHub release response.");
            foreach (var row in rows.EnumerateArray())
            {
                var release = ReadRelease(row, includePreview);
                if (release is null) continue;
                var version = ReleaseVersion.Parse(release.Version)!;
                if (version.CompareTo(current) > 0 && (newest is null
                    || version.CompareTo(ReleaseVersion.Parse(newest.Version)) > 0)) newest = release;
            }
            if (rows.GetArrayLength() < 100) return newest;
        }
        throw new IOException("The release listing is too large. Open GitHub releases to check manually.");
    }

    internal static ReleaseUpdate? ReadRelease(JsonElement row, bool includePreview)
    {
        if (row.GetProperty("draft").GetBoolean()) return null;
        string tag = row.GetProperty("tag_name").GetString() ?? string.Empty;
        if (!tag.StartsWith(TagPrefix, StringComparison.Ordinal)) return null;
        string text = tag[TagPrefix.Length..];
        var version = ReleaseVersion.Parse(text);
        if (version is null || !includePreview && (row.GetProperty("prerelease").GetBoolean() || version.Preview.Length != 0))
            return null;
        string name = $"RaceControl-{text}-Setup.exe";
        var expectedUrl = new Uri(Repository + "/releases/download/" + Uri.EscapeDataString(tag) + "/" + Uri.EscapeDataString(name));
        UpdateInstaller? installer = null;
        foreach (var asset in row.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != name || asset.GetProperty("state").GetString() != "uploaded") continue;
            long bytes = asset.GetProperty("size").GetInt64();
            string digest = asset.TryGetProperty("digest", out var hash) && hash.ValueKind == JsonValueKind.String
                ? hash.GetString()! : string.Empty;
            if (bytes is <= 0 or > MaximumInstallerBytes || !Regex.IsMatch(digest, @"\Asha256:[a-fA-F0-9]{64}\z")
                || asset.GetProperty("browser_download_url").GetString() != expectedUrl.AbsoluteUri) continue;
            if (installer is not null) throw new InvalidDataException("Release contains duplicate installers.");
            installer = new(name, expectedUrl, bytes, digest[7..].ToLowerInvariant());
        }
        return new(text, new Uri(Repository + "/releases/tag/" + Uri.EscapeDataString(tag)), installer);
    }

    public async Task<string> DownloadAsync(UpdateInstaller installer, RaceControlPaths paths,
        IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        ValidateInstaller(installer);
        string folder = Path.Combine(paths.DownloadsDirectory, "Updates");
        paths.RequireInsidePackage(folder);
        RequirePlainPath(folder);
        Directory.CreateDirectory(folder);
        string target = Path.Combine(folder, installer.Name);
        RequirePlainPath(target);
        string partial = target + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            using var response = await client.GetAsync(installer.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length != installer.Bytes)
                throw new InvalidDataException("Installer size differs from GitHub release information.");
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                byte[] buffer = new byte[65536];
                long total = 0;
                int lastPercent = -1;
                int count;
                while ((count = await source.ReadAsync(buffer, cancellationToken)) != 0)
                {
                    total += count;
                    if (total > installer.Bytes) throw new InvalidDataException("Installer exceeds its declared size.");
                    hash.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                    int percent = (int)(100 * total / installer.Bytes);
                    if (percent != lastPercent) { progress?.Report(percent); lastPercent = percent; }
                }
                if (total != installer.Bytes || Convert.ToHexStringLower(hash.GetHashAndReset()) != installer.Sha256)
                    throw new InvalidDataException("Installer verification failed. The download was discarded.");
                await output.FlushAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            RequirePlainPath(target);
            File.Move(partial, target, true);
            return target;
        }
        finally
        {
            if (File.Exists(partial)) File.Delete(partial);
        }
    }

    public static ProcessStartInfo InstallerCommand(UpdateInstaller installer, string downloadedPath, RaceControlPaths paths)
    {
        ValidateInstaller(installer);
        string expected = Path.GetFullPath(Path.Combine(paths.DownloadsDirectory, "Updates", installer.Name));
        if (!Path.GetFullPath(downloadedPath).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Installer is outside the update download folder.");
        RequirePlainPath(expected);
        using var stream = File.OpenRead(expected);
        if (stream.Length != installer.Bytes || Convert.ToHexStringLower(SHA256.HashData(stream)) != installer.Sha256)
            throw new InvalidDataException("Installer changed after download. Check for updates and download again.");
        var command = new ProcessStartInfo(expected) { UseShellExecute = true, WorkingDirectory = paths.PackageRoot };
        if (!paths.IsPortable) command.ArgumentList.Add("/DIR=" + paths.PackageRoot);
        command.ArgumentList.Add("/NORESTART");
        return command;
    }

    private static void ValidateInstaller(UpdateInstaller installer)
    {
        const string prefix = "RaceControl-", suffix = "-Setup.exe";
        if (!installer.Name.StartsWith(prefix, StringComparison.Ordinal) || !installer.Name.EndsWith(suffix, StringComparison.Ordinal)
            || ReleaseVersion.Parse(installer.Name[prefix.Length..^suffix.Length]) is null
            || installer.Bytes is <= 0 or > MaximumInstallerBytes || !Regex.IsMatch(installer.Sha256, @"\A[0-9a-f]{64}\z"))
            throw new InvalidDataException("Invalid update installer information.");
        string version = installer.Name[prefix.Length..^suffix.Length];
        string expectedUrl = Repository + "/releases/download/" + Uri.EscapeDataString(TagPrefix + version)
                             + "/" + Uri.EscapeDataString(installer.Name);
        if (installer.Url.AbsoluteUri != expectedUrl) throw new InvalidDataException("Installer does not belong to this Race Control release.");
    }

    private static void RequirePlainPath(string path)
    {
        for (string? current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if (Path.Exists(current) && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("Update downloads cannot use linked folders or files.");
    }

    private static async Task<byte[]> ReadLimitedAsync(HttpResponseMessage response, int limit, CancellationToken token)
    {
        await using var source = await response.Content.ReadAsStreamAsync(token);
        using var output = new MemoryStream();
        byte[] buffer = new byte[32768];
        int count;
        while ((count = await source.ReadAsync(buffer, token)) != 0)
        {
            if (output.Length + count > limit) throw new InvalidDataException("GitHub release response exceeds its size limit.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }
}
