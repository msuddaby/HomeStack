using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HomeStack.Core;

/// <summary>
/// A background job that wakes up on an interval, gets a fresh DI scope, and decides for itself
/// whether anything is due. Set <c>BackgroundJobs:Enabled=false</c> to switch all of them off (tests do).
/// </summary>
public abstract class PeriodicWorker(IServiceScopeFactory scopes, IConfiguration config, ILogger logger) : BackgroundService
{
    protected IServiceScopeFactory Scopes { get; } = scopes;
    protected abstract TimeSpan Interval { get; }
    protected abstract Task TickAsync(IServiceProvider services, CancellationToken ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue("BackgroundJobs:Enabled", true)) return;
        try
        {
            // Let startup (migrations, first requests) finish before the first run.
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            using var timer = new PeriodicTimer(Interval);
            do
            {
                try
                {
                    await using var scope = Scopes.CreateAsyncScope();
                    await TickAsync(scope.ServiceProvider, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "{Worker} failed", GetType().Name);
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) { }
    }
}
