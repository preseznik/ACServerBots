using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AssettoServer.RaceControl.Core.Configuration;
using AssettoServer.RaceControl.Core.Models;

namespace AssettoServer.RaceControl.Core.Content;

public sealed partial class AcContentScanner
{
    public Task<AcContentCatalog> ScanAsync(string assettoCorsaRoot, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Scan(assettoCorsaRoot, cancellationToken), cancellationToken);
    }

    public AcContentCatalog Scan(string assettoCorsaRoot, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assettoCorsaRoot);
        var contentRoot = Path.Combine(assettoCorsaRoot, "content");
        if (!Directory.Exists(contentRoot))
        {
            throw new DirectoryNotFoundException($"Assetto Corsa content directory was not found: {contentRoot}");
        }

        var cars = ScanCars(Path.Combine(contentRoot, "cars"), cancellationToken);
        var tracks = ScanTracks(Path.Combine(contentRoot, "tracks"), cancellationToken);
        var weather = ScanWeather(Path.Combine(contentRoot, "weather"), cancellationToken);
        return new(cars, tracks, weather, DateTimeOffset.Now);
    }

    private static List<AcCar> ScanCars(string carsRoot, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(carsRoot))
        {
            return [];
        }

        var cars = new List<AcCar>();
        foreach (var directory in Directory.EnumerateDirectories(carsRoot).Order(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = Path.GetFileName(directory);
            var uiPath = Path.Combine(directory, "ui", "ui_car.json");
            using var metadata = LooseJson.TryRead(uiPath);
            var root = metadata?.RootElement;
            var name = LooseJson.String(root, "name") ?? Humanize(id);
            var brand = LooseJson.String(root, "brand") ?? string.Empty;
            var className = LooseJson.String(root, "class") ?? string.Empty;
            var country = LooseJson.String(root, "country") ?? string.Empty;
            var tags = LooseJson.StringArray(root, "tags");
            var skins = ScanSkins(Path.Combine(directory, "skins"));

            cars.Add(new AcCar(
                id,
                name,
                brand,
                className,
                country,
                tags,
                directory,
                ExistingPath(Path.Combine(directory, "ui", "badge.png")),
                skins,
                File.Exists(Path.Combine(directory, "data.acd")) || Directory.Exists(Path.Combine(directory, "data")),
                File.Exists(Path.Combine(directory, "collider.kn5")),
                ExistingPath(Path.Combine(directory, "data.acd")),
                ExistingPath(Path.Combine(directory, "collider.kn5")),
                LooseJson.SpecNumber(root, "weight"),
                LooseJson.SpecNumber(root, "bhp") ?? LooseJson.SpecNumber(root, "power"),
                LooseJson.SpecNumber(root, "torque"),
                LooseJson.SpecNumber(root, "topspeed")));
        }

        return cars.OrderBy(car => car.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static List<AcSkin> ScanSkins(string skinsRoot)
    {
        if (!Directory.Exists(skinsRoot))
        {
            return [];
        }

        var skins = new List<AcSkin>();
        foreach (var directory in Directory.EnumerateDirectories(skinsRoot).Order(StringComparer.OrdinalIgnoreCase))
        {
            var id = Path.GetFileName(directory);
            using var metadata = LooseJson.TryRead(Path.Combine(directory, "ui_skin.json"));
            var name = LooseJson.String(metadata?.RootElement, "skinname")
                ?? LooseJson.String(metadata?.RootElement, "name")
                ?? Humanize(id);
            skins.Add(new(
                id,
                name,
                ExistingPath(Path.Combine(directory, "preview.jpg")) ?? ExistingPath(Path.Combine(directory, "preview.png")),
                ExistingPath(Path.Combine(directory, "livery.png"))));
        }

        return skins;
    }

    private static List<AcTrackLayout> ScanTracks(string tracksRoot, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(tracksRoot))
        {
            return [];
        }

        var tracks = new List<AcTrackLayout>();
        foreach (var directory in Directory.EnumerateDirectories(tracksRoot).Order(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var trackId = Path.GetFileName(directory);
            var layouts = FindLayouts(directory);
            foreach (var layoutId in layouts)
            {
                var uiDirectory = string.IsNullOrEmpty(layoutId)
                    ? Path.Combine(directory, "ui")
                    : Path.Combine(directory, "ui", layoutId);
                var uiPath = Path.Combine(uiDirectory, "ui_track.json");
                if (!File.Exists(uiPath) && !string.IsNullOrEmpty(layoutId))
                {
                    var alternate = Path.Combine(directory, layoutId, "ui", "ui_track.json");
                    if (File.Exists(alternate))
                    {
                        uiPath = alternate;
                        uiDirectory = Path.GetDirectoryName(alternate)!;
                    }
                }

                using var metadata = LooseJson.TryRead(uiPath);
                var root = metadata?.RootElement;
                var name = LooseJson.String(root, "name") ?? Humanize(trackId);
                var layoutName = LooseJson.String(root, "layout")
                    ?? (string.IsNullOrEmpty(layoutId) ? string.Empty : Humanize(layoutId));
                var models = string.IsNullOrEmpty(layoutId)
                    ? Path.Combine(directory, "models.ini")
                    : Path.Combine(directory, $"models_{layoutId}.ini");
                if (!File.Exists(models))
                {
                    models = Path.Combine(directory, layoutId, "models.ini");
                }

                var fastLane = FindFastLane(directory, layoutId);
                tracks.Add(new AcTrackLayout(
                    trackId,
                    layoutId,
                    name,
                    layoutName,
                    LooseJson.String(root, "country") ?? string.Empty,
                    LooseJson.String(root, "city") ?? string.Empty,
                    LooseJson.Int32(root, "pitboxes") ?? 0,
                    directory,
                    File.Exists(uiPath) ? uiPath : null,
                    ExistingPath(Path.Combine(uiDirectory, "preview.png")),
                    ExistingPath(Path.Combine(uiDirectory, "outline.png")),
                    models,
                    fastLane));
            }
        }

        return tracks.OrderBy(track => track.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static IReadOnlyList<string> FindLayouts(string trackDirectory)
    {
        var layouts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(Path.Combine(trackDirectory, "models.ini")))
        {
            layouts.Add(string.Empty);
        }

        foreach (var modelsFile in Directory.EnumerateFiles(trackDirectory, "models_*.ini", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileNameWithoutExtension(modelsFile);
            layouts.Add(name["models_".Length..]);
        }

        var uiRoot = Path.Combine(trackDirectory, "ui");
        if (Directory.Exists(uiRoot))
        {
            foreach (var uiDirectory in Directory.EnumerateDirectories(uiRoot))
            {
                if (File.Exists(Path.Combine(uiDirectory, "ui_track.json")))
                {
                    layouts.Add(Path.GetFileName(uiDirectory));
                }
            }
        }

        if (layouts.Count == 0)
        {
            layouts.Add(string.Empty);
        }

        return layouts.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string FindFastLane(string trackDirectory, string layoutId)
    {
        var candidates = string.IsNullOrEmpty(layoutId)
            ? new[] { Path.Combine(trackDirectory, "ai", "fast_lane.ai") }
            : new[]
            {
                Path.Combine(trackDirectory, layoutId, "ai", "fast_lane.ai"),
                Path.Combine(trackDirectory, "ai", layoutId, "fast_lane.ai"),
            };
        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    private static List<AcWeather> ScanWeather(string weatherRoot, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(weatherRoot))
        {
            return [];
        }

        var weather = new List<AcWeather>();
        foreach (var directory in Directory.EnumerateDirectories(weatherRoot).Order(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = Path.GetFileName(directory);
            var name = Humanize(id);
            int? weatherFxType = null;
            var configurationPath = Path.Combine(directory, "weather.ini");
            if (File.Exists(configurationPath))
            {
                try
                {
                    var configuration = IniDocument.Load(configurationPath);
                    name = CleanIniValue(configuration.Get("LAUNCHER", "NAME")) ?? name;
                    var weatherTypeText = CleanIniValue(configuration.Get("__LAUNCHER_CM", "WEATHER_TYPE"));
                    if (int.TryParse(weatherTypeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedType)
                        && parsedType is >= 0 and <= 32)
                    {
                        weatherFxType = parsedType;
                    }
                }
                catch (IOException)
                {
                    // Keep content visible even if optional launcher metadata cannot be read.
                }
            }

            weather.Add(new(id, name, directory, ExistingPath(Path.Combine(directory, "preview.jpg")), weatherFxType));
        }

        return weather;
    }

    private static string? ExistingPath(string path) => File.Exists(path) ? path : null;

    private static string Humanize(string id) => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(id.Replace('_', ' ').Trim());

    private static string? CleanIniValue(string? value)
    {
        var cleaned = value?.Split(';', 2)[0].Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }

    private static partial class LooseJson
    {
        [GeneratedRegex(@"[-+]?\d+(?:[\.,]\d+)?", RegexOptions.CultureInvariant)]
        private static partial Regex NumberRegex();

        public static JsonDocument? TryRead(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                var text = File.ReadAllText(path);
                var options = new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                };
                try
                {
                    return JsonDocument.Parse(text, options);
                }
                catch (JsonException) when (text.Contains("\"specs\"",
                                                StringComparison.OrdinalIgnoreCase))
                {
                    // A number of stock Kunos ui_car.json files contain literal newlines in
                    // their long description string. Keep rejecting arbitrary broken mod JSON,
                    // but recover stock-style files with a specs block so their mass, power and
                    // top speed do not silently collapse to the generic vehicle profile.
                    return JsonDocument.Parse(EscapeStringControlCharacters(text), options);
                }
            }
            catch (JsonException)
            {
                return null;
            }
            catch (IOException)
            {
                return null;
            }
        }

        private static string EscapeStringControlCharacters(string text)
        {
            var builder = new System.Text.StringBuilder(text.Length + 32);
            bool inString = false;
            bool escaped = false;
            foreach (char character in text)
            {
                if (!inString)
                {
                    builder.Append(character);
                    if (character == '"')
                        inString = true;
                    continue;
                }

                if (escaped)
                {
                    builder.Append(character);
                    escaped = false;
                    continue;
                }
                if (character == '\\')
                {
                    builder.Append(character);
                    escaped = true;
                    continue;
                }
                if (character == '"')
                {
                    builder.Append(character);
                    inString = false;
                    continue;
                }

                builder.Append(character switch
                {
                    '\r' => "\\r",
                    '\n' => "\\n",
                    '\t' => "\\t",
                    _ => character.ToString(),
                });
            }
            return builder.ToString();
        }

        public static string? String(JsonElement? root, string name)
        {
            if (root is not { ValueKind: JsonValueKind.Object } value || !TryGet(value, name, out var property))
            {
                return null;
            }

            return property.ValueKind switch
            {
                JsonValueKind.String => property.GetString(),
                JsonValueKind.Number => property.GetRawText(),
                _ => null,
            };
        }

        public static int? Int32(JsonElement? root, string name)
        {
            var text = String(root, name);
            if (text is not null && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }

            if (root is { ValueKind: JsonValueKind.Object } value && TryGet(value, name, out var property)
                && property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out parsed))
            {
                return parsed;
            }

            return null;
        }

        public static IReadOnlyList<string> StringArray(JsonElement? root, string name)
        {
            if (root is not { ValueKind: JsonValueKind.Object } value || !TryGet(value, name, out var property)
                || property.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return property.EnumerateArray()
                .Where(element => element.ValueKind == JsonValueKind.String)
                .Select(element => element.GetString())
                .OfType<string>()
                .ToArray();
        }

        public static double? SpecNumber(JsonElement? root, string name)
        {
            if (root is not { ValueKind: JsonValueKind.Object } value)
            {
                return null;
            }

            JsonElement spec;
            if (TryGet(value, "specs", out var specs) && specs.ValueKind == JsonValueKind.Object && TryGet(specs, name, out spec))
            {
                return ParseNumber(spec);
            }

            return TryGet(value, name, out spec) ? ParseNumber(spec) : null;
        }

        private static double? ParseNumber(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var numeric))
            {
                return numeric;
            }

            if (element.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var match = NumberRegex().Match(element.GetString() ?? string.Empty);
            return match.Success && double.TryParse(match.Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out numeric)
                ? numeric
                : null;
        }

        private static bool TryGet(JsonElement element, string name, out JsonElement value)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }
    }
}
