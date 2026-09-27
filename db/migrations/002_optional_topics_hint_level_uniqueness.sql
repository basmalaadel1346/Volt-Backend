/* ============================================================================
   Migration 002 — optional question topics, one saved hint per level
   ----------------------------------------------------------------------------
   For databases built from an EARLIER version of 000_AssessmentSchema.sql. Run
   it after 001. A database built from the current 000 already has all of this;
   running the script on it is harmless (every step checks the current state
   first).

   Part 1 — a question may belong to no topic
     Questions.TopicId and QuizAttemptQuestions.TopicId become NULLable. A
     question with no topic is asked, graded and earns XP like any other; its
     answers count toward no topic in UserTopicStats. Every existing row keeps
     its topic.

   Part 2 — each Hint-button level is saved once, whatever the language
     UQ_QuestionHints_AttemptId_QuestionId_Language_Sequence is per language, so
     two presses at the same moment (in two languages, or in one where the
     second read the sequence after the first saved) could both store level 1
     and use up the child's levels. The new filtered unique index
     UQ_QuestionHints_AttemptId_QuestionId_AttemptNumber lets only one of them
     keep the level; the application answers the other with 409.
     Levels that race already duplicated are renumbered first, in the order the
     hints were generated. The child saw every one of those hints, so none is
     deleted.

   Order and safety
   * Each part is ONE batch and ONE transaction (XACT_ABORT ON): if any step
     fails, that part rolls back entirely.
   * ALTER COLUMN refuses a column an index or a foreign key depends on, so
     those are dropped and re-created with the same names and definitions
     inside the same transaction.
   * Run with `sqlcmd -b` (or stop on the first error in SSMS).

   Deploy together with the application code of this change: the old code maps
   TopicId as NOT NULL and fails to read a question without a topic.
   ========================================================================== */

USE VoltDB;
GO

-- Required for DDL and DML on tables with filtered indexes; sqlcmd defaults
-- QUOTED_IDENTIFIER OFF.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/* GATE — the Assessment schema must exist (built from 000). */
IF OBJECT_ID(N'Assessment.Questions') IS NULL
   OR OBJECT_ID(N'Assessment.QuizAttemptQuestions') IS NULL
   OR OBJECT_ID(N'Assessment.QuestionHints') IS NULL
   OR OBJECT_ID(N'Assessment.Topics') IS NULL
    THROW 50001, N'002: the Assessment schema was not found. Build it with 000_AssessmentSchema.sql first.', 1;
GO


/* ============================================================================
   Part 1 — Questions.TopicId and QuizAttemptQuestions.TopicId are optional
   ========================================================================== */

SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COLUMNPROPERTY(OBJECT_ID(N'Assessment.Questions'), N'TopicId', 'AllowsNull') = 0
BEGIN
    IF EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'Assessment.Questions') AND name = N'IX_Questions_TopicId')
        DROP INDEX IX_Questions_TopicId ON Assessment.Questions;

    IF OBJECT_ID(N'Assessment.FK_Questions_Topics', N'F') IS NOT NULL
        ALTER TABLE Assessment.Questions DROP CONSTRAINT FK_Questions_Topics;

    ALTER TABLE Assessment.Questions ALTER COLUMN TopicId INT NULL;

    -- No ON DELETE, as before: a Topic with Questions cannot be deleted.
    ALTER TABLE Assessment.Questions WITH CHECK
        ADD CONSTRAINT FK_Questions_Topics FOREIGN KEY (TopicId) REFERENCES Assessment.Topics (Id);

    CREATE INDEX IX_Questions_TopicId ON Assessment.Questions (TopicId);

    PRINT N'Part 1: Questions.TopicId is now optional.';
END
ELSE
    PRINT N'Part 1: Questions.TopicId is already optional.';

IF COLUMNPROPERTY(OBJECT_ID(N'Assessment.QuizAttemptQuestions'), N'TopicId', 'AllowsNull') = 0
BEGIN
    IF OBJECT_ID(N'Assessment.FK_QuizAttemptQuestions_Topics', N'F') IS NOT NULL
        ALTER TABLE Assessment.QuizAttemptQuestions DROP CONSTRAINT FK_QuizAttemptQuestions_Topics;

    ALTER TABLE Assessment.QuizAttemptQuestions ALTER COLUMN TopicId INT NULL;

    ALTER TABLE Assessment.QuizAttemptQuestions WITH CHECK
        ADD CONSTRAINT FK_QuizAttemptQuestions_Topics FOREIGN KEY (TopicId) REFERENCES Assessment.Topics (Id);

    PRINT N'Part 1: QuizAttemptQuestions.TopicId is now optional.';
END
ELSE
    PRINT N'Part 1: QuizAttemptQuestions.TopicId is already optional.';

COMMIT TRANSACTION;
GO


/* ============================================================================
   Part 2 — UQ_QuestionHints_AttemptId_QuestionId_AttemptNumber
   ========================================================================== */

SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'Assessment.QuestionHints')
                 AND name = N'UQ_QuestionHints_AttemptId_QuestionId_AttemptNumber')
BEGIN
    -- Renumbering can only produce levels 1..n per attempt and question. More
    -- than 5 would break CK_QuestionHints_AttemptNumber; stop and say so.
    IF EXISTS (SELECT 1
               FROM Assessment.QuestionHints
               WHERE AttemptNumber IS NOT NULL
               GROUP BY QuizAttemptId, QuestionId
               HAVING COUNT(*) > 5)
        THROW 50021, N'002: an attempt has more than 5 Hint-button hints for one question. Renumbering them would break CK_QuestionHints_AttemptNumber; review those QuestionHints rows first.', 1;

    WITH Ranked AS
    (
        SELECT AttemptNumber,
               ROW_NUMBER() OVER (PARTITION BY QuizAttemptId, QuestionId ORDER BY GeneratedAt, Id) AS Level
        FROM Assessment.QuestionHints
        WHERE AttemptNumber IS NOT NULL
    )
    UPDATE Ranked
    SET AttemptNumber = CAST(Level AS TINYINT)
    WHERE AttemptNumber <> Level;

    DECLARE @Renumbered INT = @@ROWCOUNT;
    PRINT CONCAT(N'Part 2: ', @Renumbered, N' Hint-button hint(s) renumbered to remove duplicated levels.');

    CREATE UNIQUE INDEX UQ_QuestionHints_AttemptId_QuestionId_AttemptNumber
        ON Assessment.QuestionHints (QuizAttemptId, QuestionId, AttemptNumber)
        WHERE AttemptNumber IS NOT NULL;

    PRINT N'Part 2: UQ_QuestionHints_AttemptId_QuestionId_AttemptNumber created.';
END
ELSE
    PRINT N'Part 2: UQ_QuestionHints_AttemptId_QuestionId_AttemptNumber already exists.';

COMMIT TRANSACTION;
GO


/* ============================================================================
   Verification
   ========================================================================== */

-- Expect two rows, both is_nullable = 1.
SELECT OBJECT_SCHEMA_NAME(c.object_id) + N'.' + OBJECT_NAME(c.object_id) AS TableName,
       c.name AS ColumnName,
       c.is_nullable
FROM sys.columns AS c
WHERE c.object_id IN (OBJECT_ID(N'Assessment.Questions'), OBJECT_ID(N'Assessment.QuizAttemptQuestions'))
  AND c.name = N'TopicId';

-- Expect both unique hint indexes; the AttemptNumber one filtered.
SELECT i.name AS IndexName, i.is_unique, i.filter_definition
FROM sys.indexes AS i
WHERE i.object_id = OBJECT_ID(N'Assessment.QuestionHints')
  AND i.name LIKE N'UQ[_]QuestionHints[_]%'
ORDER BY i.name;
GO
