using System.Threading.Channels;
using Shared.Common.BackgroundWork;

namespace ElectroWorld.BackgroundJobs;

/// <summary>
/// The in-process queue a submitted attempt's AI work goes on.
///
/// Bounded on purpose. An unbounded queue would answer a burst of submissions by
/// growing until the process died; a bounded one, when full, drops the OLDEST
/// waiting item and logs it — and dropping follow-up work is survivable by
/// design: the essays stay Pending for EssayEvaluationWorker to find, and a
/// missing hint shows up honestly as hintsStatus "Unavailable". Nothing a
/// submission committed is ever at risk.
///
/// Enqueuing never blocks and never throws, because the caller is a request
/// thread holding a committed result.
/// </summary>
public sealed class AttemptFollowUpQueue : IAttemptFollowUpQueue
{
    private readonly Channel<AttemptFollowUp> _channel;
    private readonly ILogger<AttemptFollowUpQueue> _logger;

    public AttemptFollowUpQueue(ILogger<AttemptFollowUpQueue> logger)
    {
        _logger = logger;

        _channel = Channel.CreateBounded<AttemptFollowUp>(new BoundedChannelOptions(Capacity)
        {
            // Never block a request thread; shed the oldest instead, which is the
            // item whose essays the background evaluator is closest to picking up
            // anyway.
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
    }

    /// <summary>Roughly a busy minute of submissions; far more than the worker ever falls behind by.</summary>
    private const int Capacity = 1024;

    public void Enqueue(AttemptFollowUp work)
    {
        ArgumentNullException.ThrowIfNull(work);

        if (!_channel.Writer.TryWrite(work))
            _logger.LogWarning(
                "The AI follow-up for attempt {AttemptId} could not be queued. Its result is saved; "
              + "essays will be graded by the background evaluator and its retry questions carry no hints.",
                work.AttemptId);
    }

    public IAsyncEnumerable<AttemptFollowUp> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
