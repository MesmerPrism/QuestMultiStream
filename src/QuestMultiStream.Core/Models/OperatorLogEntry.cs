namespace QuestMultiStream.Core.Models;

public sealed record OperatorLogEntry(
    DateTimeOffset Timestamp,
    string Level,
    string Message,
    string? Detail = null);
