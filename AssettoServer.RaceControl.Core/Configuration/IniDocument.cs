using System.Text;

namespace AssettoServer.RaceControl.Core.Configuration;

public sealed class IniDocument
{
    private readonly List<IniSection> _sections = [];

    public IReadOnlyList<IniSection> Sections => _sections;

    public static IniDocument Load(string path) => Parse(File.ReadAllText(path));

    public static IniDocument Parse(string text)
    {
        var document = new IniDocument();
        IniSection? current = null;
        foreach (var rawLine in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                current = document.GetOrAddSection(line[1..^1].Trim());
                continue;
            }

            var separator = line.IndexOf('=');
            if (current is not null && separator > 0)
            {
                current.Set(line[..separator].Trim(), line[(separator + 1)..].Trim());
            }
        }

        return document;
    }

    public IniSection GetOrAddSection(string name)
    {
        var section = FindSection(name);
        if (section is not null)
        {
            return section;
        }

        section = new IniSection(name);
        _sections.Add(section);
        return section;
    }

    public IniSection? FindSection(string name) => _sections.FirstOrDefault(
        section => section.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public string? Get(string section, string key) => FindSection(section)?.Get(key);

    public void Set(string section, string key, object? value) => GetOrAddSection(section).Set(key, value?.ToString() ?? string.Empty);

    public void RemoveSection(string name) => _sections.RemoveAll(section => section.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, ToString(), new UTF8Encoding(false));
    }

    public override string ToString()
    {
        var builder = new StringBuilder();
        foreach (var section in _sections)
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append('[').Append(section.Name).AppendLine("]");
            foreach (var pair in section.Values)
            {
                builder.Append(pair.Key).Append('=').AppendLine(pair.Value);
            }
        }

        return builder.ToString();
    }
}

public sealed class IniSection
{
    private readonly List<KeyValuePair<string, string>> _values = [];

    internal IniSection(string name) => Name = name;

    public string Name { get; }
    public IReadOnlyList<KeyValuePair<string, string>> Values => _values;

    public string? Get(string key) => _values.FirstOrDefault(
        pair => pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value;

    public void Set(string key, string value)
    {
        value = value.Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
        var index = _values.FindIndex(pair => pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        var pair = new KeyValuePair<string, string>(key, value);
        if (index >= 0)
        {
            _values[index] = pair;
        }
        else
        {
            _values.Add(pair);
        }
    }
}
