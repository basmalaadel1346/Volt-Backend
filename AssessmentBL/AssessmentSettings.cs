namespace AssessmentBL
{
    /// <summary>
    /// Assessment module settings, bound from the "Assessment" configuration
    /// section. Every value has a safe default, so the section is optional.
    /// </summary>
    public sealed class AssessmentSettings
    {
        public const string SectionName = "Assessment";

        /// <summary>
        /// How long an attempt may stay InProgress after StartedAt. Once this has
        /// elapsed the attempt is Abandoned: it can no longer be submitted, and the
        /// sweep persists that status. Generous on purpose — a quiz takes minutes,
        /// so this only ever catches attempts the child walked away from.
        /// </summary>
        public int InProgressAttemptTimeoutMinutes { get; set; } = 180;

        /// <summary>How often the abandoned-attempt sweep runs.</summary>
        public int AbandonedAttemptSweepIntervalMinutes { get; set; } = 15;

        /// <summary>
        /// Upper bound on ALL optional AI work of a submission — hints first, then
        /// inline essay evaluation with whatever is left. The result is already
        /// committed when it starts, so this only bounds how long the child waits;
        /// essays not evaluated in time are picked up by the background evaluator.
        /// </summary>
        public int AiHintTimeoutSeconds { get; set; } = 15;

        /// <summary>
        /// Send image bytes (base64) alongside image descriptions. Turn on only for
        /// a vision-capable model — otherwise it is payload the model ignores.
        /// </summary>
        public bool AiSendImageContent { get; set; } = false;

        /// <summary>Largest image sent to the AI; bigger ones go as description only.</summary>
        public int AiMaxImageBytes { get; set; } = 1_000_000;

        /// <summary>
        /// Total image bytes one AI request may carry. Once spent, remaining images
        /// in that request go as description only.
        /// </summary>
        public int AiMaxImageBytesPerRequest { get; set; } = 4_000_000;

        public long EffectiveAiMaxImageBytesPerRequest => Math.Clamp(AiMaxImageBytesPerRequest, 10_000, 20_000_000);

        /// <summary>Longest hint accepted from the AI; a longer one is dropped.</summary>
        public int MaxHintLength { get; set; } = 400;

        /// <summary>
        /// How close a hint may come to the correct answer's wording before it is
        /// treated as giving it away (0–1). 0.8 rejects a near-copy — a changed
        /// letter, a diacritic, one word of a two-word answer — while leaving a
        /// hint that merely talks about the same topic alone.
        /// </summary>
        public decimal HintSimilarityThreshold { get; set; } = 0.80m;

        /// <summary>
        /// Escalation levels the Hint button offers per question: 1 = a soft nudge,
        /// 2 = a more direct hint. A third press is refused.
        /// </summary>
        public int MaxHintLevels { get; set; } = 2;

        public decimal EffectiveHintSimilarityThreshold => Math.Clamp(HintSimilarityThreshold, 0.5m, 1m);

        public int EffectiveMaxHintLevels => Math.Clamp(MaxHintLevels, 1, 5);

        /// <summary>Longest essay feedback accepted from the AI.</summary>
        public int MaxEssayFeedbackLength { get; set; } = 1000;

        /// <summary>How often the background essay evaluator runs.</summary>
        public int EssayEvaluationIntervalMinutes { get; set; } = 2;

        /// <summary>
        /// AI attempts per essay. When none of them produced a usable grade the
        /// essay is closed as NotGraded — essays are graded by the AI only.
        /// </summary>
        public int EssayEvaluationMaxAttempts { get; set; } = 5;

        /// <summary>Wait between two AI attempts on the same essay.</summary>
        public int EssayEvaluationRetryMinutes { get; set; } = 10;

        /// <summary>
        /// How long one AI evaluation request (one attempt's essays) may take. An
        /// AI that does not answer in time has used up one of the essay's attempts.
        /// A background batch also stops starting new requests after this long.
        /// </summary>
        public int EssayEvaluationTimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// The background evaluator leaves essays younger than this alone, so the
        /// submission that created them gets the first try.
        /// </summary>
        public int EssayInlineGraceMinutes { get; set; } = 5;

        public long EffectiveAiMaxImageBytes => Math.Clamp(AiMaxImageBytes, 10_000, 5_000_000);

        public int EffectiveMaxHintLength => Math.Clamp(MaxHintLength, 50, 2000);

        public int EffectiveMaxEssayFeedbackLength => Math.Clamp(MaxEssayFeedbackLength, 100, 4000);

        public int EffectiveEssayEvaluationMaxAttempts => Math.Clamp(EssayEvaluationMaxAttempts, 1, 20);

        public TimeSpan EssayEvaluationInterval =>
            TimeSpan.FromMinutes(Math.Max(EssayEvaluationIntervalMinutes, 1));

        public TimeSpan EssayEvaluationRetryDelay =>
            TimeSpan.FromMinutes(Math.Max(EssayEvaluationRetryMinutes, 1));

        public TimeSpan EssayEvaluationTimeout =>
            TimeSpan.FromSeconds(Math.Clamp(EssayEvaluationTimeoutSeconds, 5, 120));

        public TimeSpan EssayInlineGrace =>
            TimeSpan.FromMinutes(Math.Max(EssayInlineGraceMinutes, 1));

        /// <summary>
        /// Longer than any run can hold a claim before saving its decisions. Inline,
        /// everything but the save is bounded by AiHintTimeout. In the background, a
        /// request may start just before the batch deadline (EssayEvaluationTimeout)
        /// and then take one EssayEvaluationTimeout of its own. The extra minute
        /// covers the database work around it. An older claim belongs to a run that
        /// died, so its essay may be closed without cutting off a live AI call.
        /// </summary>
        public TimeSpan EssayClaimLifetime =>
            (AiHintTimeout > 2 * EssayEvaluationTimeout ? AiHintTimeout : 2 * EssayEvaluationTimeout)
            + TimeSpan.FromMinutes(1);

        /// <summary>
        /// Questions the placement test takes from each level's LevelAssessment
        /// quiz (in that quiz's DisplayOrder).
        /// </summary>
        public int PlacementQuestionsPerLevel { get; set; } = 4;

        /// <summary>
        /// Percentage of the Points of a level's placement questions a child must
        /// earn for that level to count as mastered. With 4 one-point questions,
        /// 75 means 3 of 4.
        /// </summary>
        public int PlacementPassPercentage { get; set; } = 75;

        /// <summary>
        /// How long an essay answer may stay Pending before the grading deadline
        /// closes it, whatever the AI is doing.
        ///
        /// Retry limits alone were not enough: an AI that never answers leaves an
        /// answer claimed-and-retried for hours, and the child is shown "Pending"
        /// with no end in sight and no final result. At this age the answer is
        /// settled — by the question's keywords when it has them, and as NotGraded
        /// when it does not.
        /// </summary>
        public int EssayGradingDeadlineMinutes { get; set; } = 60;

        public TimeSpan EssayGradingDeadline =>
            TimeSpan.FromMinutes(Math.Clamp(EssayGradingDeadlineMinutes, 5, 1440));

        /// <summary>
        /// Share of an essay question's keywords the child must mention to earn
        /// ALL its points in a keyword-graded fallback. Below it, points are
        /// awarded in proportion to the keywords found.
        /// </summary>
        public int EssayKeywordFullCreditPercentage { get; set; } = 80;

        public int EffectiveEssayKeywordFullCreditPercentage =>
            Math.Clamp(EssayKeywordFullCreditPercentage, 10, 100);

        /// <summary>
        /// Percentage of a lesson quiz a child must score for the lesson to count
        /// as learned, which is what unlocks the next one. Not the same as topic
        /// mastery: this is one quiz, judged once, not a running average.
        /// </summary>
        public int LessonQuizPassPercentage { get; set; } = 60;

        public decimal EffectiveLessonQuizPassPercentage => Math.Clamp(LessonQuizPassPercentage, 1, 100);

        /// <summary>
        /// Questions the level-skip challenge draws from the lesson quizzes of the
        /// level being skipped.
        /// </summary>
        public int LevelSkipQuestionCount { get; set; } = 10;

        /// <summary>Seconds the whole level-skip challenge may take. Three minutes by default.</summary>
        public int LevelSkipTimeLimitSeconds { get; set; } = 180;

        /// <summary>Wrong answers allowed in a level-skip challenge before it is lost.</summary>
        public int LevelSkipHearts { get; set; } = 3;

        /// <summary>
        /// Percentage of the level-skip challenge's Points the child must earn to
        /// skip the level. Hearts usually decide it first; this covers a challenge
        /// finished with hearts to spare but too few points.
        /// </summary>
        public int LevelSkipPassPercentage { get; set; } = 80;

        public int EffectiveLevelSkipQuestionCount => Math.Clamp(LevelSkipQuestionCount, 3, 50);

        public int EffectiveLevelSkipTimeLimitSeconds => Math.Clamp(LevelSkipTimeLimitSeconds, 30, 3600);

        public byte EffectiveLevelSkipHearts => (byte)Math.Clamp(LevelSkipHearts, 1, 10);

        public decimal EffectiveLevelSkipPassPercentage => Math.Clamp(LevelSkipPassPercentage, 1, 100);

        /// <summary>Longest essay answer accepted, in characters.</summary>
        public int EssayAnswerMaxLength { get; set; } = 4000;

        public int EffectivePlacementQuestionsPerLevel => Math.Clamp(PlacementQuestionsPerLevel, 1, 20);

        public decimal EffectivePlacementPassPercentage => Math.Clamp(PlacementPassPercentage, 1, 100);

        public int EffectiveEssayAnswerMaxLength => Math.Clamp(EssayAnswerMaxLength, 100, 20000);

        /// <summary>
        /// Share of correct MultipleChoice/TrueFalse answers, in percent, a child
        /// needs in a topic for the progress screen to show it as Mastered.
        /// </summary>
        public int TopicMasteryPercentage { get; set; } = 80;

        /// <summary>
        /// Answers a topic needs before it can show as Mastered, so a single lucky
        /// answer is not mastery.
        /// </summary>
        public int TopicMasteryMinQuestions { get; set; } = 5;

        public int EffectiveTopicMasteryPercentage => Math.Clamp(TopicMasteryPercentage, 50, 100);

        public int EffectiveTopicMasteryMinQuestions => Math.Clamp(TopicMasteryMinQuestions, 1, 50);

        /// <summary>
        /// The abandonment rule, in one place: an attempt that is still InProgress
        /// and started at or before this instant is Abandoned.
        /// </summary>
        public DateTime AbandonCutoff(DateTime utcNow) => utcNow - InProgressAttemptTimeout;

        // Clamped so a zero or negative value in configuration can never abandon
        // live attempts the moment they start, spin the sweep, or disable the
        // AI bound entirely.
        public TimeSpan InProgressAttemptTimeout =>
            TimeSpan.FromMinutes(Math.Max(InProgressAttemptTimeoutMinutes, 30));

        public TimeSpan AbandonedAttemptSweepInterval =>
            TimeSpan.FromMinutes(Math.Max(AbandonedAttemptSweepIntervalMinutes, 1));

        public TimeSpan AiHintTimeout =>
            TimeSpan.FromSeconds(Math.Clamp(AiHintTimeoutSeconds, 1, 60));
    }
}
