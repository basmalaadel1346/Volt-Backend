/* ============================================================================
   Migration 001 — snapshot Points, AI-only essay grading, described images
   ----------------------------------------------------------------------------
   For databases built from an EARLIER version of 000_AssessmentSchema.sql. A
   database built from the current 000 already has all of this; running the
   script on it is harmless (every step checks the current state first).

   Part 1 — QuizAttemptQuestions.Points
     A question's weight is frozen at attempt start, like its answer key. The
     score, an essay's MaxPoints, the placement and the result totals are
     counted from the snapshot, never from the live Questions.Points.

   Part 2 — essays are graded by the AI only
     There is no person who reviews an essay. Status: Pending | Graded |
     NotGraded. GradedBy: 'Ai' only. AiOutcome: Accepted | Declined | Failed,
     NULL exactly while Pending. AiProposedPoints and AiFeedback are dropped:
     an AI grade is either accepted as it is or not stored at all.

   Part 3 — every image carries a description
     The AI never looks at images, only at text. A question or option with an
     ImageUrl must have a non-blank ImageDescription; without an image the
     description is NULL.

   Order and safety
   * Each part is ONE batch and ONE transaction (XACT_ABORT ON): if any step
     fails, that part rolls back entirely and the old constraints stay.
   * Inside each part, data is fixed BEFORE the new CHECKs are added, and
     constraints on a column are dropped BEFORE the column is.
   * Statements that reference a column that may not exist yet (or any more)
     at compile time run through sp_executesql behind a COL_LENGTH check.
   * Run with `sqlcmd -b` (or stop on the first error in SSMS): a failed part
     must not let the parts after it run.

   Deploy together with the application code of this change: the old code
   writes 'NeedsReview' and reads AiProposedPoints/AiFeedback, which Part 2
   removes.
   ========================================================================== */

USE VoltDB;
GO

-- Required for DML on tables with filtered indexes (QuestionOptions,
-- QuizAttemptEssayAnswers); sqlcmd defaults QUOTED_IDENTIFIER OFF.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/* GATE — the Assessment schema must exist (built from 000). */
IF OBJECT_ID(N'Assessment.QuizAttemptQuestions') IS NULL
   OR OBJECT_ID(N'Assessment.QuizAttemptEssayAnswers') IS NULL
   OR OBJECT_ID(N'Assessment.Questions') IS NULL
   OR OBJECT_ID(N'Assessment.QuestionOptions') IS NULL
    THROW 50001, N'001: the Assessment schema was not found. Build it with 000_AssessmentSchema.sql first.', 1;
GO


/* ============================================================================
   Part 1 — QuizAttemptQuestions.Points, frozen at attempt start
   ========================================================================== */

SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH(N'Assessment.QuizAttemptQuestions', N'Points') IS NULL
BEGIN
    ALTER TABLE Assessment.QuizAttemptQuestions
        ADD Points TINYINT NOT NULL
            CONSTRAINT DF_QuizAttemptQuestions_Points DEFAULT (1);

    -- One-time backfill, only in the run that adds the column (and in the same
    -- transaction, so a failure here also un-adds the column and a re-run
    -- backfills again). A later re-run must never overwrite snapshots frozen by
    -- attempts started after the deploy.
    --
    -- Every existing row now holds the default 1. Correct only what 1 would misstate:
    --  * Essay rows of submitted attempts take the MaxPoints frozen on the essay
    --    answer at submit. The essay's grade is already bounded by that value
    --    (CK_QuizAttemptEssayAnswers_AwardedWithinMax), so the snapshot must equal it.
    --  * Every row of an attempt still InProgress takes the live Questions.Points:
    --    nothing is graded yet, and it is the closest value to what the question
    --    weighed when the attempt started.
    --  * MultipleChoice/TrueFalse rows of Completed and Abandoned attempts stay 1.
    --    Their committed ScorePercentage (and any placement) was computed with
    --    every question worth 1, and a committed score must never change. With 1,
    --    the new TotalPoints/EarnedPoints and the re-described placement agree
    --    with what was committed.
    EXEC sys.sp_executesql N'
        UPDATE aq
           SET aq.Points = ea.MaxPoints
          FROM Assessment.QuizAttemptQuestions AS aq
          JOIN Assessment.QuizAttemptEssayAnswers AS ea
            ON ea.QuizAttemptId = aq.QuizAttemptId AND ea.QuestionId = aq.QuestionId
         WHERE aq.QuestionType = N''Essay'' AND ea.MaxPoints > 0;

        UPDATE aq
           SET aq.Points = q.Points
          FROM Assessment.QuizAttemptQuestions AS aq
          JOIN Assessment.QuizAttempts AS a ON a.Id = aq.QuizAttemptId
          JOIN Assessment.Questions AS q ON q.Id = aq.QuestionId
         WHERE a.Status = N''InProgress'' AND q.Points > 0;';

    PRINT N'Part 1: QuizAttemptQuestions.Points added and backfilled.';
END

-- Every row is > 0 at this point (default 1, MaxPoints > 0, Questions.Points > 0),
-- so the constraint is added WITH CHECK and is trusted.
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE name = N'CK_QuizAttemptQuestions_Points'
                 AND parent_object_id = OBJECT_ID(N'Assessment.QuizAttemptQuestions'))
    EXEC sys.sp_executesql N'
        ALTER TABLE Assessment.QuizAttemptQuestions WITH CHECK
            ADD CONSTRAINT CK_QuizAttemptQuestions_Points CHECK (Points > 0);';

COMMIT TRANSACTION;
GO


/* ============================================================================
   Part 2 — essays are graded by the AI only
   ----------------------------------------------------------------------------
   What happens to existing rows, and why:
   * Pending + AiOutcome 'NeedsReview' or 'Failed' were parked for a person who
     does not exist, so nothing would ever finish them. They become NotGraded
     (a final state the child sees) and are not sent to the AI again: they were
     already tried (the AI declined, was unsure, or failed every attempt), or
     they are an old backlog whose language was never recorded. Outcome:
       - 'Failed'                                                  → Failed
       - 'NeedsReview' with nothing stored after an AI try         → Declined
         (an AI "Skipped"; a legacy question with no text looks the same and
         cannot be told apart)
       - any other 'NeedsReview' (an unsure or flagged proposal, or a backlog
         row that never reached the AI)                            → Failed
     Unsure or flagged proposals are NOT promoted to grades: they were never
     shown to the child, and some were flagged (e.g. PersonalData, Unsafe).
   * Pending + 'Accepted' (should not exist: Accepted was always written with
     Graded) goes back to AiOutcome NULL, unclaimed, so the AI evaluates it again.
   * Status 'Skipped' → NotGraded. Outcome Failed if it was Failed, otherwise
     Declined (the essay was deliberately not graded).
   * Graded rows KEEP their grade. The child has already seen it and it counts
     in their earned points; dropping it would silently change a final result.
     CK_..._GradedIsComplete requires a grader on a Graded row and the new
     CK_..._GradedBy allows only 'Ai', so a 'Human' grade has to be relabelled
     'Ai' (with AiOutcome 'Accepted' for CK_..._OutcomeMatchesStatus). Those
     rows are listed BEFORE they are changed, so their real provenance stays in
     the deployment output.
   The new CHECKs are added WITH CHECK (trusted): a row that still violates one
   fails this part and rolls it back, rather than being relabelled blindly.
   ========================================================================== */

SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @Rows INT;
DECLARE @Sql NVARCHAR(MAX);

/* §2.1 Drop the old CHECKs whose value lists the data fix must step outside of. */
IF EXISTS (SELECT 1 FROM sys.check_constraints
           WHERE name = N'CK_QuizAttemptEssayAnswers_Status'
             AND parent_object_id = OBJECT_ID(N'Assessment.QuizAttemptEssayAnswers')
             AND definition NOT LIKE N'%NotGraded%')
    ALTER TABLE Assessment.QuizAttemptEssayAnswers DROP CONSTRAINT CK_QuizAttemptEssayAnswers_Status;

IF EXISTS (SELECT 1 FROM sys.check_constraints
           WHERE name = N'CK_QuizAttemptEssayAnswers_GradedBy'
             AND parent_object_id = OBJECT_ID(N'Assessment.QuizAttemptEssayAnswers')
             AND definition LIKE N'%Human%')
    ALTER TABLE Assessment.QuizAttemptEssayAnswers DROP CONSTRAINT CK_QuizAttemptEssayAnswers_GradedBy;

IF EXISTS (SELECT 1 FROM sys.check_constraints
           WHERE name = N'CK_QuizAttemptEssayAnswers_AiOutcome'
             AND parent_object_id = OBJECT_ID(N'Assessment.QuizAttemptEssayAnswers')
             AND definition NOT LIKE N'%Declined%')
    ALTER TABLE Assessment.QuizAttemptEssayAnswers DROP CONSTRAINT CK_QuizAttemptEssayAnswers_AiOutcome;

/* §2.2 Graded by a person: keep the grade and relabel it (see header). Listed first. */
IF EXISTS (SELECT 1 FROM Assessment.QuizAttemptEssayAnswers WHERE Status = N'Graded' AND GradedBy = N'Human')
BEGIN
    SELECT Id, QuizAttemptId, QuestionId, AwardedPoints, MaxPoints, GradedAt, AiOutcome
    FROM Assessment.QuizAttemptEssayAnswers
    WHERE Status = N'Graded' AND GradedBy = N'Human'
    ORDER BY Id;

    PRINT N'Part 2: the essay answers listed above were graded by a person; their grades are kept and relabelled GradedBy=Ai.';
END

UPDATE Assessment.QuizAttemptEssayAnswers
SET GradedBy = N'Ai',
    AiOutcome = N'Accepted'
WHERE Status = N'Graded'
  AND (GradedBy <> N'Ai' OR AiOutcome IS NULL OR AiOutcome <> N'Accepted');
SET @Rows = @@ROWCOUNT;

IF @Rows > 0
    PRINT CONCAT(N'Part 2: ', @Rows, N' Graded essay answer(s) aligned to GradedBy=Ai / AiOutcome=Accepted.');

/* §2.3 Parked for a person: close them. On a re-run the proposal columns may
   already be gone, so the statement that reads them is compiled only when they exist. */
IF COL_LENGTH(N'Assessment.QuizAttemptEssayAnswers', N'AiProposedPoints') IS NOT NULL
   AND COL_LENGTH(N'Assessment.QuizAttemptEssayAnswers', N'AiFeedback') IS NOT NULL
BEGIN
    EXEC sys.sp_executesql N'
        UPDATE Assessment.QuizAttemptEssayAnswers
        SET Status = N''NotGraded'',
            AiOutcome = CASE
                            WHEN AiOutcome = N''NeedsReview''
                             AND AiProposedPoints IS NULL
                             AND AiFeedback IS NULL
                             AND AiConfidence IS NULL
                             AND AiEvaluationAttempts > 0 THEN N''Declined''
                            ELSE N''Failed''
                        END,
            AiClaimId = NULL
        WHERE Status = N''Pending''
          AND AiOutcome IN (N''NeedsReview'', N''Failed'');
        SET @Rows = @@ROWCOUNT;',
        N'@Rows INT OUTPUT',
        @Rows = @Rows OUTPUT;
END
ELSE
BEGIN
    -- Without the proposal columns an AI "Skipped" cannot be recognised: Failed.
    UPDATE Assessment.QuizAttemptEssayAnswers
    SET Status = N'NotGraded',
        AiOutcome = N'Failed',
        AiClaimId = NULL
    WHERE Status = N'Pending'
      AND AiOutcome IN (N'NeedsReview', N'Failed');
    SET @Rows = @@ROWCOUNT;
END

IF @Rows > 0
    PRINT CONCAT(N'Part 2: ', @Rows, N' essay answer(s) waiting for a person closed as NotGraded.');

/* §2.4 Pending with an outcome that never became a grade: evaluate it again. */
UPDATE Assessment.QuizAttemptEssayAnswers
SET AiOutcome = NULL,
    AiClaimId = NULL
WHERE Status = N'Pending'
  AND AiOutcome IS NOT NULL;
SET @Rows = @@ROWCOUNT;

IF @Rows > 0
    PRINT CONCAT(N'Part 2: ', @Rows, N' Pending essay answer(s) with a stray outcome returned to the AI queue.');

/* §2.5 'Skipped' becomes NotGraded. */
UPDATE Assessment.QuizAttemptEssayAnswers
SET Status = N'NotGraded',
    AiOutcome = CASE WHEN AiOutcome = N'Failed' THEN N'Failed' ELSE N'Declined' END,
    AiClaimId = NULL
WHERE Status = N'Skipped';
SET @Rows = @@ROWCOUNT;

IF @Rows > 0
    PRINT CONCAT(N'Part 2: ', @Rows, N' Skipped essay answer(s) are now NotGraded.');

/* §2.6 Drop the proposal columns. Nothing reads or writes them any more. Any
   default or check constraint bound to them is dropped first (000 declares
   none, but a hand-altered database could have one, and DROP COLUMN would fail). */
SET @Sql = N'';

SELECT @Sql += N'ALTER TABLE Assessment.QuizAttemptEssayAnswers DROP CONSTRAINT ' + QUOTENAME(dc.name) + N';'
FROM sys.default_constraints AS dc
JOIN sys.columns AS c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
WHERE dc.parent_object_id = OBJECT_ID(N'Assessment.QuizAttemptEssayAnswers')
  AND c.name IN (N'AiProposedPoints', N'AiFeedback');

SELECT @Sql += N'ALTER TABLE Assessment.QuizAttemptEssayAnswers DROP CONSTRAINT ' + QUOTENAME(cc.name) + N';'
FROM sys.check_constraints AS cc
WHERE cc.parent_object_id = OBJECT_ID(N'Assessment.QuizAttemptEssayAnswers')
  AND (cc.definition LIKE N'%AiProposedPoints%' OR cc.definition LIKE N'%AiFeedback%');

IF @Sql <> N''
    EXEC sys.sp_executesql @Sql;

IF COL_LENGTH(N'Assessment.QuizAttemptEssayAnswers', N'AiProposedPoints') IS NOT NULL
    ALTER TABLE Assessment.QuizAttemptEssayAnswers DROP COLUMN AiProposedPoints;

IF COL_LENGTH(N'Assessment.QuizAttemptEssayAnswers', N'AiFeedback') IS NOT NULL
    ALTER TABLE Assessment.QuizAttemptEssayAnswers DROP COLUMN AiFeedback;

/* §2.7 Add the new CHECKs, validated against every row (§2.2–§2.5 made them hold). */
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE name = N'CK_QuizAttemptEssayAnswers_Status'
                 AND parent_object_id = OBJECT_ID(N'Assessment.QuizAttemptEssayAnswers'))
    ALTER TABLE Assessment.QuizAttemptEssayAnswers WITH CHECK
        ADD CONSTRAINT CK_QuizAttemptEssayAnswers_Status
            CHECK (Status IN ('Pending', 'Graded', 'NotGraded'));

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE name = N'CK_QuizAttemptEssayAnswers_GradedBy'
                 AND parent_object_id = OBJECT_ID(N'Assessment.QuizAttemptEssayAnswers'))
    ALTER TABLE Assessment.QuizAttemptEssayAnswers WITH CHECK
        ADD CONSTRAINT CK_QuizAttemptEssayAnswers_GradedBy
            CHECK (GradedBy IS NULL OR GradedBy = 'Ai');

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE name = N'CK_QuizAttemptEssayAnswers_AiOutcome'
                 AND parent_object_id = OBJECT_ID(N'Assessment.QuizAttemptEssayAnswers'))
    ALTER TABLE Assessment.QuizAttemptEssayAnswers WITH CHECK
        ADD CONSTRAINT CK_QuizAttemptEssayAnswers_AiOutcome
            CHECK (AiOutcome IS NULL OR AiOutcome IN ('Accepted', 'Declined', 'Failed'));

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE name = N'CK_QuizAttemptEssayAnswers_OutcomeMatchesStatus'
                 AND parent_object_id = OBJECT_ID(N'Assessment.QuizAttemptEssayAnswers'))
    ALTER TABLE Assessment.QuizAttemptEssayAnswers WITH CHECK
        ADD CONSTRAINT CK_QuizAttemptEssayAnswers_OutcomeMatchesStatus
            CHECK ((Status = 'Pending' AND AiOutcome IS NULL)
                OR (Status = 'Graded' AND AiOutcome = 'Accepted')
                OR (Status = 'NotGraded' AND AiOutcome IN ('Declined', 'Failed')));

COMMIT TRANSACTION;
GO


/* ============================================================================
   Part 3 — every image carries a description (Questions, QuestionOptions)
   ----------------------------------------------------------------------------
   Descriptions are never invented here: only an admin knows what an image
   means. Questions that break the rule (directly, or through one of their
   options) are deactivated and PRINTed; activation refuses them until an admin
   writes the descriptions. The new CHECKs are therefore added WITH NOCHECK, so
   those existing rows do not block the deploy while every INSERT and UPDATE of
   the image columns from now on is enforced. Once no row violates a rule (now,
   or on a later re-run after admins fix the data) the constraint is
   re-validated, so it ends up trusted like one built by 000.
   ========================================================================== */

SET XACT_ABORT ON;
BEGIN TRANSACTION;

/* §3.1 Drop the old option rule. It only covered image-ONLY options; the new
   rule covers every option that has an image. Dropped first so §3.4 can clear
   orphan descriptions without tripping it. */
IF EXISTS (SELECT 1 FROM sys.check_constraints
           WHERE name = N'CK_QuestionOptions_ImageOptionHasDescription'
             AND parent_object_id = OBJECT_ID(N'Assessment.QuestionOptions'))
    ALTER TABLE Assessment.QuestionOptions DROP CONSTRAINT CK_QuestionOptions_ImageOptionHasDescription;

/* §3.2 The services treat a blank ImageUrl as "no image". Make stored data
   agree, so a blank URL is not mistaken for an image with no description.
   Options only where OptionText keeps CK_QuestionOptions_TextOrImage satisfied. */
UPDATE Assessment.Questions SET ImageUrl = NULL
WHERE ImageUrl IS NOT NULL AND LTRIM(RTRIM(ImageUrl)) = N'';

UPDATE Assessment.QuestionOptions SET ImageUrl = NULL
WHERE ImageUrl IS NOT NULL AND LTRIM(RTRIM(ImageUrl)) = N'' AND OptionText IS NOT NULL;

/* §3.3 Deactivate, and PRINT, every question that breaks the rule directly or
   through one of its options. Re-running lists the ones still waiting for a
   description. */
DECLARE @Violations TABLE
(
    QuestionId      INT NOT NULL PRIMARY KEY,
    WasActive       BIT NOT NULL,
    QuestionImage   BIT NOT NULL,
    OptionImages    BIT NOT NULL
);

INSERT INTO @Violations (QuestionId, WasActive, QuestionImage, OptionImages)
SELECT q.Id,
       q.IsActive,
       CASE WHEN q.ImageUrl IS NOT NULL
                 AND (q.ImageDescription IS NULL OR LTRIM(RTRIM(q.ImageDescription)) = N'')
            THEN 1 ELSE 0 END,
       CASE WHEN EXISTS (SELECT 1 FROM Assessment.QuestionOptions AS o
                         WHERE o.QuestionId = q.Id
                           AND o.ImageUrl IS NOT NULL
                           AND (o.ImageDescription IS NULL OR LTRIM(RTRIM(o.ImageDescription)) = N''))
            THEN 1 ELSE 0 END
FROM Assessment.Questions AS q
WHERE (q.ImageUrl IS NOT NULL
       AND (q.ImageDescription IS NULL OR LTRIM(RTRIM(q.ImageDescription)) = N''))
   OR EXISTS (SELECT 1 FROM Assessment.QuestionOptions AS o
              WHERE o.QuestionId = q.Id
                AND o.ImageUrl IS NOT NULL
                AND (o.ImageDescription IS NULL OR LTRIM(RTRIM(o.ImageDescription)) = N''));

UPDATE q SET IsActive = 0
FROM Assessment.Questions AS q
JOIN @Violations AS v ON v.QuestionId = q.Id
WHERE q.IsActive = 1;

DECLARE @Id INT = 0, @Msg NVARCHAR(400);
WHILE 1 = 1
BEGIN
    SELECT TOP (1)
        @Id  = QuestionId,
        @Msg = N'Question ' + CAST(QuestionId AS NVARCHAR(12))
             + CASE WasActive WHEN 1 THEN N' was DEACTIVATED' ELSE N' (already inactive)' END
             + N': '
             + CASE WHEN QuestionImage = 1 THEN N'its image has no description' ELSE N'' END
             + CASE WHEN QuestionImage = 1 AND OptionImages = 1 THEN N'; ' ELSE N'' END
             + CASE WHEN OptionImages = 1 THEN N'an option image has no description' ELSE N'' END
             + N'. Add the description(s), then reactivate it.'
    FROM @Violations
    WHERE QuestionId > @Id
    ORDER BY QuestionId;

    IF @@ROWCOUNT = 0 BREAK;
    PRINT @Msg;
END

-- PRINT accepts only scalar expressions: a subquery inside it (Msg 1046) would
-- fail the compile of this whole batch, so the count goes through a variable.
DECLARE @ViolationCount INT = (SELECT COUNT(*) FROM @Violations);
PRINT CONCAT(N'Part 3: ', @ViolationCount, N' question(s) have an image without a description.');

/* §3.4 A description must never outlive its image. */
UPDATE Assessment.Questions SET ImageDescription = NULL
WHERE ImageUrl IS NULL AND ImageDescription IS NOT NULL;

UPDATE Assessment.QuestionOptions SET ImageDescription = NULL
WHERE ImageUrl IS NULL AND ImageDescription IS NOT NULL;

/* §3.5 Add the constraints WITH NOCHECK (see the Part 3 header for why). */
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE name = N'CK_Questions_ImageHasDescription'
                 AND parent_object_id = OBJECT_ID(N'Assessment.Questions'))
    ALTER TABLE Assessment.Questions WITH NOCHECK
        ADD CONSTRAINT CK_Questions_ImageHasDescription
            CHECK (ImageUrl IS NULL
                OR (ImageDescription IS NOT NULL AND LTRIM(RTRIM(ImageDescription)) <> N''));

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE name = N'CK_QuestionOptions_ImageHasDescription'
                 AND parent_object_id = OBJECT_ID(N'Assessment.QuestionOptions'))
    ALTER TABLE Assessment.QuestionOptions WITH NOCHECK
        ADD CONSTRAINT CK_QuestionOptions_ImageHasDescription
            CHECK (ImageUrl IS NULL
                OR (ImageDescription IS NOT NULL AND LTRIM(RTRIM(ImageDescription)) <> N''));

/* §3.6 Re-validate a constraint once no row violates it, so it is trusted.
   Dynamic SQL: the constraint may have been created earlier in this batch. */
IF NOT EXISTS (SELECT 1 FROM Assessment.Questions
               WHERE ImageUrl IS NOT NULL
                 AND (ImageDescription IS NULL OR LTRIM(RTRIM(ImageDescription)) = N''))
   AND EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE name = N'CK_Questions_ImageHasDescription'
                 AND parent_object_id = OBJECT_ID(N'Assessment.Questions')
                 AND is_not_trusted = 1)
    EXEC sys.sp_executesql N'
        ALTER TABLE Assessment.Questions WITH CHECK CHECK CONSTRAINT CK_Questions_ImageHasDescription;';

IF NOT EXISTS (SELECT 1 FROM Assessment.QuestionOptions
               WHERE ImageUrl IS NOT NULL
                 AND (ImageDescription IS NULL OR LTRIM(RTRIM(ImageDescription)) = N''))
   AND EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE name = N'CK_QuestionOptions_ImageHasDescription'
                 AND parent_object_id = OBJECT_ID(N'Assessment.QuestionOptions')
                 AND is_not_trusted = 1)
    EXEC sys.sp_executesql N'
        ALTER TABLE Assessment.QuestionOptions WITH CHECK CHECK CONSTRAINT CK_QuestionOptions_ImageHasDescription;';

COMMIT TRANSACTION;
GO


/* ============================================================================
   Verification
   ========================================================================== */

-- Expect the new value lists only. is_not_trusted = 1 is expected ONLY for the
-- two ImageHasDescription constraints, and only while Part 3 printed questions.
SELECT OBJECT_NAME(parent_object_id) AS TableName, name, definition, is_not_trusted
FROM sys.check_constraints
WHERE parent_object_id IN (OBJECT_ID(N'Assessment.QuizAttemptQuestions'),
                           OBJECT_ID(N'Assessment.QuizAttemptEssayAnswers'),
                           OBJECT_ID(N'Assessment.Questions'),
                           OBJECT_ID(N'Assessment.QuestionOptions'))
ORDER BY TableName, name;

SELECT Status, AiOutcome, GradedBy, COUNT(*) AS Answers
FROM Assessment.QuizAttemptEssayAnswers
GROUP BY Status, AiOutcome, GradedBy
ORDER BY Status, AiOutcome, GradedBy;

-- Expect exactly one row: QuizAttemptQuestions.Points.
SELECT OBJECT_NAME(object_id) AS TableName, name
FROM sys.columns
WHERE (object_id = OBJECT_ID(N'Assessment.QuizAttemptQuestions') AND name = N'Points')
   OR (object_id = OBJECT_ID(N'Assessment.QuizAttemptEssayAnswers') AND name IN (N'AiProposedPoints', N'AiFeedback'));
GO
