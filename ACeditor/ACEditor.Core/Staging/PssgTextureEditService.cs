using System.Security.Cryptography;
using System.Text.Json;
using ACEditor.Core.Infrastructure;
using ACEditor.Core.Models;
using EgoEngineLibrary.Formats.Pssg;
using EgoEngineLibrary.Graphics;
using EgoEngineLibrary.Graphics.Dds;

namespace ACEditor.Core.Staging;

public static class PssgTextureEditService
{
    public const string EditKind = "pssg.texture.replace";

    public static void Apply(TrackProject project, string stagedRoot, CancellationToken cancellationToken)
    {
        TrackEditDelta[] edits = project.EditDeltas
            .Where(edit => edit.Kind.Equals(EditKind, StringComparison.OrdinalIgnoreCase))
            .GroupBy(edit => edit.TargetId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToArray();

        foreach (IGrouping<string, TrackEditDelta> artifactEdits in edits.GroupBy(
                     edit => edit.RequiredArtifact ?? string.Empty, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relativePssg = artifactEdits.Key;
            if (string.IsNullOrWhiteSpace(relativePssg) ||
                !Path.GetExtension(relativePssg).Equals(".pssg", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("A PSSG texture edit does not identify its source .pssg artifact.");

            SourceArtifact artifact = project.SourceArtifacts.FirstOrDefault(item =>
                item.RelativePath.Equals(relativePssg, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException($"PSSG source artifact is missing: {relativePssg}");
            if (artifact.WriteDisposition != WriteDisposition.RewriteKnown)
                throw new InvalidDataException($"PSSG source artifact is not writable: {relativePssg}");

            string stagedPssg = PathRules.ResolveInside(stagedRoot, relativePssg);
            PssgFile pssg;
            using (FileStream input = File.Open(stagedPssg, FileMode.Open, FileAccess.Read, FileShare.Read))
                pssg = PssgFile.Open(input);

            foreach (TrackEditDelta edit in artifactEdits)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string textureId = GetTextureId(edit.TargetId, relativePssg);
                PssgNode textureNode = FindTexture(pssg, textureId)
                    ?? throw new InvalidDataException($"PSSG texture '{textureId}' no longer exists in {relativePssg}.");
                PssgTextureReplacement replacement = edit.AfterJson is null
                    ? throw new InvalidDataException($"PSSG texture '{textureId}' has no replacement metadata.")
                    : JsonSerializer.Deserialize<PssgTextureReplacement>(edit.AfterJson)
                      ?? throw new InvalidDataException($"PSSG texture '{textureId}' replacement metadata is invalid.");

                string replacementPath = Path.GetFullPath(replacement.Path);
                if (!File.Exists(replacementPath))
                    throw new FileNotFoundException($"Replacement DDS is missing for '{textureId}'.", replacementPath);
                byte[] replacementBytes = File.ReadAllBytes(replacementPath);
                string actualHash = Convert.ToHexString(SHA256.HashData(replacementBytes)).ToLowerInvariant();
                if (!actualHash.Equals(replacement.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Replacement DDS changed after it was selected: {replacementPath}");

                using var ddsStream = new MemoryStream(replacementBytes, writable: false);
                var dds = new DdsFile(ddsStream);
                dds.ToPssgNode(textureNode);
            }

            string temporaryPath = stagedPssg + $".aceditor-tmp-{Guid.NewGuid():N}";
            try
            {
                using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                           FileShare.None, 1024 * 1024, FileOptions.WriteThrough))
                    pssg.Save(output);

                using (FileStream verificationStream = File.Open(temporaryPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    PssgFile reopened = PssgFile.Open(verificationStream);
                    foreach (TrackEditDelta edit in artifactEdits)
                    {
                        string textureId = GetTextureId(edit.TargetId, relativePssg);
                        PssgNode verified = FindTexture(reopened, textureId)
                            ?? throw new InvalidDataException($"Rewritten PSSG lost texture '{textureId}'.");
                        using var ddsOutput = new MemoryStream();
                        verified.ToDdsFile(cubePreview: false).Write(ddsOutput, -1);
                        if (ddsOutput.Length == 0)
                            throw new InvalidDataException($"Rewritten PSSG texture '{textureId}' could not be reopened.");
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporaryPath, stagedPssg, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }
    }

    private static PssgNode? FindTexture(PssgFile file, string id) =>
        file.FindNodes("TEXTURE", "id", id).FirstOrDefault();

    internal static string GetTextureId(string targetId, string relativePssg)
    {
        string normalizedTarget = targetId.Replace('\\', '/');
        string prefix = relativePssg.Replace('\\', '/') + "#";
        if (!normalizedTarget.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            normalizedTarget.Length == prefix.Length)
            throw new InvalidDataException($"Texture target '{targetId}' does not belong to {relativePssg}.");
        return normalizedTarget[prefix.Length..];
    }
}
