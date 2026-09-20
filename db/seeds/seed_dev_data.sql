/* ============================================================================
   seed_dev_data.sql — fake data for development and frontend work
   ----------------------------------------------------------------------------
   GENERATED FILE. Fills every table the app reads with realistic Arabic/English
   content so the Child app, the Parent app and the Admin dashboard have data.

   Prerequisites (database VoltDB):
     * Users.* and LearningContent.* tables exist (the Users and Content modules).
     * The Assessment schema is current: db/migrations/000_AssessmentSchema.sql
       for an empty database, or 001_points_ai_essays_image_descriptions.sql then
       002_optional_topics_hint_level_uniqueness.sql for one built from an earlier 000.
     * The placeholder images live in ElectroWorld/wwwroot/uploads/lessons/seed-*.png
       (committed with this script).

   Accounts (log in with POST /api/auth/login):
     Admin     admin@volt.dev            Admin@Volt2026
     Parent    mona.parent@volt.dev      Demo@Volt2026   (children: Omar, Salma)
     Parent    khaled.parent@volt.dev    Demo@Volt2026   (child: Youssef)
     Child     omar@volt.dev             Demo@Volt2026   placed at level 2, quiz history, hints, retry available
     Child     salma@volt.dev            Demo@Volt2026   placed at level 2 (both mastered), one essay Pending, one NotGraded
     Child     youssef@volt.dev          Demo@Volt2026   brand new: placement Required
     Child     laila@volt.dev            Demo@Volt2026   not placed but has history: placement Optional
     Child     disabled.child@volt.dev   Demo@Volt2026   deactivated: login is refused
   These passwords are for development only. Never run this script in production.

   What it creates
     Users            9 users, 3 parent-child links (no refresh tokens or reset
                      codes: those are created by logging in / forgot-password)
     Content          2 levels, 7 lessons (6 published, 1 draft), 20 content blocks,
                      the content types Text / Image / TextAndImage if missing
     Assessment       2 categories, 6 topics (ar + en), 11 quizzes: 1 placement,
                      2 level final exams, 6 lesson quizzes, 1 lesson review,
                      1 standalone; 73 questions and 189 options with
                      ar + en translations, images with descriptions, points 1–3,
                      essays, one question with no topic, and one inactive draft question
     History          12 completed attempts, 13 wrong answers, 4 essay answers,
                      10 hints, 2 placements, 22 topic statistics rows

   Safety
     * Fixed ids in reserved ranges (levels/categories/topics/quizzes 101+, lessons
       and questions 1001+, content blocks and options 10001+, attempts 100001+,
       users 5EED0000-…), so it can sit next to data you already have.
     * Runs once: if the seed admin already exists it prints a message and stops.
     * All-or-nothing: one transaction. If any seed id, email or name is already
       taken by other data, it stops before writing and lists the conflicts.
     * db/seeds/remove_seed_data.sql removes everything this script added.

   Run: sqlcmd -S <server> -d VoltDB -b -i db/seeds/seed_dev_data.sql
   ========================================================================== */

USE VoltDB;
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET NOCOUNT ON;
GO

/* One batch from here to the end, so the checks below can stop the whole run. */

IF OBJECT_ID(N'Users.Users') IS NULL OR OBJECT_ID(N'Users.ParentChildLinks') IS NULL
   OR OBJECT_ID(N'LearningContent.Levels') IS NULL OR OBJECT_ID(N'LearningContent.Lessons') IS NULL
   OR OBJECT_ID(N'LearningContent.LessonContents') IS NULL OR OBJECT_ID(N'LearningContent.ContentTypes') IS NULL
   OR OBJECT_ID(N'Assessment.Quizzes') IS NULL OR OBJECT_ID(N'Assessment.UserPlacements') IS NULL
    THROW 50100, N'seed: a Users, LearningContent or Assessment table is missing. Create the schema first.', 1;

IF COL_LENGTH(N'Assessment.QuizAttemptQuestions', N'Points') IS NULL
   OR NOT EXISTS (SELECT 1 FROM sys.check_constraints
                  WHERE name = N'CK_QuizAttemptEssayAnswers_Status' AND definition LIKE N'%NotGraded%')
    THROW 50101, N'seed: the Assessment schema is out of date. Run db/migrations/001_points_ai_essays_image_descriptions.sql first.', 1;

IF COLUMNPROPERTY(OBJECT_ID(N'Assessment.Questions'), N'TopicId', 'AllowsNull') = 0
   OR NOT EXISTS (SELECT 1 FROM sys.indexes
                  WHERE object_id = OBJECT_ID(N'Assessment.QuestionHints')
                    AND name = N'UQ_QuestionHints_AttemptId_QuestionId_AttemptNumber')
    THROW 50103, N'seed: the Assessment schema is out of date. Run db/migrations/002_optional_topics_hint_level_uniqueness.sql first.', 1;

IF EXISTS (SELECT 1 FROM Users.Users WHERE Id = '5EED0000-0000-4000-8000-000000000001')
BEGIN
    PRINT N'seed: the development data is already there (admin@volt.dev exists). Nothing to do.';
    RETURN;
END

/* ---- Conflicts: anything in the seed's id ranges or with its unique names ---- */
DECLARE @Conflicts NVARCHAR(MAX) = N'';

SELECT @Conflicts += N'Users.Users: ' + ISNULL(Email, CONVERT(NVARCHAR(36), Id)) + NCHAR(10)
FROM Users.Users
WHERE Id IN ('5EED0000-0000-4000-8000-000000000001', '5EED0000-0000-4000-8000-000000000002', '5EED0000-0000-4000-8000-000000000003', '5EED0000-0000-4000-8000-000000000011', '5EED0000-0000-4000-8000-000000000012', '5EED0000-0000-4000-8000-000000000013', '5EED0000-0000-4000-8000-000000000014', '5EED0000-0000-4000-8000-000000000015', '5EED0000-0000-4000-8000-000000000021')
   OR Email IN (N'admin@volt.dev', N'mona.parent@volt.dev', N'khaled.parent@volt.dev', N'omar@volt.dev', N'salma@volt.dev', N'youssef@volt.dev', N'laila@volt.dev', N'disabled.child@volt.dev');

SELECT @Conflicts += N'LearningContent.Levels id ' + CONVERT(NVARCHAR(12), Id) + NCHAR(10)
FROM LearningContent.Levels WHERE Id BETWEEN 101 AND 199;
SELECT @Conflicts += N'LearningContent.Lessons id ' + CONVERT(NVARCHAR(12), Id) + NCHAR(10)
FROM LearningContent.Lessons WHERE Id BETWEEN 1001 AND 1099;
SELECT @Conflicts += N'LearningContent.LessonContents id ' + CONVERT(NVARCHAR(12), Id) + NCHAR(10)
FROM LearningContent.LessonContents WHERE Id BETWEEN 10001 AND 10999;
SELECT @Conflicts += N'Assessment.Categories: ' + Name + NCHAR(10)
FROM Assessment.Categories WHERE Id BETWEEN 101 AND 199 OR Name IN (N'أساسيات الكهرباء', N'المكونات الإلكترونية');
SELECT @Conflicts += N'Assessment.Topics: ' + Name + NCHAR(10)
FROM Assessment.Topics WHERE Id BETWEEN 101 AND 199 OR Name IN (N'مفهوم الكهرباء', N'الدائرة الكهربية', N'السلامة الكهربية', N'الثنائي الباعث للضوء', N'المقاومة', N'زر الضغط');
SELECT @Conflicts += N'Assessment.Quizzes id ' + CONVERT(NVARCHAR(12), Id) + NCHAR(10)
FROM Assessment.Quizzes WHERE Id BETWEEN 101 AND 199;
SELECT @Conflicts += N'Assessment.Questions id ' + CONVERT(NVARCHAR(12), Id) + NCHAR(10)
FROM Assessment.Questions WHERE Id BETWEEN 1001 AND 1099;
SELECT @Conflicts += N'Assessment.QuestionOptions id ' + CONVERT(NVARCHAR(12), Id) + NCHAR(10)
FROM Assessment.QuestionOptions WHERE Id BETWEEN 10001 AND 10999;
SELECT @Conflicts += N'Assessment.QuizAttempts id ' + CONVERT(NVARCHAR(20), Id) + NCHAR(10)
FROM Assessment.QuizAttempts WHERE Id BETWEEN 100001 AND 100999;
SELECT @Conflicts += N'Assessment.QuizAttemptMistakes id ' + CONVERT(NVARCHAR(20), Id) + NCHAR(10)
FROM Assessment.QuizAttemptMistakes WHERE Id BETWEEN 100001 AND 100999;

IF @Conflicts <> N''
BEGIN
    DECLARE @Message NVARCHAR(2047) = LEFT(N'seed: stopped before writing anything; these rows already exist:' + NCHAR(10) + @Conflicts, 2047);
    THROW 50102, @Message, 1;
END

SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @Now DATETIME2(3) = SYSUTCDATETIME();

/* ============================== Users ============================== */
INSERT INTO Users.Users (Id, Email, PasswordHash, FullName, Role, AuthProvider, ProviderUserId, Age, IsActive, ConvertedFromGuestAt, CreatedAt)
VALUES
    ('5EED0000-0000-4000-8000-000000000001', N'admin@volt.dev', N'$2a$11$BiMMjyJU2jyoP4Sa02jUyulTGsYJzgfurlZpMKb.8GUxqMio/rlb6', N'مدير النظام', N'Admin', N'Email', NULL, NULL, 1, NULL, DATEADD(MINUTE, -86400, @Now)),
    ('5EED0000-0000-4000-8000-000000000002', N'mona.parent@volt.dev', N'$2a$11$64syxQcDRXh2miTFvqiEvOX9IUwux9i2oS.m.RKwY2v60uGCjGD.q', N'منى عبد الرحمن', N'Parent', N'Email', NULL, NULL, 1, NULL, DATEADD(MINUTE, -64800, @Now)),
    ('5EED0000-0000-4000-8000-000000000003', N'khaled.parent@volt.dev', N'$2a$11$64syxQcDRXh2miTFvqiEvOX9IUwux9i2oS.m.RKwY2v60uGCjGD.q', N'خالد محمود', N'Parent', N'Email', NULL, NULL, 1, NULL, DATEADD(MINUTE, -57600, @Now)),
    ('5EED0000-0000-4000-8000-000000000011', N'omar@volt.dev', N'$2a$11$64syxQcDRXh2miTFvqiEvOX9IUwux9i2oS.m.RKwY2v60uGCjGD.q', N'عمر ياسر', N'Child', N'Email', NULL, 9, 1, NULL, DATEADD(MINUTE, -63360, @Now)),
    ('5EED0000-0000-4000-8000-000000000012', N'salma@volt.dev', N'$2a$11$64syxQcDRXh2miTFvqiEvOX9IUwux9i2oS.m.RKwY2v60uGCjGD.q', N'سلمى ياسر', N'Child', N'Email', NULL, 11, 1, NULL, DATEADD(MINUTE, -63360, @Now)),
    ('5EED0000-0000-4000-8000-000000000013', N'youssef@volt.dev', N'$2a$11$64syxQcDRXh2miTFvqiEvOX9IUwux9i2oS.m.RKwY2v60uGCjGD.q', N'يوسف خالد', N'Child', N'Email', NULL, 8, 1, NULL, DATEADD(MINUTE, -56160, @Now)),
    ('5EED0000-0000-4000-8000-000000000014', N'laila@volt.dev', N'$2a$11$64syxQcDRXh2miTFvqiEvOX9IUwux9i2oS.m.RKwY2v60uGCjGD.q', N'ليلى حسن', N'Child', N'Email', NULL, 12, 1, NULL, DATEADD(MINUTE, -43200, @Now)),
    ('5EED0000-0000-4000-8000-000000000015', N'disabled.child@volt.dev', N'$2a$11$64syxQcDRXh2miTFvqiEvOX9IUwux9i2oS.m.RKwY2v60uGCjGD.q', N'حساب موقوف', N'Child', N'Email', NULL, 10, 0, NULL, DATEADD(MINUTE, -36000, @Now)),
    ('5EED0000-0000-4000-8000-000000000021', NULL, NULL, N'ضيف', N'Child', N'Guest', NULL, NULL, 1, NULL, DATEADD(MINUTE, -2880, @Now));

INSERT INTO Users.ParentChildLinks (Id, ParentUserId, ChildUserId, CreatedAt)
VALUES
    ('5EED0000-0000-4000-8000-000000000101', '5EED0000-0000-4000-8000-000000000002', '5EED0000-0000-4000-8000-000000000011', DATEADD(MINUTE, -54720, @Now)),
    ('5EED0000-0000-4000-8000-000000000102', '5EED0000-0000-4000-8000-000000000002', '5EED0000-0000-4000-8000-000000000012', DATEADD(MINUTE, -54720, @Now)),
    ('5EED0000-0000-4000-8000-000000000103', '5EED0000-0000-4000-8000-000000000003', '5EED0000-0000-4000-8000-000000000013', DATEADD(MINUTE, -54720, @Now));

/* ============================== Content ============================== */
-- Content types are shared reference data: added only when missing, found by name.
INSERT INTO LearningContent.ContentTypes (Name)
SELECT v.Name FROM (VALUES (N'Text'), (N'Image'), (N'TextAndImage')) AS v (Name)
WHERE NOT EXISTS (SELECT 1 FROM LearningContent.ContentTypes AS ct WHERE ct.Name = v.Name);

DECLARE @CtText INT = (SELECT Id FROM LearningContent.ContentTypes WHERE Name = N'Text');
DECLARE @CtImage INT = (SELECT Id FROM LearningContent.ContentTypes WHERE Name = N'Image');
DECLARE @CtTextAndImage INT = (SELECT Id FROM LearningContent.ContentTypes WHERE Name = N'TextAndImage');

-- Seed levels go after any level that already exists.
DECLARE @LevelOrderBase INT = ISNULL((SELECT MAX([Order]) FROM LearningContent.Levels), 0);

SET IDENTITY_INSERT LearningContent.Levels ON;
INSERT INTO LearningContent.Levels (Id, Title, Description, [Order])
VALUES
    (101, N'المستوى الأول: أساسيات الكهرباء', N'اكتشف ما هي الكهرباء، وكيف تعمل الدائرة الكهربية، وكيف تتعامل مع الكهرباء بأمان.', @LevelOrderBase + 1),
    (102, N'المستوى الثاني: المكونات الإلكترونية', N'تعرّف على الثنائي الباعث للضوء (LED) والمقاومة وزر الضغط، وابنِ دائرة آمنة.', @LevelOrderBase + 2);
SET IDENTITY_INSERT LearningContent.Levels OFF;

SET IDENTITY_INSERT LearningContent.Lessons ON;
INSERT INTO LearningContent.Lessons (Id, LevelId, Title, Description, SortOrder, IsPublished, CreatedAt)
VALUES
    (1001, 101, N'ما هي الكهرباء؟', N'نكتشف معًا الطاقة التي تشغّل ألعابنا وأجهزتنا كل يوم.', 1, 1, DATEADD(MINUTE, -72000, @Now)),
    (1002, 101, N'الدائرة الكهربية', N'بطارية وأسلاك ومصباح ومفتاح: كيف تسير الكهرباء؟', 2, 1, DATEADD(MINUTE, -70560, @Now)),
    (1003, 101, N'السلامة الكهربية', N'قواعد مهمة تحمينا ونحن نستخدم الكهرباء.', 3, 1, DATEADD(MINUTE, -69120, @Now)),
    (1004, 102, N'الثنائي الباعث للضوء (LED)', N'مصباح صغير ملوّن يستهلك طاقة قليلة جدًا.', 1, 1, DATEADD(MINUTE, -67680, @Now)),
    (1005, 102, N'المقاومة', N'مطبّ صغير يهدّئ الإلكترونات ويحمي المكونات.', 2, 1, DATEADD(MINUTE, -66240, @Now)),
    (1006, 102, N'زر الضغط', N'نضغط فتضيء الدائرة، ونرفع أيدينا فتنطفئ.', 3, 1, DATEADD(MINUTE, -64800, @Now)),
    (1007, 102, N'المكثف (مسودة)', N'درس قيد الإعداد — غير منشور بعد.', 4, 0, DATEADD(MINUTE, -63360, @Now));
SET IDENTITY_INSERT LearningContent.Lessons OFF;

SET IDENTITY_INSERT LearningContent.LessonContents ON;
INSERT INTO LearningContent.LessonContents (Id, LessonId, ContentTypeId, Content, MediaUrl, SortOrder)
VALUES
    (10001, 1001, @CtText, N'الكهرباء طاقة تنتج عن حركة شحنات صغيرة جدًا اسمها الإلكترونات داخل الأسلاك. البطارية مصدر صغير وآمن للكهرباء في ألعابنا، أما الكهرباء القوية التي تصل إلى بيوتنا فتُنتَج في محطات توليد الكهرباء.', NULL, 1),
    (10002, 1001, @CtImage, NULL, N'/uploads/lessons/seed-lesson-electricity.png', 2),
    (10003, 1001, @CtTextAndImage, N'لا نستطيع رؤية الكهرباء بأعيننا، لكننا نرى آثارها: فهي تتحول إلى ضوء في المصباح، وحرارة في المكواة، وصوت في التلفاز. والبرق في السماء نوع من الكهرباء الطبيعية القوية.', N'/uploads/lessons/seed-lesson-electricity.png', 3),
    (10004, 1002, @CtText, N'الدائرة الكهربية طريق مغلق تسير فيه الإلكترونات. تتكون أبسط دائرة من بطارية تمدّها بالطاقة، وأسلاك نحاسية تنقل الكهرباء، ومصباح يضيء.', NULL, 1),
    (10005, 1002, @CtImage, NULL, N'/uploads/lessons/seed-lesson-circuit.png', 2),
    (10006, 1002, @CtTextAndImage, N'عندما تكون الدائرة مغلقة تمر الكهرباء فيضيء المصباح، وإذا انقطع السلك أو فتحنا المفتاح تصبح الدائرة مفتوحة فتتوقف الكهرباء وينطفئ المصباح.', N'/uploads/lessons/seed-lesson-circuit.png', 3),
    (10007, 1003, @CtText, N'الماء يوصل الكهرباء بسرعة، لذلك لا نلمس المقبس أو الأجهزة الكهربية بأيدٍ مبللة أبدًا، ولا ندخل أي جسم معدني في المقبس.', NULL, 1),
    (10008, 1003, @CtImage, NULL, N'/uploads/lessons/seed-lesson-safety.png', 2),
    (10009, 1003, @CtTextAndImage, N'تُغطّى الأسلاك بالبلاستيك لأنه مادة عازلة تحمينا. إذا رأيت سلكًا مكشوفًا فابتعد عنه وأخبر شخصًا كبيرًا، واترك إصلاح الأعطال للكهربائي المختص.', N'/uploads/lessons/seed-lesson-safety.png', 3),
    (10010, 1004, @CtText, N'الـ LED اختصار لعبارة Light-Emitting Diode أي الثنائي الباعث للضوء. نراه في شاشات التلفاز وإشارات المرور وأضواء الألعاب، ويستهلك طاقة قليلة جدًا ولا يسخن كثيرًا.', NULL, 1),
    (10011, 1004, @CtImage, NULL, N'/uploads/lessons/seed-lesson-led.png', 2),
    (10012, 1004, @CtTextAndImage, N'للـ LED رجل طويلة هي الطرف الموجب (الأنود +) ورجل قصيرة هي الطرف السالب (الكاثود −)، لأن الكهرباء تمر فيه في اتجاه واحد فقط. إذا وصّلناه بالعكس لا يضيء.', N'/uploads/lessons/seed-lesson-led.png', 3),
    (10013, 1005, @CtText, N'المقاومة تنظّم مرور الكهرباء في الدائرة وتحمي المكونات الحساسة مثل الـ LED من التيار القوي. وتُقاس بوحدة الأوم (Ω)، وكلما زادت قيمتها قلّت حركة الإلكترونات.', NULL, 1),
    (10014, 1005, @CtImage, NULL, N'/uploads/lessons/seed-lesson-resistor.png', 2),
    (10015, 1005, @CtTextAndImage, N'الحلقات الملوّنة على جسم المقاومة تحدد قيمتها. ويمكن توصيل المقاومة في أي اتجاه، وهي تحوّل الطاقة الزائدة إلى حرارة خفيفة.', N'/uploads/lessons/seed-lesson-resistor.png', 3),
    (10016, 1006, @CtText, N'زر الضغط يغلق الدائرة فقط ما دمنا نضغط عليه، وبداخله زنبرك يعيده إلى مكانه بمجرد أن نرفع أيدينا فتنفتح الدائرة وتتوقف الكهرباء.', NULL, 1),
    (10017, 1006, @CtImage, NULL, N'/uploads/lessons/seed-lesson-button.png', 2),
    (10018, 1006, @CtTextAndImage, N'نستخدم أزرار الضغط في أذرع ألعاب الفيديو ولوحات المفاتيح، وهي توفّر طاقة البطارية لأنها تسمح بمرور الكهرباء عند الحاجة فقط.', N'/uploads/lessons/seed-lesson-button.png', 3),
    (10019, 1007, @CtText, N'المكثف يخزّن الشحنة الكهربية لفترة قصيرة ثم يفرّغها.', NULL, 1),
    (10020, 1007, @CtImage, NULL, N'/uploads/lessons/seed-lesson-capacitor.png', 2);
SET IDENTITY_INSERT LearningContent.LessonContents OFF;

/* ============================== Assessment: classification ============================== */
INSERT INTO Assessment.Languages (Code, Name)
SELECT v.Code, v.Name FROM (VALUES (N'ar', N'Arabic'), (N'en', N'English')) AS v (Code, Name)
WHERE NOT EXISTS (SELECT 1 FROM Assessment.Languages AS l WHERE l.Code = v.Code);

INSERT INTO Assessment.Categories (Id, Name, SortOrder, IsActive)
VALUES
    (101, N'أساسيات الكهرباء', 1, 1),
    (102, N'المكونات الإلكترونية', 2, 1);

INSERT INTO Assessment.CategoryTranslations (CategoryId, LanguageCode, Name)
VALUES
    (101, N'ar', N'أساسيات الكهرباء'),
    (101, N'en', N'Electricity basics'),
    (102, N'ar', N'المكونات الإلكترونية'),
    (102, N'en', N'Electronic components');

SET IDENTITY_INSERT Assessment.Topics ON;
INSERT INTO Assessment.Topics (Id, Name, Description, CategoryId, LearningLevel, IsActive, CreatedAt)
VALUES
    (101, N'مفهوم الكهرباء', N'الإلكترونات ومصادر الكهرباء وتحوّلها إلى ضوء وحرارة وصوت.', 101, N'Beginner', 1, DATEADD(MINUTE, -79200, @Now)),
    (102, N'الدائرة الكهربية', N'الدائرة المغلقة والمفتوحة، والبطارية والأسلاك والمفتاح.', 101, N'Beginner', 1, DATEADD(MINUTE, -79200, @Now)),
    (103, N'السلامة الكهربية', N'كيف نتعامل مع الكهرباء بأمان في البيت.', 101, N'Beginner', 1, DATEADD(MINUTE, -79200, @Now)),
    (104, N'الثنائي الباعث للضوء', N'كيف يعمل الـ LED ولماذا له رجل طويلة ورجل قصيرة.', 102, N'Intermediate', 1, DATEADD(MINUTE, -79200, @Now)),
    (105, N'المقاومة', N'لماذا نحتاج المقاومة في الدائرة وكيف نقرأ قيمتها.', 102, N'Intermediate', 1, DATEADD(MINUTE, -79200, @Now)),
    (106, N'زر الضغط', N'كيف يتحكم زر الضغط في الدائرة ويوفّر طاقة البطارية.', 102, N'Intermediate', 1, DATEADD(MINUTE, -79200, @Now));
SET IDENTITY_INSERT Assessment.Topics OFF;

INSERT INTO Assessment.TopicTranslations (TopicId, LanguageCode, Name, Description)
VALUES
    (101, N'ar', N'مفهوم الكهرباء', N'الإلكترونات ومصادر الكهرباء وتحوّلها إلى ضوء وحرارة وصوت.'),
    (101, N'en', N'What is electricity', N'Electrons, where electricity comes from, and how it turns into light, heat and sound.'),
    (102, N'ar', N'الدائرة الكهربية', N'الدائرة المغلقة والمفتوحة، والبطارية والأسلاك والمفتاح.'),
    (102, N'en', N'The electric circuit', N'Closed and open circuits, batteries, wires and switches.'),
    (103, N'ar', N'السلامة الكهربية', N'كيف نتعامل مع الكهرباء بأمان في البيت.'),
    (103, N'en', N'Electrical safety', N'How to use electricity safely at home.'),
    (104, N'ar', N'الثنائي الباعث للضوء', N'كيف يعمل الـ LED ولماذا له رجل طويلة ورجل قصيرة.'),
    (104, N'en', N'The LED', N'How an LED works and why it has a long leg and a short leg.'),
    (105, N'ar', N'المقاومة', N'لماذا نحتاج المقاومة في الدائرة وكيف نقرأ قيمتها.'),
    (105, N'en', N'The resistor', N'Why a circuit needs a resistor and how to read its value.'),
    (106, N'ar', N'زر الضغط', N'كيف يتحكم زر الضغط في الدائرة ويوفّر طاقة البطارية.'),
    (106, N'en', N'The push button', N'How a push button controls a circuit and saves battery power.');

/* ============================== Assessment: quizzes and questions ============================== */
-- Only one placement test may be active. If one already exists, the seed's copy is
-- added inactive and the existing one keeps serving (it samples every level anyway).
DECLARE @PlacementActive BIT = CASE WHEN EXISTS (SELECT 1 FROM Assessment.Quizzes
                                                 WHERE QuizType = N'Placement' AND IsActive = 1) THEN 0 ELSE 1 END;

SET IDENTITY_INSERT Assessment.Quizzes ON;
INSERT INTO Assessment.Quizzes (Id, Title, Description, QuizType, LevelId, LessonId, IsActive, CreatedAt)
VALUES
    (101, N'اختبار تحديد المستوى', N'اختبار قصير يحدد المستوى المناسب لك لتبدأ منه.', N'Placement', NULL, NULL, @PlacementActive, DATEADD(MINUTE, -74880, @Now)),
    (102, N'الاختبار النهائي للمستوى الأول', N'اختبر كل ما تعلمته عن الكهرباء والدائرة الكهربية والسلامة.', N'LevelAssessment', 101, NULL, 1, DATEADD(MINUTE, -74880, @Now)),
    (103, N'الاختبار النهائي للمستوى الثاني', N'هل تعرف الـ LED والمقاومة وزر الضغط جيدًا؟', N'LevelAssessment', 102, NULL, 1, DATEADD(MINUTE, -74880, @Now)),
    (104, N'اختبار درس: ما هي الكهرباء؟', N'ثمانية أسئلة عن الكهرباء ومصادرها وآثارها.', N'LessonQuiz', NULL, 1001, 1, DATEADD(MINUTE, -74880, @Now)),
    (105, N'اختبار درس: الدائرة الكهربية', N'متى تكون الدائرة مغلقة؟ ومتى تتوقف الكهرباء؟', N'LessonQuiz', NULL, 1002, 1, DATEADD(MINUTE, -74880, @Now)),
    (106, N'اختبار درس: السلامة الكهربية', N'هل تعرف كيف تحمي نفسك من خطر الكهرباء؟', N'LessonQuiz', NULL, 1003, 1, DATEADD(MINUTE, -74880, @Now)),
    (107, N'اختبار درس: الثنائي الباعث للضوء (LED)', N'كل ما تحتاج معرفته عن الـ LED.', N'LessonQuiz', NULL, 1004, 1, DATEADD(MINUTE, -74880, @Now)),
    (108, N'اختبار درس: المقاومة', N'لماذا نحتاج المقاومة؟ وكيف نقرأ قيمتها؟', N'LessonQuiz', NULL, 1005, 1, DATEADD(MINUTE, -74880, @Now)),
    (109, N'اختبار درس: زر الضغط', N'كيف يتحكم زر الضغط في الدائرة؟', N'LessonQuiz', NULL, 1006, 1, DATEADD(MINUTE, -74880, @Now)),
    (110, N'مراجعة: الدائرة الكهربية', N'راجع ما تعلمته بسؤال بالصور وسؤال تكتب إجابته بنفسك.', N'LessonReview', NULL, 1002, 1, DATEADD(MINUTE, -74880, @Now)),
    (111, N'تحدي الكهرباء', N'أسئلة متنوعة بالصور وسؤال تكتب إجابته، لكل المستويات.', N'Standalone', NULL, NULL, 1, DATEADD(MINUTE, -74880, @Now));
SET IDENTITY_INSERT Assessment.Quizzes OFF;

INSERT INTO Assessment.QuizTranslations (QuizId, LanguageCode, Title, Description)
VALUES
    (101, N'ar', N'اختبار تحديد المستوى', N'اختبار قصير يحدد المستوى المناسب لك لتبدأ منه.'),
    (101, N'en', N'Placement test', N'A short test that finds the right level for you to start at.'),
    (102, N'ar', N'الاختبار النهائي للمستوى الأول', N'اختبر كل ما تعلمته عن الكهرباء والدائرة الكهربية والسلامة.'),
    (102, N'en', N'Level 1 final exam', N'Test everything you learned about electricity, circuits and safety.'),
    (103, N'ar', N'الاختبار النهائي للمستوى الثاني', N'هل تعرف الـ LED والمقاومة وزر الضغط جيدًا؟'),
    (103, N'en', N'Level 2 final quiz', N'Do you know the LED, the resistor and the push button well?'),
    (104, N'ar', N'اختبار درس: ما هي الكهرباء؟', N'ثمانية أسئلة عن الكهرباء ومصادرها وآثارها.'),
    (104, N'en', N'Lesson quiz: What is electricity?', N'Eight questions about electricity, where it comes from and what it does.'),
    (105, N'ar', N'اختبار درس: الدائرة الكهربية', N'متى تكون الدائرة مغلقة؟ ومتى تتوقف الكهرباء؟'),
    (105, N'en', N'Lesson quiz: The electric circuit', N'When is a circuit closed, and when does electricity stop?'),
    (106, N'ar', N'اختبار درس: السلامة الكهربية', N'هل تعرف كيف تحمي نفسك من خطر الكهرباء؟'),
    (106, N'en', N'Lesson quiz: Electrical safety', N'Do you know how to stay safe around electricity?'),
    (107, N'ar', N'اختبار درس: الثنائي الباعث للضوء (LED)', N'كل ما تحتاج معرفته عن الـ LED.'),
    (107, N'en', N'Lesson quiz: The LED (Light-Emitting Diode)', N'Everything you need to know about the LED.'),
    (108, N'ar', N'اختبار درس: المقاومة', N'لماذا نحتاج المقاومة؟ وكيف نقرأ قيمتها؟'),
    (108, N'en', N'Lesson quiz: The resistor', N'Why do we need a resistor, and how do we read its value?'),
    (109, N'ar', N'اختبار درس: زر الضغط', N'كيف يتحكم زر الضغط في الدائرة؟'),
    (109, N'en', N'Lesson quiz: The push button', N'How does a push button control a circuit?'),
    (110, N'ar', N'مراجعة: الدائرة الكهربية', N'راجع ما تعلمته بسؤال بالصور وسؤال تكتب إجابته بنفسك.'),
    (110, N'en', N'Review: The electric circuit', N'Review what you learned with a picture question and a written answer.'),
    (111, N'ar', N'تحدي الكهرباء', N'أسئلة متنوعة بالصور وسؤال تكتب إجابته، لكل المستويات.'),
    (111, N'en', N'Electricity challenge', N'Mixed questions with pictures and a written answer, for every level.');

SET IDENTITY_INSERT Assessment.Questions ON;
INSERT INTO Assessment.Questions (Id, QuizId, TopicId, QuestionText, QuestionType, ImageUrl, ImageDescription, Difficulty, DisplayOrder, Points, IsActive, CreatedAt)
VALUES
    (1001, 102, 101, N'ما اسم الشحنات الصغيرة جدًا التي تتحرك في الأسلاك وتولّد الكهرباء؟', N'MultipleChoice', NULL, NULL, N'Medium', 1, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1002, 102, 101, N'من أين تأتي الكهرباء القوية التي تضيء بيوتنا وتشغّل الثلاجة؟', N'MultipleChoice', NULL, NULL, N'Medium', 2, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1003, 102, 101, N'عندما نوصّل التلفاز بالكهرباء، تتحول الطاقة الكهربية إلى...', N'MultipleChoice', NULL, NULL, N'Medium', 3, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1004, 102, 103, N'لماذا يُمنع تمامًا لمس المقبس الكهربي بأيدٍ مبللة؟', N'MultipleChoice', NULL, NULL, N'Medium', 4, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1005, 102, 102, N'ما دور البطارية الصغيرة في ألعابنا؟', N'MultipleChoice', NULL, NULL, N'Medium', 5, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1006, 102, 102, N'إذا انقطع السلك في الدائرة الكهربية يظل المصباح مضيئًا بشكل طبيعي.', N'TrueFalse', NULL, NULL, N'Medium', 6, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1007, 102, 102, N'أي مكوّن نستخدمه لفتح الدائرة (إطفائها) أو غلقها (تشغيلها)؟', N'MultipleChoice', NULL, NULL, N'Medium', 7, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1008, 102, 103, N'لماذا تُغطّى الأسلاك الكهربية من الخارج بطبقة من البلاستيك؟', N'MultipleChoice', NULL, NULL, N'Medium', 8, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1009, 103, 104, N'أي مكوّن يضيء بألوان مبهجة ويستهلك طاقة كهربية قليلة جدًا؟', N'MultipleChoice', NULL, NULL, N'Medium', 1, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1010, 103, 105, N'أي مكوّن يعمل مثل "مطبّ صناعي" يبطئ الإلكترونات ليحمي المكونات الحساسة؟', N'MultipleChoice', NULL, NULL, N'Medium', 2, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1011, 103, 104, N'الرجل الطويلة للـ LED توصَّل دائمًا بالطرف السالب (−) للبطارية.', N'TrueFalse', NULL, NULL, N'Medium', 3, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1012, 103, 106, N'يسمح زر الضغط بمرور الكهرباء في الدائرة...', N'MultipleChoice', NULL, NULL, N'Medium', 4, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1013, 103, 105, N'كيف يعرف المهندسون قيمة المقاومة الكهربية؟', N'MultipleChoice', NULL, NULL, N'Medium', 5, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1014, 103, 105, N'يمكن توصيل المقاومة في الدائرة الكهربية في أي اتجاه دون القلق من الطرفين الموجب والسالب.', N'TrueFalse', NULL, NULL, N'Medium', 6, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1015, 103, 106, N'لماذا يُفضَّل استخدام زر الضغط في الدوائر الإلكترونية؟', N'MultipleChoice', NULL, NULL, N'Medium', 7, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1016, 103, NULL, N'ما المكونات الصحيحة لبناء دائرة آمنة تضيء LED باستخدام زر تحكم؟', N'MultipleChoice', NULL, NULL, N'Medium', 8, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1017, 104, 101, N'من أين تأتي الكهرباء التي تشغّل ألعابنا؟', N'MultipleChoice', NULL, NULL, N'Easy', 1, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1018, 104, 101, N'البرق في السماء نوع من الكهرباء الطبيعية القوية.', N'TrueFalse', NULL, NULL, N'Easy', 2, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1019, 104, 101, N'ماذا نسمي الأشياء التي تحتاج إلى الكهرباء لتعمل؟', N'MultipleChoice', NULL, NULL, N'Easy', 3, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1020, 104, 101, N'البطارية مصدر صغير وآمن للكهرباء في ألعابنا.', N'TrueFalse', NULL, NULL, N'Easy', 4, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1021, 104, 101, N'ما اسم الشحنات التي تتحرك بسرعة لتصنع الكهرباء؟', N'MultipleChoice', NULL, NULL, N'Easy', 5, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1022, 104, 101, N'نستطيع أن نرى الكهرباء بأعيننا بوضوح وهي تتحرك داخل الأسلاك.', N'TrueFalse', NULL, NULL, N'Easy', 6, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1023, 104, 101, N'أين تُنتَج الكهرباء القوية التي تصل إلى بيوتنا؟', N'MultipleChoice', NULL, NULL, N'Easy', 7, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1024, 104, 101, N'يمكن أن تتحول الطاقة الكهربية إلى...', N'MultipleChoice', NULL, NULL, N'Easy', 8, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1025, 105, 102, N'ماذا يحتاج المصباح ليضيء؟', N'MultipleChoice', NULL, NULL, N'Easy', 1, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1026, 105, 102, N'أيٌّ مما يلي دائرة مفتوحة (لا تمر فيها الكهرباء)؟', N'MultipleChoice', NULL, NULL, N'Easy', 2, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1027, 105, 102, N'ما وظيفة المفتاح الكهربي؟', N'MultipleChoice', NULL, NULL, N'Easy', 3, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1028, 105, 102, N'يضيء المصباح حتى لو لم تكن البطارية موجودة في الدائرة.', N'TrueFalse', NULL, NULL, N'Easy', 4, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1029, 105, 102, N'ما وظيفة السلك النحاسي في الدائرة الكهربية؟', N'MultipleChoice', NULL, NULL, N'Easy', 5, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1030, 105, 102, N'عندما تكون الدائرة "مغلقة" يكون الطريق كاملًا وتمر الكهرباء بنجاح.', N'TrueFalse', NULL, NULL, N'Easy', 6, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1031, 105, 102, N'ماذا يحدث عندما نطفئ المفتاح الكهربي (OFF)؟', N'MultipleChoice', NULL, NULL, N'Easy', 7, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1032, 105, 102, N'المكوّن الذي يمدّ الدائرة الكهربية البسيطة بالطاقة هو...', N'MultipleChoice', NULL, NULL, N'Easy', 8, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1033, 106, 103, N'هل من الآمن لمس المقبس الكهربي بأيدٍ مبللة؟', N'MultipleChoice', NULL, NULL, N'Easy', 1, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1034, 106, 103, N'ماذا نفعل إذا وجدنا سلكًا كهربيًا مكشوفًا؟', N'MultipleChoice', NULL, NULL, N'Easy', 2, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1035, 106, 103, N'إدخال الأجسام المعدنية، مثل المسمار، في المقبس الكهربي تصرّف آمن.', N'TrueFalse', NULL, NULL, N'Easy', 3, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1036, 106, 103, N'لماذا تُغطّى الأسلاك الكهربية بطبقة من البلاستيك؟', N'MultipleChoice', NULL, NULL, N'Easy', 4, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1037, 106, 103, N'يجب إطفاء الأجهزة وفصلها عن الكهرباء إذا لم نستخدمها لفترة طويلة.', N'TrueFalse', NULL, NULL, N'Easy', 5, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1038, 106, 103, N'عند فصل جهاز عن الكهرباء، يجب أن نمسك...', N'MultipleChoice', NULL, NULL, N'Easy', 6, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1039, 106, 103, N'شدّ السلك بقوة لفصله عن الكهرباء هو التصرف الصحيح.', N'TrueFalse', NULL, NULL, N'Easy', 7, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1040, 106, 103, N'من الشخص المناسب لإصلاح الأعطال الكهربية في البيت؟', N'MultipleChoice', NULL, NULL, N'Easy', 8, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1041, 107, 104, N'ماذا يعني الاختصار "LED" في عالم الإلكترونيات؟', N'MultipleChoice', NULL, NULL, N'Easy', 1, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1042, 107, 104, N'لماذا للـ LED رجل طويلة ورجل قصيرة؟', N'MultipleChoice', NULL, NULL, N'Easy', 2, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1043, 107, 104, N'ماذا يحدث إذا وصّلنا الـ LED بالعكس في الدائرة الكهربية؟', N'MultipleChoice', NULL, NULL, N'Easy', 3, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1044, 107, 104, N'الـ LED صديق للبيئة لأنه يستهلك طاقة كهربية قليلة جدًا مقارنة بالمصابيح القديمة.', N'TrueFalse', NULL, NULL, N'Easy', 4, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1045, 107, 104, N'الرجل الطويلة للـ LED تمثّل...', N'MultipleChoice', NULL, NULL, N'Easy', 5, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1046, 107, 104, N'الـ LED يصدر حرارة عالية جدًا، وقد تحترق يدك إذا لمسته وهو مضيء.', N'TrueFalse', NULL, NULL, N'Easy', 6, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1047, 107, 104, N'نرى الـ LED في حياتنا اليومية في...', N'MultipleChoice', NULL, NULL, N'Easy', 7, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1048, 107, 104, N'الـ LED يحتاج إلى طاقة هائلة ليضيء، ولن يعمل أبدًا مع بطارية صغيرة.', N'TrueFalse', NULL, NULL, N'Easy', 8, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1049, 108, 105, N'ما الوظيفة الأساسية للمقاومة في الدائرة؟', N'MultipleChoice', NULL, NULL, N'Easy', 1, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1050, 108, 105, N'ماذا يحدث للـ LED إذا وصّلناه ببطارية قوية جدًا بدون مقاومة؟', N'MultipleChoice', NULL, NULL, N'Easy', 2, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1051, 108, 105, N'ما فائدة الحلقات الملوّنة المرسومة على جسم المقاومة؟', N'MultipleChoice', NULL, NULL, N'Easy', 3, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1052, 108, 105, N'يجب توصيل المقاومة في اتجاه محدد (مع مراعاة الطرفين الموجب والسالب) مثل الـ LED تمامًا.', N'TrueFalse', NULL, NULL, N'Easy', 4, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1053, 108, 105, N'ما وحدة قياس المقاومة الكهربية؟', N'MultipleChoice', NULL, NULL, N'Easy', 5, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1054, 108, 105, N'كلما زادت قيمة المقاومة (بالأوم)، فإن حركة الإلكترونات...', N'MultipleChoice', NULL, NULL, N'Easy', 6, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1055, 108, 105, N'تحوّل المقاومة الطاقة الكهربية الزائدة التي تمتصها إلى...', N'MultipleChoice', NULL, NULL, N'Easy', 7, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1056, 108, 105, N'يمكننا استبدال المقاومة بسلك عادي وسيؤدي الوظيفة نفسها تمامًا.', N'TrueFalse', NULL, NULL, N'Easy', 8, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1057, 109, 106, N'كيف يعمل زر الضغط؟', N'MultipleChoice', NULL, NULL, N'Easy', 1, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1058, 109, 106, N'ماذا يحدث للدائرة عندما ترفع يدك عن زر الضغط؟', N'MultipleChoice', NULL, NULL, N'Easy', 2, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1059, 109, 106, N'نستخدم أزرار الضغط في أجهزة كثيرة حولنا، مثل...', N'MultipleChoice', NULL, NULL, N'Easy', 3, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1060, 109, 106, N'يساعدنا المفتاح على توفير طاقة البطارية لأنه يسمح بمرور الكهرباء عند الحاجة فقط.', N'TrueFalse', NULL, NULL, N'Easy', 4, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1061, 109, 106, N'عندما تضغط على المفتاح، تُسمّى حالة الدائرة...', N'MultipleChoice', NULL, NULL, N'Easy', 5, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1062, 109, 106, N'يحتوي زر الضغط على زنبرك داخلي يعيده إلى الأعلى بمجرد أن ترفع يدك.', N'TrueFalse', NULL, NULL, N'Easy', 6, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1063, 109, 106, N'زر الضغط العادي يولّد الكهرباء تلقائيًا دون الحاجة إلى بطارية.', N'TrueFalse', NULL, NULL, N'Easy', 7, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1064, 109, 106, N'أيٌّ مما يلي يصف القابس أو المفتاح الكهربي بشكل صحيح؟', N'MultipleChoice', NULL, NULL, N'Easy', 8, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1065, 110, 102, N'الدائرة الموضحة في الصورة دائرة مغلقة.', N'TrueFalse', N'/uploads/lessons/seed-q-open-circuit.png', N'دائرة فيها بطارية على اليسار ومصباح في الأعلى، والسلك السفلي مقطوع من المنتصف، والمصباح مطفأ.', N'Medium', 1, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1066, 110, 102, N'ما اسم المكوّن الموضح في الصورة؟', N'MultipleChoice', N'/uploads/lessons/seed-q-battery.png', N'بطارية أسطوانية خضراء مكتوب على أحد طرفيها علامة + وعلى الطرف الآخر علامة −.', N'Easy', 2, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1067, 110, 102, N'ماذا يحدث للمصباح إذا فتحنا المفتاح؟', N'MultipleChoice', NULL, NULL, N'Medium', 3, 2, 1, DATEADD(MINUTE, -73440, @Now)),
    (1068, 110, 102, N'صف ماذا يحدث للمصباح عندما نفتح المفتاح ثم نغلقه.', N'Essay', NULL, NULL, N'Medium', 4, 2, 1, DATEADD(MINUTE, -73440, @Now)),
    (1069, 111, 103, N'أي صورة تُظهر مادة موصلة للكهرباء؟', N'MultipleChoice', NULL, NULL, N'Medium', 1, 2, 1, DATEADD(MINUTE, -73440, @Now)),
    (1070, 111, 101, N'أيٌّ من هذه يحتاج إلى كهرباء ليعمل؟', N'MultipleChoice', NULL, NULL, N'Easy', 2, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1071, 111, 103, N'ماء الصنبور يمكن أن يوصل الكهرباء.', N'TrueFalse', NULL, NULL, N'Hard', 3, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (1072, 111, 103, N'اشرح بكلماتك لماذا يجب ألا نلمس الأسلاك المكشوفة.', N'Essay', NULL, NULL, N'Medium', 4, 3, 1, DATEADD(MINUTE, -73440, @Now)),
    (1073, 111, 104, N'ما لون ضوء الـ LED؟ (مسودة)', N'MultipleChoice', NULL, NULL, N'Easy', 5, 1, 0, DATEADD(MINUTE, -73440, @Now));
SET IDENTITY_INSERT Assessment.Questions OFF;

INSERT INTO Assessment.QuestionTranslations (QuestionId, LanguageCode, QuestionText)
VALUES
    (1001, N'ar', N'ما اسم الشحنات الصغيرة جدًا التي تتحرك في الأسلاك وتولّد الكهرباء؟'),
    (1001, N'en', N'What are the tiny charges that flow through wires and generate electricity called?'),
    (1002, N'ar', N'من أين تأتي الكهرباء القوية التي تضيء بيوتنا وتشغّل الثلاجة؟'),
    (1002, N'en', N'Where does the powerful electricity that lights up our homes and powers the refrigerator come from?'),
    (1003, N'ar', N'عندما نوصّل التلفاز بالكهرباء، تتحول الطاقة الكهربية إلى...'),
    (1003, N'en', N'When we plug a TV into the electricity, electrical energy transforms into...'),
    (1004, N'ar', N'لماذا يُمنع تمامًا لمس المقبس الكهربي بأيدٍ مبللة؟'),
    (1004, N'en', N'Why is it strictly forbidden to touch an electrical outlet with wet hands?'),
    (1005, N'ar', N'ما دور البطارية الصغيرة في ألعابنا؟'),
    (1005, N'en', N'What is the role of the small battery in our toys?'),
    (1006, N'ar', N'إذا انقطع السلك في الدائرة الكهربية يظل المصباح مضيئًا بشكل طبيعي.'),
    (1006, N'en', N'If the wire in an electrical circuit breaks, the light bulb remains lit normally.'),
    (1007, N'ar', N'أي مكوّن نستخدمه لفتح الدائرة (إطفائها) أو غلقها (تشغيلها)؟'),
    (1007, N'en', N'Which component do we use to open the circuit (turn it off) or close it (turn it on)?'),
    (1008, N'ar', N'لماذا تُغطّى الأسلاك الكهربية من الخارج بطبقة من البلاستيك؟'),
    (1008, N'en', N'Why are electrical wires covered on the outside with a layer of plastic?'),
    (1009, N'ar', N'أي مكوّن يضيء بألوان مبهجة ويستهلك طاقة كهربية قليلة جدًا؟'),
    (1009, N'en', N'Which of the following components lights up in cheerful colors and consumes very little electrical energy?'),
    (1010, N'ar', N'أي مكوّن يعمل مثل "مطبّ صناعي" يبطئ الإلكترونات ليحمي المكونات الحساسة؟'),
    (1010, N'en', N'Which component acts like a "speed bump," slowing down electrons to protect delicate parts?'),
    (1011, N'ar', N'الرجل الطويلة للـ LED توصَّل دائمًا بالطرف السالب (−) للبطارية.'),
    (1011, N'en', N'The long leg of an LED always connects to the negative (-) terminal of the battery.'),
    (1012, N'ar', N'يسمح زر الضغط بمرور الكهرباء في الدائرة...'),
    (1012, N'en', N'A push button allows electricity to flow through the circuit...'),
    (1013, N'ar', N'كيف يعرف المهندسون قيمة المقاومة الكهربية؟'),
    (1013, N'en', N'How do engineers determine the value and rating of an electrical resistor?'),
    (1014, N'ar', N'يمكن توصيل المقاومة في الدائرة الكهربية في أي اتجاه دون القلق من الطرفين الموجب والسالب.'),
    (1014, N'en', N'A resistor can be connected in an electrical circuit in either direction without worrying about positive and negative terminals.'),
    (1015, N'ar', N'لماذا يُفضَّل استخدام زر الضغط في الدوائر الإلكترونية؟'),
    (1015, N'en', N'Why is it preferable to use a push button in electronic circuits?'),
    (1016, N'ar', N'ما المكونات الصحيحة لبناء دائرة آمنة تضيء LED باستخدام زر تحكم؟'),
    (1016, N'en', N'What are the correct components to build a safe circuit that lights up an LED using a control button?'),
    (1017, N'ar', N'من أين تأتي الكهرباء التي تشغّل ألعابنا؟'),
    (1017, N'en', N'Where does the electricity that powers our toys come from?'),
    (1018, N'ar', N'البرق في السماء نوع من الكهرباء الطبيعية القوية.'),
    (1018, N'en', N'Lightning in the sky is a type of powerful natural electricity.'),
    (1019, N'ar', N'ماذا نسمي الأشياء التي تحتاج إلى الكهرباء لتعمل؟'),
    (1019, N'en', N'What do we call things that need electricity to work?'),
    (1020, N'ar', N'البطارية مصدر صغير وآمن للكهرباء في ألعابنا.'),
    (1020, N'en', N'A battery is considered a small, safe source of electricity for our toys.'),
    (1021, N'ar', N'ما اسم الشحنات التي تتحرك بسرعة لتصنع الكهرباء؟'),
    (1021, N'en', N'What are the charges that move quickly to create electricity?'),
    (1022, N'ar', N'نستطيع أن نرى الكهرباء بأعيننا بوضوح وهي تتحرك داخل الأسلاك.'),
    (1022, N'en', N'We can clearly see electricity with our eyes as it moves through wires.'),
    (1023, N'ar', N'أين تُنتَج الكهرباء القوية التي تصل إلى بيوتنا؟'),
    (1023, N'en', N'Where is the powerful electricity that reaches our homes produced?'),
    (1024, N'ar', N'يمكن أن تتحول الطاقة الكهربية إلى...'),
    (1024, N'en', N'Electrical energy can be converted into...'),
    (1025, N'ar', N'ماذا يحتاج المصباح ليضيء؟'),
    (1025, N'en', N'What does a light bulb need to light up?'),
    (1026, N'ar', N'أيٌّ مما يلي دائرة مفتوحة (لا تمر فيها الكهرباء)؟'),
    (1026, N'en', N'Which of these is considered an open circuit (where electricity does not flow)?'),
    (1027, N'ar', N'ما وظيفة المفتاح الكهربي؟'),
    (1027, N'en', N'What is the function of an electric switch?'),
    (1028, N'ar', N'يضيء المصباح حتى لو لم تكن البطارية موجودة في الدائرة.'),
    (1028, N'en', N'The light bulb lights up even if the battery is not in the circuit.'),
    (1029, N'ar', N'ما وظيفة السلك النحاسي في الدائرة الكهربية؟'),
    (1029, N'en', N'What is the function of the copper wire in an electrical circuit?'),
    (1030, N'ar', N'عندما تكون الدائرة "مغلقة" يكون الطريق كاملًا وتمر الكهرباء بنجاح.'),
    (1030, N'en', N'When the circuit is "closed," it means the path is complete and electricity flows successfully.'),
    (1031, N'ar', N'ماذا يحدث عندما نطفئ المفتاح الكهربي (OFF)؟'),
    (1031, N'en', N'What happens when the electrical switch is turned off (OFF)?'),
    (1032, N'ar', N'المكوّن الذي يمدّ الدائرة الكهربية البسيطة بالطاقة هو...'),
    (1032, N'en', N'The component that provides energy in a simple electrical circuit is:'),
    (1033, N'ar', N'هل من الآمن لمس المقبس الكهربي بأيدٍ مبللة؟'),
    (1033, N'en', N'Is it safe to touch an electrical socket with wet hands?'),
    (1034, N'ar', N'ماذا نفعل إذا وجدنا سلكًا كهربيًا مكشوفًا؟'),
    (1034, N'en', N'What should we do if we find an exposed electrical wire?'),
    (1035, N'ar', N'إدخال الأجسام المعدنية، مثل المسمار، في المقبس الكهربي تصرّف آمن.'),
    (1035, N'en', N'Inserting metal objects, like a nail, into an electrical socket is a safe action.'),
    (1036, N'ar', N'لماذا تُغطّى الأسلاك الكهربية بطبقة من البلاستيك؟'),
    (1036, N'en', N'Why are electrical wires covered with a layer of plastic?'),
    (1037, N'ar', N'يجب إطفاء الأجهزة وفصلها عن الكهرباء إذا لم نستخدمها لفترة طويلة.'),
    (1037, N'en', N'Appliances should be turned off and unplugged when not in use for a long time.'),
    (1038, N'ar', N'عند فصل جهاز عن الكهرباء، يجب أن نمسك...'),
    (1038, N'en', N'When unplugging a device from the wall, we should hold...'),
    (1039, N'ar', N'شدّ السلك بقوة لفصله عن الكهرباء هو التصرف الصحيح.'),
    (1039, N'en', N'Pulling the cord forcefully to disconnect it from the power is the correct thing to do.'),
    (1040, N'ar', N'من الشخص المناسب لإصلاح الأعطال الكهربية في البيت؟'),
    (1040, N'en', N'Who is the right person to fix electrical faults at home?'),
    (1041, N'ar', N'ماذا يعني الاختصار "LED" في عالم الإلكترونيات؟'),
    (1041, N'en', N'What does the acronym "LED" stand for in the world of electronics?'),
    (1042, N'ar', N'لماذا للـ LED رجل طويلة ورجل قصيرة؟'),
    (1042, N'en', N'Why does an LED have one long leg and one short leg?'),
    (1043, N'ar', N'ماذا يحدث إذا وصّلنا الـ LED بالعكس في الدائرة الكهربية؟'),
    (1043, N'en', N'What happens if we connect the LED backwards in an electrical circuit?'),
    (1044, N'ar', N'الـ LED صديق للبيئة لأنه يستهلك طاقة كهربية قليلة جدًا مقارنة بالمصابيح القديمة.'),
    (1044, N'en', N'The LED is considered eco-friendly because it consumes very little electrical energy compared to older light bulbs.'),
    (1045, N'ar', N'الرجل الطويلة للـ LED تمثّل...'),
    (1045, N'en', N'The long leg of the LED represents the:'),
    (1046, N'ar', N'الـ LED يصدر حرارة عالية جدًا، وقد تحترق يدك إذا لمسته وهو مضيء.'),
    (1046, N'en', N'The LED emits very high heat, and your hand would burn if you touched it while it is lit.'),
    (1047, N'ar', N'نرى الـ LED في حياتنا اليومية في...'),
    (1047, N'en', N'We see LEDs in our daily lives in:'),
    (1048, N'ar', N'الـ LED يحتاج إلى طاقة هائلة ليضيء، ولن يعمل أبدًا مع بطارية صغيرة.'),
    (1048, N'en', N'The LED requires a massive amount of energy to light up and will never work with a small battery.'),
    (1049, N'ar', N'ما الوظيفة الأساسية للمقاومة في الدائرة؟'),
    (1049, N'en', N'What is the primary function of a resistor in a circuit?'),
    (1050, N'ar', N'ماذا يحدث للـ LED إذا وصّلناه ببطارية قوية جدًا بدون مقاومة؟'),
    (1050, N'en', N'What happens to an LED if connected to a very powerful battery without a resistor?'),
    (1051, N'ar', N'ما فائدة الحلقات الملوّنة المرسومة على جسم المقاومة؟'),
    (1051, N'en', N'What is the purpose of the colored bands marked on the body of the resistor?'),
    (1052, N'ar', N'يجب توصيل المقاومة في اتجاه محدد (مع مراعاة الطرفين الموجب والسالب) مثل الـ LED تمامًا.'),
    (1052, N'en', N'A resistor must be connected in a specific direction (observing positive and negative terminals), just like an LED.'),
    (1053, N'ar', N'ما وحدة قياس المقاومة الكهربية؟'),
    (1053, N'en', N'What is the unit of measurement for electrical resistance?'),
    (1054, N'ar', N'كلما زادت قيمة المقاومة (بالأوم)، فإن حركة الإلكترونات...'),
    (1054, N'en', N'As the resistance value (Ohms) increases, the movement of electrons:'),
    (1055, N'ar', N'تحوّل المقاومة الطاقة الكهربية الزائدة التي تمتصها إلى...'),
    (1055, N'en', N'The resistor converts the excess electrical energy it absorbs into:'),
    (1056, N'ar', N'يمكننا استبدال المقاومة بسلك عادي وسيؤدي الوظيفة نفسها تمامًا.'),
    (1056, N'en', N'We can replace the resistor with an ordinary wire, and it will perform the exact same function.'),
    (1057, N'ar', N'كيف يعمل زر الضغط؟'),
    (1057, N'en', N'How does a push button work?'),
    (1058, N'ar', N'ماذا يحدث للدائرة عندما ترفع يدك عن زر الضغط؟'),
    (1058, N'en', N'What happens to the circuit when you remove your hand from the push button?'),
    (1059, N'ar', N'نستخدم أزرار الضغط في أجهزة كثيرة حولنا، مثل...'),
    (1059, N'en', N'We use push buttons in many devices around us, such as...?'),
    (1060, N'ar', N'يساعدنا المفتاح على توفير طاقة البطارية لأنه يسمح بمرور الكهرباء عند الحاجة فقط.'),
    (1060, N'en', N'A switch helps us save battery power because it allows electricity to flow only when needed.'),
    (1061, N'ar', N'عندما تضغط على المفتاح، تُسمّى حالة الدائرة...'),
    (1061, N'en', N'When you press the switch, the circuit state is called:'),
    (1062, N'ar', N'يحتوي زر الضغط على زنبرك داخلي يعيده إلى الأعلى بمجرد أن ترفع يدك.'),
    (1062, N'en', N'A push button contains an internal spring that pushes it back up as soon as you release your hand.'),
    (1063, N'ar', N'زر الضغط العادي يولّد الكهرباء تلقائيًا دون الحاجة إلى بطارية.'),
    (1063, N'en', N'A standard push button automatically generates electricity without needing a battery.'),
    (1064, N'ar', N'أيٌّ مما يلي يصف القابس أو المفتاح الكهربي بشكل صحيح؟'),
    (1064, N'en', N'Which of the following correctly describes an electrical plug or switch?'),
    (1065, N'ar', N'الدائرة الموضحة في الصورة دائرة مغلقة.'),
    (1065, N'en', N'The circuit in the picture is closed.'),
    (1066, N'ar', N'ما اسم المكوّن الموضح في الصورة؟'),
    (1066, N'en', N'What is the part shown in the picture?'),
    (1067, N'ar', N'ماذا يحدث للمصباح إذا فتحنا المفتاح؟'),
    (1068, N'ar', N'صف ماذا يحدث للمصباح عندما نفتح المفتاح ثم نغلقه.'),
    (1068, N'en', N'Describe what happens to the lamp when we open the switch and then close it.'),
    (1069, N'ar', N'أي صورة تُظهر مادة موصلة للكهرباء؟'),
    (1069, N'en', N'Which picture shows a material that conducts electricity?'),
    (1070, N'ar', N'أيٌّ من هذه يحتاج إلى كهرباء ليعمل؟'),
    (1070, N'en', N'Which of these needs electricity to work?'),
    (1071, N'ar', N'ماء الصنبور يمكن أن يوصل الكهرباء.'),
    (1071, N'en', N'Tap water can conduct electricity.'),
    (1072, N'ar', N'اشرح بكلماتك لماذا يجب ألا نلمس الأسلاك المكشوفة.'),
    (1072, N'en', N'Explain in your own words why we must not touch bare wires.'),
    (1073, N'ar', N'ما لون ضوء الـ LED؟ (مسودة)'),
    (1073, N'en', N'What colour is the light of an LED? (draft)');

SET IDENTITY_INSERT Assessment.QuestionOptions ON;
INSERT INTO Assessment.QuestionOptions (Id, QuestionId, OptionText, ImageUrl, ImageDescription, IsCorrect, DisplayOrder, CreatedAt)
VALUES
    (10001, 1001, N'فقاعات الهواء', NULL, NULL, 0, 1, DATEADD(MINUTE, -73440, @Now)),
    (10002, 1001, N'قطرات الماء', NULL, NULL, 0, 2, DATEADD(MINUTE, -73440, @Now)),
    (10003, 1001, N'الإلكترونات', NULL, NULL, 1, 3, DATEADD(MINUTE, -73440, @Now)),
    (10004, 1002, N'محطات توليد الكهرباء', NULL, NULL, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (10005, 1002, N'أشجار الحديقة', NULL, NULL, 0, 2, DATEADD(MINUTE, -73440, @Now)),
    (10006, 1002, N'المخبز', NULL, NULL, 0, 3, DATEADD(MINUTE, -73440, @Now)),
    (10007, 1003, N'ضوء وصوت', NULL, NULL, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (10008, 1003, N'طعام وماء', NULL, NULL, 0, 2, DATEADD(MINUTE, -73440, @Now)),
    (10009, 1003, N'ضوء وحرارة فقط', NULL, NULL, 0, 3, DATEADD(MINUTE, -73440, @Now)),
    (10010, 1004, N'لأن الماء يقطع الكهرباء', NULL, NULL, 0, 1, DATEADD(MINUTE, -73440, @Now)),
    (10011, 1004, N'لأن الماء يوصل الكهرباء بسرعة وهذا خطير', NULL, NULL, 1, 2, DATEADD(MINUTE, -73440, @Now)),
    (10012, 1004, N'لأن المقبس سيتّسخ', NULL, NULL, 0, 3, DATEADD(MINUTE, -73440, @Now)),
    (10013, 1005, N'نفق تسير فيه الكهرباء', NULL, NULL, 0, 1, DATEADD(MINUTE, -73440, @Now)),
    (10014, 1005, N'مصدر الطاقة الذي يدفع الإلكترونات', NULL, NULL, 1, 2, DATEADD(MINUTE, -73440, @Now)),
    (10015, 1005, N'زر لتشغيل اللعبة وإطفائها', NULL, NULL, 0, 3, DATEADD(MINUTE, -73440, @Now)),
    (10016, 1006, N'صح', NULL, NULL, 0, 1, DATEADD(MINUTE, -73440, @Now)),
    (10017, 1006, N'خطأ', NULL, NULL, 1, 2, DATEADD(MINUTE, -73440, @Now)),
    (10018, 1007, N'المصباح الزجاجي', NULL, NULL, 0, 1, DATEADD(MINUTE, -73440, @Now)),
    (10019, 1007, N'المفتاح الكهربي', NULL, NULL, 1, 2, DATEADD(MINUTE, -73440, @Now)),
    (10020, 1007, N'السلك النحاسي', NULL, NULL, 0, 3, DATEADD(MINUTE, -73440, @Now)),
    (10021, 1008, N'لحمايتنا، لأن البلاستيك مادة عازلة لا توصل الكهرباء', NULL, NULL, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (10022, 1008, N'فقط لتصبح الأسلاك مرنة', NULL, NULL, 0, 2, DATEADD(MINUTE, -73440, @Now)),
    (10023, 1008, N'لأن البلاستيك يضيء في الظلام', NULL, NULL, 0, 3, DATEADD(MINUTE, -73440, @Now)),
    (10024, 1009, N'الثنائي الباعث للضوء (LED)', NULL, NULL, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (10025, 1009, N'زر الضغط', NULL, NULL, 0, 2, DATEADD(MINUTE, -73440, @Now)),
    (10026, 1009, N'المقاومة', NULL, NULL, 0, 3, DATEADD(MINUTE, -73440, @Now)),
    (10027, 1010, N'السلك النحاسي', NULL, NULL, 0, 1, DATEADD(MINUTE, -73440, @Now)),
    (10028, 1010, N'المقاومة', NULL, NULL, 1, 2, DATEADD(MINUTE, -73440, @Now)),
    (10029, 1010, N'البطارية', NULL, NULL, 0, 3, DATEADD(MINUTE, -73440, @Now)),
    (10030, 1011, N'صح', NULL, NULL, 0, 1, DATEADD(MINUTE, -73440, @Now)),
    (10031, 1011, N'خطأ', NULL, NULL, 1, 2, DATEADD(MINUTE, -73440, @Now)),
    (10032, 1012, N'بمجرد أن نرفع أيدينا عنه', NULL, NULL, 0, 1, DATEADD(MINUTE, -73440, @Now)),
    (10033, 1012, N'تلقائيًا دون ضغط', NULL, NULL, 0, 2, DATEADD(MINUTE, -73440, @Now)),
    (10034, 1012, N'ما دمنا نضغط عليه', NULL, NULL, 1, 3, DATEADD(MINUTE, -73440, @Now)),
    (10035, 1013, N'من طول أرجلها', NULL, NULL, 0, 1, DATEADD(MINUTE, -73440, @Now)),
    (10036, 1013, N'من الحلقات الملوّنة على جسمها', NULL, NULL, 1, 2, DATEADD(MINUTE, -73440, @Now)),
    (10037, 1013, N'من وزنها وثقلها', NULL, NULL, 0, 3, DATEADD(MINUTE, -73440, @Now)),
    (10038, 1014, N'صح', NULL, NULL, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (10039, 1014, N'خطأ', NULL, NULL, 0, 2, DATEADD(MINUTE, -73440, @Now)),
    (10040, 1015, N'لأنه يغيّر لون الضوء', NULL, NULL, 0, 1, DATEADD(MINUTE, -73440, @Now)),
    (10041, 1015, N'لأنه يزيد قوة البطارية', NULL, NULL, 0, 2, DATEADD(MINUTE, -73440, @Now)),
    (10042, 1015, N'لأنه يمنع استهلاك طاقة البطارية إلا عند الحاجة', NULL, NULL, 1, 3, DATEADD(MINUTE, -73440, @Now)),
    (10043, 1016, N'مقاومة + مفتاح بدون بطارية', NULL, NULL, 0, 1, DATEADD(MINUTE, -73440, @Now)),
    (10044, 1016, N'بطارية + مقاومة + زر ضغط + LED', NULL, NULL, 1, 2, DATEADD(MINUTE, -73440, @Now)),
    (10045, 1016, N'بطارية + LED فقط', NULL, NULL, 0, 3, DATEADD(MINUTE, -73440, @Now)),
    (10046, 1017, N'من حركة الإلكترونات الصغيرة جدًا', NULL, NULL, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (10047, 1017, N'من ماء الصنبور', NULL, NULL, 0, 2, DATEADD(MINUTE, -73440, @Now)),
    (10048, 1017, N'من الهواء', NULL, NULL, 0, 3, DATEADD(MINUTE, -73440, @Now)),
    (10049, 1018, N'صح', NULL, NULL, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (10050, 1018, N'خطأ', NULL, NULL, 0, 2, DATEADD(MINUTE, -73440, @Now)),
    (10051, 1019, N'الكتب الورقية', NULL, NULL, 0, 1, DATEADD(MINUTE, -73440, @Now)),
    (10052, 1019, N'الأجهزة الكهربية والإلكترونية', NULL, NULL, 1, 2, DATEADD(MINUTE, -73440, @Now)),
    (10053, 1019, N'الألعاب الخشبية', NULL, NULL, 0, 3, DATEADD(MINUTE, -73440, @Now)),
    (10054, 1020, N'صح', NULL, NULL, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (10055, 1020, N'خطأ', NULL, NULL, 0, 2, DATEADD(MINUTE, -73440, @Now)),
    (10056, 1021, N'القطرات', NULL, NULL, 0, 1, DATEADD(MINUTE, -73440, @Now)),
    (10057, 1021, N'الكرات', NULL, NULL, 0, 2, DATEADD(MINUTE, -73440, @Now)),
    (10058, 1021, N'الإلكترونات', NULL, NULL, 1, 3, DATEADD(MINUTE, -73440, @Now)),
    (10059, 1022, N'صح', NULL, NULL, 0, 1, DATEADD(MINUTE, -73440, @Now)),
    (10060, 1022, N'خطأ', NULL, NULL, 1, 2, DATEADD(MINUTE, -73440, @Now)),
    (10061, 1023, N'في محطات توليد الكهرباء', NULL, NULL, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (10062, 1023, N'في المخبز', NULL, NULL, 0, 2, DATEADD(MINUTE, -73440, @Now)),
    (10063, 1023, N'في الحدائق', NULL, NULL, 0, 3, DATEADD(MINUTE, -73440, @Now)),
    (10064, 1024, N'طعام وماء', NULL, NULL, 0, 1, DATEADD(MINUTE, -73440, @Now)),
    (10065, 1024, N'ألعاب بلاستيكية', NULL, NULL, 0, 2, DATEADD(MINUTE, -73440, @Now)),
    (10066, 1024, N'ضوء وحرارة وصوت', NULL, NULL, 1, 3, DATEADD(MINUTE, -73440, @Now)),
    (10067, 1025, N'دائرة مفتوحة', NULL, NULL, 0, 1, DATEADD(MINUTE, -73440, @Now)),
    (10068, 1025, N'دائرة كهربية مغلقة ومتصلة', NULL, NULL, 1, 2, DATEADD(MINUTE, -73440, @Now)),
    (10069, 1025, N'سلك مقطوع', NULL, NULL, 0, 3, DATEADD(MINUTE, -73440, @Now)),
    (10070, 1026, N'دائرة فيها سلك مقطوع', NULL, NULL, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (10071, 1026, N'دائرة كاملة ومتصلة', NULL, NULL, 0, 2, DATEADD(MINUTE, -73440, @Now)),
    (10072, 1027, N'إنتاج الطاقة', NULL, NULL, 0, 1, DATEADD(MINUTE, -73440, @Now)),
    (10073, 1027, N'تغيير لون المصباح', NULL, NULL, 0, 2, DATEADD(MINUTE, -73440, @Now)),
    (10074, 1027, N'فتح الدائرة وغلقها للتحكم في الكهرباء', NULL, NULL, 1, 3, DATEADD(MINUTE, -73440, @Now)),
    (10075, 1028, N'صح', NULL, NULL, 0, 1, DATEADD(MINUTE, -73440, @Now)),
    (10076, 1028, N'خطأ', NULL, NULL, 1, 2, DATEADD(MINUTE, -73440, @Now)),
    (10077, 1029, N'نقل الإلكترونات والكهرباء', NULL, NULL, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (10078, 1029, N'إيقاف الكهرباء', NULL, NULL, 0, 2, DATEADD(MINUTE, -73440, @Now)),
    (10079, 1029, N'تبريد البطارية', NULL, NULL, 0, 3, DATEADD(MINUTE, -73440, @Now)),
    (10080, 1030, N'صح', NULL, NULL, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (10081, 1030, N'خطأ', NULL, NULL, 0, 2, DATEADD(MINUTE, -73440, @Now)),
    (10082, 1031, N'تنفجر البطارية', NULL, NULL, 0, 1, DATEADD(MINUTE, -73440, @Now)),
    (10083, 1031, N'تتوقف الكهرباء وينطفئ المصباح', NULL, NULL, 1, 2, DATEADD(MINUTE, -73440, @Now)),
    (10084, 1031, N'يضيء المصباح بقوة', NULL, NULL, 0, 3, DATEADD(MINUTE, -73440, @Now)),
    (10085, 1032, N'البطارية', NULL, NULL, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (10086, 1032, N'المفتاح', NULL, NULL, 0, 2, DATEADD(MINUTE, -73440, @Now)),
    (10087, 1032, N'السلك', NULL, NULL, 0, 3, DATEADD(MINUTE, -73440, @Now)),
    (10088, 1033, N'نعم، لا مشكلة', NULL, NULL, 0, 1, DATEADD(MINUTE, -73440, @Now)),
    (10089, 1033, N'لا؛ الماء يوصل الكهرباء وهذا خطير', NULL, NULL, 1, 2, DATEADD(MINUTE, -73440, @Now)),
    (10090, 1034, N'نلعب به', NULL, NULL, 0, 1, DATEADD(MINUTE, -73440, @Now)),
    (10091, 1034, N'نبتعد عنه فورًا ونخبر شخصًا كبيرًا', NULL, NULL, 1, 2, DATEADD(MINUTE, -73440, @Now)),
    (10092, 1034, N'نلمسه بأظافرنا', NULL, NULL, 0, 3, DATEADD(MINUTE, -73440, @Now)),
    (10093, 1035, N'صح', NULL, NULL, 0, 1, DATEADD(MINUTE, -73440, @Now)),
    (10094, 1035, N'خطأ', NULL, NULL, 1, 2, DATEADD(MINUTE, -73440, @Now)),
    (10095, 1036, N'فقط لتصبح ملوّنة', NULL, NULL, 0, 1, DATEADD(MINUTE, -73440, @Now)),
    (10096, 1036, N'لتصبح أثقل', NULL, NULL, 0, 2, DATEADD(MINUTE, -73440, @Now)),
    (10097, 1036, N'لأن البلاستيك يعزل الكهرباء ويحمينا', NULL, NULL, 1, 3, DATEADD(MINUTE, -73440, @Now)),
    (10098, 1037, N'صح', NULL, NULL, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (10099, 1037, N'خطأ', NULL, NULL, 0, 2, DATEADD(MINUTE, -73440, @Now)),
    (10100, 1038, N'القابس البلاستيكي نفسه', NULL, NULL, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (10101, 1038, N'السلك ونشدّه بقوة', NULL, NULL, 0, 2, DATEADD(MINUTE, -73440, @Now)),
    (10102, 1038, N'الحائط', NULL, NULL, 0, 3, DATEADD(MINUTE, -73440, @Now)),
    (10103, 1039, N'صح', NULL, NULL, 0, 1, DATEADD(MINUTE, -73440, @Now)),
    (10104, 1039, N'خطأ', NULL, NULL, 1, 2, DATEADD(MINUTE, -73440, @Now)),
    (10105, 1040, N'لا أحد', NULL, NULL, 0, 1, DATEADD(MINUTE, -73440, @Now)),
    (10106, 1040, N'كهربائي مختص أو شخص كبير', NULL, NULL, 1, 2, DATEADD(MINUTE, -73440, @Now)),
    (10107, 1040, N'الأطفال الصغار', NULL, NULL, 0, 3, DATEADD(MINUTE, -73440, @Now)),
    (10108, 1041, N'الثنائي الباعث للضوء (Light-Emitting Diode)', NULL, NULL, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (10109, 1041, N'جهاز كهربي صغير (Little Electric Device)', NULL, NULL, 0, 2, DATEADD(MINUTE, -73440, @Now)),
    (10110, 1041, N'جهاز كهربي متصل (Linked Electric Device)', NULL, NULL, 0, 3, DATEADD(MINUTE, -73440, @Now)),
    (10111, 1042, N'ليثبت في الأرض', NULL, NULL, 0, 1, DATEADD(MINUTE, -73440, @Now)),
    (10112, 1042, N'إنها مجرد زينة', NULL, NULL, 0, 2, DATEADD(MINUTE, -73440, @Now)),
    (10113, 1042, N'لأن الكهرباء تمر فيه في اتجاه واحد فقط', NULL, NULL, 1, 3, DATEADD(MINUTE, -73440, @Now)),
    (10114, 1043, N'سينفجر فورًا', NULL, NULL, 0, 1, DATEADD(MINUTE, -73440, @Now)),
    (10115, 1043, N'لن يضيء لأن الكهرباء لا تمر في الاتجاه المعاكس', NULL, NULL, 1, 2, DATEADD(MINUTE, -73440, @Now)),
    (10116, 1043, N'سيضيء بلون مختلف', NULL, NULL, 0, 3, DATEADD(MINUTE, -73440, @Now)),
    (10117, 1044, N'صح', NULL, NULL, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (10118, 1044, N'خطأ', NULL, NULL, 0, 2, DATEADD(MINUTE, -73440, @Now)),
    (10119, 1045, N'الطرف السالب (الكاثود −)', NULL, NULL, 0, 1, DATEADD(MINUTE, -73440, @Now)),
    (10120, 1045, N'الطرف المحايد', NULL, NULL, 0, 2, DATEADD(MINUTE, -73440, @Now)),
    (10121, 1045, N'الطرف الموجب (الأنود +)', NULL, NULL, 1, 3, DATEADD(MINUTE, -73440, @Now)),
    (10122, 1046, N'صح', NULL, NULL, 0, 1, DATEADD(MINUTE, -73440, @Now)),
    (10123, 1046, N'خطأ', NULL, NULL, 1, 2, DATEADD(MINUTE, -73440, @Now)),
    (10124, 1047, N'شاشات التلفاز وإشارات المرور وأضواء الألعاب', NULL, NULL, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (10125, 1047, N'الأدوات الخشبية', NULL, NULL, 0, 2, DATEADD(MINUTE, -73440, @Now)),
    (10126, 1047, N'الملابس القطنية', NULL, NULL, 0, 3, DATEADD(MINUTE, -73440, @Now)),
    (10127, 1048, N'صح', NULL, NULL, 0, 1, DATEADD(MINUTE, -73440, @Now)),
    (10128, 1048, N'خطأ', NULL, NULL, 1, 2, DATEADD(MINUTE, -73440, @Now)),
    (10129, 1049, N'إضاءة الدائرة', NULL, NULL, 0, 1, DATEADD(MINUTE, -73440, @Now)),
    (10130, 1049, N'حماية المكونات وتنظيم مرور الكهرباء', NULL, NULL, 1, 2, DATEADD(MINUTE, -73440, @Now)),
    (10131, 1049, N'زيادة القدرة الكهربية', NULL, NULL, 0, 3, DATEADD(MINUTE, -73440, @Now)),
    (10132, 1050, N'قد يحترق بسبب التيار القوي', NULL, NULL, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (10133, 1050, N'سيضيء إلى الأبد', NULL, NULL, 0, 2, DATEADD(MINUTE, -73440, @Now)),
    (10134, 1050, N'لن يتأثر', NULL, NULL, 0, 3, DATEADD(MINUTE, -73440, @Now)),
    (10135, 1051, N'لتبدو جميلة', NULL, NULL, 0, 1, DATEADD(MINUTE, -73440, @Now)),
    (10136, 1051, N'لتحديد الطرفين الموجب والسالب', NULL, NULL, 0, 2, DATEADD(MINUTE, -73440, @Now)),
    (10137, 1051, N'تحديد قيمة المقاومة', NULL, NULL, 1, 3, DATEADD(MINUTE, -73440, @Now)),
    (10138, 1052, N'صح', NULL, NULL, 0, 1, DATEADD(MINUTE, -73440, @Now)),
    (10139, 1052, N'خطأ', NULL, NULL, 1, 2, DATEADD(MINUTE, -73440, @Now)),
    (10140, 1053, N'الأوم (Ω)', NULL, NULL, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (10141, 1053, N'الكيلوجرام', NULL, NULL, 0, 2, DATEADD(MINUTE, -73440, @Now)),
    (10142, 1053, N'المتر', NULL, NULL, 0, 3, DATEADD(MINUTE, -73440, @Now)),
    (10143, 1054, N'تزيد وتسرع', NULL, NULL, 0, 1, DATEADD(MINUTE, -73440, @Now)),
    (10144, 1054, N'تتوقف وتنفجر الدائرة', NULL, NULL, 0, 2, DATEADD(MINUTE, -73440, @Now)),
    (10145, 1054, N'تقل وتبطؤ', NULL, NULL, 1, 3, DATEADD(MINUTE, -73440, @Now)),
    (10146, 1055, N'صوت موسيقي', NULL, NULL, 0, 1, DATEADD(MINUTE, -73440, @Now)),
    (10147, 1055, N'حرارة خفيفة', NULL, NULL, 1, 2, DATEADD(MINUTE, -73440, @Now)),
    (10148, 1055, N'ضوء ساطع', NULL, NULL, 0, 3, DATEADD(MINUTE, -73440, @Now)),
    (10149, 1056, N'صح', NULL, NULL, 0, 1, DATEADD(MINUTE, -73440, @Now)),
    (10150, 1056, N'خطأ', NULL, NULL, 1, 2, DATEADD(MINUTE, -73440, @Now)),
    (10151, 1057, N'يظل يعمل إلى الأبد بعد ضغطة واحدة', NULL, NULL, 0, 1, DATEADD(MINUTE, -73440, @Now)),
    (10152, 1057, N'يولّد الكهرباء بنفسه', NULL, NULL, 0, 2, DATEADD(MINUTE, -73440, @Now)),
    (10153, 1057, N'يغلق الدائرة فقط عند الضغط عليه', NULL, NULL, 1, 3, DATEADD(MINUTE, -73440, @Now)),
    (10154, 1058, N'تنفجر البطارية', NULL, NULL, 0, 1, DATEADD(MINUTE, -73440, @Now)),
    (10155, 1058, N'تنفتح الدائرة وتتوقف الكهرباء', NULL, NULL, 1, 2, DATEADD(MINUTE, -73440, @Now)),
    (10156, 1058, N'تستمر الكهرباء في المرور', NULL, NULL, 0, 3, DATEADD(MINUTE, -73440, @Now)),
    (10157, 1059, N'أذرع ألعاب الفيديو ولوحات المفاتيح', NULL, NULL, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (10158, 1059, N'الأسلاك النحاسية', NULL, NULL, 0, 2, DATEADD(MINUTE, -73440, @Now)),
    (10159, 1059, N'المصابيح الزجاجية', NULL, NULL, 0, 3, DATEADD(MINUTE, -73440, @Now)),
    (10160, 1060, N'صح', NULL, NULL, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (10161, 1060, N'خطأ', NULL, NULL, 0, 2, DATEADD(MINUTE, -73440, @Now)),
    (10162, 1061, N'دائرة مكسورة', NULL, NULL, 0, 1, DATEADD(MINUTE, -73440, @Now)),
    (10163, 1061, N'دائرة مغلقة (تشغيل ON)', NULL, NULL, 1, 2, DATEADD(MINUTE, -73440, @Now)),
    (10164, 1061, N'دائرة مفتوحة (إيقاف OFF)', NULL, NULL, 0, 3, DATEADD(MINUTE, -73440, @Now)),
    (10165, 1062, N'صح', NULL, NULL, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (10166, 1062, N'خطأ', NULL, NULL, 0, 2, DATEADD(MINUTE, -73440, @Now)),
    (10167, 1063, N'صح', NULL, NULL, 0, 1, DATEADD(MINUTE, -73440, @Now)),
    (10168, 1063, N'خطأ', NULL, NULL, 1, 2, DATEADD(MINUTE, -73440, @Now)),
    (10169, 1064, N'ضوء يلمع بألوان مختلفة', NULL, NULL, 0, 1, DATEADD(MINUTE, -73440, @Now)),
    (10170, 1064, N'أداة للتحكم في أمان الدائرة واتصالها', NULL, NULL, 1, 2, DATEADD(MINUTE, -73440, @Now)),
    (10171, 1064, N'مكوّن لزيادة حجم السلك', NULL, NULL, 0, 3, DATEADD(MINUTE, -73440, @Now)),
    (10172, 1065, N'صح', NULL, NULL, 0, 1, DATEADD(MINUTE, -73440, @Now)),
    (10173, 1065, N'خطأ', NULL, NULL, 1, 2, DATEADD(MINUTE, -73440, @Now)),
    (10174, 1066, N'مصباح', NULL, NULL, 0, 1, DATEADD(MINUTE, -73440, @Now)),
    (10175, 1066, N'مفتاح', NULL, NULL, 0, 2, DATEADD(MINUTE, -73440, @Now)),
    (10176, 1066, N'بطارية', NULL, NULL, 1, 3, DATEADD(MINUTE, -73440, @Now)),
    (10177, 1067, N'لا يتغير', NULL, NULL, 0, 1, DATEADD(MINUTE, -73440, @Now)),
    (10178, 1067, N'ينطفئ', NULL, NULL, 1, 2, DATEADD(MINUTE, -73440, @Now)),
    (10179, 1067, N'يزداد ضوءه', NULL, NULL, 0, 3, DATEADD(MINUTE, -73440, @Now)),
    (10180, 1069, NULL, N'/uploads/lessons/seed-opt-rubber.png', N'قطعة مطاط سوداء مستطيلة.', 0, 1, DATEADD(MINUTE, -73440, @Now)),
    (10181, 1069, NULL, N'/uploads/lessons/seed-opt-wood.png', N'قطعة خشب جاف بلون بني فاتح.', 0, 2, DATEADD(MINUTE, -73440, @Now)),
    (10182, 1069, NULL, N'/uploads/lessons/seed-opt-copper.png', N'سلك نحاسي لامع ملفوف على شكل حلقات.', 1, 3, DATEADD(MINUTE, -73440, @Now)),
    (10183, 1070, N'كرة', NULL, NULL, 0, 1, DATEADD(MINUTE, -73440, @Now)),
    (10184, 1070, N'مصباح', N'/uploads/lessons/seed-opt-bulb.png', N'مصباح كهربي مضيء باللون الأصفر.', 1, 2, DATEADD(MINUTE, -73440, @Now)),
    (10185, 1070, N'كتاب', NULL, NULL, 0, 3, DATEADD(MINUTE, -73440, @Now)),
    (10186, 1071, N'صح', NULL, NULL, 1, 1, DATEADD(MINUTE, -73440, @Now)),
    (10187, 1071, N'خطأ', NULL, NULL, 0, 2, DATEADD(MINUTE, -73440, @Now)),
    (10188, 1073, N'أحمر فقط', NULL, NULL, 0, 1, DATEADD(MINUTE, -73440, @Now)),
    (10189, 1073, N'يمكن أن يكون بألوان مختلفة', NULL, NULL, 1, 2, DATEADD(MINUTE, -73440, @Now));
SET IDENTITY_INSERT Assessment.QuestionOptions OFF;

INSERT INTO Assessment.QuestionOptionTranslations (QuestionOptionId, LanguageCode, OptionText)
VALUES
    (10001, N'ar', N'فقاعات الهواء'),
    (10001, N'en', N'Air bubbles'),
    (10002, N'ar', N'قطرات الماء'),
    (10002, N'en', N'Water droplets'),
    (10003, N'ar', N'الإلكترونات'),
    (10003, N'en', N'Electrons'),
    (10004, N'ar', N'محطات توليد الكهرباء'),
    (10004, N'en', N'Power plants'),
    (10005, N'ar', N'أشجار الحديقة'),
    (10005, N'en', N'Trees in the garden'),
    (10006, N'ar', N'المخبز'),
    (10006, N'en', N'The bakery'),
    (10007, N'ar', N'ضوء وصوت'),
    (10007, N'en', N'Light and sound'),
    (10008, N'ar', N'طعام وماء'),
    (10008, N'en', N'Food and water'),
    (10009, N'ar', N'ضوء وحرارة فقط'),
    (10009, N'en', N'Light and heat only'),
    (10010, N'ar', N'لأن الماء يقطع الكهرباء'),
    (10010, N'en', N'Because water turns off the electricity'),
    (10011, N'ar', N'لأن الماء يوصل الكهرباء بسرعة وهذا خطير'),
    (10011, N'en', N'Because water conducts electricity quickly and is dangerous'),
    (10012, N'ar', N'لأن المقبس سيتّسخ'),
    (10012, N'en', N'Because the outlet will get dirty'),
    (10013, N'ar', N'نفق تسير فيه الكهرباء'),
    (10013, N'en', N'A tunnel for electricity to travel through'),
    (10014, N'ar', N'مصدر الطاقة الذي يدفع الإلكترونات'),
    (10014, N'en', N'The power source that pushes electrons'),
    (10015, N'ar', N'زر لتشغيل اللعبة وإطفائها'),
    (10015, N'en', N'A button to turn the toy on and off'),
    (10016, N'ar', N'صح'),
    (10016, N'en', N'True'),
    (10017, N'ar', N'خطأ'),
    (10017, N'en', N'False'),
    (10018, N'ar', N'المصباح الزجاجي'),
    (10018, N'en', N'The glass light bulb'),
    (10019, N'ar', N'المفتاح الكهربي'),
    (10019, N'en', N'The electrical switch'),
    (10020, N'ar', N'السلك النحاسي'),
    (10020, N'en', N'The copper wire'),
    (10021, N'ar', N'لحمايتنا، لأن البلاستيك مادة عازلة لا توصل الكهرباء'),
    (10021, N'en', N'To protect us, because plastic is an insulating material that does not conduct electricity'),
    (10022, N'ar', N'فقط لتصبح الأسلاك مرنة'),
    (10022, N'en', N'Just to make the wires flexible'),
    (10023, N'ar', N'لأن البلاستيك يضيء في الظلام'),
    (10023, N'en', N'Because plastic glows in the dark'),
    (10024, N'ar', N'الثنائي الباعث للضوء (LED)'),
    (10024, N'en', N'LED (Light Emitting Diode)'),
    (10025, N'ar', N'زر الضغط'),
    (10025, N'en', N'Push Button'),
    (10026, N'ar', N'المقاومة'),
    (10026, N'en', N'Resistor'),
    (10027, N'ar', N'السلك النحاسي'),
    (10027, N'en', N'Copper wire'),
    (10028, N'ar', N'المقاومة'),
    (10028, N'en', N'Resistor'),
    (10029, N'ar', N'البطارية'),
    (10029, N'en', N'Battery'),
    (10030, N'ar', N'صح'),
    (10030, N'en', N'True'),
    (10031, N'ar', N'خطأ'),
    (10031, N'en', N'False'),
    (10032, N'ar', N'بمجرد أن نرفع أيدينا عنه'),
    (10032, N'en', N'As soon as we lift our hand off it'),
    (10033, N'ar', N'تلقائيًا دون ضغط'),
    (10033, N'en', N'Automatically without pressing'),
    (10034, N'ar', N'ما دمنا نضغط عليه'),
    (10034, N'en', N'As long as we keep pressing it'),
    (10035, N'ar', N'من طول أرجلها'),
    (10035, N'en', N'By the length of its leads'),
    (10036, N'ar', N'من الحلقات الملوّنة على جسمها'),
    (10036, N'en', N'By the colored bands on its body'),
    (10037, N'ar', N'من وزنها وثقلها'),
    (10037, N'en', N'By its weight and heaviness'),
    (10038, N'ar', N'صح'),
    (10038, N'en', N'True'),
    (10039, N'ar', N'خطأ'),
    (10039, N'en', N'False'),
    (10040, N'ar', N'لأنه يغيّر لون الضوء'),
    (10040, N'en', N'Because it changes the color of the light'),
    (10041, N'ar', N'لأنه يزيد قوة البطارية'),
    (10041, N'en', N'Because it increases the battery''s power'),
    (10042, N'ar', N'لأنه يمنع استهلاك طاقة البطارية إلا عند الحاجة'),
    (10042, N'en', N'Because it prevents battery power consumption except when needed'),
    (10043, N'ar', N'مقاومة + مفتاح بدون بطارية'),
    (10043, N'en', N'Resistor + switch without a battery'),
    (10044, N'ar', N'بطارية + مقاومة + زر ضغط + LED'),
    (10044, N'en', N'Battery + resistor + push-button switch + LED'),
    (10045, N'ar', N'بطارية + LED فقط'),
    (10045, N'en', N'Battery + LED only'),
    (10046, N'ar', N'من حركة الإلكترونات الصغيرة جدًا'),
    (10046, N'en', N'The flow of tiny electrons'),
    (10047, N'ar', N'من ماء الصنبور'),
    (10047, N'en', N'Tap water'),
    (10048, N'ar', N'من الهواء'),
    (10048, N'en', N'Air'),
    (10049, N'ar', N'صح'),
    (10049, N'en', N'True'),
    (10050, N'ar', N'خطأ'),
    (10050, N'en', N'False'),
    (10051, N'ar', N'الكتب الورقية'),
    (10051, N'en', N'Paper books'),
    (10052, N'ar', N'الأجهزة الكهربية والإلكترونية'),
    (10052, N'en', N'Electronic and electrical devices'),
    (10053, N'ar', N'الألعاب الخشبية'),
    (10053, N'en', N'Wooden toys'),
    (10054, N'ar', N'صح'),
    (10054, N'en', N'True'),
    (10055, N'ar', N'خطأ'),
    (10055, N'en', N'False'),
    (10056, N'ar', N'القطرات'),
    (10056, N'en', N'Droplets'),
    (10057, N'ar', N'الكرات'),
    (10057, N'en', N'Balls'),
    (10058, N'ar', N'الإلكترونات'),
    (10058, N'en', N'Electrons'),
    (10059, N'ar', N'صح'),
    (10059, N'en', N'True'),
    (10060, N'ar', N'خطأ'),
    (10060, N'en', N'False'),
    (10061, N'ar', N'في محطات توليد الكهرباء'),
    (10061, N'en', N'At power plants'),
    (10062, N'ar', N'في المخبز'),
    (10062, N'en', N'At the bakery'),
    (10063, N'ar', N'في الحدائق'),
    (10063, N'en', N'In parks'),
    (10064, N'ar', N'طعام وماء'),
    (10064, N'en', N'Food and water'),
    (10065, N'ar', N'ألعاب بلاستيكية'),
    (10065, N'en', N'Plastic toys'),
    (10066, N'ar', N'ضوء وحرارة وصوت'),
    (10066, N'en', N'Light, heat, and sound'),
    (10067, N'ar', N'دائرة مفتوحة'),
    (10067, N'en', N'An open circuit'),
    (10068, N'ar', N'دائرة كهربية مغلقة ومتصلة'),
    (10068, N'en', N'A closed, connected electric circuit'),
    (10069, N'ar', N'سلك مقطوع'),
    (10069, N'en', N'A broken wire'),
    (10070, N'ar', N'دائرة فيها سلك مقطوع'),
    (10070, N'en', N'A circuit with a broken wire'),
    (10071, N'ar', N'دائرة كاملة ومتصلة'),
    (10071, N'en', N'A complete, connected circuit'),
    (10072, N'ar', N'إنتاج الطاقة'),
    (10072, N'en', N'Providing energy'),
    (10073, N'ar', N'تغيير لون المصباح'),
    (10073, N'en', N'Changing the light bulb''s color'),
    (10074, N'ar', N'فتح الدائرة وغلقها للتحكم في الكهرباء'),
    (10074, N'en', N'Opening and closing the circuit to control electricity'),
    (10075, N'ar', N'صح'),
    (10075, N'en', N'True'),
    (10076, N'ar', N'خطأ'),
    (10076, N'en', N'False'),
    (10077, N'ar', N'نقل الإلكترونات والكهرباء'),
    (10077, N'en', N'Transporting electrons and electricity'),
    (10078, N'ar', N'إيقاف الكهرباء'),
    (10078, N'en', N'Stopping electricity'),
    (10079, N'ar', N'تبريد البطارية'),
    (10079, N'en', N'Cooling the battery'),
    (10080, N'ar', N'صح'),
    (10080, N'en', N'True'),
    (10081, N'ar', N'خطأ'),
    (10081, N'en', N'False'),
    (10082, N'ar', N'تنفجر البطارية'),
    (10082, N'en', N'The battery explodes'),
    (10083, N'ar', N'تتوقف الكهرباء وينطفئ المصباح'),
    (10083, N'en', N'Electricity stops and the light bulb turns off'),
    (10084, N'ar', N'يضيء المصباح بقوة'),
    (10084, N'en', N'The light bulb shines brightly'),
    (10085, N'ar', N'البطارية'),
    (10085, N'en', N'The battery'),
    (10086, N'ar', N'المفتاح'),
    (10086, N'en', N'The switch'),
    (10087, N'ar', N'السلك'),
    (10087, N'en', N'The wire'),
    (10088, N'ar', N'نعم، لا مشكلة'),
    (10088, N'en', N'Yes, it is fine'),
    (10089, N'ar', N'لا؛ الماء يوصل الكهرباء وهذا خطير'),
    (10089, N'en', N'No; water conducts electricity and it is dangerous'),
    (10090, N'ar', N'نلعب به'),
    (10090, N'en', N'Play with it'),
    (10091, N'ar', N'نبتعد عنه فورًا ونخبر شخصًا كبيرًا'),
    (10091, N'en', N'Stay away from it immediately and tell an adult'),
    (10092, N'ar', N'نلمسه بأظافرنا'),
    (10092, N'en', N'Touch it with our fingernails'),
    (10093, N'ar', N'صح'),
    (10093, N'en', N'True'),
    (10094, N'ar', N'خطأ'),
    (10094, N'en', N'False'),
    (10095, N'ar', N'فقط لتصبح ملوّنة'),
    (10095, N'en', N'Just to make them colorful'),
    (10096, N'ar', N'لتصبح أثقل'),
    (10096, N'en', N'To make them heavier'),
    (10097, N'ar', N'لأن البلاستيك يعزل الكهرباء ويحمينا'),
    (10097, N'en', N'Because plastic insulates electricity and protects us'),
    (10098, N'ar', N'صح'),
    (10098, N'en', N'True'),
    (10099, N'ar', N'خطأ'),
    (10099, N'en', N'False'),
    (10100, N'ar', N'القابس البلاستيكي نفسه'),
    (10100, N'en', N'The plastic plug itself');
INSERT INTO Assessment.QuestionOptionTranslations (QuestionOptionId, LanguageCode, OptionText)
VALUES
    (10101, N'ar', N'السلك ونشدّه بقوة'),
    (10101, N'en', N'The cord, and pull it forcefully'),
    (10102, N'ar', N'الحائط'),
    (10102, N'en', N'The wall'),
    (10103, N'ar', N'صح'),
    (10103, N'en', N'True'),
    (10104, N'ar', N'خطأ'),
    (10104, N'en', N'False'),
    (10105, N'ar', N'لا أحد'),
    (10105, N'en', N'No one'),
    (10106, N'ar', N'كهربائي مختص أو شخص كبير'),
    (10106, N'en', N'A professional electrician or an adult'),
    (10107, N'ar', N'الأطفال الصغار'),
    (10107, N'en', N'Young children'),
    (10108, N'ar', N'الثنائي الباعث للضوء (Light-Emitting Diode)'),
    (10108, N'en', N'Light-Emitting Diode'),
    (10109, N'ar', N'جهاز كهربي صغير (Little Electric Device)'),
    (10109, N'en', N'Little Electric Device'),
    (10110, N'ar', N'جهاز كهربي متصل (Linked Electric Device)'),
    (10110, N'en', N'Linked Electric Device'),
    (10111, N'ar', N'ليثبت في الأرض'),
    (10111, N'en', N'To anchor it into the ground'),
    (10112, N'ar', N'إنها مجرد زينة'),
    (10112, N'en', N'It is just a decorative feature'),
    (10113, N'ar', N'لأن الكهرباء تمر فيه في اتجاه واحد فقط'),
    (10113, N'en', N'Because electricity flows through it in only one direction'),
    (10114, N'ar', N'سينفجر فورًا'),
    (10114, N'en', N'It will explode immediately'),
    (10115, N'ar', N'لن يضيء لأن الكهرباء لا تمر في الاتجاه المعاكس'),
    (10115, N'en', N'It will not light up because electricity does not flow in reverse'),
    (10116, N'ar', N'سيضيء بلون مختلف'),
    (10116, N'en', N'It will light up in a different color'),
    (10117, N'ar', N'صح'),
    (10117, N'en', N'True'),
    (10118, N'ar', N'خطأ'),
    (10118, N'en', N'False'),
    (10119, N'ar', N'الطرف السالب (الكاثود −)'),
    (10119, N'en', N'Negative terminal (Cathode -)'),
    (10120, N'ar', N'الطرف المحايد'),
    (10120, N'en', N'Neutral terminal'),
    (10121, N'ar', N'الطرف الموجب (الأنود +)'),
    (10121, N'en', N'Positive terminal (Anode +)'),
    (10122, N'ar', N'صح'),
    (10122, N'en', N'True'),
    (10123, N'ar', N'خطأ'),
    (10123, N'en', N'False'),
    (10124, N'ar', N'شاشات التلفاز وإشارات المرور وأضواء الألعاب'),
    (10124, N'en', N'TV screens, traffic lights, and toy lights'),
    (10125, N'ar', N'الأدوات الخشبية'),
    (10125, N'en', N'Wooden utensils'),
    (10126, N'ar', N'الملابس القطنية'),
    (10126, N'en', N'Cotton clothing'),
    (10127, N'ar', N'صح'),
    (10127, N'en', N'True'),
    (10128, N'ar', N'خطأ'),
    (10128, N'en', N'False'),
    (10129, N'ar', N'إضاءة الدائرة'),
    (10129, N'en', N'Illuminating the circuit'),
    (10130, N'ar', N'حماية المكونات وتنظيم مرور الكهرباء'),
    (10130, N'en', N'Protecting components and regulating the flow of electricity'),
    (10131, N'ar', N'زيادة القدرة الكهربية'),
    (10131, N'en', N'Increasing electrical power'),
    (10132, N'ar', N'قد يحترق بسبب التيار القوي'),
    (10132, N'en', N'It may burn out due to the strong current'),
    (10133, N'ar', N'سيضيء إلى الأبد'),
    (10133, N'en', N'It will light up forever'),
    (10134, N'ar', N'لن يتأثر'),
    (10134, N'en', N'It will remain unaffected'),
    (10135, N'ar', N'لتبدو جميلة'),
    (10135, N'en', N'To make it look attractive'),
    (10136, N'ar', N'لتحديد الطرفين الموجب والسالب'),
    (10136, N'en', N'To indicate positive and negative terminals'),
    (10137, N'ar', N'تحديد قيمة المقاومة'),
    (10137, N'en', N'To determine the resistor''s value and rating'),
    (10138, N'ar', N'صح'),
    (10138, N'en', N'True'),
    (10139, N'ar', N'خطأ'),
    (10139, N'en', N'False'),
    (10140, N'ar', N'الأوم (Ω)'),
    (10140, N'en', N'Ohm (Ω)'),
    (10141, N'ar', N'الكيلوجرام'),
    (10141, N'en', N'Kilogram'),
    (10142, N'ar', N'المتر'),
    (10142, N'en', N'Meter'),
    (10143, N'ar', N'تزيد وتسرع'),
    (10143, N'en', N'Increases and speeds up'),
    (10144, N'ar', N'تتوقف وتنفجر الدائرة'),
    (10144, N'en', N'Stops, causing the circuit to explode'),
    (10145, N'ar', N'تقل وتبطؤ'),
    (10145, N'en', N'Decreases and slows down'),
    (10146, N'ar', N'صوت موسيقي'),
    (10146, N'en', N'Musical sound'),
    (10147, N'ar', N'حرارة خفيفة'),
    (10147, N'en', N'Mild heat'),
    (10148, N'ar', N'ضوء ساطع'),
    (10148, N'en', N'Bright light'),
    (10149, N'ar', N'صح'),
    (10149, N'en', N'True'),
    (10150, N'ar', N'خطأ'),
    (10150, N'en', N'False'),
    (10151, N'ar', N'يظل يعمل إلى الأبد بعد ضغطة واحدة'),
    (10151, N'en', N'It keeps working forever after a single press'),
    (10152, N'ar', N'يولّد الكهرباء بنفسه'),
    (10152, N'en', N'It generates its own electricity'),
    (10153, N'ar', N'يغلق الدائرة فقط عند الضغط عليه'),
    (10153, N'en', N'It closes the circuit only when pressed'),
    (10154, N'ar', N'تنفجر البطارية'),
    (10154, N'en', N'The battery explodes'),
    (10155, N'ar', N'تنفتح الدائرة وتتوقف الكهرباء'),
    (10155, N'en', N'The circuit opens and the electricity stops'),
    (10156, N'ar', N'تستمر الكهرباء في المرور'),
    (10156, N'en', N'Electricity continues to flow'),
    (10157, N'ar', N'أذرع ألعاب الفيديو ولوحات المفاتيح'),
    (10157, N'en', N'Video game controllers and keyboards'),
    (10158, N'ar', N'الأسلاك النحاسية'),
    (10158, N'en', N'Copper wires'),
    (10159, N'ar', N'المصابيح الزجاجية'),
    (10159, N'en', N'Glass light bulbs'),
    (10160, N'ar', N'صح'),
    (10160, N'en', N'True'),
    (10161, N'ar', N'خطأ'),
    (10161, N'en', N'False'),
    (10162, N'ar', N'دائرة مكسورة'),
    (10162, N'en', N'Broken circuit'),
    (10163, N'ar', N'دائرة مغلقة (تشغيل ON)'),
    (10163, N'en', N'Closed circuit (ON)'),
    (10164, N'ar', N'دائرة مفتوحة (إيقاف OFF)'),
    (10164, N'en', N'Open circuit (OFF)'),
    (10165, N'ar', N'صح'),
    (10165, N'en', N'True'),
    (10166, N'ar', N'خطأ'),
    (10166, N'en', N'False'),
    (10167, N'ar', N'صح'),
    (10167, N'en', N'True'),
    (10168, N'ar', N'خطأ'),
    (10168, N'en', N'False'),
    (10169, N'ar', N'ضوء يلمع بألوان مختلفة'),
    (10169, N'en', N'A light that shines in different colors'),
    (10170, N'ar', N'أداة للتحكم في أمان الدائرة واتصالها'),
    (10170, N'en', N'A device to control circuit safety and connectivity'),
    (10171, N'ar', N'مكوّن لزيادة حجم السلك'),
    (10171, N'en', N'A component to increase wire size'),
    (10172, N'ar', N'صح'),
    (10172, N'en', N'True'),
    (10173, N'ar', N'خطأ'),
    (10173, N'en', N'False'),
    (10174, N'ar', N'مصباح'),
    (10174, N'en', N'A lamp'),
    (10175, N'ar', N'مفتاح'),
    (10175, N'en', N'A switch'),
    (10176, N'ar', N'بطارية'),
    (10176, N'en', N'A battery'),
    (10177, N'ar', N'لا يتغير'),
    (10178, N'ar', N'ينطفئ'),
    (10179, N'ar', N'يزداد ضوءه'),
    (10180, N'ar', NULL),
    (10180, N'en', NULL),
    (10181, N'ar', NULL),
    (10181, N'en', NULL),
    (10182, N'ar', NULL),
    (10182, N'en', NULL),
    (10183, N'ar', N'كرة'),
    (10183, N'en', N'A ball'),
    (10184, N'ar', N'مصباح'),
    (10184, N'en', N'A lamp'),
    (10185, N'ar', N'كتاب'),
    (10185, N'en', N'A book'),
    (10186, N'ar', N'صح'),
    (10186, N'en', N'True'),
    (10187, N'ar', N'خطأ'),
    (10187, N'en', N'False'),
    (10188, N'ar', N'أحمر فقط'),
    (10188, N'en', N'Only red'),
    (10189, N'ar', N'يمكن أن يكون بألوان مختلفة'),
    (10189, N'en', N'It can be many different colours');

/* ============================== Assessment: quiz history ============================== */
SET IDENTITY_INSERT Assessment.QuizAttempts ON;
INSERT INTO Assessment.QuizAttempts (Id, QuizId, UserId, QuestionsAnsweredCount, TotalQuestionsAtAttempt, CorrectAnswersCount, ScorePercentage, Status, StartedAt, CompletedAt, PreviousAttemptId)
VALUES
    (100001, 101, '5EED0000-0000-4000-8000-000000000011', 8, 8, 5, 62.50, N'Completed', DATEADD(MINUTE, -28800, @Now), DATEADD(MINUTE, -28786, @Now), NULL),
    (100002, 110, '5EED0000-0000-4000-8000-000000000011', 4, 4, 2, 75.00, N'Completed', DATEADD(MINUTE, -25920, @Now), DATEADD(MINUTE, -25911, @Now), NULL),
    (100003, 110, '5EED0000-0000-4000-8000-000000000011', 1, 1, 1, 100.00, N'Completed', DATEADD(MINUTE, -25890, @Now), DATEADD(MINUTE, -25888, @Now), 100002),
    (100004, 107, '5EED0000-0000-4000-8000-000000000011', 8, 8, 6, 75.00, N'Completed', DATEADD(MINUTE, -14400, @Now), DATEADD(MINUTE, -14393, @Now), NULL),
    (100005, 105, '5EED0000-0000-4000-8000-000000000011', 8, 8, 8, 100.00, N'Completed', DATEADD(MINUTE, -7200, @Now), DATEADD(MINUTE, -7194, @Now), NULL),
    (100006, 101, '5EED0000-0000-4000-8000-000000000012', 8, 8, 6, 75.00, N'Completed', DATEADD(MINUTE, -21600, @Now), DATEADD(MINUTE, -21589, @Now), NULL),
    (100007, 110, '5EED0000-0000-4000-8000-000000000012', 4, 4, 2, 75.00, N'Completed', DATEADD(MINUTE, -4320, @Now), DATEADD(MINUTE, -4310, @Now), NULL),
    (100008, 111, '5EED0000-0000-4000-8000-000000000012', 4, 4, 3, 100.00, N'Completed', DATEADD(MINUTE, -1440, @Now), DATEADD(MINUTE, -1435, @Now), NULL),
    (100009, 111, '5EED0000-0000-4000-8000-000000000014', 4, 4, 2, 75.00, N'Completed', DATEADD(MINUTE, -2880, @Now), DATEADD(MINUTE, -2876, @Now), NULL),
    (100010, 104, '5EED0000-0000-4000-8000-000000000011', 8, 8, 7, 87.50, N'Completed', DATEADD(MINUTE, -11520, @Now), DATEADD(MINUTE, -11515, @Now), NULL),
    (100011, 108, '5EED0000-0000-4000-8000-000000000012', 8, 8, 7, 87.50, N'Completed', DATEADD(MINUTE, -8640, @Now), DATEADD(MINUTE, -8634, @Now), NULL),
    (100012, 103, '5EED0000-0000-4000-8000-000000000012', 8, 8, 7, 87.50, N'Completed', DATEADD(MINUTE, -5760, @Now), DATEADD(MINUTE, -5752, @Now), NULL);
SET IDENTITY_INSERT Assessment.QuizAttempts OFF;

INSERT INTO Assessment.QuizAttemptQuestions (QuizAttemptId, QuestionId, TopicId, Difficulty, CorrectOptionId, QuestionType, Points, CreatedAt)
VALUES
    (100001, 1001, 101, N'Medium', 10003, N'MultipleChoice', 1, DATEADD(MINUTE, -28800, @Now)),
    (100001, 1002, 101, N'Medium', 10004, N'MultipleChoice', 1, DATEADD(MINUTE, -28800, @Now)),
    (100001, 1003, 101, N'Medium', 10007, N'MultipleChoice', 1, DATEADD(MINUTE, -28800, @Now)),
    (100001, 1004, 103, N'Medium', 10011, N'MultipleChoice', 1, DATEADD(MINUTE, -28800, @Now)),
    (100001, 1009, 104, N'Medium', 10024, N'MultipleChoice', 1, DATEADD(MINUTE, -28800, @Now)),
    (100001, 1010, 105, N'Medium', 10028, N'MultipleChoice', 1, DATEADD(MINUTE, -28800, @Now)),
    (100001, 1011, 104, N'Medium', 10031, N'TrueFalse', 1, DATEADD(MINUTE, -28800, @Now)),
    (100001, 1012, 106, N'Medium', 10034, N'MultipleChoice', 1, DATEADD(MINUTE, -28800, @Now)),
    (100002, 1065, 102, N'Medium', 10173, N'TrueFalse', 1, DATEADD(MINUTE, -25920, @Now)),
    (100002, 1066, 102, N'Easy', 10176, N'MultipleChoice', 1, DATEADD(MINUTE, -25920, @Now)),
    (100002, 1067, 102, N'Medium', 10178, N'MultipleChoice', 2, DATEADD(MINUTE, -25920, @Now)),
    (100002, 1068, 102, N'Medium', NULL, N'Essay', 2, DATEADD(MINUTE, -25920, @Now)),
    (100003, 1066, 102, N'Easy', 10176, N'MultipleChoice', 1, DATEADD(MINUTE, -25890, @Now)),
    (100004, 1041, 104, N'Easy', 10108, N'MultipleChoice', 1, DATEADD(MINUTE, -14400, @Now)),
    (100004, 1042, 104, N'Easy', 10113, N'MultipleChoice', 1, DATEADD(MINUTE, -14400, @Now)),
    (100004, 1043, 104, N'Easy', 10115, N'MultipleChoice', 1, DATEADD(MINUTE, -14400, @Now)),
    (100004, 1044, 104, N'Easy', 10117, N'TrueFalse', 1, DATEADD(MINUTE, -14400, @Now)),
    (100004, 1045, 104, N'Easy', 10121, N'MultipleChoice', 1, DATEADD(MINUTE, -14400, @Now)),
    (100004, 1046, 104, N'Easy', 10123, N'TrueFalse', 1, DATEADD(MINUTE, -14400, @Now)),
    (100004, 1047, 104, N'Easy', 10124, N'MultipleChoice', 1, DATEADD(MINUTE, -14400, @Now)),
    (100004, 1048, 104, N'Easy', 10128, N'TrueFalse', 1, DATEADD(MINUTE, -14400, @Now)),
    (100005, 1025, 102, N'Easy', 10068, N'MultipleChoice', 1, DATEADD(MINUTE, -7200, @Now)),
    (100005, 1026, 102, N'Easy', 10070, N'MultipleChoice', 1, DATEADD(MINUTE, -7200, @Now)),
    (100005, 1027, 102, N'Easy', 10074, N'MultipleChoice', 1, DATEADD(MINUTE, -7200, @Now)),
    (100005, 1028, 102, N'Easy', 10076, N'TrueFalse', 1, DATEADD(MINUTE, -7200, @Now)),
    (100005, 1029, 102, N'Easy', 10077, N'MultipleChoice', 1, DATEADD(MINUTE, -7200, @Now)),
    (100005, 1030, 102, N'Easy', 10080, N'TrueFalse', 1, DATEADD(MINUTE, -7200, @Now)),
    (100005, 1031, 102, N'Easy', 10083, N'MultipleChoice', 1, DATEADD(MINUTE, -7200, @Now)),
    (100005, 1032, 102, N'Easy', 10085, N'MultipleChoice', 1, DATEADD(MINUTE, -7200, @Now)),
    (100006, 1001, 101, N'Medium', 10003, N'MultipleChoice', 1, DATEADD(MINUTE, -21600, @Now)),
    (100006, 1002, 101, N'Medium', 10004, N'MultipleChoice', 1, DATEADD(MINUTE, -21600, @Now)),
    (100006, 1003, 101, N'Medium', 10007, N'MultipleChoice', 1, DATEADD(MINUTE, -21600, @Now)),
    (100006, 1004, 103, N'Medium', 10011, N'MultipleChoice', 1, DATEADD(MINUTE, -21600, @Now)),
    (100006, 1009, 104, N'Medium', 10024, N'MultipleChoice', 1, DATEADD(MINUTE, -21600, @Now)),
    (100006, 1010, 105, N'Medium', 10028, N'MultipleChoice', 1, DATEADD(MINUTE, -21600, @Now)),
    (100006, 1011, 104, N'Medium', 10031, N'TrueFalse', 1, DATEADD(MINUTE, -21600, @Now)),
    (100006, 1012, 106, N'Medium', 10034, N'MultipleChoice', 1, DATEADD(MINUTE, -21600, @Now)),
    (100007, 1065, 102, N'Medium', 10173, N'TrueFalse', 1, DATEADD(MINUTE, -4320, @Now)),
    (100007, 1066, 102, N'Easy', 10176, N'MultipleChoice', 1, DATEADD(MINUTE, -4320, @Now)),
    (100007, 1067, 102, N'Medium', 10178, N'MultipleChoice', 2, DATEADD(MINUTE, -4320, @Now)),
    (100007, 1068, 102, N'Medium', NULL, N'Essay', 2, DATEADD(MINUTE, -4320, @Now)),
    (100008, 1069, 103, N'Medium', 10182, N'MultipleChoice', 2, DATEADD(MINUTE, -1440, @Now)),
    (100008, 1070, 101, N'Easy', 10184, N'MultipleChoice', 1, DATEADD(MINUTE, -1440, @Now)),
    (100008, 1071, 103, N'Hard', 10186, N'TrueFalse', 1, DATEADD(MINUTE, -1440, @Now)),
    (100008, 1072, 103, N'Medium', NULL, N'Essay', 3, DATEADD(MINUTE, -1440, @Now)),
    (100009, 1069, 103, N'Medium', 10182, N'MultipleChoice', 2, DATEADD(MINUTE, -2880, @Now)),
    (100009, 1070, 101, N'Easy', 10184, N'MultipleChoice', 1, DATEADD(MINUTE, -2880, @Now)),
    (100009, 1071, 103, N'Hard', 10186, N'TrueFalse', 1, DATEADD(MINUTE, -2880, @Now)),
    (100009, 1072, 103, N'Medium', NULL, N'Essay', 3, DATEADD(MINUTE, -2880, @Now)),
    (100010, 1017, 101, N'Easy', 10046, N'MultipleChoice', 1, DATEADD(MINUTE, -11520, @Now)),
    (100010, 1018, 101, N'Easy', 10049, N'TrueFalse', 1, DATEADD(MINUTE, -11520, @Now)),
    (100010, 1019, 101, N'Easy', 10052, N'MultipleChoice', 1, DATEADD(MINUTE, -11520, @Now)),
    (100010, 1020, 101, N'Easy', 10054, N'TrueFalse', 1, DATEADD(MINUTE, -11520, @Now)),
    (100010, 1021, 101, N'Easy', 10058, N'MultipleChoice', 1, DATEADD(MINUTE, -11520, @Now)),
    (100010, 1022, 101, N'Easy', 10060, N'TrueFalse', 1, DATEADD(MINUTE, -11520, @Now)),
    (100010, 1023, 101, N'Easy', 10061, N'MultipleChoice', 1, DATEADD(MINUTE, -11520, @Now)),
    (100010, 1024, 101, N'Easy', 10066, N'MultipleChoice', 1, DATEADD(MINUTE, -11520, @Now)),
    (100011, 1049, 105, N'Easy', 10130, N'MultipleChoice', 1, DATEADD(MINUTE, -8640, @Now)),
    (100011, 1050, 105, N'Easy', 10132, N'MultipleChoice', 1, DATEADD(MINUTE, -8640, @Now)),
    (100011, 1051, 105, N'Easy', 10137, N'MultipleChoice', 1, DATEADD(MINUTE, -8640, @Now)),
    (100011, 1052, 105, N'Easy', 10139, N'TrueFalse', 1, DATEADD(MINUTE, -8640, @Now)),
    (100011, 1053, 105, N'Easy', 10140, N'MultipleChoice', 1, DATEADD(MINUTE, -8640, @Now)),
    (100011, 1054, 105, N'Easy', 10145, N'MultipleChoice', 1, DATEADD(MINUTE, -8640, @Now)),
    (100011, 1055, 105, N'Easy', 10147, N'MultipleChoice', 1, DATEADD(MINUTE, -8640, @Now)),
    (100011, 1056, 105, N'Easy', 10150, N'TrueFalse', 1, DATEADD(MINUTE, -8640, @Now)),
    (100012, 1009, 104, N'Medium', 10024, N'MultipleChoice', 1, DATEADD(MINUTE, -5760, @Now)),
    (100012, 1010, 105, N'Medium', 10028, N'MultipleChoice', 1, DATEADD(MINUTE, -5760, @Now)),
    (100012, 1011, 104, N'Medium', 10031, N'TrueFalse', 1, DATEADD(MINUTE, -5760, @Now)),
    (100012, 1012, 106, N'Medium', 10034, N'MultipleChoice', 1, DATEADD(MINUTE, -5760, @Now)),
    (100012, 1013, 105, N'Medium', 10036, N'MultipleChoice', 1, DATEADD(MINUTE, -5760, @Now)),
    (100012, 1014, 105, N'Medium', 10038, N'TrueFalse', 1, DATEADD(MINUTE, -5760, @Now)),
    (100012, 1015, 106, N'Medium', 10042, N'MultipleChoice', 1, DATEADD(MINUTE, -5760, @Now)),
    (100012, 1016, NULL, N'Medium', 10044, N'MultipleChoice', 1, DATEADD(MINUTE, -5760, @Now));

SET IDENTITY_INSERT Assessment.QuizAttemptMistakes ON;
INSERT INTO Assessment.QuizAttemptMistakes (Id, QuizAttemptId, QuestionId, SelectedOptionId, CreatedAt)
VALUES
    (100001, 100001, 1003, 10008, DATEADD(MINUTE, -28786, @Now)),
    (100002, 100001, 1009, 10025, DATEADD(MINUTE, -28786, @Now)),
    (100003, 100001, 1011, 10030, DATEADD(MINUTE, -28786, @Now)),
    (100004, 100002, 1066, 10174, DATEADD(MINUTE, -25911, @Now)),
    (100005, 100004, 1043, 10114, DATEADD(MINUTE, -14393, @Now)),
    (100006, 100004, 1046, 10122, DATEADD(MINUTE, -14393, @Now)),
    (100007, 100006, 1002, 10005, DATEADD(MINUTE, -21589, @Now)),
    (100008, 100006, 1012, 10032, DATEADD(MINUTE, -21589, @Now)),
    (100009, 100007, 1065, 10172, DATEADD(MINUTE, -4310, @Now)),
    (100010, 100009, 1071, 10187, DATEADD(MINUTE, -2876, @Now)),
    (100011, 100010, 1022, 10059, DATEADD(MINUTE, -11515, @Now)),
    (100012, 100011, 1052, 10138, DATEADD(MINUTE, -8634, @Now)),
    (100013, 100012, 1011, 10030, DATEADD(MINUTE, -5752, @Now));
SET IDENTITY_INSERT Assessment.QuizAttemptMistakes OFF;

INSERT INTO Assessment.QuizAttemptEssayAnswers (QuizAttemptId, QuestionId, AnswerText, Status, AwardedPoints, Feedback, GradedBy, GradedAt, CreatedAt, LanguageCode, MaxPoints, AiOutcome, AiEvaluationAttempts, AiLastAttemptAt, AiConfidence)
VALUES
    (100002, 1068, N'لما بنفتح المفتاح اللمبة بتطفي عشان الكهربا اتقطعت، ولما نقفله بتنور تاني.', N'Graded', 2, N'إجابة رائعة! عندما نفتح المفتاح تنقطع الدائرة فينطفئ المصباح، وعندما نغلقه تكتمل الدائرة فيضيء من جديد.', N'Ai', DATEADD(MINUTE, -25910, @Now), DATEADD(MINUTE, -25911, @Now), N'ar', 2, N'Accepted', 1, DATEADD(MINUTE, -25910, @Now), 0.91),
    (100007, 1068, N'لو فتحنا المفتاح النور بيقفل، ولو قفلناه بينور تاني عشان الدايرة بتكمل.', N'Pending', NULL, NULL, NULL, NULL, DATEADD(MINUTE, -4310, @Now), N'ar', 2, NULL, 0, NULL, NULL),
    (100008, 1072, N'مش عارفة', N'NotGraded', NULL, NULL, NULL, NULL, DATEADD(MINUTE, -1435, @Now), N'ar', 3, N'Declined', 1, DATEADD(MINUTE, -1434, @Now), NULL),
    (100009, 1072, N'عشان ممكن تكهربنا.', N'Graded', 1, N'صحيح، السلك المكشوف قد يسبب صدمة كهربية. أضف ماذا يجب أن نفعل عندما نراه: نبتعد عنه ونخبر شخصًا كبيرًا.', N'Ai', DATEADD(MINUTE, -2875, @Now), DATEADD(MINUTE, -2876, @Now), N'ar', 3, N'Accepted', 1, DATEADD(MINUTE, -2875, @Now), 0.84);

INSERT INTO Assessment.QuestionHints (QuizAttemptId, QuestionId, QuizAttemptMistakeId, HintText, HintSequence, AttemptNumber, LanguageCode, GeneratedAt)
VALUES
    (100002, 1066, NULL, N'انظر إلى العلامتين + و − على طرفي هذا المكوّن: ما الذي يمدّ الدائرة بالطاقة؟', 1, 1, N'ar', DATEADD(MINUTE, -25918, @Now)),
    (100002, 1066, 100004, N'هذا المكوّن مخزن صغير للطاقة نضعه في الألعاب وجهاز التحكم، وله طرف موجب وطرف سالب.', 2, NULL, N'ar', DATEADD(MINUTE, -25911, @Now)),
    (100004, 1043, NULL, N'فكّر في الشارع ذي الاتجاه الواحد: هل تستطيع السيارات أن تسير فيه بالعكس؟', 1, 1, N'ar', DATEADD(MINUTE, -14398, @Now)),
    (100004, 1043, NULL, N'تذكّر أن الكهرباء في الـ LED تسير في اتجاه واحد فقط، من الرجل الطويلة إلى الرجل القصيرة.', 2, 2, N'ar', DATEADD(MINUTE, -14398, @Now)),
    (100004, 1043, 100005, N'جرّب أن تتخيل ماء يحاول أن يصعد في منزلق مائي من الأسفل إلى الأعلى.', 3, NULL, N'ar', DATEADD(MINUTE, -14393, @Now)),
    (100004, 1046, 100006, N'فكّر: هل تشعر بسخونة عندما تلمس أضواء لعبتك المضيئة؟', 1, NULL, N'ar', DATEADD(MINUTE, -14393, @Now)),
    (100007, 1065, 100009, N'انظر جيدًا إلى السلك في أسفل الصورة: هل الطريق متصل من أوله إلى آخره؟', 1, NULL, N'ar', DATEADD(MINUTE, -4310, @Now)),
    (100009, 1071, 100010, N'فكّر في الفرق بين الماء المقطّر وماء الصنبور الذي تذوب فيه أملاح.', 1, NULL, N'ar', DATEADD(MINUTE, -2876, @Now)),
    (100010, 1022, 100011, N'فكّر: عندما يضيء المصباح، هل نرى ما يتحرك داخل السلك أم نرى الضوء فقط؟', 1, NULL, N'ar', DATEADD(MINUTE, -11515, @Now)),
    (100011, 1052, 100012, N'قارن بين المقاومة والـ LED: أيهما له رجل طويلة ورجل قصيرة؟', 1, NULL, N'ar', DATEADD(MINUTE, -8634, @Now));

INSERT INTO Assessment.UserPlacements (UserId, QuizAttemptId, PlacedLevelId, ScorePercentage, PassPercentage, PlacedAt)
VALUES
    ('5EED0000-0000-4000-8000-000000000011', 100001, 102, 62.50, 75, DATEADD(MINUTE, -28786, @Now)),
    ('5EED0000-0000-4000-8000-000000000012', 100006, 102, 75.00, 75, DATEADD(MINUTE, -21589, @Now));

INSERT INTO Assessment.UserTopicStats (UserId, TopicId, Difficulty, QuestionsAnsweredCount, CorrectCount, HintsUsedCount, LastQuizAttemptId, LastPracticedAt, UpdatedAt)
VALUES
    ('5EED0000-0000-4000-8000-000000000011', 101, N'Easy', 8, 7, 1, 100010, DATEADD(MINUTE, -11515, @Now), DATEADD(MINUTE, -11515, @Now)),
    ('5EED0000-0000-4000-8000-000000000011', 101, N'Medium', 3, 2, 0, 100001, DATEADD(MINUTE, -28786, @Now), DATEADD(MINUTE, -28786, @Now)),
    ('5EED0000-0000-4000-8000-000000000011', 102, N'Easy', 10, 9, 2, 100005, DATEADD(MINUTE, -7194, @Now), DATEADD(MINUTE, -7194, @Now)),
    ('5EED0000-0000-4000-8000-000000000011', 102, N'Medium', 2, 2, 0, 100002, DATEADD(MINUTE, -25911, @Now), DATEADD(MINUTE, -25911, @Now)),
    ('5EED0000-0000-4000-8000-000000000011', 103, N'Medium', 1, 1, 0, 100001, DATEADD(MINUTE, -28786, @Now), DATEADD(MINUTE, -28786, @Now)),
    ('5EED0000-0000-4000-8000-000000000011', 104, N'Easy', 8, 6, 4, 100004, DATEADD(MINUTE, -14393, @Now), DATEADD(MINUTE, -14393, @Now)),
    ('5EED0000-0000-4000-8000-000000000011', 104, N'Medium', 2, 0, 0, 100001, DATEADD(MINUTE, -28786, @Now), DATEADD(MINUTE, -28786, @Now)),
    ('5EED0000-0000-4000-8000-000000000011', 105, N'Medium', 1, 1, 0, 100001, DATEADD(MINUTE, -28786, @Now), DATEADD(MINUTE, -28786, @Now)),
    ('5EED0000-0000-4000-8000-000000000011', 106, N'Medium', 1, 1, 0, 100001, DATEADD(MINUTE, -28786, @Now), DATEADD(MINUTE, -28786, @Now)),
    ('5EED0000-0000-4000-8000-000000000012', 101, N'Easy', 1, 1, 0, 100008, DATEADD(MINUTE, -1435, @Now), DATEADD(MINUTE, -1435, @Now)),
    ('5EED0000-0000-4000-8000-000000000012', 101, N'Medium', 3, 2, 0, 100006, DATEADD(MINUTE, -21589, @Now), DATEADD(MINUTE, -21589, @Now)),
    ('5EED0000-0000-4000-8000-000000000012', 102, N'Easy', 1, 1, 0, 100007, DATEADD(MINUTE, -4310, @Now), DATEADD(MINUTE, -4310, @Now)),
    ('5EED0000-0000-4000-8000-000000000012', 102, N'Medium', 2, 1, 1, 100007, DATEADD(MINUTE, -4310, @Now), DATEADD(MINUTE, -4310, @Now)),
    ('5EED0000-0000-4000-8000-000000000012', 103, N'Hard', 1, 1, 0, 100008, DATEADD(MINUTE, -1435, @Now), DATEADD(MINUTE, -1435, @Now)),
    ('5EED0000-0000-4000-8000-000000000012', 103, N'Medium', 2, 2, 0, 100008, DATEADD(MINUTE, -1435, @Now), DATEADD(MINUTE, -1435, @Now)),
    ('5EED0000-0000-4000-8000-000000000012', 104, N'Medium', 4, 3, 0, 100012, DATEADD(MINUTE, -5752, @Now), DATEADD(MINUTE, -5752, @Now)),
    ('5EED0000-0000-4000-8000-000000000012', 105, N'Easy', 8, 7, 1, 100011, DATEADD(MINUTE, -8634, @Now), DATEADD(MINUTE, -8634, @Now)),
    ('5EED0000-0000-4000-8000-000000000012', 105, N'Medium', 4, 4, 0, 100012, DATEADD(MINUTE, -5752, @Now), DATEADD(MINUTE, -5752, @Now)),
    ('5EED0000-0000-4000-8000-000000000012', 106, N'Medium', 3, 2, 0, 100012, DATEADD(MINUTE, -5752, @Now), DATEADD(MINUTE, -5752, @Now)),
    ('5EED0000-0000-4000-8000-000000000014', 101, N'Easy', 1, 1, 0, 100009, DATEADD(MINUTE, -2876, @Now), DATEADD(MINUTE, -2876, @Now)),
    ('5EED0000-0000-4000-8000-000000000014', 103, N'Hard', 1, 0, 1, 100009, DATEADD(MINUTE, -2876, @Now), DATEADD(MINUTE, -2876, @Now)),
    ('5EED0000-0000-4000-8000-000000000014', 103, N'Medium', 1, 1, 0, 100009, DATEADD(MINUTE, -2876, @Now), DATEADD(MINUTE, -2876, @Now));

COMMIT TRANSACTION;

PRINT N'seed: development data created. Admin login: admin@volt.dev / Admin@Volt2026';

/* What was written. */
SELECT N'Users.Users' AS TableName, COUNT(*) AS SeedRows FROM Users.Users WHERE Id LIKE N'5EED0000-%'
UNION ALL SELECT N'Users.ParentChildLinks', COUNT(*) FROM Users.ParentChildLinks WHERE ParentUserId LIKE N'5EED0000-%'
UNION ALL SELECT N'LearningContent.Levels', COUNT(*) FROM LearningContent.Levels WHERE Id BETWEEN 101 AND 199
UNION ALL SELECT N'LearningContent.Lessons', COUNT(*) FROM LearningContent.Lessons WHERE Id BETWEEN 1001 AND 1099
UNION ALL SELECT N'LearningContent.LessonContents', COUNT(*) FROM LearningContent.LessonContents WHERE Id BETWEEN 10001 AND 10999
UNION ALL SELECT N'Assessment.Topics', COUNT(*) FROM Assessment.Topics WHERE Id BETWEEN 101 AND 199
UNION ALL SELECT N'Assessment.Quizzes', COUNT(*) FROM Assessment.Quizzes WHERE Id BETWEEN 101 AND 199
UNION ALL SELECT N'Assessment.Questions', COUNT(*) FROM Assessment.Questions WHERE Id BETWEEN 1001 AND 1099
UNION ALL SELECT N'Assessment.QuestionOptions', COUNT(*) FROM Assessment.QuestionOptions WHERE Id BETWEEN 10001 AND 10999
UNION ALL SELECT N'Assessment.QuizAttempts', COUNT(*) FROM Assessment.QuizAttempts WHERE Id BETWEEN 100001 AND 100999
UNION ALL SELECT N'Assessment.UserTopicStats', COUNT(*) FROM Assessment.UserTopicStats WHERE UserId LIKE N'5EED0000-%';
GO
