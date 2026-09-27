namespace Shared.Common.BackgroundWork;

/// <summary>
/// Where a submitted attempt's OPTIONAL work goes: the AI hints for the wrong
/// answers, and the AI grading of the essays.
///
/// Submitting used to do both inline, inside a 15-second budget, so a child
/// waited on the AI for a score that had already been committed before the AI
/// was called at all. Now the submission commits, hands the follow-up to this
/// queue and returns; the work runs on a background scope and the app is told it
/// finished through <see cref="Realtime.ILearnerNotifier"/> — no polling.
///
/// Enqueuing never blocks and never throws: losing follow-up work degrades a
/// result to "no hints yet", which the essay worker and the retry endpoint both
/// recover from, and must never fail a submission that is already saved.
/// </summary>
public interface IAttemptFollowUpQueue
{
    void Enqueue(AttemptFollowUp work);
}

/// <summary>One submitted attempt's follow-up. Plain values: it outlives the request scope.</summary>
/// <param name="AttemptId">The committed attempt.</param>
/// <param name="UserId">Its owner — who to push the "ready" notification to.</param>
/// <param name="Language">The language the attempt was submitted in; hints and feedback are written in it.</param>
/// <param name="NeedsHints">There was at least one wrong auto-graded answer.</param>
/// <param name="NeedsEssayGrading">The attempt contains at least one essay answer.</param>
public sealed record AttemptFollowUp(
    long AttemptId,
    Guid UserId,
    string Language,
    bool NeedsHints,
    bool NeedsEssayGrading);
