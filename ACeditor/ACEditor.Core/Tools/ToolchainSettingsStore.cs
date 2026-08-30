using System.Text.Json;
using System.Text.Json.Serialization;

namespace ACEditor.Core.Tools;

public enum AppTheme
{
    System,
    Dark,
    Light
}

public sealed class ApplicationSettings
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AppTheme Theme { get; set; } = AppTheme.System;
    public ToolchainPaths Toolchain { get; set; } = new();
}

public sealed class ToolchainSettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public ToolchainSettingsStore(string? settingsPath = null)
    {
        SettingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AC Editor", "settings.json");
    }

    public string SettingsPath { get; }

    public ApplicationSettings LoadApplicationSettings()
    {
        if (!File.Exists(SettingsPath)) return new ApplicationSettings();
        try
        {
            string json = File.ReadAllText(SettingsPath);
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("Toolchain", out _) ||
                document.RootElement.TryGetProperty("Theme", out _))
                return JsonSerializer.Deserialize<ApplicationSettings>(json, Options)
                       ?? new ApplicationSettings();

            // Migrate the original flat tool-path settings file without losing overrides.
            ToolchainPaths legacy = JsonSerializer.Deserialize<ToolchainPaths>(json, Options)
                                    ?? new ToolchainPaths();
            return new ApplicationSettings { Toolchain = legacy };
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Invalid AC Editor settings: {SettingsPath}", exception);
        }
    }

    public ToolchainPaths Load() => LoadApplicationSettings().Toolchain;

    public void Save(ToolchainPaths paths)
    {
        ApplicationSettings settings;
        try { settings = LoadApplicationSettings(); }
        catch (InvalidDataException) { settings = new ApplicationSettings(); }
        settings.Toolchain = paths;
        Save(settings);
    }

    public void Save(ApplicationSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        string temporary = SettingsPath + $".tmp-{Guid.NewGuid():N}";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Options));
            File.Move(temporary, SettingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
