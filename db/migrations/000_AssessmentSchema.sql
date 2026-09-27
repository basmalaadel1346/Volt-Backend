/* ============================================================================
   000_AssessmentSchema.sql — the Assessment schema, from an empty database
   ----------------------------------------------------------------------------
   This script's only job is to stand up Assessment.* objects. It contains NO
   DDL for any other module: nothing for LearningContent.*, Users.*, dbo.*,
   Gamification or anything else — not even a reference copy.

   Cross-module references are plain columns with NO foreign key, by design:
       QuizAttempts.UserId, UserTopicStats.UserId, UserPlacements.UserId
                                      → Users.Users.Id
       Quizzes.LevelId, UserPlacements.PlacedLevelId
                                      → LearningContent.Levels.Id
       Quizzes.LessonId               → LearningContent.Lessons.Id
   If a requirement ever seems to need one of these as a real FK, that is the
   signal to use a Shared contract instead (ILessonAvailability, ILevelCatalog,
   ILearnerProfile are the existing ones) — not to add the FK here.

   End state: this is the current schema, so a database built from this script
   needs no migration. A database built from an EARLIER version of this script
   is brought up to date by the numbered migrations in this folder, in order:
       001_points_ai_essays_image_descriptions.sql — snapshot Points per
           attempt question, AI-only essay grading, and a required description
           for every image.
       002_optional_topics_hint_level_uniqueness.sql — a question may belong
           to no topic, and each Hint-button level is saved once per attempt
           and question, whatever the language.

   Run against an empty database, then scaffold AssessmentDA from it.
   ========================================================================== */

USE VoltDB;
GO

-- Required by the filtered indexes below; sqlcmd defaults QUOTED_IDENTIFIER OFF.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF SCHEMA_ID(N'Assessment') IS NULL
    EXEC(N'CREATE SCHEMA Assessment');
GO


/* ============================================================================
   Languages — the content languages every translation table points at.
   ========================================================================== */

CREATE TABLE Assessment.Languages
(
    Code        NVARCHAR(5)     NOT NULL,
    Name        NVARCHAR(50)    NOT NULL,
    IsActive    BIT             NOT NULL CONSTRAINT DF_Languages_IsActive DEFAULT (1),

    CONSTRAINT PK_Languages PRIMARY KEY CLUSTERED (Code),
    CONSTRAINT UQ_Languages_Name UNIQUE (Name)
);
GO

INSERT INTO Assessment.Languages (Code, Name) VALUES (N'ar', N'Arabic'), (N'en', N'English');
GO


/* ============================================================================
   Categories → Topics — how questions are classified for statistics.
   ========================================================================== */

CREATE TABLE Assessment.Categories
(
    -- Not IDENTITY: ids are assigned by seed data, or by the API as the lowest free id.
    Id          TINYINT         NOT NULL,
    Name        NVARCHAR(100)   NOT NULL,
    SortOrder   SMALLINT        NOT NULL CONSTRAINT DF_Categories_SortOrder DEFAULT (0),
    IsActive    BIT             NOT NULL CONSTRAINT DF_Categories_IsActive DEFAULT (1),

    CONSTRAINT PK_Categories PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_Categories_Name UNIQUE (Name)
);
GO

CREATE TABLE Assessment.CategoryTranslations
(
    Id              INT IDENTITY(1,1)   NOT NULL,
    CategoryId      TINYINT             NOT NULL,
    LanguageCode    NVARCHAR(5)         NOT NULL,
    Name            NVARCHAR(100)       NOT NULL,

    CONSTRAINT PK_CategoryTranslations PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_CategoryTranslations_CategoryId_Language UNIQUE (CategoryId, LanguageCode),
    CONSTRAINT FK_CategoryTranslations_Categories FOREIGN KEY (CategoryId)
        REFERENCES Assessment.Categories (Id) ON DELETE CASCADE,
    CONSTRAINT FK_CategoryTranslations_Languages FOREIGN KEY (LanguageCode)
        REFERENCES Assessment.Languages (Code)
);
GO

CREATE TABLE Assessment.Topics
(
    Id              INT IDENTITY(1,1)   NOT NULL,
    Name            NVARCHAR(200)       NOT NULL,
    Description     NVARCHAR(MAX)       NULL,
    CategoryId      TINYINT             NOT NULL,
    LearningLevel   NVARCHAR(20)        NOT NULL,
    IsActive        BIT                 NOT NULL CONSTRAINT DF_Topics_IsActive DEFAULT (1),
    CreatedAt       DATETIME2(3)        NOT NULL CONSTRAINT DF_Topics_CreatedAt DEFAULT (SYSUTCDATETIME()),
    UpdatedAt       DATETIME2(3)        NULL,

    CONSTRAINT PK_Topics PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_Topics_Name UNIQUE (Name),
    -- No ON DELETE: a Category with Topics cannot be deleted.
    CONSTRAINT FK_Topics_Categories FOREIGN KEY (CategoryId) REFERENCES Assessment.Categories (Id),
    CONSTRAINT CK_Topics_LearningLevel CHECK (LearningLevel IN ('Beginner', 'Intermediate', 'Advanced'))
);
GO

CREATE INDEX IX_Topics_CategoryId ON Assessment.Topics (CategoryId);
CREATE INDEX IX_Topics_LearningLevel ON Assessment.Topics (LearningLevel);
GO

CREATE TABLE Assessment.TopicTranslations
(
    Id              INT IDENTITY(1,1)   NOT NULL,
    TopicId         INT                 NOT NULL,
    LanguageCode    NVARCHAR(5)         NOT NULL,
    Name            NVARCHAR(200)       NOT NULL,
    Description     NVARCHAR(MAX)       NULL,

    CONSTRAINT PK_TopicTranslations PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_TopicTranslations_TopicId_Language UNIQUE (TopicId, LanguageCode),
    CONSTRAINT FK_TopicTranslations_Topics FOREIGN KEY (TopicId)
        REFERENCES Assessment.Topics (Id) ON DELETE CASCADE,
    CONSTRAINT FK_TopicTranslations_Languages FOREIGN KEY (LanguageCode)
        REFERENCES Assessment.Languages (Code)
);
GO


/* ============================================================================
   Quizzes → Questions → QuestionOptions.
   ========================================================================== */

CREATE TABLE Assessment.Quizzes
(
    Id          INT IDENTITY(1,1)   NOT NULL,
    Title       NVARCHAR(300)       NOT NULL,
    Description NVARCHAR(MAX)       NULL,
    QuizType    NVARCHAR(30)        NOT NULL CONSTRAINT DF_Quizzes_QuizType DEFAULT ('Standalone'),
    -- Cross-module, no FK (see header).
    LevelId     INT                 NULL,
    LessonId    INT                 NULL,
    IsActive    BIT                 NOT NULL CONSTRAINT DF_Quizzes_IsActive DEFAULT (1),
    CreatedAt   DATETIME2(3)        NOT NULL CONSTRAINT DF_Quizzes_CreatedAt DEFAULT (SYSUTCDATETIME()),
    UpdatedAt   DATETIME2(3)        NULL,

    CONSTRAINT PK_Quizzes PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT CK_Quizzes_QuizType
        CHECK (QuizType IN ('LevelAssessment', 'LessonQuiz', 'LessonReview', 'Standalone', 'Placement')),
    CONSTRAINT CK_Quizzes_TypeMatchesReference
        CHECK ((QuizType = 'LevelAssessment' AND LevelId IS NOT NULL AND LessonId IS NULL)
            OR (QuizType IN ('LessonQuiz', 'LessonReview') AND LessonId IS NOT NULL AND LevelId IS NULL)
            OR (QuizType IN ('Standalone', 'Placement') AND LevelId IS NULL AND LessonId IS NULL))
);
GO

CREATE INDEX IX_Quizzes_IsActive ON Assessment.Quizzes (IsActive);
CREATE INDEX IX_Quizzes_LessonId ON Assessment.Quizzes (LessonId);
CREATE INDEX IX_Quizzes_LevelId ON Assessment.Quizzes (LevelId);
CREATE INDEX IX_Quizzes_QuizType ON Assessment.Quizzes (QuizType);

-- One active placement test, and one active quiz per lesson: the lesson lookup
-- and the placement test must never have to guess between two.
CREATE UNIQUE INDEX UQ_Quizzes_OneActivePlacement
    ON Assessment.Quizzes (QuizType)
    WHERE QuizType = N'Placement' AND IsActive = 1;
CREATE UNIQUE INDEX UQ_Quizzes_OneActiveLessonQuizPerLesson
    ON Assessment.Quizzes (LessonId)
    WHERE QuizType = N'LessonQuiz' AND IsActive = 1;
GO

CREATE TABLE Assessment.QuizTranslations
(
    Id              INT IDENTITY(1,1)   NOT NULL,
    QuizId          INT                 NOT NULL,
    LanguageCode    NVARCHAR(5)         NOT NULL,
    Title           NVARCHAR(300)       NOT NULL,
    Description     NVARCHAR(MAX)       NULL,

    CONSTRAINT PK_QuizTranslations PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_QuizTranslations_QuizId_Language UNIQUE (QuizId, LanguageCode),
    CONSTRAINT FK_QuizTranslations_Quizzes FOREIGN KEY (QuizId)
        REFERENCES Assessment.Quizzes (Id) ON DELETE CASCADE,
    CONSTRAINT FK_QuizTranslations_Languages FOREIGN KEY (LanguageCode)
        REFERENCES Assessment.Languages (Code)
);
GO

CREATE TABLE Assessment.Questions
(
    Id                  INT IDENTITY(1,1)   NOT NULL,
    QuizId              INT                 NOT NULL,
    -- Optional: a question with no topic is asked, graded and earns XP like any
    -- other, and counts toward no topic in UserTopicStats.
    TopicId             INT                 NULL,
    QuestionText        NVARCHAR(MAX)       NOT NULL,
    QuestionType        NVARCHAR(30)        NOT NULL
        CONSTRAINT DF_Questions_QuestionType DEFAULT ('MultipleChoice'),
    -- Server-relative path, e.g. /uploads/lessons/x.png. Never sent to the AI.
    ImageUrl            NVARCHAR(500)       NULL,
    -- Admin-authored meaning of the image, for the AI. Never shown to a child.
    -- Required whenever ImageUrl is set; NULL when there is no image.
    ImageDescription    NVARCHAR(1000)      NULL,
    Difficulty          NVARCHAR(20)        NOT NULL CONSTRAINT DF_Questions_Difficulty DEFAULT ('Medium'),
    DisplayOrder        SMALLINT            NOT NULL CONSTRAINT DF_Questions_DisplayOrder DEFAULT (0),
    Points              TINYINT             NOT NULL CONSTRAINT DF_Questions_Points DEFAULT (1),
    -- New questions start hidden until an admin publishes them.
    IsActive            BIT                 NOT NULL CONSTRAINT DF_Questions_IsActive DEFAULT (0),
    CreatedAt           DATETIME2(3)        NOT NULL CONSTRAINT DF_Questions_CreatedAt DEFAULT (SYSUTCDATETIME()),

    CONSTRAINT PK_Questions PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_Questions_QuizId_DisplayOrder UNIQUE (QuizId, DisplayOrder),
    CONSTRAINT FK_Questions_Quizzes FOREIGN KEY (QuizId)
        REFERENCES Assessment.Quizzes (Id) ON DELETE CASCADE,
    -- No ON DELETE: a Topic with Questions cannot be deleted.
    CONSTRAINT FK_Questions_Topics FOREIGN KEY (TopicId) REFERENCES Assessment.Topics (Id),
    CONSTRAINT CK_Questions_QuestionType CHECK (QuestionType IN ('MultipleChoice', 'TrueFalse', 'Essay')),
    CONSTRAINT CK_Questions_Difficulty CHECK (Difficulty IN ('Easy', 'Medium', 'Hard', 'Advanced')),
    CONSTRAINT CK_Questions_Points CHECK (Points > 0),
    -- The AI never looks at images, only at text: a question with an image must
    -- say in words what the image shows.
    CONSTRAINT CK_Questions_ImageHasDescription
        CHECK (ImageUrl IS NULL
            OR (ImageDescription IS NOT NULL AND LTRIM(RTRIM(ImageDescription)) <> N''))
);
GO

CREATE INDEX IX_Questions_Difficulty ON Assessment.Questions (Difficulty);
CREATE INDEX IX_Questions_QuestionType ON Assessment.Questions (QuestionType);
CREATE INDEX IX_Questions_TopicId ON Assessment.Questions (TopicId);
GO

CREATE TABLE Assessment.QuestionTranslations
(
    Id              INT IDENTITY(1,1)   NOT NULL,
    QuestionId      INT                 NOT NULL,
    LanguageCode    NVARCHAR(5)         NOT NULL,
    QuestionText    NVARCHAR(MAX)       NOT NULL,

    CONSTRAINT PK_QuestionTranslations PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_QuestionTranslations_QuestionId_Language UNIQUE (QuestionId, LanguageCode),
    CONSTRAINT FK_QuestionTranslations_Questions FOREIGN KEY (QuestionId)
        REFERENCES Assessment.Questions (Id) ON DELETE CASCADE,
    CONSTRAINT FK_QuestionTranslations_Languages FOREIGN KEY (LanguageCode)
        REFERENCES Assessment.Languages (Code)
);
GO

CREATE TABLE Assessment.QuestionOptions
(
    Id                  INT IDENTITY(1,1)   NOT NULL,
    QuestionId          INT                 NOT NULL,
    -- Null when the option is image-only.
    OptionText          NVARCHAR(MAX)       NULL,
    ImageUrl            NVARCHAR(500)       NULL,
    ImageDescription    NVARCHAR(1000)      NULL,
    IsCorrect           BIT                 NOT NULL CONSTRAINT DF_QuestionOptions_IsCorrect DEFAULT (0),
    DisplayOrder        SMALLINT            NOT NULL CONSTRAINT DF_QuestionOptions_DisplayOrder DEFAULT (0),
    CreatedAt           DATETIME2(3)        NOT NULL
        CONSTRAINT DF_QuestionOptions_CreatedAt DEFAULT (SYSUTCDATETIME()),

    CONSTRAINT PK_QuestionOptions PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_QuestionOptions_QuestionId_DisplayOrder UNIQUE (QuestionId, DisplayOrder),
    -- Backs the question-scoped composite FKs below: an option can only ever be
    -- referenced together with its own question.
    CONSTRAINT UQ_QuestionOptions_QuestionId_Id UNIQUE (QuestionId, Id),
    CONSTRAINT FK_QuestionOptions_Questions FOREIGN KEY (QuestionId)
        REFERENCES Assessment.Questions (Id) ON DELETE CASCADE,
    CONSTRAINT CK_QuestionOptions_TextOrImage CHECK (OptionText IS NOT NULL OR ImageUrl IS NOT NULL),
    -- Any option with an image must describe it, even when it also has text: the
    -- AI reads text only, and the text may just label the picture.
    CONSTRAINT CK_QuestionOptions_ImageHasDescription
        CHECK (ImageUrl IS NULL
            OR (ImageDescription IS NOT NULL AND LTRIM(RTRIM(ImageDescription)) <> N''))
);
GO

-- At most one correct option per question. The service enforces "exactly one"
-- before a question may be published.
CREATE UNIQUE INDEX UQ_QuestionOptions_OneCorrectPerQuestion
    ON Assessment.QuestionOptions (QuestionId)
    WHERE IsCorrect = 1;
GO

CREATE TABLE Assessment.QuestionOptionTranslations
(
    Id                  INT IDENTITY(1,1)   NOT NULL,
    QuestionOptionId    INT                 NOT NULL,
    LanguageCode        NVARCHAR(5)         NOT NULL,
    OptionText          NVARCHAR(MAX)       NULL,

    CONSTRAINT PK_QuestionOptionTranslations PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_QuestionOptionTranslations_OptionId_Language UNIQUE (QuestionOptionId, LanguageCode),
    CONSTRAINT FK_QuestionOptionTranslations_QuestionOptions FOREIGN KEY (QuestionOptionId)
        REFERENCES Assessment.QuestionOptions (Id) ON DELETE CASCADE,
    CONSTRAINT FK_QuestionOptionTranslations_Languages FOREIGN KEY (LanguageCode)
        REFERENCES Assessment.Languages (Code)
);
GO


/* ============================================================================
   Attempts — what a child was asked, what they answered, what it scored.
   ========================================================================== */

CREATE TABLE Assessment.QuizAttempts
(
    Id                      BIGINT IDENTITY(1,1)    NOT NULL,
    QuizId                  INT                     NOT NULL,
    -- Cross-module, no FK (see header).
    UserId                  UNIQUEIDENTIFIER        NOT NULL,
    QuestionsAnsweredCount  SMALLINT                NOT NULL,
    TotalQuestionsAtAttempt SMALLINT                NOT NULL,
    CorrectAnswersCount     SMALLINT                NOT NULL,
    WrongAnswersCount       AS (QuestionsAnsweredCount - CorrectAnswersCount) PERSISTED,
    ScorePercentage         DECIMAL(5,2)            NOT NULL,
    Status                  NVARCHAR(20)            NOT NULL
        CONSTRAINT DF_QuizAttempts_Status DEFAULT ('InProgress'),
    StartedAt               DATETIME2(3)            NOT NULL
        CONSTRAINT DF_QuizAttempts_StartedAt DEFAULT (SYSUTCDATETIME()),
    CompletedAt             DATETIME2(3)            NULL,
    DurationSeconds         AS (DATEDIFF(SECOND, StartedAt, CompletedAt)) PERSISTED,
    -- A retry points at the attempt it retries; at most one retry each.
    PreviousAttemptId       BIGINT                  NULL,
    -- Makes InProgress → Completed happen exactly once under concurrency.
    RowVersion              ROWVERSION              NOT NULL,

    CONSTRAINT PK_QuizAttempts PRIMARY KEY CLUSTERED (Id),
    -- No ON DELETE: a Quiz with attempts cannot be deleted (historical data).
    CONSTRAINT FK_QuizAttempts_Quizzes FOREIGN KEY (QuizId) REFERENCES Assessment.Quizzes (Id),
    CONSTRAINT FK_QuizAttempts_PreviousAttempt FOREIGN KEY (PreviousAttemptId)
        REFERENCES Assessment.QuizAttempts (Id),
    CONSTRAINT CK_QuizAttempts_Status CHECK (Status IN ('InProgress', 'Completed', 'Abandoned')),
    CONSTRAINT CK_QuizAttempts_ScorePercentage CHECK (ScorePercentage BETWEEN 0 AND 100),
    CONSTRAINT CK_QuizAttempts_TotalQuestionsPositive CHECK (TotalQuestionsAtAttempt > 0),
    CONSTRAINT CK_QuizAttempts_AnsweredVsTotal CHECK (QuestionsAnsweredCount <= TotalQuestionsAtAttempt),
    CONSTRAINT CK_QuizAttempts_CorrectVsAnswered
        CHECK (CorrectAnswersCount >= 0 AND CorrectAnswersCount <= QuestionsAnsweredCount),
    CONSTRAINT CK_QuizAttempts_CompletedAfterStarted CHECK (CompletedAt IS NULL OR CompletedAt >= StartedAt),
    CONSTRAINT CK_QuizAttempts_CompletedRequiresAllAnswered
        CHECK (Status <> 'Completed'
            OR (QuestionsAnsweredCount = TotalQuestionsAtAttempt AND CompletedAt IS NOT NULL)),
    CONSTRAINT CK_QuizAttempts_NotSelfReferencing
        CHECK (PreviousAttemptId IS NULL OR PreviousAttemptId <> Id)
);
GO

-- An attempt may be retried once. FILTERED on purpose: a plain UNIQUE key would
-- treat every NULL as equal and allow only one first attempt in the database.
CREATE UNIQUE INDEX UQ_QuizAttempts_PreviousAttemptId
    ON Assessment.QuizAttempts (PreviousAttemptId)
    WHERE PreviousAttemptId IS NOT NULL;

CREATE INDEX IX_QuizAttempts_QuizId ON Assessment.QuizAttempts (QuizId);
CREATE INDEX IX_QuizAttempts_UserId_QuizId_StartedAt
    ON Assessment.QuizAttempts (UserId, QuizId, StartedAt);
-- Serves the abandoned-attempt sweep; filtered, so it only holds live attempts.
CREATE INDEX IX_QuizAttempts_InProgress_StartedAt
    ON Assessment.QuizAttempts (StartedAt)
    WHERE Status = N'InProgress';
GO

CREATE TABLE Assessment.QuizAttemptQuestions
(
    Id              BIGINT IDENTITY(1,1)    NOT NULL,
    QuizAttemptId   BIGINT                  NOT NULL,
    QuestionId      INT                     NOT NULL,
    -- Classification, answer key and weight FROZEN at attempt start: an admin
    -- editing the question later cannot re-grade or re-weight an in-flight
    -- attempt or re-attribute its statistics. TopicId is NULL when the question
    -- had no topic.
    TopicId         INT                     NULL,
    Difficulty      NVARCHAR(20)            NOT NULL,
    CorrectOptionId INT                     NULL,
    QuestionType    NVARCHAR(30)            NOT NULL
        CONSTRAINT DF_QuizAttemptQuestions_QuestionType DEFAULT ('MultipleChoice'),
    -- Questions.Points at attempt start. The score, an essay's MaxPoints, the
    -- placement and the result totals are counted from this, never the live value.
    Points          TINYINT                 NOT NULL
        CONSTRAINT DF_QuizAttemptQuestions_Points DEFAULT (1),
    CreatedAt       DATETIME2(3)            NOT NULL
        CONSTRAINT DF_QuizAttemptQuestions_CreatedAt DEFAULT (SYSUTCDATETIME()),

    CONSTRAINT PK_QuizAttemptQuestions PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_QuizAttemptQuestions_AttemptId_QuestionId UNIQUE (QuizAttemptId, QuestionId),
    CONSTRAINT FK_QuizAttemptQuestions_QuizAttempts FOREIGN KEY (QuizAttemptId)
        REFERENCES Assessment.QuizAttempts (Id) ON DELETE CASCADE,
    CONSTRAINT FK_QuizAttemptQuestions_Questions FOREIGN KEY (QuestionId)
        REFERENCES Assessment.Questions (Id),
    CONSTRAINT FK_QuizAttemptQuestions_Topics FOREIGN KEY (TopicId)
        REFERENCES Assessment.Topics (Id),
    -- The key must be an option OF THIS QUESTION, and cannot be deleted while an
    -- attempt still grades against it.
    CONSTRAINT FK_QuizAttemptQuestions_QuestionId_CorrectOptionId
        FOREIGN KEY (QuestionId, CorrectOptionId)
        REFERENCES Assessment.QuestionOptions (QuestionId, Id),
    CONSTRAINT CK_QuizAttemptQuestions_QuestionType
        CHECK (QuestionType IN ('MultipleChoice', 'TrueFalse', 'Essay')),
    CONSTRAINT CK_QuizAttemptQuestions_Difficulty
        CHECK (Difficulty IN ('Easy', 'Medium', 'Hard', 'Advanced')),
    -- Only an Essay has no answer key.
    CONSTRAINT CK_QuizAttemptQuestions_EssayHasNoKey
        CHECK ((QuestionType = 'Essay' AND CorrectOptionId IS NULL)
            OR (QuestionType <> 'Essay' AND CorrectOptionId IS NOT NULL)),
    CONSTRAINT CK_QuizAttemptQuestions_Points CHECK (Points > 0)
);
GO

CREATE INDEX IX_QuizAttemptQuestions_QuestionId ON Assessment.QuizAttemptQuestions (QuestionId);
GO

CREATE TABLE Assessment.QuizAttemptMistakes
(
    Id                  BIGINT IDENTITY(1,1)    NOT NULL,
    QuizAttemptId       BIGINT                  NOT NULL,
    QuestionId          INT                     NOT NULL,
    SelectedOptionId    INT                     NOT NULL,
    CreatedAt           DATETIME2(3)            NOT NULL
        CONSTRAINT DF_QuizAttemptMistakes_CreatedAt DEFAULT (SYSUTCDATETIME()),

    CONSTRAINT PK_QuizAttemptMistakes PRIMARY KEY CLUSTERED (Id),
    -- One wrong answer per question per attempt: makes a double submit a database
    -- error rather than duplicate data.
    CONSTRAINT UQ_QuizAttemptMistakes_AttemptId_QuestionId UNIQUE (QuizAttemptId, QuestionId),
    CONSTRAINT FK_QuizAttemptMistakes_QuizAttempts FOREIGN KEY (QuizAttemptId)
        REFERENCES Assessment.QuizAttempts (Id) ON DELETE CASCADE,
    CONSTRAINT FK_QuizAttemptMistakes_Questions FOREIGN KEY (QuestionId)
        REFERENCES Assessment.Questions (Id),
    -- The selected option really belongs to the question.
    CONSTRAINT FK_QuizAttemptMistakes_QuestionId_SelectedOptionId
        FOREIGN KEY (QuestionId, SelectedOptionId)
        REFERENCES Assessment.QuestionOptions (QuestionId, Id),
    -- …and the question really was part of this attempt.
    CONSTRAINT FK_QuizAttemptMistakes_QuizAttemptQuestions
        FOREIGN KEY (QuizAttemptId, QuestionId)
        REFERENCES Assessment.QuizAttemptQuestions (QuizAttemptId, QuestionId)
);
GO

CREATE INDEX IX_QuizAttemptMistakes_QuestionId ON Assessment.QuizAttemptMistakes (QuestionId);
GO

CREATE TABLE Assessment.QuizAttemptEssayAnswers
(
    Id                      BIGINT IDENTITY(1,1)    NOT NULL,
    QuizAttemptId           BIGINT                  NOT NULL,
    QuestionId              INT                     NOT NULL,
    AnswerText              NVARCHAR(MAX)           NOT NULL,
    -- The grade the child sees.
    Status                  NVARCHAR(20)            NOT NULL
        CONSTRAINT DF_QuizAttemptEssayAnswers_Status DEFAULT ('Pending'),
    AwardedPoints           TINYINT                 NULL,
    Feedback                NVARCHAR(MAX)           NULL,
    GradedBy                NVARCHAR(20)            NULL,
    GradedAt                DATETIME2(3)            NULL,
    CreatedAt               DATETIME2(3)            NOT NULL
        CONSTRAINT DF_QuizAttemptEssayAnswers_CreatedAt DEFAULT (SYSUTCDATETIME()),
    -- The language the child answered in; AI feedback is written in it.
    LanguageCode            NVARCHAR(5)             NOT NULL
        CONSTRAINT DF_QuizAttemptEssayAnswers_LanguageCode DEFAULT (N'ar'),
    -- Copied at submit from QuizAttemptQuestions.Points (the question's Points
    -- frozen at attempt start): the grade's ceiling.
    MaxPoints               TINYINT                 NOT NULL
        CONSTRAINT DF_QuizAttemptEssayAnswers_MaxPoints DEFAULT (1),
    -- AI evaluation state. Essays are graded by the AI only; AiOutcome says how
    -- the evaluation ended (NULL while Pending).
    AiOutcome               NVARCHAR(20)            NULL,
    AiEvaluationAttempts    TINYINT                 NOT NULL
        CONSTRAINT DF_QuizAttemptEssayAnswers_AiEvaluationAttempts DEFAULT (0),
    AiLastAttemptAt         DATETIME2(3)            NULL,
    AiClaimId               UNIQUEIDENTIFIER        NULL,
    -- Reported by the AI with its grade, if at all. Monitoring only: it never
    -- decides whether the grade is accepted.
    AiConfidence            DECIMAL(3,2)            NULL,

    CONSTRAINT PK_QuizAttemptEssayAnswers PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_QuizAttemptEssayAnswers_AttemptId_QuestionId UNIQUE (QuizAttemptId, QuestionId),
    CONSTRAINT FK_QuizAttemptEssayAnswers_QuizAttempts FOREIGN KEY (QuizAttemptId)
        REFERENCES Assessment.QuizAttempts (Id) ON DELETE CASCADE,
    CONSTRAINT FK_QuizAttemptEssayAnswers_Questions FOREIGN KEY (QuestionId)
        REFERENCES Assessment.Questions (Id),
    CONSTRAINT FK_QuizAttemptEssayAnswers_QuizAttemptQuestions
        FOREIGN KEY (QuizAttemptId, QuestionId)
        REFERENCES Assessment.QuizAttemptQuestions (QuizAttemptId, QuestionId),
    CONSTRAINT FK_QuizAttemptEssayAnswers_Languages FOREIGN KEY (LanguageCode)
        REFERENCES Assessment.Languages (Code),
    -- Pending = waiting for the AI; Graded and NotGraded are final.
    CONSTRAINT CK_QuizAttemptEssayAnswers_Status CHECK (Status IN ('Pending', 'Graded', 'NotGraded')),
    -- The AI is the only grader.
    CONSTRAINT CK_QuizAttemptEssayAnswers_GradedBy CHECK (GradedBy IS NULL OR GradedBy = 'Ai'),
    -- A graded answer carries its grade and its grader; any other carries none.
    CONSTRAINT CK_QuizAttemptEssayAnswers_GradedIsComplete
        CHECK ((Status = 'Graded' AND AwardedPoints IS NOT NULL AND GradedAt IS NOT NULL AND GradedBy IS NOT NULL)
            OR (Status <> 'Graded' AND AwardedPoints IS NULL AND GradedAt IS NULL AND GradedBy IS NULL)),
    CONSTRAINT CK_QuizAttemptEssayAnswers_AwardedWithinMax
        CHECK (AwardedPoints IS NULL OR AwardedPoints <= MaxPoints),
    CONSTRAINT CK_QuizAttemptEssayAnswers_AiOutcome
        CHECK (AiOutcome IS NULL OR AiOutcome IN ('Accepted', 'Declined', 'Failed')),
    -- Pending has no outcome yet; a final status always says how it ended.
    CONSTRAINT CK_QuizAttemptEssayAnswers_OutcomeMatchesStatus
        CHECK ((Status = 'Pending' AND AiOutcome IS NULL)
            OR (Status = 'Graded' AND AiOutcome = 'Accepted')
            OR (Status = 'NotGraded' AND AiOutcome IN ('Declined', 'Failed'))),
    CONSTRAINT CK_QuizAttemptEssayAnswers_AiConfidence
        CHECK (AiConfidence IS NULL OR AiConfidence BETWEEN 0 AND 1)
);
GO

CREATE INDEX IX_QuizAttemptEssayAnswers_QuestionId ON Assessment.QuizAttemptEssayAnswers (QuestionId);
-- Essays whose grade is not final yet (still waiting for the AI).
CREATE INDEX IX_QuizAttemptEssayAnswers_Status
    ON Assessment.QuizAttemptEssayAnswers (Status)
    WHERE Status = N'Pending';
-- What the background AI evaluator picks up.
CREATE INDEX IX_QuizAttemptEssayAnswers_AiDue
    ON Assessment.QuizAttemptEssayAnswers (CreatedAt)
    INCLUDE (AiLastAttemptAt, AiEvaluationAttempts)
    WHERE Status = N'Pending' AND AiOutcome IS NULL;
GO


/* ============================================================================
   QuestionHints — both kinds: asked for mid-attempt (the Hint button, with an
   escalation level) and generated after a submission (tied to the mistake).
   ========================================================================== */

CREATE TABLE Assessment.QuestionHints
(
    Id                      BIGINT IDENTITY(1,1)    NOT NULL,
    QuizAttemptId           BIGINT                  NOT NULL,
    QuestionId              INT                     NOT NULL,
    -- NULL for a Hint-button hint: there is no mistake row until the submission.
    QuizAttemptMistakeId    BIGINT                  NULL,
    HintText                NVARCHAR(MAX)           NOT NULL,
    -- Position in this (attempt, question, language) chain.
    HintSequence            TINYINT                 NOT NULL,
    -- Hint-button escalation level; NULL for a post-submission hint.
    AttemptNumber           TINYINT                 NULL,
    LanguageCode            NVARCHAR(5)             NOT NULL
        CONSTRAINT DF_QuestionHints_LanguageCode DEFAULT (N'ar'),
    GeneratedAt             DATETIME2(3)            NOT NULL
        CONSTRAINT DF_QuestionHints_GeneratedAt DEFAULT (SYSUTCDATETIME()),

    CONSTRAINT PK_QuestionHints PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_QuestionHints_AttemptId_QuestionId_Language_Sequence
        UNIQUE (QuizAttemptId, QuestionId, LanguageCode, HintSequence),
    -- The only cascade path down to a hint: deleting an attempt removes its
    -- questions, and with them their hints. (The mistake FK below is NO ACTION
    -- because SQL Server allows only one cascade path.)
    CONSTRAINT FK_QuestionHints_QuizAttemptQuestions
        FOREIGN KEY (QuizAttemptId, QuestionId)
        REFERENCES Assessment.QuizAttemptQuestions (QuizAttemptId, QuestionId) ON DELETE CASCADE,
    CONSTRAINT FK_QuestionHints_QuizAttemptMistakes FOREIGN KEY (QuizAttemptMistakeId)
        REFERENCES Assessment.QuizAttemptMistakes (Id),
    CONSTRAINT FK_QuestionHints_Languages FOREIGN KEY (LanguageCode)
        REFERENCES Assessment.Languages (Code),
    CONSTRAINT CK_QuestionHints_HintSequence CHECK (HintSequence > 0),
    CONSTRAINT CK_QuestionHints_AttemptNumber
        CHECK (AttemptNumber IS NULL OR AttemptNumber BETWEEN 1 AND 5)
);
GO

-- Each Hint-button level is saved once per attempt and question, in ANY language.
-- The sequence key above is per language, so on its own it let two presses at
-- the same moment both keep level 1. Filtered: post-submission hints have no level.
CREATE UNIQUE INDEX UQ_QuestionHints_AttemptId_QuestionId_AttemptNumber
    ON Assessment.QuestionHints (QuizAttemptId, QuestionId, AttemptNumber)
    WHERE AttemptNumber IS NOT NULL;
GO


/* ============================================================================
   Per-child aggregates and the placement result.
   ========================================================================== */

CREATE TABLE Assessment.UserTopicStats
(
    Id                      BIGINT IDENTITY(1,1)    NOT NULL,
    -- Cross-module, no FK (see header).
    UserId                  UNIQUEIDENTIFIER        NOT NULL,
    TopicId                 INT                     NOT NULL,
    Difficulty              NVARCHAR(20)            NOT NULL,
    QuestionsAnsweredCount  INT                     NOT NULL
        CONSTRAINT DF_UserTopicStats_QAC DEFAULT (0),
    CorrectCount            INT                     NOT NULL
        CONSTRAINT DF_UserTopicStats_CC DEFAULT (0),
    WrongCount              AS (QuestionsAnsweredCount - CorrectCount) PERSISTED,
    HintsUsedCount          INT                     NOT NULL
        CONSTRAINT DF_UserTopicStats_HUC DEFAULT (0),
    LastQuizAttemptId       BIGINT                  NULL,
    LastPracticedAt         DATETIME2(3)            NULL,
    UpdatedAt               DATETIME2(3)            NOT NULL
        CONSTRAINT DF_UserTopicStats_UpdatedAt DEFAULT (SYSUTCDATETIME()),
    -- Two submissions touching one row no longer lose each other's counts.
    RowVersion              ROWVERSION              NOT NULL,

    CONSTRAINT PK_UserTopicStats PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_UserTopicStats_UserId_TopicId_Difficulty UNIQUE (UserId, TopicId, Difficulty),
    CONSTRAINT FK_UserTopicStats_Topics FOREIGN KEY (TopicId) REFERENCES Assessment.Topics (Id),
    -- Only the traceability pointer is cleared if an attempt is ever deleted.
    CONSTRAINT FK_UserTopicStats_QuizAttempts FOREIGN KEY (LastQuizAttemptId)
        REFERENCES Assessment.QuizAttempts (Id) ON DELETE SET NULL,
    CONSTRAINT CK_UserTopicStats_Difficulty CHECK (Difficulty IN ('Easy', 'Medium', 'Hard', 'Advanced')),
    CONSTRAINT CK_UserTopicStats_QuestionsAnsweredNonNegative CHECK (QuestionsAnsweredCount >= 0),
    CONSTRAINT CK_UserTopicStats_CorrectCountNonNegative CHECK (CorrectCount >= 0),
    CONSTRAINT CK_UserTopicStats_HintsUsedNonNegative CHECK (HintsUsedCount >= 0),
    CONSTRAINT CK_UserTopicStats_CorrectVsAnswered CHECK (CorrectCount <= QuestionsAnsweredCount)
);
GO

CREATE INDEX IX_UserTopicStats_TopicId ON Assessment.UserTopicStats (TopicId);
GO

CREATE TABLE Assessment.UserPlacements
(
    Id              BIGINT IDENTITY(1,1)    NOT NULL,
    -- Cross-module, no FK (see header).
    UserId          UNIQUEIDENTIFIER        NOT NULL,
    QuizAttemptId   BIGINT                  NOT NULL,
    PlacedLevelId   INT                     NOT NULL,
    ScorePercentage DECIMAL(5,2)            NOT NULL,
    -- The mastery threshold in force when the learner was placed, so the
    -- breakdown always explains the stored level.
    PassPercentage  TINYINT                 NOT NULL,
    PlacedAt        DATETIME2(3)            NOT NULL
        CONSTRAINT DF_UserPlacements_PlacedAt DEFAULT (SYSUTCDATETIME()),

    CONSTRAINT PK_UserPlacements PRIMARY KEY CLUSTERED (Id),
    -- One placement per learner, and one per attempt.
    CONSTRAINT UQ_UserPlacements_UserId UNIQUE (UserId),
    CONSTRAINT UQ_UserPlacements_QuizAttemptId UNIQUE (QuizAttemptId),
    -- No ON DELETE: the attempt that justifies a placement cannot be deleted.
    CONSTRAINT FK_UserPlacements_QuizAttempts FOREIGN KEY (QuizAttemptId)
        REFERENCES Assessment.QuizAttempts (Id),
    CONSTRAINT CK_UserPlacements_ScorePercentage CHECK (ScorePercentage BETWEEN 0 AND 100),
    CONSTRAINT CK_UserPlacements_PassPercentage CHECK (PassPercentage BETWEEN 1 AND 100)
);
GO


/* ============================================================================
   Verification — every object below belongs to Assessment, and nothing else was
   created. Both queries should list only Assessment.* objects.
   ========================================================================== */

SELECT t.name AS TableName, SCHEMA_NAME(t.schema_id) AS SchemaName
FROM sys.tables AS t
WHERE t.schema_id = SCHEMA_ID(N'Assessment')
ORDER BY t.name;

SELECT SCHEMA_NAME(t.schema_id) AS SchemaName, t.name AS TableName, i.name AS IndexName, i.filter_definition
FROM sys.indexes AS i
JOIN sys.tables AS t ON t.object_id = i.object_id
WHERE t.schema_id = SCHEMA_ID(N'Assessment') AND i.name IS NOT NULL
ORDER BY t.name, i.name;
GO
