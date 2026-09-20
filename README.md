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
* Assessment business logic and workflows
* AI-assisted hints
* AI-assisted essay evaluation
* Placement assessment
* Quiz attempts, retries, and grading flows
* Learning statistics and mastery logic
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
├── AIIntegration/
│
├── Shared/
│
├── Tests/
│   └── Assessment.Tests/
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

* `LevelAssessment`
* `LessonQuiz`
* `LessonReview`
* `Standalone`
* `Placement`

Each type has its own business purpose within the learning flow.

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

### Phase B — AI-Assisted Processing

After the deterministic result has been safely committed, the backend may perform:

* AI hint generation
* Inline essay evaluation

AI failures do not invalidate the already committed assessment result.

This prevents an unavailable or failing AI service from turning a valid quiz submission into a failed API request.

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

Essay evaluation can happen:

* Inline after submission
* Through the background `EssayEvaluationWorker`

The background worker retries pending essay evaluations according to the configured retry policy.

If AI evaluation is unavailable, the attempt remains valid and the essay can remain pending.

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

### Retry Idempotency

A unique constraint on:

```text
PreviousAttemptId
```

prevents duplicate retry attempts from being created from the same previous attempt.

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

Used to process pending essay evaluations that were not successfully completed inline.

These workers follow configured retry and timing rules.

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
* Assessment workflows and business rules
* Quiz attempts and grading
* Retry and hint systems
* Placement assessment
* Learning statistics and mastery
* Flutter Integration Mocks
* Flutter/backend integration support

## Eng. Rawda

* Users Module
* Content Module

---

# Project Status

The backend implements the core ElectroWorld backend architecture and assessment workflows, including authentication-related integration, learning content integration, assessment management, quiz attempts, grading, retries, hints, AI-assisted assessment functionality, background processing, statistics, and Flutter integration support.