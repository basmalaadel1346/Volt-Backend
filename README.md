# ElectroWorld Backend

Backend system for **ElectroWorld**, a children’s electronics learning and gamified application.

The backend provides authentication, learning content, assessments, gamification-related data, AI-assisted assessment features, background processing, and APIs consumed by the Flutter application.

This repository contains the **ElectroWorld Backend Track**.

---

## Project Overview

ElectroWorld is designed to provide children with an interactive environment for learning electronics through lessons, assessments, virtual labs, projects, games, and progression systems.

The backend is responsible for:

* Authentication and authorization
* Learning content management
* Quiz and assessment management
* Quiz attempts and grading
* Retry and hint systems
* AI-assisted hints and essay evaluation
* Learning statistics and topic mastery
* Placement assessment
* Background processing
* Secure APIs for Flutter integration

The Flutter application is responsible for the client-side experience and interactive UI, while the backend handles persistent data, business rules, validation, grading, authorization, and server-side processing.

---

## Team Ownership

### Eng. Basmala Adel Mohamed

Responsible for:

* Assessment Module
* AI Integration
* Gamification Module
* Assessment business logic and workflows
* AI-assisted hints
* AI-assisted essay evaluation
* Placement assessment
* Level-Skip Challenge
* Quiz attempts, retries, and grading flows
* Learning statistics and mastery logic
* Gamification and reward systems
* Background processing
* SignalR real-time updates
* Response caching
* Authorization and ownership enforcement for the implemented modules
* Concurrency and idempotency protection
* Flutter integration support
* Flutter Integration Mocks

### Eng. Rawda

Responsible for:

* Users Module
* Content Module

The modules communicate through defined contracts and shared interfaces rather than directly accessing another module's `DbContext`.

---

## Architecture

The backend follows a **Modular Monolith + 3-Tier Architecture**.

The system is organized into independent business modules while remaining within one deployable application.

Each module follows the separation:

```text
Controller / API
      ↓
Business Logic
      ↓
Data Access
      ↓
SQL Server
```

The main modules are:

```text
Users
Content
Assessment
Gamification
AI Integration
Shared
```

Each module owns its own database context and data-access layer.

Cross-module database access is intentionally avoided. Modules communicate through contracts and interfaces.

---

## Technology Stack

* .NET 8
* C# 12
* ASP.NET Core Web API
* Entity Framework Core 8
* SQL Server
* Database First
* JWT Bearer Authentication
* BCrypt
* Swagger / OpenAPI
* Background Services
* AI Integration

---

## Project Structure

The repository is organized approximately as follows:

```text
Project_Volt/
│
├── ElectroWorld/
│   ├── Controllers/
│   │   ├── AssessmentModule/
│   │   ├── UsersModule/
│   │   └── ContentModule/
│   │
│   ├── BackgroundJobs/
│   ├── Middleware/
│   └── ...
│
├── AssessmentBL/
├── AssessmentDA/
│
├── UsersBL/
├── UsersDA/
│
├── ContentBL/
├── ContentDA/
│
├── GamificationBL/
├── GamificationDA/
│
├── AIIntegration/
│
├── Shared/
│
├── Tests/
│   ├── Assessment.Tests/
│   └── Gamification.Tests/
│
└── docs/
```

---

# Users Module

The Users Module handles user identity and authentication-related functionality.

It is owned by **Eng. Rawda**.

The module is responsible for user-related functionality such as:

* Registration
* Login
* Authentication
* JWT access tokens
* Refresh tokens
* Guest users
* User identity information required by other modules
* Password handling

The Assessment Module does not directly query the Users database.

Instead, it accesses the required learner information through defined contracts such as:

```text
ILearnerProfile
```

This keeps module boundaries explicit.

---

# Content Module

The Content Module manages the educational structure and learning content.

It is owned by **Eng. Rawda**.

The learning structure includes concepts such as:

```text
Courses
Levels
Lessons
Lesson Contents
```

Lessons are created as unpublished drafts. Publishing controls whether lesson-related functionality becomes available to learners.

The Assessment Module accesses required content information through contracts such as:

```text
ILevelCatalog
ILessonAvailability
```

rather than directly querying the Content database context.

---

# Assessment Module

The Assessment Module is owned by **Eng. Basmala Adel Mohamed**.

It provides the complete assessment lifecycle, including:

* Quiz authoring
* Question management
* Quiz attempts
* Automatic grading
* Essay evaluation
* Retry system
* Progressive hints
* Placement assessment
* Learning statistics
* Topic mastery
* Assessment localization
* Concurrency and idempotency protection

The Assessment database is isolated behind its own `AssessmentDbContext`.

---

## Quiz Types

The system supports multiple quiz types:

* `LevelAssessment` — what a level expects a child to know.
* `LessonQuiz` — the quiz of one lesson. **Passing it is what unlocks the next lesson.**
* `LessonReview`
* `Standalone`
* `Placement` — the first-run test. Owns no questions: it samples each level's `LevelAssessment` quiz.
* `LevelSkip` — the level-skip challenge. Owns no questions either: it samples the level's own `LessonQuiz` quizzes.

Each type has its own business purpose within the learning flow.

**A quiz is created as a DRAFT** (`IsActive = false`) and published as a separate step. Creation used to publish immediately, which collided with the "only one active" rules below: preparing a replacement meant taking the live quiz down first and leaving children with no quiz while the new one was written. Drafts occupy no slot, so any number of them can be prepared alongside the quiz that is running.

**Only one quiz may be ACTIVE per slot** — one `Placement` overall, one `LessonQuiz` per lesson, one `LevelSkip` per level — because otherwise which quiz a child gets would be a guess. The rule applies at publish time, and publishing **swaps**: the quiz holding the slot is moved back to draft in the same transaction, so there is never an instant with two active quizzes or none.

---

## Question Types

The assessment system supports:

* Multiple Choice
* True / False
* Essay

Multiple-choice and true/false questions can be automatically graded.

Essay questions require AI-assisted evaluation and may remain pending until evaluation is completed.

---

# Quiz Lifecycle

A quiz attempt follows a controlled lifecycle:

```text
Start
  ↓
InProgress
  ↓
Submit
  ↓
Completed
```

An attempt that exceeds the allowed time can become:

```text
InProgress
  ↓
Expired
  ↓
Abandoned
```

The system also includes background processing for abandoned attempts.

**A learner has at most one live attempt per quiz.** A second "Start" — a double-tapped button, or an app retrying after a lost response — **resumes** the open attempt rather than creating another. It used to create a second attempt: the first was orphaned `InProgress` until the sweep abandoned it, and only one of the two could ever be submitted. A filtered unique index (`UQ_QuizAttempts_OneInProgressPerUserQuiz`) enforces this at the database, so even two simultaneous requests can only produce one attempt.

---

# Assessment Submission

Assessment submission is divided into two logical phases.

### Phase A — Deterministic Processing

The backend first:

* Validates the attempt
* Validates submitted answers
* Grades automatically gradable questions
* Calculates the deterministic score
* Persists the attempt result
* Persists mistakes
* Updates required assessment data

This phase is committed before optional AI processing.

This phase is all the child waits for. Once it commits, the request **returns**.

### Phase B — AI-Assisted Processing, on a background worker

After the deterministic result has been committed, the submission hands the optional work to an in-process queue (`IAttemptFollowUpQueue`) and returns. A background worker then performs:

* AI hint generation for the wrong answers
* AI evaluation of the essay answers

and pushes the outcome to the learner's app over SignalR, so the UI updates without polling.

**Phase B used to run inline**, inside a 15-second budget, on the request thread. A child therefore waited on an AI call for a score that had already been committed **before the AI was asked at all** — the worst possible trade, since nothing in Phase B can change that score or fail the request. Moving it off the thread makes `POST .../submit` a database write again.

AI failures still do not invalidate the committed result: a missing hint is reported honestly as `hintsStatus: "Unavailable"`, and an essay that was not graded stays `Pending` until the essay worker or the grading deadline settles it.

---

# Frozen Assessment Data

Assessment data that affects an attempt is frozen when the attempt is created.

For example, question points are stored in:

```text
QuizAttemptQuestions.Points
```

This prevents later administrative changes from altering the historical result of an existing attempt.

The same principle is important for preserving historical assessment consistency when questions or options are modified later.

---

# Retry System

The assessment system supports retrying incorrectly answered questions.

The retry flow is based on the previous attempt:

```text
Original Attempt
      ↓
Wrong Questions
      ↓
Retry Attempt
      ↓
Remaining Wrong Questions
      ↓
Next Retry
```

The backend tracks the relationship between attempts using `PreviousAttemptId`.

**The retry questions are their own endpoint** (`GET /api/quiz-attempts/{id}/retry-questions`), not part of the result, because their hints are written after the result is committed.

**A question the admin retired is explained, not silently dropped.** If a question the child got wrong has been deactivated since, the retry leaves it out — as it always did — but the start response now lists it in `removedQuestionIds` with a `notice` the app shows the child. Before, the child was simply handed a shorter quiz than the result had promised, with nothing to explain why.

A unique constraint prevents duplicate retry chains from being created for the same previous attempt.

Retry attempts can also receive the appropriate previously generated hints.

---

# Hint System

Hints are generated progressively.

The learner does not choose the hint level directly. The backend derives the next available level.

The default maximum is:

```text
2 hint levels
```

A failed or rejected AI-generated hint does not consume a hint level.

Hints do not affect the learner's score.

For post-submission hints, hints are generated for incorrect:

* Multiple Choice questions
* True / False questions

Hints are validated before persistence.

The validation includes checks such as:

* One result for each requested question
* Valid response status
* Non-empty hint
* Maximum hint length
* The hint must not reveal the correct answer
* Similarity checks for non-true/false questions

AI failures leave the hint unavailable rather than corrupting the assessment result.

---

# AI Integration

The AI Integration layer provides AI-assisted assessment capabilities.

The current AI-supported operations include:

### AI Hints

The backend prepares structured question information and sends it to the AI service.

The AI request contains information required for generating a useful hint while avoiding unnecessary learner-identifying information.

The AI integration does not send:

* Internal user IDs
* Attempt IDs
* Account IDs
* Image URLs

The backend validates the AI response before accepting and storing hints.

### AI Essay Evaluation

The AI integration can evaluate essay answers.

A valid AI evaluation can provide:

* Grade
* Feedback

If the AI response is malformed or unusable, the essay remains:

```text
NotGraded
```

and can be processed later by the background evaluation worker.

---

# Essay Evaluation

Essay evaluation is intentionally separated from deterministic quiz grading.

After submission:

```text
Quiz Submission
      ↓
Deterministic Result Saved
      ↓
Essay Evaluation
      ↓
AI Evaluation
      ↓
Final Essay Result
```

Essay evaluation happens on a background scope — never on the submitting request — in two ways:

* Immediately after the submission commits, through the attempt follow-up worker
* Through the periodic `EssayEvaluationWorker`, for anything that did not finish

The background worker retries pending essay evaluations according to the configured retry policy.

If AI evaluation is unavailable, the attempt remains valid.

**An essay always reaches a final state.** Retry limits alone were not enough: an AI that never answers, or that keeps failing in a way that does not consume an attempt, left an essay `Pending` for as long as the outage lasted — and the child looking at "being graded" with no end in sight and no final result. Two mechanisms close it:

* **A grading deadline** (`Assessment:EssayGradingDeadlineMinutes`, default 60). Past it, the answer is settled whatever the AI is doing.
* **An admin-authored keyword fallback.** An essay question may carry `EssayKeywords` — the ideas an acceptable answer mentions. When the deadline arrives and the AI has not graded the answer, it is graded from those keywords instead (`Graded`, `GradedBy = Keywords`), with points proportional to how many were found and feedback saying so. Matching tolerates the Arabic spelling a child actually types (`الاوم` for `الأوم`, dropped diacritics, `ه`/`ة`).

  It is deliberately crude, and only ever reached once the AI has definitively failed. The alternative was leaving the child with `NotGraded` and zero points because a service they never heard of was down: an approximate grade they can see beats a perfect one that never arrives.

A question with no keywords is closed honestly as `NotGraded` (`TimedOut`) rather than being given an invented score.

The assessment response distinguishes between:

* Automatically graded questions
* Pending essay questions
* Essay results

---

# Placement Assessment

The system supports placement assessment for determining the learner's starting level.

Placement questions are sampled from level-assessment quizzes.

The default configuration includes:

```text
4 questions per level
75% passing percentage
```

Placement attempts follow the same general assessment principles while having their own placement-specific rules.

**Placement completes the levels it proved.** A child placed at level 3 has demonstrated levels 1 and 2, so those levels' lessons are marked complete in the same flow. Without it, the progress map showed them as homework the child never did — and, with the lesson gate in place, the level they were placed at would not even be open to them. Only levels actually shown mastered are credited; a level the test could not check is never credited, however far up the child was placed.

**The result lists every level.** It used to omit any level with no placement questions, which handed the app a ladder with missing rungs and no way to tell which were missing. Every level is now present, with an `Assessed` flag that distinguishes "asked, and got it all wrong" (score 0) from "nothing to ask" — the latter being exactly why the child was not placed higher.

---

# Level-Skip Challenge

A child who already knows a level proves it once instead of sitting through every lesson.

The level-skip quiz owns no questions: it samples the level's **own lesson quizzes**, which are by definition what that level teaches.

```text
~10 questions   (Assessment:LevelSkipQuestionCount)
3 minutes       (Assessment:LevelSkipTimeLimitSeconds)
3 hearts        (Assessment:LevelSkipHearts)
80% to pass     (Assessment:LevelSkipPassPercentage)
```

Two decisions shape it:

* **Questions are spread round-robin across the level's lessons**, not taken lesson by lesson, so a child who knows only the first lesson cannot pass.
* **The sample rotates with the number of previous runs**, so a retake is a different paper — without randomness, which would make a resumed attempt inconsistent with the one that was started.

Passing marks the level's lessons complete, which is what actually opens the next level. A failed challenge can be taken again immediately.

---

# Lesson Gating

A child cannot open a lesson until they have **passed the quiz of the lesson before it** in the same level.

The knowledge is split across two modules — Content owns lesson order, Assessment owns quiz attempts — so the rule is assembled over a Shared contract (`ILessonQuizGate`) rather than by either module reaching into the other.

Two deliberate escapes:

* The **first lesson of a level** is always open.
* A lesson whose predecessor has **no active quiz** counts as passed. Locking a whole course because an admin has not written a quiz yet would be worse than not gating at all.

Enforcement is on the lesson itself (`GET /api/content/lessons/{id}` → 403 for a locked lesson), with query endpoints (`.../access`) so the app can draw padlocks without offering a door it knows is shut. Admins and parents are never gated: they review and supervise content they have no progress in.

---

# Gamification

The Gamification Module is owned by **Eng. Basmala Adel Mohamed**.

A separate module (`GamificationBL` / `GamificationDA`, schema `Gamification`), reached from the learning modules only through the Shared contract `ILearningRewards` — so Assessment and Content report "this child just did something that counts" without referencing it, and keep working when it is not registered at all.

**The loop.** The child studies, earns **Sparks**, builds a **streak**, and spends the Sparks protecting the streak. The streak is the engine: nobody wants to lose a 15-day run they worked for.

```text
Finish a lesson          →   5 Sparks
Submit any quiz          →   5 Sparks
...with no mistakes      → +10 Sparks
Finish the placement     →  20 Sparks
Pass a level skip        →  25 Sparks
7-day streak box         →  50 Sparks
```

**Streaks** count days, not sessions: several activities on one day advance it once.

**Streak freezes** are the point of the currency. Miss a day with a freeze in hand and it is spent automatically, preserving the streak; two missed days cost two freezes. Miss more days than you hold freezes and the streak restarts at 1 — and the freezes are **not** taken, because spending them on a rescue that did not happen would be more discouraging than losing the streak. Holding is capped (two by default), so buying ten is not a way to stay away for ten days.

**Every award is idempotent.** A replayed submit or a retried "mark lesson complete" pays once, enforced by a unique index on `(UserId, Reason, ReferenceKey)` rather than by a check that could be raced past. A replayed activity does not advance the streak either: submitting the same attempt twice is not two days of learning.

**No reward can fail the thing that earned it.** `ILearningRewards.RecordActivityAsync` never throws; a gamification hiccup costs Sparks, never a committed lesson or quiz result.

---

# Learning Statistics and Mastery

The Assessment Module maintains learner topic statistics through:

```text
UserTopicStats
```

Statistics are aggregated by factors such as:

* User
* Topic
* Difficulty

The system also tracks previous-attempt hint usage when calculating relevant learning statistics.

Topic mastery uses configurable rules.

The default configuration includes:

```text
80% mastery percentage
5 minimum relevant questions
```

**Mastery is judged on the same rounded percentage the child is shown.** It used to compare exactly, so 159 correct of 200 displayed "80%" next to "Practicing" while the threshold was 80% — a child reading their own screen had no way to understand the gap, and no number on the screen explained it. Whatever the rounding costs in strictness, an accuracy the child can read and a mastery they can predict from it is worth more.

**Questions with no topic are reported under a hidden "General" bucket.** Their XP used to exist only inside `totalXp`, so the progress map added up to less than the total with nothing on screen to explain the difference, and the app had to show `totalXp` as a number detached from everything below it. The bucket is not a row in `Assessment.Categories` — no admin creates or edits it and no question can be filed under it — it exists only in the progress response, so the map always adds up.

---

# Security and Authorization

The backend uses JWT Bearer authentication.

Authorization is enforced server-side.

Child-facing endpoints use the authenticated user's identity rather than trusting user IDs supplied by the client.

For example, ownership validation can use:

```text
UserId
LearnerId
```

to ensure that a learner can only access their own assessment data.

The system also avoids exposing resource existence unnecessarily. For another user's protected records, the API can return `404 Not Found` rather than revealing whether the resource exists.

Child-facing DTOs do not expose assessment answer keys or administrative information.

For example, learner-facing question responses do not expose:

```text
IsCorrect
CorrectOptionId
ImageDescription
IsActive
```

---

# Image-Based Questions

The system supports questions and options that may contain images.

For AI processing, image content is handled according to the configured AI policy.

When image content itself cannot be sent to the AI service, the backend can rely on the administrator-provided image description as the textual representation required for AI processing.

This allows the AI integration to reason about image-based questions without exposing image URLs or unnecessary identifying information.

---

# Concurrency and Idempotency

The Assessment Module contains protections against duplicate or concurrent operations.

### Double Submission

Quiz attempts use:

```text
RowVersion
```

to protect against concurrent submissions.

If an already completed attempt is submitted again, the saved result can be replayed instead of regrading the attempt or sending another AI request.

### Duplicate Start ("Start Quiz" pressed twice)

A filtered unique index:

```text
UQ_QuizAttempts_OneInProgressPerUserQuiz  (UserId, QuizId) WHERE Status = 'InProgress'
```

allows a learner only one live attempt per quiz. A second start resumes the first and reports `resumed: true`; if two requests race past the application check, the loser catches the unique violation and serves the winner's attempt. Before, the second request created a whole second attempt that could never be submitted.

### Retry Idempotency

A unique constraint on:

```text
PreviousAttemptId
```

prevents duplicate retry attempts from being created from the same previous attempt. An open retry is resumed rather than refused.

### Reward Idempotency

A filtered unique index on `(UserId, Reason, ReferenceKey)` in `Gamification.SparkTransactions` means the same finished lesson or attempt can only ever pay once, however many times it is reported.

### Order Changes

Ordering is set from an **absolute list of ids**, not by swapping pairs. A swap is not idempotent — applying it twice reverts it — so a retried request silently undid the admin's change and left the dashboard and the database disagreeing. The absolute form describes a state, so repeating it changes nothing; an incomplete list is rejected rather than half-applied.

### Atomic Answer-Key Changes

Moving a question's correct answer is one transactional request (`PATCH /api/questions/{id}/correct-option`). It used to take four (deactivate → clear → set → activate), and a connection lost at step 3 left the question deactivated with no correct answer — a partial failure with nothing to indicate it.

### Hint Concurrency

Unique constraints prevent duplicate hint levels from being created during concurrent requests.

### Essay Evaluation

Pending essay evaluations are claimed atomically to prevent multiple workers from processing the same evaluation simultaneously.

---

# Localization

Assessment language can be resolved using:

```text
?language=
```

followed by:

```text
Accept-Language
```

with the configured default language:

```text
ar
```

Fallback behavior can use English or the base content when a requested localization is unavailable.

The hint level is counted across languages, so changing the language does not provide additional hint levels.

---

# Database

The backend follows a Database-First approach.

The Assessment module uses its own database schema and `DbContext`.

Cross-module references such as:

```text
Quiz.LevelId
Quiz.LessonId
UserId
```

are handled through application-level contracts rather than direct cross-module foreign-key relationships between module databases/contexts.

The database remains the source of persisted assessment state, while business rules are enforced by the backend.

---

# Background Processing

The backend contains background processing for operations that should not block the main API request.

Current background responsibilities include:

### Abandoned Quiz Attempt Sweeper

```text
AbandonedQuizAttemptSweeper
```

Used to detect and process attempts that remain in progress beyond the allowed time.

### Essay Evaluation Worker

```text
EssayEvaluationWorker
```

Used to process pending essay evaluations that did not finish, and to apply the grading deadline (keyword fallback, or `NotGraded`) so no essay can wait forever.

### Attempt Follow-Up Worker

```text
AttemptFollowUpWorker
```

Runs the optional AI work of a submitted attempt — hints, then essay grading — off the request thread, and pushes the outcome to the learner's app. The queue behind it is **bounded**: under a burst it sheds the oldest waiting item rather than growing until the process dies, which is survivable precisely because everything on it is optional (the essays are picked up by the evaluator; a missing hint is reported as `Unavailable`).

These workers follow configured retry and timing rules.

---

# Real-Time Updates

The backend exposes an authenticated SignalR hub at `/hubs/learner`.

It exists because a submission now returns before the AI has written its hints or graded its essays. Without a push, the only way to learn that the work was done was to ask every couple of minutes — a battery-expensive guess that is usually wrong in both directions.

The hub is **one-way**: it sends, and never takes a command. Every message names the ordinary endpoint that returns the same information, so a dropped connection can only ever cost a notification, never a result — and a client that ignores the hub entirely still works.

```text
hintsReady      → GET /api/quiz-attempts/{id}/retry-questions
essaysGraded    → GET /api/quiz-attempts/{id}/result
rewardsChanged  → GET /api/gamification/me
```

Messages go to a group named after the caller's own user id, taken from the token: a connection cannot ask to listen to somebody else.

---

# Response Caching

ASP.NET Core output caching is enabled and applied **per endpoint**, never globally:

* **`PublicContent` (60 s)** — the level and lesson catalogue and the quiz metadata behind it: identical for everyone and hit by a whole class opening the app at once.
* **`PerLearner` (15 s)** — the gamification wallet and shop. Keyed on the `Authorization` header, and the cache runs *after* authentication, so one child's response can never be served to another.

Anything else that depends on who is asking — attempts, results, progress, placement — is **not cached at all**. A cache keyed on the URL alone would serve one child another child's answers, which is why these policies are opt-in per endpoint rather than a default.

---

# Flutter Integration Mocks

Mock API responses were prepared to help the Flutter team understand and integrate with the backend endpoints without requiring the actual backend endpoints to be executed during the initial integration stage.

These mocks represent the expected request/response structures and allow frontend development to proceed independently from backend execution.

They are intended specifically for **Flutter/frontend integration support**.


---
## 📚 Technical Documentation

We have prepared comprehensive documentation for the backend system:

* [Frontend API Reference](docs/FRONTEND_API.md)
* [AI Service Integration Contract](docs/AI_INTEGRATION_CONTRACT.md)
* [Backend Business Flows & Architecture](docs/BACKEND_ARCHITECTURE.md)

---

# API Conventions

Two conventions apply to **every** endpoint:

* **`null` fields are omitted from responses.** A field with no value is absent from the JSON rather than serialized as `null` — smaller payloads on a phone connection, and one representation of "nothing" instead of two. Clients must treat an absent field exactly as they treated a null one; collections are always written, so an empty list stays an empty list.
* **One error shape.** Every failure is the `ApiResponse` envelope (`success`, `message`, `data`). The framework's own model-binding failures used to return `ValidationProblemDetails` — `title`/`status`/`errors` and **no `success`** — so a client interceptor reading `response.success` crashed on exactly the responses a developer meets most often while still getting a request right. A custom `InvalidModelStateResponseFactory` now writes the envelope for those too.

Text that stands for an enum (`quizType`, `questionType`, `difficulty`, `learningLevel`) is matched **case-insensitively** and returned in its canonical spelling. Previously only `learningLevel` was flexible while every other field was strictly case-sensitive, so `"multiplechoice"` was rejected by one field and accepted by another with nothing to indicate which.

---

# API Documentation

The backend exposes its APIs through Swagger/OpenAPI.

Swagger can be used to inspect:

* Available endpoints
* Request models
* Response models
* Authentication requirements
* Assessment flows
* Admin operations
* Learner operations

The API documentation is intended to support both backend development and Flutter integration.

---

# Testing

Assessment and Gamification testing are maintained by Eng. Basmala Adel Mohamed.
The repository includes assessment-related tests under:

```text
Tests/Assessment.Tests
```

Testing focuses on important assessment business rules and behavior.

Critical areas include:

* Quiz attempts
* Submission
* Grading
* Retry behavior
* Hint behavior
* Ownership
* Concurrency
* AI-related failure handling

---

# Backend Responsibilities vs Flutter Responsibilities

The backend is responsible for:

* Authentication
* Authorization
* Persistent data
* Business rules
* Quiz configuration
* Assessment validation
* Automatic grading
* Essay evaluation
* Retry logic
* Hint rules
* Statistics
* Mastery calculations
* Concurrency protection
* Idempotency
* AI integration

Flutter is responsible for:

* User interface
* Navigation
* Displaying lessons and assessments
* Collecting learner answers
* Local interaction logic
* Virtual Lab interaction
* Client-side visual feedback
* Sending requests to the backend
* Displaying backend results

For example, Virtual Lab circuit correctness can be evaluated on the Flutter side based on the application's circuit graph and interaction logic, while the backend can receive the resulting state when required by the application flow.

---

# Important Design Decisions

### Modular Monolith

The system uses a Modular Monolith instead of introducing unnecessary microservice complexity.

### Separate DbContexts

Modules do not directly query another module's `DbContext`.

### Database First

The database schema is treated as the foundation of persistence, with EF Core entities generated from the database.

### Server-Side Ownership

Protected resources are associated with the authenticated learner rather than trusting client-provided ownership identifiers.

### Deterministic Grading Before AI

The assessment result is persisted before optional AI operations are performed.

### AI as an Optional Processing Layer

AI failures should not invalidate already committed assessment data.

### Frozen Attempt Data

Historical assessment results should remain stable even when administrators later modify questions or points.

### Retry as a Separate Attempt

Retries are persisted as separate attempts and linked through `PreviousAttemptId`.

### No Answer-Key Leakage

Correct answers and administrative metadata are never included in child-facing assessment DTOs.

---

# Repository Scope

This repository represents the **ElectroWorld Backend Track**.

The repository is intentionally focused on backend implementation and backend/frontend integration support.

Internal development materials and private integration contracts are not included as public repository documentation.

The repository focuses on the implemented backend modules, APIs, business rules, database integration, AI integration, background processing, testing, and Flutter integration support.

---

# Contributors

## Eng. Basmala Adel Mohamed

* Assessment Module
* AI Integration
* Gamification Module
* Assessment workflows and business rules
* Quiz attempts and grading
* Retry and hint systems
* Placement assessment
* Level-Skip Challenge
* Learning statistics and mastery
* Gamification and reward systems
* Background processing
* SignalR real-time updates
* Response caching
* Authorization and ownership enforcement for implemented modules
* Concurrency and idempotency protection
* Flutter Integration Mocks
* Flutter/backend integration support
* Assessment and Gamification testing
## Eng. Rawda

* Users Module
* Content Module

---

# Project Status

The backend implements the core ElectroWorld backend architecture and assessment workflows, including authentication-related integration, learning content integration, assessment management, quiz attempts, grading, retries, hints, AI-assisted assessment functionality, gamification, background processing, statistics, and Flutter integration support.
