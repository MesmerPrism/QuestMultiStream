namespace QuestMultiStream.Browser;

// The default ConsoleLifetime would stop ASP.NET before an unresolved capture retires.
public sealed class OwnedConsoleLifetime(Action requestStop) : IHostLifetime
{
    public Task WaitForStartAsync(CancellationToken cancellationToken)
    {
        Console.CancelKeyPress += Cancel;
        return Task.CompletedTask;
    }
    void Cancel(object? sender, ConsoleCancelEventArgs args) { args.Cancel = true; requestStop(); }
    public Task StopAsync(CancellationToken cancellationToken)
    {
        Console.CancelKeyPress -= Cancel;
        return Task.CompletedTask;
    }
}
