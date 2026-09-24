using AssessmentBL.Services.Constants;
using AssessmentDA.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AssessmentBL.Services
{
    /// <summary>
    /// How a quiz is PLAYED, as opposed to what it contains: whether it runs
    /// against a clock, and whether a wrong answer costs a heart.
    ///
    /// Only the level-skip challenge uses either today. Keeping the rules here
    /// rather than scattered through QuizAttemptService means the start response,
    /// the submit check and the documentation all read the same numbers, and a new
    /// timed quiz type is one switch arm.
    /// </summary>
    public sealed class QuizRules
    {
        private readonly AssessmentDbContext _db;
        private readonly AssessmentSettings _settings;

        public QuizRules(AssessmentDbContext db, IOptions<AssessmentSettings> settings)
        {
            _db = db;
            _settings = settings.Value;
        }

        /// <summary>The play rules of a quiz type. Untimed and heartless unless stated.</summary>
        public QuizPlayRules For(string quizType) => quizType switch
        {
            QuizTypes.LevelSkip => new QuizPlayRules(
                TimeLimit: TimeSpan.FromSeconds(_settings.EffectiveLevelSkipTimeLimitSeconds),
                Hearts: _settings.EffectiveLevelSkipHearts),

            _ => QuizPlayRules.Unlimited
        };

        public async Task<QuizPlayRules> ForQuizAsync(int quizId, CancellationToken cancellationToken = default)
        {
            var quizType = await _db.Quizzes
                .AsNoTracking()
                .Where(q => q.Id == quizId)
                .Select(q => q.QuizType)
                .FirstOrDefaultAsync(cancellationToken);

            return quizType is null ? QuizPlayRules.Unlimited : For(quizType);
        }
    }

    /// <param name="TimeLimit">
    /// How long the child has from StartedAt. Null means only the generous
    /// abandonment window applies (AssessmentSettings.InProgressAttemptTimeout).
    /// </param>
    /// <param name="Hearts">
    /// Wrong answers allowed before the attempt is lost, or null when wrong
    /// answers only cost points.
    /// </param>
    public sealed record QuizPlayRules(TimeSpan? TimeLimit, byte? Hearts)
    {
        public static readonly QuizPlayRules Unlimited = new(null, null);

        public int? TimeLimitSeconds => TimeLimit is TimeSpan limit ? (int)limit.TotalSeconds : null;

        public DateTime? ExpiresAt(DateTime startedAt) =>
            TimeLimit is TimeSpan limit ? startedAt + limit : null;

        /// <summary>True once a timed attempt's window has closed.</summary>
        public bool HasRunOut(DateTime startedAt, DateTime utcNow) =>
            TimeLimit is TimeSpan limit && utcNow > startedAt + limit;
    }
}
