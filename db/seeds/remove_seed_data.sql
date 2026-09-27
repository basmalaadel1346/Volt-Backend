/* ============================================================================
   remove_seed_data.sql — deletes everything seed_dev_data.sql added
   ----------------------------------------------------------------------------
   GENERATED FILE. Development databases only.

   Deletes, by the seed's reserved ids and user ids:
     * the seed users (their refresh tokens and reset codes go with them) and links,
     * the seed levels, lessons and content blocks (content types are kept),
     * the seed categories, topics, quizzes, questions, options and translations,
     * EVERY quiz attempt on a seed quiz or by a seed user — including attempts
       real accounts made on seed quizzes — with their answers, hints,
       placements, and the topic statistics of seed topics and seed users.
   Real users placed at a seed level keep their placement row (PlacedLevelId has
   no foreign key); the removal lists them first so you can decide.

   Run: sqlcmd -S <server> -d VoltDB -b -i db/seeds/remove_seed_data.sql
   ========================================================================== */

USE VoltDB;
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET NOCOUNT ON;
GO

SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @SeedUsers TABLE (Id UNIQUEIDENTIFIER PRIMARY KEY);
INSERT INTO @SeedUsers (Id) VALUES ('5EED0000-0000-4000-8000-000000000001'), ('5EED0000-0000-4000-8000-000000000002'), ('5EED0000-0000-4000-8000-000000000003'), ('5EED0000-0000-4000-8000-000000000011'), ('5EED0000-0000-4000-8000-000000000012'), ('5EED0000-0000-4000-8000-000000000013'), ('5EED0000-0000-4000-8000-000000000014'), ('5EED0000-0000-4000-8000-000000000015'), ('5EED0000-0000-4000-8000-000000000021');

DECLARE @Attempts TABLE (Id BIGINT PRIMARY KEY);
INSERT INTO @Attempts (Id)
SELECT Id FROM Assessment.QuizAttempts
WHERE QuizId BETWEEN 101 AND 199
   OR UserId IN (SELECT Id FROM @SeedUsers)
   OR Id BETWEEN 100001 AND 100999;

-- Retries of those attempts (made later by anyone) point at them: include them too.
WHILE 1 = 1
BEGIN
    INSERT INTO @Attempts (Id)
    SELECT a.Id FROM Assessment.QuizAttempts AS a
    WHERE a.PreviousAttemptId IN (SELECT Id FROM @Attempts)
      AND a.Id NOT IN (SELECT Id FROM @Attempts);
    IF @@ROWCOUNT = 0 BREAK;
END

SELECT UserId, QuizAttemptId, PlacedLevelId, PlacedAt AS KeptPlacementAtASeedLevel
FROM Assessment.UserPlacements
WHERE PlacedLevelId BETWEEN 101 AND 199
  AND QuizAttemptId NOT IN (SELECT Id FROM @Attempts);

DELETE FROM Assessment.QuestionHints
WHERE QuizAttemptId IN (SELECT Id FROM @Attempts) OR QuestionId BETWEEN 1001 AND 1099;
DELETE FROM Assessment.UserPlacements WHERE QuizAttemptId IN (SELECT Id FROM @Attempts);
DELETE FROM Assessment.UserTopicStats
WHERE TopicId BETWEEN 101 AND 199 OR UserId IN (SELECT Id FROM @SeedUsers)
   OR LastQuizAttemptId IN (SELECT Id FROM @Attempts);
DELETE FROM Assessment.QuizAttemptEssayAnswers WHERE QuizAttemptId IN (SELECT Id FROM @Attempts);
DELETE FROM Assessment.QuizAttemptMistakes WHERE QuizAttemptId IN (SELECT Id FROM @Attempts);
DELETE FROM Assessment.QuizAttemptQuestions WHERE QuizAttemptId IN (SELECT Id FROM @Attempts);

-- Newest first, so a retry is deleted before the attempt it points at.
DECLARE @Deleted INT = 1;
WHILE @Deleted > 0
BEGIN
    DELETE FROM Assessment.QuizAttempts
    WHERE Id IN (SELECT Id FROM @Attempts)
      AND Id NOT IN (SELECT PreviousAttemptId FROM Assessment.QuizAttempts WHERE PreviousAttemptId IS NOT NULL);
    SET @Deleted = @@ROWCOUNT;
END

DELETE FROM Assessment.QuestionOptionTranslations
WHERE QuestionOptionId IN (SELECT Id FROM Assessment.QuestionOptions WHERE QuestionId BETWEEN 1001 AND 1099);
DELETE FROM Assessment.QuestionOptions WHERE QuestionId BETWEEN 1001 AND 1099;
DELETE FROM Assessment.QuestionTranslations WHERE QuestionId BETWEEN 1001 AND 1099;
DELETE FROM Assessment.Questions WHERE Id BETWEEN 1001 AND 1099 OR QuizId BETWEEN 101 AND 199;
DELETE FROM Assessment.QuizTranslations WHERE QuizId BETWEEN 101 AND 199;
DELETE FROM Assessment.Quizzes WHERE Id BETWEEN 101 AND 199;
DELETE FROM Assessment.TopicTranslations WHERE TopicId BETWEEN 101 AND 199;
DELETE FROM Assessment.Topics WHERE Id BETWEEN 101 AND 199;
DELETE FROM Assessment.CategoryTranslations WHERE CategoryId BETWEEN 101 AND 199;
DELETE FROM Assessment.Categories WHERE Id BETWEEN 101 AND 199;

DELETE FROM LearningContent.LessonContents WHERE Id BETWEEN 10001 AND 10999 OR LessonId BETWEEN 1001 AND 1099;
DELETE FROM LearningContent.Lessons WHERE Id BETWEEN 1001 AND 1099;
DELETE FROM LearningContent.Levels WHERE Id BETWEEN 101 AND 199;

DELETE FROM Users.ParentChildLinks
WHERE ParentUserId IN (SELECT Id FROM @SeedUsers) OR ChildUserId IN (SELECT Id FROM @SeedUsers);
DELETE FROM Users.Users WHERE Id IN (SELECT Id FROM @SeedUsers);

COMMIT TRANSACTION;
PRINT N'seed: development data removed.';
GO
