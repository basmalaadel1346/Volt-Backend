using System.Globalization;
using AssessmentBL.Interfaces;
using AssessmentBL.Services.Constants;
using AssessmentDA.Context;
using AssessmentDA.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Assessment.AI;
using Shared.Common.Abstractions;

namespace AssessmentBL.Services
{
    /// <summary>
    /// AI evaluation of essay answers. Essays are graded by the AI only — there is
    /// no model answer, no rubric and no person reviewing the result. The AI sees
    /// the question (text and image description) and the child's answer and
    /// returns points and feedback; the backend only checks that the grade is
    /// well-formed (<see cref="Decide"/>) before it becomes final.
    ///
    /// Runs twice over, never on the path to the commit: inline right after a
    /// submission commits (inside that submission's AI budget) so the child
    /// usually gets feedback at once, and from EssayEvaluationWorker for anything
    /// that did not finish. Every run first CLAIMS its answers in one UPDATE, so
    /// two app instances never evaluate the same answer, and writes a decision only
    /// while it still holds that claim on a Pending answer, so a final status is
    /// never overwritten. One AI request carries one attempt — one child — so a
    /// child's text can never influence another child's evaluation.
    /// </summary>
    public sealed class EssayEvaluationService : IEssayEvaluationService
    {
        private const int BatchSize = 20;

        // The AI's decline reason is free text that may quote the child's answer:
        // only a bounded prefix reaches the logs.
        private const int MaxLoggedReasonLength = 200;

        private readonly AssessmentDbContext _db;
        private readonly IAiEssayEvaluator _evaluator;
        private readonly AiRequestBuilder _aiRequests;
        private readonly IDateTimeProvider _clock;
        private readonly AssessmentSettings _settings;
        private readonly ILogger<EssayEvaluationService> _logger;

        public EssayEvaluationService(
            AssessmentDbContext db,
            IAiEssayEvaluator evaluator,
            AiRequestBuilder aiRequests,
            IDateTimeProvider clock,
            IOptions<AssessmentSettings> settings,
            ILogger<EssayEvaluationService> logger)
        {
            _db = db;
            _evaluator = evaluator;
            _aiRequests = aiRequests;
            _clock = clock;
            _settings = settings.Value;
            _logger = logger;
        }

        public async Task EvaluateAttemptAsync(long attemptId, CancellationToken cancellationToken)
        {
            // Not configured: nothing is attempted, so nothing is counted against
            // the essay — it is evaluated once the AI is set up. Budget already
            // spent (slow hints): leave it to the background run.
            if (!_evaluator.IsConfigured || cancellationToken.IsCancellationRequested)
                return;

            var candidateIds = await _db.QuizAttemptEssayAnswers
                .AsNoTracking()
                .Where(e => e.QuizAttemptId == attemptId
                         && e.Status == EssayAnswerStatuses.Pending
                         && e.AiOutcome == null)
                .Select(e => e.Id)
                .ToListAsync(cancellationToken);

            // Inline, the submission's budget bounds everything, including a request
            // already under way: what it cuts short is not the AI's failure.
            if (candidateIds.Count > 0)
                await ClaimAndEvaluateAsync(candidateIds, cancellationToken, cancellationToken);
        }

        public async Task<int> EvaluateDueAsync(CancellationToken cancellationToken)
        {
            if (!_evaluator.IsConfigured)
                return 0;

            var now = _clock.UtcNow;
            var maxAttempts = _settings.EffectiveEssayEvaluationMaxAttempts;
            var settledBefore = now - _settings.EssayInlineGrace;
            var retryMinutes = (int)_settings.EssayEvaluationRetryDelay.TotalMinutes;
            var claimsStaleBefore = now - _settings.EssayClaimLifetime;

            // Answers that used up their attempts but are still Pending (a run died
            // after claiming the last one, or the limit was lowered) would otherwise
            // sit unclaimed forever: close them. Only once the last claim is older
            // than any live run can hold one — an answer on its last attempt may
            // still be with the AI, and closing it would throw that grade away.
            // (If a run does outlive the bound, SaveDecisionAsync still refuses to
            // overwrite what is closed here.) Status and outcome change together —
            // CK_QuizAttemptEssayAnswers_OutcomeMatchesStatus allows no Pending row
            // with an outcome.
            await _db.QuizAttemptEssayAnswers
                .Where(e => e.Status == EssayAnswerStatuses.Pending
                         && e.AiOutcome == null
                         && e.AiEvaluationAttempts >= maxAttempts
                         && (e.AiLastAttemptAt == null || e.AiLastAttemptAt <= claimsStaleBefore))
                .ExecuteUpdateAsync(s => s
                        .SetProperty(e => e.Status, EssayAnswerStatuses.NotGraded)
                        .SetProperty(e => e.AiOutcome, EssayAiOutcomes.Failed),
                    cancellationToken);

            // Served by IX_QuizAttemptEssayAnswers_AiDue. The retry wait grows with
            // each attempt: 10, 20, 30… minutes.
            var candidateIds = await _db.QuizAttemptEssayAnswers
                .AsNoTracking()
                .Where(e => e.Status == EssayAnswerStatuses.Pending
                         && e.AiOutcome == null
                         && e.AiEvaluationAttempts < maxAttempts
                         && e.CreatedAt <= settledBefore
                         && (e.AiLastAttemptAt == null
                             || e.AiLastAttemptAt.Value.AddMinutes(retryMinutes * e.AiEvaluationAttempts) <= now))
                .OrderBy(e => e.Id)
                .Select(e => e.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            if (candidateIds.Count == 0)
                return 0;

            // The batch deadline only stops NEW requests from starting. A request
            // that has started gets its own full EssayEvaluationTimeout, bounded
            // otherwise only by host shutdown, so a slow attempt early in the batch
            // cannot cut the next one short and cost it an attempt.
            using var batchDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            batchDeadline.CancelAfter(_settings.EssayEvaluationTimeout);

            return await ClaimAndEvaluateAsync(candidateIds, batchDeadline.Token, cancellationToken);
        }

        /// <param name="startBudget">Once cancelled, no further attempt's request starts.</param>
        /// <param name="runBudget">
        /// Cancelling it abandons even a request under way, with the attempt uncounted:
        /// the caller ran out of time, the AI did not fail.
        /// </param>
        private async Task<int> ClaimAndEvaluateAsync(
            List<long> candidateIds,
            CancellationToken startBudget,
            CancellationToken runBudget)
        {
            if (startBudget.IsCancellationRequested)
                return 0;

            var now = _clock.UtcNow;
            var claimId = Guid.NewGuid();
            var maxAttempts = _settings.EffectiveEssayEvaluationMaxAttempts;
            var retryMinutes = (int)_settings.EssayEvaluationRetryDelay.TotalMinutes;

            // One atomic UPDATE: an answer another run claimed a moment ago no
            // longer matches the WHERE and stays theirs. The attempt is counted
            // here; ReleaseAsync hands it back if we run out of time first.
            var claimed = await _db.QuizAttemptEssayAnswers
                .Where(e => candidateIds.Contains(e.Id)
                         && e.Status == EssayAnswerStatuses.Pending
                         && e.AiOutcome == null
                         && e.AiEvaluationAttempts < maxAttempts
                         && (e.AiLastAttemptAt == null
                             || e.AiLastAttemptAt.Value.AddMinutes(retryMinutes * e.AiEvaluationAttempts) <= now))
                .ExecuteUpdateAsync(s => s
                        .SetProperty(e => e.AiClaimId, (Guid?)claimId)
                        .SetProperty(e => e.AiLastAttemptAt, (DateTime?)now)
                        .SetProperty(e => e.AiEvaluationAttempts, e => (byte)(e.AiEvaluationAttempts + 1)),
                    CancellationToken.None);

            if (claimed == 0)
                return 0;

            // Not tracked: decisions are written by SaveDecisionAsync, never by
            // SaveChanges. Oldest first, like the candidates.
            var essays = await _db.QuizAttemptEssayAnswers
                .AsNoTracking()
                .Where(e => candidateIds.Contains(e.Id) && e.AiClaimId == claimId)
                .OrderBy(e => e.Id)
                .ToListAsync(CancellationToken.None);

            // One request per attempt: one child's answers, in the language they
            // answered in.
            var byAttempt = essays
                .GroupBy(e => e.QuizAttemptId)
                .Select(g => g.ToList())
                .ToList();

            var decided = 0;

            for (var i = 0; i < byAttempt.Count; i++)
            {
                try
                {
                    startBudget.ThrowIfCancellationRequested();
                    decided += await EvaluateAttemptEssaysAsync(byAttempt[i], claimId, runBudget);
                }
                catch (OperationCanceledException)
                    when (startBudget.IsCancellationRequested || runBudget.IsCancellationRequested)
                {
                    // The caller's time ran out before this request could start or
                    // finish — not the AI's failure. Hand the unfinished answers
                    // back, attempt uncounted. (An AI that is simply too slow is
                    // counted inside EvaluateAttemptEssaysAsync instead.)
                    await ReleaseAsync(
                        byAttempt.Skip(i).SelectMany(g => g).Select(e => e.Id).ToList(), claimId);
                    break;
                }
                catch (Exception ex)
                {
                    // A database hiccup while preparing or saving — not the AI's
                    // failure either. Hand this attempt's answers back and carry on
                    // with the others.
                    _logger.LogWarning(ex,
                        "Essay evaluation for attempt {AttemptId} could not complete; its answers are released for the next run.",
                        byAttempt[i][0].QuizAttemptId);

                    await ReleaseAsync(byAttempt[i].Select(e => e.Id).ToList(), claimId);
                }
            }

            return decided;
        }

        private async Task<int> EvaluateAttemptEssaysAsync(
            List<QuizAttemptEssayAnswer> essays,
            Guid claimId,
            CancellationToken runBudget)
        {
            // This request's own deadline, preparation included, whatever the batch
            // has left.
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(runBudget);
            deadline.CancelAfter(_settings.EssayEvaluationTimeout);
            var cancellationToken = deadline.Token;

            var language = essays[0].LanguageCode;
            var questionIds = essays.Select(e => e.QuestionId).Distinct().ToList();
            var maxAttempts = _settings.EffectiveEssayEvaluationMaxAttempts;

            var rows = (await LocalizedQuestionQuery.Project(
                        _db.Questions.AsNoTracking().Where(q => questionIds.Contains(q.Id)), language)
                    .ToListAsync(cancellationToken))
                .ToDictionary(r => r.QuestionId);

            var topics = await _aiRequests.LoadTopicNamesAsync(questionIds, language, cancellationToken);
            var imageBudget = _aiRequests.NewImageBudget();
            var now = _clock.UtcNow;

            var items = new List<EssayRequestItem>(essays.Count);
            var awaiting = new Dictionary<string, QuizAttemptEssayAnswer>();

            foreach (var essay in essays)
            {
                if (!rows.TryGetValue(essay.QuestionId, out var row)
                    || AiRequestBuilder.QuestionSemanticText(row.QuestionText, row.ImageDescription) is null)
                {
                    // Without the question the AI has nothing to judge the answer
                    // against, and retrying cannot change that. Nobody else grades
                    // essays, so it is closed now rather than retried to the limit.
                    _logger.LogWarning(
                        "Essay question {QuestionId} has neither text nor an image description; answer {EssayAnswerId} is closed as NotGraded.",
                        essay.QuestionId, essay.Id);

                    CloseAsNotGraded(essay, EssayAiOutcomes.Failed);
                    continue;
                }

                // Opaque per-request key — never a database id.
                var itemId = (items.Count + 1).ToString(CultureInfo.InvariantCulture);

                items.Add(new EssayRequestItem
                {
                    ItemId = itemId,
                    Difficulty = row.Difficulty,
                    Topic = topics.GetValueOrDefault(essay.QuestionId),
                    Question = new AiQuestion
                    {
                        Text = AiRequestBuilder.Clean(row.QuestionText),
                        Image = await _aiRequests.BuildImageAsync(
                            row.ImageUrl, row.ImageDescription, imageBudget, cancellationToken)
                    },
                    MaxPoints = essay.MaxPoints,
                    StudentAnswer = new EssayStudentAnswer { Text = essay.AnswerText }
                });

                awaiting[itemId] = essay;
            }

            if (awaiting.Count > 0)
            {
                EssayEvaluationResponse? response = null;

                try
                {
                    response = await _evaluator.EvaluateAsync(
                        new EssayEvaluationRequest { RequestId = Guid.NewGuid(), Language = language, Items = items },
                        cancellationToken);
                }
                catch (OperationCanceledException) when (runBudget.IsCancellationRequested)
                {
                    throw;   // the caller's time, not the AI's failure — the caller releases the claim
                }
                catch (OperationCanceledException) when (deadline.IsCancellationRequested)
                {
                    // The AI did not answer within the time one evaluation may take.
                    // That is a failed attempt like any other, counted: handed back
                    // uncounted, an essay the AI can never finish in time would stay
                    // Pending forever instead of ending NotGraded after its attempts.
                    _logger.LogWarning(
                        "The AI did not evaluate {Count} essay answer(s) within {Seconds}s; they stay Pending and will be retried.",
                        awaiting.Count, _settings.EssayEvaluationTimeout.TotalSeconds);
                }
                catch (Exception ex)
                {
                    // Down, unreachable, HTTP error, malformed — retried later.
                    _logger.LogWarning(ex,
                        "AI essay evaluation failed for {Count} answer(s); they stay Pending and will be retried.",
                        awaiting.Count);
                }

                // An item answered twice is ambiguous: neither answer is trusted.
                var resultsByItem = (response?.Results ?? Array.Empty<EssayEvaluationResult>())
                    .Where(r => r?.ItemId is not null)
                    .GroupBy(r => r.ItemId!)
                    .Where(g => g.Count() == 1)
                    .ToDictionary(g => g.Key, g => g.Single());

                foreach (var (itemId, essay) in awaiting)
                {
                    var result = resultsByItem.GetValueOrDefault(itemId);
                    var decision = response is null
                        ? EssayDecision.Unusable
                        : Decide(result, essay.MaxPoints, _settings.EffectiveMaxEssayFeedbackLength);

                    if (decision.Kind == EssayDecisionKind.Declined)
                        _logger.LogInformation(
                            "The AI declined to grade essay answer {EssayAnswerId}; it is closed as NotGraded. Reason: {Reason}",
                            essay.Id, ForLog(result?.Reason));

                    Apply(essay, decision, now, maxAttempts);
                }
            }

            // The decision is the valuable part — saved even if the deadline passes
            // right after the AI answered. An essay still Pending needs no write: its
            // attempt was counted when it was claimed.
            var decided = 0;

            foreach (var essay in essays.Where(e => e.Status != EssayAnswerStatuses.Pending))
            {
                if (await SaveDecisionAsync(essay, claimId) == 1)
                    decided++;
                else
                    _logger.LogInformation(
                        "Essay answer {EssayAnswerId} was closed or re-claimed by another run while this one evaluated it; this decision is discarded.",
                        essay.Id);
            }

            return decided;
        }

        /// <summary>
        /// Writes a decision only while this run still owns the answer: still
        /// claimed by it and still Pending. Otherwise another run has closed it
        /// (e.g. the pre-close of an answer on its last attempt) or re-claimed it,
        /// and a final status must never change afterwards.
        /// </summary>
        private Task<int> SaveDecisionAsync(QuizAttemptEssayAnswer essay, Guid claimId)
        {
            var id = essay.Id;
            var status = essay.Status;
            var awardedPoints = essay.AwardedPoints;
            var feedback = essay.Feedback;
            var gradedBy = essay.GradedBy;
            var gradedAt = essay.GradedAt;
            var outcome = essay.AiOutcome;
            var confidence = essay.AiConfidence;

            return _db.QuizAttemptEssayAnswers
                .Where(e => e.Id == id
                         && e.AiClaimId == claimId
                         && e.Status == EssayAnswerStatuses.Pending
                         && e.AiOutcome == null)
                .ExecuteUpdateAsync(s => s
                        .SetProperty(e => e.Status, status)
                        .SetProperty(e => e.AwardedPoints, awardedPoints)
                        .SetProperty(e => e.Feedback, feedback)
                        .SetProperty(e => e.GradedBy, gradedBy)
                        .SetProperty(e => e.GradedAt, gradedAt)
                        .SetProperty(e => e.AiOutcome, outcome)
                        .SetProperty(e => e.AiConfidence, confidence),
                    CancellationToken.None);
        }

        /// <summary>
        /// Un-counts the attempt and frees the claim. AiLastAttemptAt is kept, so
        /// the growing retry wait still applies (an answer back at 0 attempts is
        /// due at once: its wait is 0 × delay).
        /// </summary>
        private Task<int> ReleaseAsync(List<long> essayIds, Guid claimId) =>
            _db.QuizAttemptEssayAnswers
                .Where(e => essayIds.Contains(e.Id)
                         && e.AiClaimId == claimId
                         && e.Status == EssayAnswerStatuses.Pending
                         && e.AiOutcome == null)
                .ExecuteUpdateAsync(s => s
                        .SetProperty(e => e.AiClaimId, (Guid?)null)
                        .SetProperty(e => e.AiEvaluationAttempts, e => (byte)(e.AiEvaluationAttempts - 1)),
                    CancellationToken.None);

        /// <summary>
        /// Whether the AI's answer for one essay is a usable grade. There is no
        /// confidence threshold and no review: a well-formed Ok result IS the grade.
        /// <list type="bullet">
        /// <item>No result, or an unknown status → Unusable (retried later).</item>
        /// <item>"Skipped" → Declined (final: NotGraded).</item>
        /// <item>Ok (or no status) with whole points within 0…maxPoints, non-blank
        /// feedback no longer than maxFeedbackLength, and a confidence that is
        /// either absent or within 0…1 → Accept. Anything else → Unusable.</item>
        /// </list>
        /// </summary>
        public static EssayDecision Decide(
            EssayEvaluationResult? result,
            int maxPoints,
            int maxFeedbackLength)
        {
            if (result is null)
                return EssayDecision.Unusable;

            var status = result.Status ?? AiResultStatuses.Ok;

            if (string.Equals(status, AiResultStatuses.Skipped, StringComparison.OrdinalIgnoreCase))
                return EssayDecision.Declined;

            if (!string.Equals(status, AiResultStatuses.Ok, StringComparison.OrdinalIgnoreCase))
                return EssayDecision.Unusable;

            if (result.Points is not int points || points < 0 || points > maxPoints)
                return EssayDecision.Unusable;

            var feedback = result.Feedback?.Trim();
            if (string.IsNullOrEmpty(feedback) || feedback.Length > maxFeedbackLength)
                return EssayDecision.Unusable;

            // Optional and never a gate, but a value outside 0…1 means the response
            // is not what the contract describes, so nothing in it is trusted.
            if (result.Confidence is decimal confidence && (confidence < 0m || confidence > 1m))
                return EssayDecision.Unusable;

            return EssayDecision.Accept(points, feedback, result.Confidence);
        }

        /// <summary>
        /// Writes a decision onto a claimed Pending answer. The attempt was already
        /// counted when the answer was claimed, so an Unusable result on the last
        /// allowed attempt closes the answer as NotGraded / Failed.
        /// </summary>
        public static void Apply(QuizAttemptEssayAnswer essay, EssayDecision decision, DateTime now, int maxAttempts)
        {
            switch (decision.Kind)
            {
                case EssayDecisionKind.Accept:
                    essay.Status = EssayAnswerStatuses.Graded;
                    essay.AwardedPoints = (byte)decision.Points!.Value;
                    essay.Feedback = decision.Feedback;
                    essay.GradedBy = EssayGraders.Ai;
                    essay.GradedAt = now;
                    essay.AiOutcome = EssayAiOutcomes.Accepted;
                    essay.AiConfidence = decision.Confidence is decimal confidence
                        ? Math.Round(confidence, 2, MidpointRounding.AwayFromZero)
                        : null;
                    break;

                case EssayDecisionKind.Declined:
                    CloseAsNotGraded(essay, EssayAiOutcomes.Declined);
                    break;

                default:
                    // Stays Pending for the next run — unless that was the last try.
                    if (essay.AiEvaluationAttempts >= maxAttempts)
                        CloseAsNotGraded(essay, EssayAiOutcomes.Failed);
                    break;
            }
        }

        /// <summary>Final, with no grade: no points, no feedback, no grader.</summary>
        private static void CloseAsNotGraded(QuizAttemptEssayAnswer essay, string outcome)
        {
            essay.Status = EssayAnswerStatuses.NotGraded;
            essay.AiOutcome = outcome;
            essay.AwardedPoints = null;
            essay.Feedback = null;
            essay.GradedBy = null;
            essay.GradedAt = null;
        }

        private static string? ForLog(string? reason)
        {
            var clean = AiRequestBuilder.Clean(reason);

            return clean is null || clean.Length <= MaxLoggedReasonLength
                ? clean
                : clean[..MaxLoggedReasonLength] + "…";
        }
    }

    public enum EssayDecisionKind
    {
        /// <summary>A well-formed grade: the essay becomes Graded.</summary>
        Accept,

        /// <summary>The AI answered Skipped: the essay becomes NotGraded, for good.</summary>
        Declined,

        /// <summary>Nothing usable came back: retried, until the attempts run out.</summary>
        Unusable
    }

    public sealed record EssayDecision(EssayDecisionKind Kind, int? Points, string? Feedback, decimal? Confidence)
    {
        public static EssayDecision Unusable { get; } = new(EssayDecisionKind.Unusable, null, null, null);

        public static EssayDecision Declined { get; } = new(EssayDecisionKind.Declined, null, null, null);

        public static EssayDecision Accept(int points, string feedback, decimal? confidence) =>
            new(EssayDecisionKind.Accept, points, feedback, confidence);
    }
}
