namespace QuestMultiStream.Core.Models;

public sealed record ProcessCommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);
