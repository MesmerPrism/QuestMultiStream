using System.Text.RegularExpressions;
using QuestMultiStream.Core.Models;

namespace QuestMultiStream.Core.Parsing;

public static partial class AdbDeviceParser
{
    public static IReadOnlyList<QuestDevice> Parse(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return Array.Empty<QuestDevice>();
        }

        var devices = new List<QuestDevice>();

        foreach (var rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("List of devices attached", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = WhitespacePattern().Split(line);
            if (parts.Length < 2)
            {
                continue;
            }

            var serial = parts[0];
            var state = parts[1];
            var metadata = ParseMetadata(parts.Skip(2));
            var modelName = NormalizeValue(GetMetadataValue(metadata, "model"));
            var productName = NormalizeValue(GetMetadataValue(metadata, "product"));
            var deviceCodeName = NormalizeValue(GetMetadataValue(metadata, "device"));
            var ipAddress = TryParseIpAddress(serial);
            var connectionKind = ipAddress is not null
                ? QuestDeviceConnectionKind.TcpIp
                : QuestDeviceConnectionKind.Usb;

            devices.Add(new QuestDevice(
                serial,
                BuildDisplayName(modelName, productName, deviceCodeName, serial),
                modelName,
                productName,
                deviceCodeName,
                state,
                connectionKind,
                IsQuestCandidate(modelName, productName, deviceCodeName),
                ipAddress));
        }

        return devices
            .OrderByDescending(device => device.IsQuest)
            .ThenBy(device => device.ConnectionKind)
            .ThenBy(device => device.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static Dictionary<string, string> ParseMetadata(IEnumerable<string> parts)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var part in parts)
        {
            var separatorIndex = part.IndexOf(':');
            if (separatorIndex <= 0 || separatorIndex >= part.Length - 1)
            {
                continue;
            }

            var key = part[..separatorIndex];
            var value = part[(separatorIndex + 1)..];
            metadata[key] = value;
        }

        return metadata;
    }

    private static string? GetMetadataValue(IReadOnlyDictionary<string, string> metadata, string key)
        => metadata.TryGetValue(key, out var value) ? value : null;

    private static string? NormalizeValue(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : value.Replace('_', ' ').Trim();

    private static string BuildDisplayName(string? modelName, string? productName, string? deviceCodeName, string serial)
    {
        foreach (var candidate in new[] { modelName, productName, deviceCodeName })
        {
            var questName = HumanizeQuestName(candidate);
            if (!string.IsNullOrWhiteSpace(questName))
            {
                return questName;
            }
        }

        foreach (var candidate in new[] { modelName, productName, deviceCodeName })
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate;
            }
        }

        return serial;
    }

    private static bool IsQuestCandidate(params string?[] values)
        => values.Any(value =>
            !string.IsNullOrWhiteSpace(value) &&
            QuestTokenPattern().IsMatch(value));

    private static string? HumanizeQuestName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Replace('_', ' ').Trim().ToLowerInvariant();
        if (normalized.Contains("quest 3s", StringComparison.Ordinal))
        {
            return "Quest 3S";
        }

        if (normalized.Contains("quest 3", StringComparison.Ordinal) || normalized.Contains("eureka", StringComparison.Ordinal))
        {
            return "Quest 3";
        }

        if (normalized.Contains("quest pro", StringComparison.Ordinal) || normalized.Contains("seacliff", StringComparison.Ordinal))
        {
            return "Quest Pro";
        }

        if (normalized.Contains("quest 2", StringComparison.Ordinal) || normalized.Contains("hollywood", StringComparison.Ordinal))
        {
            return "Quest 2";
        }

        return null;
    }

    private static string? TryParseIpAddress(string serial)
    {
        var separatorIndex = serial.IndexOf(':');
        if (separatorIndex <= 0)
        {
            return null;
        }

        var address = serial[..separatorIndex];
        return IpAddressPattern().IsMatch(address) ? address : null;
    }

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex WhitespacePattern();

    [GeneratedRegex(@"(quest|eureka|hollywood|seacliff)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex QuestTokenPattern();

    [GeneratedRegex(@"^\d{1,3}(?:\.\d{1,3}){3}$", RegexOptions.Compiled)]
    private static partial Regex IpAddressPattern();
}
