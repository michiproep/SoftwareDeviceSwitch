using System.Globalization;
using System.Text.RegularExpressions;

namespace SoftwareDeviceSwitch;

internal sealed record MonitorInputOption(int Code, string Label);

internal static class MonitorInputs
{
    internal static readonly int[] CommonCodes = [1, 3, 4, 15, 16, 17, 18];

    // MCCS input-source values. Manufacturers may use different values, especially for USB-C.
    internal static string Name(int code) => code switch
    {
        1 => "VGA 1", 2 => "VGA 2", 3 => "DVI 1", 4 => "DVI 2",
        5 => "Composite video 1", 6 => "Composite video 2", 7 => "S-Video 1", 8 => "S-Video 2",
        9 => "Tuner 1", 10 => "Tuner 2", 11 => "Tuner 3",
        12 => "Component video 1", 13 => "Component video 2", 14 => "Component video 3",
        15 => "DisplayPort 1", 16 => "DisplayPort 2", 17 => "HDMI 1", 18 => "HDMI 2",
        _ => "Custom input"
    };

    internal static string Label(int code) => $"{Name(code)} ({code})";

    internal static List<MonitorInputOption> Options(IEnumerable<int> codes) => codes
        .Where(c => c is > 0 and <= 65535).Distinct().Order().Select(c => new MonitorInputOption(c, Label(c))).ToList();

    internal static List<int> ParseCapabilities(string capabilities)
    {
        // Walk only top-level features inside vcp(...). A 60 nested inside another
        // feature's values is not an input-source declaration. Values here are hexadecimal.
        var section = Regex.Match(capabilities, @"(?<![A-Za-z0-9_])vcp\s*\(", RegexOptions.IgnoreCase);
        if (!section.Success) return [];
        int position = section.Index + section.Length;
        var inputs = new HashSet<int>();
        while (position < capabilities.Length)
        {
            while (position < capabilities.Length && char.IsWhiteSpace(capabilities[position])) position++;
            if (position >= capabilities.Length) break;
            if (capabilities[position] == ')') return inputs.Order().ToList();
            int start = position;
            while (position < capabilities.Length && !char.IsWhiteSpace(capabilities[position]) && capabilities[position] is not '(' and not ')') position++;
            string feature = capabilities[start..position];
            while (position < capabilities.Length && char.IsWhiteSpace(capabilities[position])) position++;
            if (position >= capabilities.Length || capabilities[position] != '(')
            {
                if (position == start) return []; // malformed input must not stall the scanner
                continue;
            }
            int valuesStart = ++position;
            int depth = 1;
            while (position < capabilities.Length && depth > 0)
            {
                if (capabilities[position] == '(') depth++;
                if (capabilities[position] == ')') depth--;
                position++;
            }
            if (depth != 0) return []; // incomplete replies cannot establish supported inputs
            if (!feature.Equals("60", StringComparison.OrdinalIgnoreCase)) continue;
            string values = capabilities[valuesStart..(position - 1)];
            if (values.Contains('(') || values.Contains(')')) return [];
            foreach (string token in values.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                if (int.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code) && code is > 0 and <= 65535)
                    inputs.Add(code);
        }
        return [];
    }
}
