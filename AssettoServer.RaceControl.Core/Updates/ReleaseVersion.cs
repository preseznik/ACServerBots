using System.Text.RegularExpressions;

namespace AssettoServer.RaceControl.Core.Updates;

internal sealed record ReleaseVersion(int Major, int Minor, int Patch, string Preview) : IComparable<ReleaseVersion>
{
    public static ReleaseVersion? Parse(string text)
    {
        var match = Regex.Match(text, @"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?\z");
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out int major)
            || !int.TryParse(match.Groups[2].Value, out int minor)
            || !int.TryParse(match.Groups[3].Value, out int patch)) return null;
        string preview = match.Groups[4].Value;
        if (preview.Split('.').Any(part => part.Length > 1 && part.All(char.IsAsciiDigit) && part[0] == '0'))
            return null;
        return new(major, minor, patch, preview);
    }

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null) return 1;
        foreach (int result in new[] { Major.CompareTo(other.Major), Minor.CompareTo(other.Minor), Patch.CompareTo(other.Patch) })
            if (result != 0) return result;
        if (Preview == other.Preview) return 0;
        if (Preview.Length == 0 || other.Preview.Length == 0) return Preview.Length == 0 ? 1 : -1;
        string[] left = Preview.Split('.'), right = other.Preview.Split('.');
        for (int i = 0; i < Math.Min(left.Length, right.Length); i++)
        {
            bool leftNumeric = left[i].All(char.IsAsciiDigit), rightNumeric = right[i].All(char.IsAsciiDigit);
            int result = leftNumeric && rightNumeric ? left[i].Length.CompareTo(right[i].Length)
                : leftNumeric != rightNumeric ? leftNumeric ? -1 : 1 : 0;
            if (result == 0) result = string.CompareOrdinal(left[i], right[i]);
            if (result != 0) return result;
        }
        return left.Length.CompareTo(right.Length);
    }
}
