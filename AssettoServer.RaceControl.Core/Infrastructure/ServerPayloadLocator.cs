using AssettoServer.Release;

namespace AssettoServer.RaceControl.Core.Infrastructure;

public static class ServerPayloadLocator
{
    public static string? Find(bool requireMatchingRelease, params string?[] candidates) =>
        candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path)
            && File.Exists(Path.Combine(path, "AssettoServer.exe"))
            && (!requireMatchingRelease || ReleaseIdentity.ValidateServer(path) is null));
}
