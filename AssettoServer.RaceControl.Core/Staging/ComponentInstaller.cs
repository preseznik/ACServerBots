using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using AssettoServer.RaceControl.Core.Infrastructure;
using AssettoServer.RaceControl.Core.Storage;
using AssettoServer.Release;

namespace AssettoServer.RaceControl.Core.Staging;

public sealed class ComponentInstaller(RaceControlPaths paths)
{
    public static string ReleaseUrl => $"https://github.com/{ReleaseIdentity.Product}/releases/download/race-control-v{ReleaseIdentity.Version}";

    public async Task<string> DownloadAsync(string component, IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (component is not ("server" or "fps" or "maps")) throw new ArgumentException("Unknown component.", nameof(component));
        paths.EnsureCreated();
        paths.RequireInsidePackage(paths.DownloadsDirectory);
        Directory.CreateDirectory(paths.DownloadsDirectory);
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RaceControl/" + ReleaseIdentity.Version);
        progress?.Report("Reading compatible release information...");
        using var manifestResponse = await client.GetAsync(ReleaseUrl + "/release.json", cancellationToken);
        if (manifestResponse.StatusCode == HttpStatusCode.NotFound)
            throw new IOException("This release has not been published online yet. Use the matching Import ZIP button with an archive from your release folder.");
        manifestResponse.EnsureSuccessStatusCode();
        using var manifest = JsonDocument.Parse(await manifestResponse.Content.ReadAsStringAsync(cancellationToken));
        if (manifest.RootElement.GetProperty("version").GetString() != ReleaseIdentity.Version)
            throw new InvalidDataException("The download manifest is for a different release.");
        var asset = manifest.RootElement.GetProperty("assets").EnumerateArray()
            .Single(item => item.GetProperty("kind").GetString() == component);
        string name = asset.GetProperty("name").GetString()!;
        if (Path.GetFileName(name) != name || name.Contains(':') || name.Contains('\\'))
            throw new InvalidDataException("Invalid release asset name.");
        string temporary = Path.Combine(paths.DownloadsDirectory, name + "." + Guid.NewGuid().ToString("N") + ".part");
        try
        {
            progress?.Report("Downloading " + name + "...");
            using (var response = await client.GetAsync(ReleaseUrl + "/" + Uri.EscapeDataString(name),
                       HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var output = File.Create(temporary);
                await input.CopyToAsync(output, cancellationToken);
            }
            await using (var input = File.OpenRead(temporary))
            {
                string hash = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken));
                if (!hash.Equals(asset.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Download checksum failed. No component was installed.");
            }
            return await ImportAsync(temporary, component, progress, cancellationToken);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public async Task<string> ImportAsync(string archivePath, string component,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (component is not ("server" or "fps" or "maps")) throw new ArgumentException("Unknown component.", nameof(component));
        paths.EnsureCreated();
        string target = component == "server" ? Path.Combine(paths.ServersDirectory, ReleaseIdentity.Version)
            : component == "fps" ? paths.FpsAssetsDirectory : paths.FpsMapsDirectory;
        paths.RequireInsidePackage(target);
        string temporary = target + ".incoming-" + Guid.NewGuid().ToString("N");
        string backup = target + ".previous-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(temporary);
            progress?.Report("Checking and extracting " + component + "...");
            await ExtractAsync(archivePath, temporary, cancellationToken);
            if (component == "server")
            {
                string? error = ReleaseIdentity.ValidateServer(temporary);
                if (error is not null) throw new InvalidDataException(error);
                await ValidateServerBinaryAsync(temporary, cancellationToken);
                // Imported payloads inherit the launcher's storage policy.
                string marker = Path.Combine(temporary, "portable.json");
                if (File.Exists(marker)) File.Delete(marker);
            }
            else
            {
                if (component == "maps") FpsMapPack.ValidateRoot(temporary);
                else FpsAssetResources.ValidateRoot(temporary);
                // Verify all entries against the release's file manifest before promotion.
                await VerifyPayloadAsync(temporary, cancellationToken);
            }
            if (Directory.Exists(target)) Directory.Move(target, backup);
            try { Directory.Move(temporary, target); }
            catch
            {
                if (Directory.Exists(backup)) Directory.Move(backup, target);
                throw;
            }
            if (Directory.Exists(backup)) Directory.Delete(backup, true);
            if (component == "fps") AppContext.SetData("ASRC.FpsAssetRoot", target);
            if (component == "maps") AppContext.SetData("ASRC.FpsMapsRoot", target);
            progress?.Report(component == "server" ? "Compatible server installed."
                : component == "maps" ? "FPS maps installed." : "FPS assets installed.");
            return target;
        }
        finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
    }

    internal static async Task ExtractAsync(string archivePath, string destination,
        CancellationToken cancellationToken = default)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > 10_000 || archive.Entries.Sum(entry => entry.Length) > 2L * 1024 * 1024 * 1024)
            throw new InvalidDataException("Component archive exceeds the supported size.");
        string prefix = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string name = entry.FullName.Replace('\\', '/');
            if (name.Contains(':') || name.StartsWith('/') || name.Split('/').Any(part => part is "." or "..")
                || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new InvalidDataException("Unsafe component archive path: " + name);
            string path = Path.GetFullPath(Path.Combine(destination, name));
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Archive path escapes the component folder.");
            if (name.EndsWith('/')) { Directory.CreateDirectory(path); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using var input = entry.Open();
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            await input.CopyToAsync(output, cancellationToken);
        }
    }

    public static async Task ValidateServerBinaryAsync(string directory, CancellationToken cancellationToken = default)
    {
        await VerifyPayloadAsync(directory, cancellationToken);
        var start = new ProcessStartInfo(Path.Combine(directory, "AssettoServer.exe"), "--capabilities-json")
        {
            WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        using var process = Process.Start(start) ?? throw new IOException("Could not inspect the server.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            Task<string> error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            string text = await output;
            await error;
            if (process.ExitCode != 0) throw new InvalidDataException("The server compatibility check failed.");
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.GetProperty("product").GetString() != ReleaseIdentity.Product
                || document.RootElement.GetProperty("version").GetString() != ReleaseIdentity.Version
                || document.RootElement.GetProperty("controlProtocol").GetInt32() != ReleaseIdentity.ControlProtocol)
                throw new InvalidDataException("The server executable does not match this launcher release.");
        }
        finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(CancellationToken.None); } }
    }

    private static async Task VerifyPayloadAsync(string directory, CancellationToken cancellationToken)
    {
        string path = Path.Combine(directory, "payload-sha256.json");
        if (!File.Exists(path)) throw new InvalidDataException("The component file manifest is missing.");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path, cancellationToken));
        string required = File.Exists(Path.Combine(directory, "AssettoServer.exe"))
            ? "AssettoServer.dll" : File.Exists(Path.Combine(directory, "asrc-fps-maps.json"))
                ? "asrc-fps-maps.json" : "asrc-fps-client.json";
        if (!document.RootElement.TryGetProperty(required, out _))
            throw new InvalidDataException("The component manifest does not describe its payload.");
        foreach (var file in document.RootElement.EnumerateObject())
        {
            string full = Path.GetFullPath(Path.Combine(directory, file.Name));
            if (!full.StartsWith(Path.GetFullPath(directory) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase) || file.Name.Contains(':'))
                throw new InvalidDataException("Invalid component manifest path.");
            await using var input = File.OpenRead(full);
            string actual = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken));
            if (!actual.Equals(file.Value.GetString(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Component checksum failed: " + file.Name);
        }
    }
}
