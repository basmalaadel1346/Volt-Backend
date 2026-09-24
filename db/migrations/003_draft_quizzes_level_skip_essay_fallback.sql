/* ============================================================================
   Migration 003 — draft quizzes, one live attempt, the level-skip challenge,
                    and a fallback grader for essays
   ----------------------------------------------------------------------------
   For databases built from an EARLIER version of 000_AssessmentSchema.sql. Run
   it after 002. A database built from the current 000 already has all of this;
   running the script on it is harmless (every step checks the current state
   first).

   Part 1 — a new quiz is a DRAFT
     Quizzes.IsActive defaults to 0. Creating a quiz and publishing it are
     separate steps, so an admin can prepare the next placement test or lesson
     quiz while the current one is still running. The "only one active" rule is
     unchanged; it simply applies at publish time instead of at creation, and the
     application retires the incumbent in the same transaction. Existing rows are
     NOT touched: a quiz that is live today stays live.

   Part 2 — one live attempt per learner per quiz
     UQ_QuizAttempts_OneInProgressPerUserQuiz. A double-tapped "Start" used to
     create a SECOND attempt, leaving the first orphaned InProgress until the
     sweep abandoned it, with only one of the two ever submittable. Existing
     duplicates are abandoned first, newest kept, so the index can be created.

   Part 3 — the level-skip challenge
     'LevelSkip' joins CK_Quizzes_QuizType, points at a level like a
     LevelAssessment does, and gets its own one-active-per-level index. Like the
     placement test it owns no questions: it samples the level's lesson quizzes.

   Part 4 — essays always reach a final state
     Questions.EssayKeywords holds the ideas a good answer mentions, and the
     essay answer's constraints widen to allow two new endings: Graded by
     'Keywords' with outcome 'Fallback' (the AI never graded it, so the keywords
     did), and NotGraded with outcome 'TimedOut' (the grading deadline passed and
     there were no keywords). Without these an essay could sit Pending forever
     whenever the AI was unreachable.

   Order and safety
   * Each part is ONE batch and ONE transaction (XACT_ABORT ON): if any step
     fails, that part rolls back entirely.
   * Every step is guarded, so the script is safe to re-run.
   * Run with `sqlcmd -b` (or stop on the first error in SSMS).

   Deploy together with the application code of this change.
   ========================================================================== */

USE VoltDB;
GO

-- Required for DDL and DML on tables with filtered indexes; sqlcmd defaults
-- QUOTED_IDENTIFIER OFF.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/* GATE — the Assessment schema must exist (built from 000). */
IF OBJECT_ID(N'Assessment.Quizzes') IS NULL
   OR OBJECT_ID(N'Assessment.Questions') IS NULL
   OR OBJECT_ID(N'Assessment.QuizAttempts') IS NULL
   OR OBJECT_ID(N'Assessment.QuizAttemptEssayAnswers') IS NULL
    THROW 50001, N'003: the Assessment schema was not found. Build it with 000_AssessmentSchema.sql first.', 1;
GO


/* ============================================================================
   Part 1 — a new quiz is created as a draft
   ========================================================================== */

SET XACT_ABORT ON;
BEGIN TRANSACTION;

    -- The default is the only thing that changes. Rows already in the table keep
    -- whatever IsActive they have: a quiz children are taking right now must not
    -- go dark because of a migration.
    IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = N'DF_Quizzes_IsActive')
        ALTER TABLE Assessment.Quizzes DROP CONSTRAINT DF_Quizzes_IsActive;

    ALTER TABLE Assessment.Quizzes
        ADD CONSTRAINT DF_Quizzes_IsActive DEFAULT (0) FOR IsActive;

COMMIT TRANSACTION;
GO


/* ============================================================================
   Part 2 — one live attempt per learner per quiz
   ========================================================================== */

SET XACT_ABORT ON;
BEGIN TRANSACTION;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes
                   WHERE name = N'UQ_QuizAttempts_OneInProgressPerUserQuiz'
                     AND object_id = OBJECT_ID(N'Assessment.QuizAttempts'))
    BEGIN
        -- Existing duplicates would block the unique index. They are exactly the
        -- orphans this change prevents: of each learner's InProgress attempts on
        -- one quiz, the newest is the one the app is actually using, so the older
        -- ones are abandoned — the same status the sweep would have given them.
        WITH Ranked AS
        (
            SELECT
                Id,
                ROW_NUMBER() OVER (PARTITION BY UserId, QuizId ORDER BY Id DESC) AS rn
            FROM Assessment.QuizAttempts
            WHERE Status = N'InProgress'
        )
        UPDATE a
           SET a.Status = N'Abandoned'
          FROM Assessment.QuizAttempts AS a
          JOIN Ranked AS r ON r.Id = a.Id
         WHERE r.rn > 1;

        CREATE UNIQUE INDEX UQ_QuizAttempts_OneInProgressPerUserQuiz
            ON Assessment.QuizAttempts (UserId, QuizId)
            WHERE Status = N'InProgress';
    END

COMMIT TRANSACTION;
GO


/* ============================================================================
   Part 3 — the level-skip challenge
   ========================================================================== */

SET XACT_ABORT ON;
BEGIN TRANSACTION;

    -- Both CHECK constraints are replaced together: a LevelSkip quiz is only
    -- valid once the type is allowed AND the type/reference matrix knows it
    -- points at a level.
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Quizzes_QuizType')
        ALTER TABLE Assessment.Quizzes DROP CONSTRAINT CK_Quizzes_QuizType;

    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Quizzes_TypeMatchesReference')
        ALTER TABLE Assessment.Quizzes DROP CONSTRAINT CK_Quizzes_TypeMatchesReference;

    ALTER TABLE Assessment.Quizzes
        ADD CONSTRAINT CK_Quizzes_QuizType
            CHECK (QuizType IN ('LevelAssessment', 'LessonQuiz', 'LessonReview', 'Standalone', 'Placement', 'LevelSkip'));

    ALTER TABLE Assessment.Quizzes
        ADD CONSTRAINT CK_Quizzes_TypeMatchesReference
            CHECK ((QuizType IN ('LevelAssessment', 'LevelSkip') AND LevelId IS NOT NULL AND LessonId IS NULL)
                OR (QuizType IN ('LessonQuiz', 'LessonReview') AND LessonId IS NOT NULL AND LevelId IS NULL)
                OR (QuizType IN ('Standalone', 'Placement') AND LevelId IS NULL AND LessonId IS NULL));

    -- One active challenge per level, for the same reason there is one active
    -- quiz per lesson: otherwise which one a child gets would be a guess.
    IF NOT EXISTS (SELECT 1 FROM sys.indexes
                   WHERE name = N'UQ_Quizzes_OneActiveLevelSkipPerLevel'
                     AND object_id = OBJECT_ID(N'Assessment.Quizzes'))
        CREATE UNIQUE INDEX UQ_Quizzes_OneActiveLevelSkipPerLevel
            ON Assessment.Quizzes (LevelId)
            WHERE QuizType = N'LevelSkip' AND IsActive = 1;

COMMIT TRANSACTION;
GO


/* ============================================================================
   Part 4 — the essay fallback grader and the grading deadline
   ========================================================================== */

SET XACT_ABORT ON;
BEGIN TRANSACTION;

    IF COL_LENGTH(N'Assessment.Questions', N'EssayKeywords') IS NULL
        ALTER TABLE Assessment.Questions ADD EssayKeywords NVARCHAR(1000) NULL;

COMMIT TRANSACTION;
GO

SET XACT_ABORT ON;
BEGIN TRANSACTION;

    -- Separate batch: the column must exist before a constraint can reference it.
    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Questions_EssayKeywordsOnlyForEssay')
        ALTER TABLE Assessment.Questions
            ADD CONSTRAINT CK_Questions_EssayKeywordsOnlyForEssay
                CHECK (EssayKeywords IS NULL OR QuestionType = 'Essay');

    -- The three constraints that describe how an essay answer may end, widened
    -- together: 'Fallback' and 'TimedOut' are meaningless unless GradedBy also
    -- allows 'Keywords' and the status/outcome pairing knows about both.
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_QuizAttemptEssayAnswers_GradedBy')
        ALTER TABLE Assessment.QuizAttemptEssayAnswers DROP CONSTRAINT CK_QuizAttemptEssayAnswers_GradedBy;

    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_QuizAttemptEssayAnswers_AiOutcome')
        ALTER TABLE Assessment.QuizAttemptEssayAnswers DROP CONSTRAINT CK_QuizAttemptEssayAnswers_AiOutcome;

    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_QuizAttemptEssayAnswers_OutcomeMatchesStatus')
        ALTER TABLE Assessment.QuizAttemptEssayAnswers DROP CONSTRAINT CK_QuizAttemptEssayAnswers_OutcomeMatchesStatus;

    ALTER TABLE Assessment.QuizAttemptEssayAnswers
        ADD CONSTRAINT CK_QuizAttemptEssayAnswers_GradedBy
            CHECK (GradedBy IS NULL OR GradedBy IN ('Ai', 'Keywords'));

    ALTER TABLE Assessment.QuizAttemptEssayAnswers
        ADD CONSTRAINT CK_QuizAttemptEssayAnswers_AiOutcome
            CHECK (AiOutcome IS NULL OR AiOutcome IN ('Accepted', 'Declined', 'Failed', 'Fallback', 'TimedOut'));

    ALTER TABLE Assessment.QuizAttemptEssayAnswers
        ADD CONSTRAINT CK_QuizAttemptEssayAnswers_OutcomeMatchesStatus
            CHECK ((Status = 'Pending' AND AiOutcome IS NULL)
                OR (Status = 'Graded' AND AiOutcome IN ('Accepted', 'Fallback'))
                OR (Status = 'NotGraded' AND AiOutcome IN ('Declined', 'Failed', 'TimedOut')));

COMMIT TRANSACTION;
GO

PRINT N'003: draft quizzes, one live attempt per quiz, the level-skip challenge and the essay fallback are in place.';
GO
