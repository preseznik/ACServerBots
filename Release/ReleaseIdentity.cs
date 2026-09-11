using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace AssettoServer.Release;

public static class ReleaseIdentity
{
    public const string Product = "preseznik/ACServerBots";
#if ASRC_RELEASE
    public static bool IsReleaseBuild => true;
#else
    public static bool IsReleaseBuild => false;
#endif
    public const int ControlProtocol = 1;
    public const int FpsPackVersion = 52;
    public static string Version => typeof(ReleaseIdentity).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
        .InformationalVersion.Split('+')[0];

    public static string CapabilitiesJson => JsonSerializer.Serialize(new
    {
        product = Product, version = Version, controlProtocol = ControlProtocol,
        racePhysics = 1, fpsPreparation = 4, fpsReadyProtocol = 6,
        fpsPackVersion = FpsPackVersion, modes = new[] { "Racing", "Fps" }
    });

    public static string? ValidateServer(string directory)
    {
        string manifest = Path.Combine(directory, "race-control-server.json");
        if (!File.Exists(manifest))
            return "Select a Race Control server release from our fork, or use Download prerequisites.";
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifest));
            var root = document.RootElement;
            if (root.GetProperty("product").GetString() != Product
                || root.GetProperty("version").GetString() != Version
                || root.GetProperty("controlProtocol").GetInt32() != ControlProtocol)
                return $"This launcher requires the matching Race Control server {Version}.";
            return null;
        }
        catch (Exception exception) when (exception is IOException or JsonException
                                         or InvalidOperationException or KeyNotFoundException)
        {
            return "The selected Race Control server has an invalid release manifest.";
        }
    }
}
