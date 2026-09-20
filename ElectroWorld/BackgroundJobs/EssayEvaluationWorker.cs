using AssessmentBL;
using AssessmentBL.Interfaces;
using Microsoft.Extensions.Options;

namespace ElectroWorld.BackgroundJobs;

/// <summary>
/// Evaluates essay answers the submission itself could not: the AI was down,
/// timed out, or the submission's AI budget ran out. One batch per tick, with the
/// retry delay and attempt limit applied by IEssayEvaluationService — this class
/// only schedules it. Does nothing while no essay endpoint is configured.
/// </summary>
public sealed class EssayEvaluationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AssessmentSettings _settings;
    private readonly ILogger<EssayEvaluationWorker> _logger;

    public EssayEvaluationWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<AssessmentSettings> settings,
        ILogger<EssayEvaluationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Don't hold up application startup with the first run.
        await Task.Yield();

        using var timer = new PeriodicTimer(_settings.EssayEvaluationInterval);

        do
        {
            await RunOnceAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            // The DbContext is scoped; a hosted service is a singleton.
            await using var scope = _scopeFactory.CreateAsyncScope();
            var essays = scope.ServiceProvider.GetRequiredService<IEssayEvaluationService>();

            var decided = await essays.EvaluateDueAsync(stoppingToken);

            if (decided > 0)
                _logger.LogInformation("Finished {Count} pending essay answer(s) (Graded or NotGraded).", decided);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host is shutting down.
        }
        catch (Exception ex)
        {
            // Never let one failed run stop the host; the next tick retries.
            _logger.LogError(ex, "Essay evaluation run failed; it will run again at the next interval.");
        }
    }
}
