using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace QuestMultiStream.Browser;

public static class MonitorLifetime
{
    public const string Bounded = "bounded", UntilStop = "until-operator-stop";
    public static DateTimeOffset ParseUtc(string text)
    {
        if (!Regex.IsMatch(text, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?(Z|\+00:00)$") ||
            !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value) || value.Offset != TimeSpan.Zero)
            throw new InvalidOperationException("Deadline must be an explicit ISO UTC string; locale dates are rejected");
        return value;
    }
    public static void ValidateCapture(string mode, DateTimeOffset? end, DateTimeOffset now, bool current)
    {
        if (mode == UntilStop) { if (end is not null) throw new InvalidOperationException("Until-stop capture cannot carry a deadline"); }
        else if (mode != Bounded || end is null || current && end <= now)
            throw new InvalidOperationException("Bounded capture requires a current deadline; unknown lifetime denied");
    }
    public static CancellationTokenSource CaptureCancellation(DateTimeOffset? end, CancellationToken stop)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(stop);
        if (end is not null) source.CancelAfter(end.Value - DateTimeOffset.UtcNow);
        return source;
    }
    public static bool OperatorStopWithinScope(bool requested, DateTimeOffset? end, DateTimeOffset now) =>
        requested && (end is null || now < end); // A late stop cannot relabel a real expiry as success.
    public static async Task StopAfterRetirement(Func<bool> idle, Action stopped)
    {
        while (!idle()) await Task.Delay(50); // Unknown owned child keeps the same invocation alive.
        stopped();
    }
}

public sealed class StrictUtcDateConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String ? MonitorLifetime.ParseUtc(reader.GetString()!) : throw new JsonException("UTC string required");
    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToUniversalTime().ToString("o"));
}
