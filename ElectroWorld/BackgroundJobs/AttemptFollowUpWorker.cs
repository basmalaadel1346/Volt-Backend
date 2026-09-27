using AssessmentBL.Interfaces;
using Shared.Common.BackgroundWork;

namespace ElectroWorld.BackgroundJobs;

/// <summary>
/// Runs the AI work of submitted attempts off the request thread: hints for the
/// wrong answers, then grading of the essays, then a push to the learner's app.
///
/// This is what lets POST .../submit return as soon as the score is committed.
/// The work it runs is all optional — every piece of it degrades to "not yet"
/// rather than to an error — so one attempt failing must never stop the loop, and
/// nothing here is ever retried at the cost of a result that is already saved.
/// </summary>
public sealed class AttemptFollowUpWorker : BackgroundService
{
    private readonly AttemptFollowUpQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AttemptFollowUpWorker> _logger;

    public AttemptFollowUpWorker(
        AttemptFollowUpQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<AttemptFollowUpWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Don't hold up application startup.
        await Task.Yield();

        try
        {
            await foreach (var work in _queue.ReadAllAsync(stoppingToken))
                await RunOneAsync(work, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host is shutting down. Whatever is still queued is picked up by the
            // essay evaluator on the next start; hints for those attempts are
            // simply missing, which the retry endpoint reports honestly.
        }
    }

    private async Task RunOneAsync(AttemptFollowUp work, CancellationToken stoppingToken)
    {
        try
        {
            // The DbContext is scoped; a hosted service is a singleton.
            await using var scope = _scopeFactory.CreateAsyncScope();
            var attempts = scope.ServiceProvider.GetRequiredService<IQuizAttemptService>();

            await attempts.RunFollowUpAsync(work, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host is shutting down.
        }
        catch (Exception ex)
        {
            // Never let one attempt stop the loop (the .NET default for an
            // unhandled BackgroundService exception is to stop the host).
            _logger.LogError(ex,
                "The AI follow-up for attempt {AttemptId} failed. Its saved result is unaffected.",
                work.AttemptId);
        }
    }
}
