using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AssettoServer.RaceControl.Core.Infrastructure;
using AssettoServer.RaceControl.Core.Updates;
using NUnit.Framework;

namespace AssettoServer.RaceControl.Tests;

public sealed class ReleaseUpdateServiceTests
{
    private static readonly byte[] InstallerBytes = "test installer content"u8.ToArray();
    private static string Digest => Convert.ToHexStringLower(SHA256.HashData(InstallerBytes));

    [TestCase(false, "0.1.3")]
    [TestCase(true, "0.1.4-beta.10")]
    public async Task ChecksOnlyOurReleasesAndOrdersVersionsNumerically(bool preview, string expected)
    {
        var rows = new[] { Release("0.1.3"), Release("0.1.4-beta.2", preview: true),
            Release("0.1.4-beta.10", preview: true), Release("9.0.0", draft: true),
            Release("99.0.0", tag: "v99.0.0"), Release("0.1.2+build.1") };
        using var client = Client(_ => JsonResponse(rows));
        var result = await new ReleaseUpdateService(client).CheckAsync("0.1.2", preview);
        Assert.That(result!.Version, Is.EqualTo(expected));
        Assert.That(result.Installer, Is.Not.Null);
    }

    [Test]
    public async Task SameOrOlderVersionIsNotAnUpdate()
    {
        using var client = Client(_ => JsonResponse(new[] { Release("0.1.1"), Release("0.1.2+build.9") }));
        Assert.That(await new ReleaseUpdateService(client).CheckAsync("0.1.2", true), Is.Null);
    }

    [Test]
    public async Task FollowsPagesAndSelectsNewestAcrossThem()
    {
        var first = Enumerable.Range(0, 99).Select(_ => Release("0.1.1")).Append(Release("0.1.3")).ToArray();
        int requests = 0;
        using var client = Client(request =>
        {
            requests++;
            return JsonResponse(request.RequestUri!.Query.EndsWith("page=1") ? first : new[] { Release("0.1.5") });
        });
        var result = await new ReleaseUpdateService(client).CheckAsync("0.1.2", false);
        Assert.Multiple(() => { Assert.That(requests, Is.EqualTo(2)); Assert.That(result!.Version, Is.EqualTo("0.1.5")); });
    }

    [TestCase("missing digest")]
    [TestCase("wrong repository")]
    [TestCase("wrong filename")]
    [TestCase("oversized")]
    public async Task UnverifiedInstallerIsNeverOffered(string problem)
    {
        var row = Release("0.1.3", problem: problem);
        using var client = Client(_ => JsonResponse(new[] { row }));
        var result = await new ReleaseUpdateService(client).CheckAsync("0.1.2", false);
        Assert.That(result!.Installer, Is.Null);
    }

    [Test]
    public async Task SupportsActualInstallerSizesAbove512MiB()
    {
        using var client = Client(_ => JsonResponse(new[] { Release("0.1.3", bytes: 700_000_000) }));
        var result = await new ReleaseUpdateService(client).CheckAsync("0.1.2", false);
        Assert.That(result!.Installer!.Bytes, Is.EqualTo(700_000_000));
    }

    [Test]
    public void RateLimitIsReportedAsFailure()
    {
        using var client = Client(_ => new(HttpStatusCode.Forbidden));
        Assert.ThrowsAsync<IOException>(() => new ReleaseUpdateService(client).CheckAsync("0.1.2", false));
    }

    [Test]
    public async Task DownloadIsVerifiedAndInstallerTargetsExistingInstallation()
    {
        using var factory = new TestContentFactory();
        var paths = new RaceControlPaths(factory.DataRoot, factory.Root);
        using var client = Client(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(InstallerBytes) });
        var installer = Installer();
        string path = await new ReleaseUpdateService(client).DownloadAsync(installer, paths);
        ProcessStartInfo command = ReleaseUpdateService.InstallerCommand(installer, path, paths);
        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(InstallerBytes));
            Assert.That(command.ArgumentList, Does.Contain("/DIR=" + paths.PackageRoot));
            Assert.That(command.ArgumentList, Does.Contain("/NORESTART"));
            Assert.That(command.UseShellExecute, Is.True);
            Assert.That(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.part"), Is.Empty);
        });
        File.AppendAllText(path, "tampered");
        Assert.Throws<InvalidDataException>(() => ReleaseUpdateService.InstallerCommand(installer, path, paths));
    }

    [TestCase("checksum")]
    [TestCase("short")]
    [TestCase("long")]
    public void FailedDownloadKeepsExistingInstallerAndRemovesPartialFile(string failure)
    {
        using var factory = new TestContentFactory();
        var paths = new RaceControlPaths(factory.DataRoot, factory.Root);
        var installer = Installer();
        string folder = Path.Combine(paths.DownloadsDirectory, "Updates");
        Directory.CreateDirectory(folder);
        string target = Path.Combine(folder, installer.Name);
        File.WriteAllText(target, "previous installer");
        byte[] body = failure == "short" ? InstallerBytes[..^1] : failure == "long"
            ? InstallerBytes.Concat(new byte[] { 0 }).ToArray() : new byte[InstallerBytes.Length];
        using var client = Client(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
        Assert.ThrowsAsync<InvalidDataException>(() => new ReleaseUpdateService(client).DownloadAsync(installer, paths));
        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(target), Is.EqualTo("previous installer"));
            Assert.That(Directory.GetFiles(folder, "*.part"), Is.Empty);
        });
    }

    [Test]
    public void CancellationLeavesNoPartialDownload()
    {
        using var factory = new TestContentFactory();
        var paths = new RaceControlPaths(factory.DataRoot, factory.Root);
        using var client = Client(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(InstallerBytes) });
        Assert.CatchAsync<OperationCanceledException>(() => new ReleaseUpdateService(client)
            .DownloadAsync(Installer(), paths, cancellationToken: new CancellationToken(true)));
        Assert.That(Directory.GetFiles(paths.DownloadsDirectory, "*.part", SearchOption.AllDirectories), Is.Empty);
    }

    [Test]
    public async Task PortableInstallerDoesNotOverwritePortableFolder()
    {
        using var factory = new TestContentFactory();
        File.WriteAllText(Path.Combine(factory.Root, "portable.json"), "{}");
        var paths = new RaceControlPaths(Path.Combine(factory.Root, "Data"), factory.Root);
        using var client = Client(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(InstallerBytes) });
        string path = await new ReleaseUpdateService(client).DownloadAsync(Installer(), paths);
        var command = ReleaseUpdateService.InstallerCommand(Installer(), path, paths);
        Assert.That(command.ArgumentList.Any(argument => argument.StartsWith("/DIR=")), Is.False);
    }

    private static UpdateInstaller Installer() => new("RaceControl-0.1.3-Setup.exe",
        new(ReleaseUpdateService.Repository + "/releases/download/race-control-v0.1.3/RaceControl-0.1.3-Setup.exe"), InstallerBytes.Length, Digest);

    private static object Release(string version, bool preview = false, bool draft = false,
        string? tag = null, string? problem = null, long? bytes = null)
    {
        string actualTag = tag ?? "race-control-v" + version;
        string name = $"RaceControl-{version}-Setup.exe";
        return new
        {
            tag_name = actualTag, prerelease = preview, draft,
            assets = new[] { new { name = problem == "wrong filename" ? "other.exe" : name, state = "uploaded",
                size = problem == "oversized" ? long.MaxValue : bytes ?? InstallerBytes.Length,
                digest = problem == "missing digest" ? null : "sha256:" + Digest,
                browser_download_url = (problem == "wrong repository" ? "https://example.com" : ReleaseUpdateService.Repository)
                    + "/releases/download/" + actualTag + "/" + name } }
        };
    }

    private static HttpResponseMessage JsonResponse(object value) => new(HttpStatusCode.OK)
        { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    private static HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> respond) => new(new Handler(respond));
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(respond(request));
        }
    }
}
