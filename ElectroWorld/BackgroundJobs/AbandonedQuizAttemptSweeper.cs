using AssessmentBL;
using AssessmentBL.Interfaces;
using Microsoft.Extensions.Options;

namespace ElectroWorld.BackgroundJobs;

/// <summary>
/// Periodically moves quiz attempts that stayed InProgress past the allowed
/// window to Abandoned. The rule lives in
/// IQuizAttemptService.AbandonExpiredAttemptsAsync — this class only schedules
/// it. The project has no job scheduler, so this uses the host's built-in
/// BackgroundService rather than introducing one.
///
/// Safe on several instances at once: the UPDATE only ever matches InProgress
/// rows, so concurrent sweeps simply find less to do.
/// </summary>
public sealed class AbandonedQuizAttemptSweeper : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AssessmentSettings _settings;
    private readonly ILogger<AbandonedQuizAttemptSweeper> _logger;

    public AbandonedQuizAttemptSweeper(
        IServiceScopeFactory scopeFactory,
        IOptions<AssessmentSettings> settings,
        ILogger<AbandonedQuizAttemptSweeper> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Don't hold up application startup with the first sweep.
        await Task.Yield();

        using var timer = new PeriodicTimer(_settings.AbandonedAttemptSweepInterval);

        do
        {
            await SweepOnceAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SweepOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            // The DbContext is scoped; a hosted service is a singleton.
            await using var scope = _scopeFactory.CreateAsyncScope();
            var attempts = scope.ServiceProvider.GetRequiredService<IQuizAttemptService>();

            var abandoned = await attempts.AbandonExpiredAttemptsAsync(stoppingToken);

            if (abandoned > 0)
                _logger.LogInformation(
                    "Marked {Count} quiz attempt(s) as Abandoned (InProgress for more than {TimeoutMinutes} minutes).",
                    abandoned, _settings.InProgressAttemptTimeout.TotalMinutes);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host is shutting down.
        }
        catch (Exception ex)
        {
            // Never let one failed run stop the host (the .NET 8 default for an
            // unhandled BackgroundService exception). The next tick retries.
            _logger.LogError(ex, "Abandoned quiz-attempt sweep failed; it will run again at the next interval.");
        }
    }
}
