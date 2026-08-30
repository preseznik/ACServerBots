using System.Text;

namespace AssettoServer.RaceControl.Core.Staging;

public static class FpsClientPackAssets
{
    public const string RifleViewmodelPath =
        "content/objects3D/asrc_fps/asrc_assault_rifle_viewmodel.kn5";
    public const string RifleWorldModelPath =
        "content/objects3D/asrc_fps/asrc_assault_rifle_world.kn5";
    public const string RifleDiffusePath =
        "content/objects3D/asrc_fps/asrc_rifle_diffuse.png";
    public const string OperatorSkinPath =
        "content/objects3D/asrc_fps/asrc_operator_skin.png";
    public const string HudManifestPath = "apps/lua/asrc_fps_hud/manifest.ini";
    public const string HudScriptPath = "apps/lua/asrc_fps_hud/asrc_fps_hud.lua";

    public static byte[] GetRifleViewmodel() => ReadEmbeddedKn5(
        "AssettoServer.RaceControl.Core.Assets.Fps.asrc_assault_rifle_viewmodel.kn5");

    public static byte[] GetRifleWorldModel() => ReadEmbeddedKn5(
        "AssettoServer.RaceControl.Core.Assets.Fps.asrc_assault_rifle_world.kn5");

    public static byte[] GetRifleDiffuse() => ReadEmbeddedPng(
        "AssettoServer.RaceControl.Core.Assets.Fps.asrc_rifle_diffuse.png");

    public static byte[] GetOperatorSkin() => ReadEmbeddedPng(
        "AssettoServer.RaceControl.Core.Assets.Fps.asrc_operator_skin.png");

    public static byte[] GetHudManifest() => ReadEmbeddedText(
        "AssettoServer.RaceControl.Core.Assets.Fps.Hud.manifest.ini");

    public static byte[] GetHudScript() => ReadEmbeddedText(
        "AssettoServer.RaceControl.Core.Assets.Fps.Hud.asrc_fps_hud.lua");

    public static string Sha256(byte[] data) => Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(data)).ToLowerInvariant();

    public static byte[] CreateRifleWave()
    {
        const int sampleRate = 44_100;
        const float durationSeconds = 0.22f;
        int sampleCount = (int)(sampleRate * durationSeconds);
        using var stream = new MemoryStream(44 + sampleCount * sizeof(short));
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + sampleCount * sizeof(short));
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * sizeof(short));
        writer.Write((short)sizeof(short));
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(sampleCount * sizeof(short));

        uint noise = 0xC0FFEEu;
        for (int index = 0; index < sampleCount; index++)
        {
            float time = index / (float)sampleRate;
            noise = unchecked(noise * 1_664_525u + 1_013_904_223u);
            float white = ((noise >> 8) & 0xffff) / 32767.5f - 1;
            float envelope = MathF.Exp(-time * 21);
            float thump = MathF.Sin(MathF.Tau * (105 - time * 180) * time) * 0.32f;
            float crack = time < 0.012f ? (1 - time / 0.012f) * 0.42f : 0;
            float sample = Math.Clamp((white * 0.68f + thump) * envelope + crack, -1, 1);
            writer.Write((short)(sample * short.MaxValue));
        }
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] ReadEmbeddedKn5(string resourceName)
    {
        using Stream stream = typeof(FpsClientPackAssets).Assembly.GetManifestResourceStream(resourceName)
                              ?? throw new InvalidOperationException(
                                  $"Embedded FPS client asset was not found: {resourceName}");
        using var output = new MemoryStream();
        stream.CopyTo(output);
        byte[] data = output.ToArray();
        if (data.Length < 1024 || !data.AsSpan(0, 6).SequenceEqual("sc6969"u8))
            throw new InvalidDataException($"Embedded FPS client asset is not a valid KN5: {resourceName}");
        return data;
    }

    private static byte[] ReadEmbeddedPng(string resourceName)
    {
        using Stream stream = typeof(FpsClientPackAssets).Assembly.GetManifestResourceStream(resourceName)
                              ?? throw new InvalidOperationException(
                                  $"Embedded FPS client asset was not found: {resourceName}");
        using var output = new MemoryStream();
        stream.CopyTo(output);
        byte[] data = output.ToArray();
        ReadOnlySpan<byte> pngMagic = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
        if (data.Length < 1024 || !data.AsSpan(0, pngMagic.Length).SequenceEqual(pngMagic))
            throw new InvalidDataException($"Embedded FPS client asset is not a valid PNG: {resourceName}");
        return data;
    }

    private static byte[] ReadEmbeddedText(string resourceName)
    {
        using Stream stream = typeof(FpsClientPackAssets).Assembly.GetManifestResourceStream(resourceName)
                              ?? throw new InvalidOperationException(
                                  $"Embedded FPS client asset was not found: {resourceName}");
        using var output = new MemoryStream();
        stream.CopyTo(output);
        byte[] data = output.ToArray();
        if (data.Length < 32 || data.Any(value => value == 0))
            throw new InvalidDataException($"Embedded FPS client text asset is invalid: {resourceName}");
        return data;
    }
}
