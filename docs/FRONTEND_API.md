# ElectroWorld Frontend API Reference

This document is the contract between the ElectroWorld backend (ASP.NET Core 8, `ElectroWorld` host) and its clients: the Child app, the Parent app (Flutter) and the Admin dashboard. It lists every HTTP endpoint the backend exposes. For each one it gives the exact request and response JSON, every error with its real (Arabic) message, and notes on how a client should call it. Everything here was read from the code (controllers, DTOs, services, `ExceptionMiddleware`, `Program.cs`) and reflects the server's actual behavior. Code is referenced by file and class/method name. Samples under `docs/mocks/` are illustrations only; where a mock and this document disagree, this document (and the code behind it) wins.

## Table of contents

1. [Conventions](#conventions)
   - [Base URL and routes](#base-url-and-routes)
   - [JSON](#json)
   - [Response shapes](#response-shapes)
   - [Authentication and sessions](#authentication-and-sessions)
   - [Roles: 401, 403 and 404](#roles-401-403-and-404)
   - [Content language](#content-language)
   - [Dates, ids and numbers](#dates-ids-and-numbers)
   - [Pagination](#pagination)
   - [Validation errors (framework 400)](#validation-errors-framework-400)
   - [Status codes](#status-codes)
2. [Endpoint index](#endpoint-index)
3. [Auth & Users](#auth--users)
4. [Content](#content)
5. [Assessment (learner)](#assessment-learner)
6. [Assessment (admin)](#assessment-admin)

---

## Conventions

### Base URL and routes

- **Host.** The repository does not configure a production base URL. For local development, `ElectroWorld/Properties/launchSettings.json` uses `http://localhost:5183` and `https://localhost:7222`. `Program.cs` calls `UseHttpsRedirection`, so plain HTTP requests are redirected to HTTPS when an HTTPS port is available.
- **Swagger UI** is served only in the Development environment (`app.UseSwagger()` / `UseSwaggerUI()` inside `IsDevelopment()`), at `/swagger`.
- **Every endpoint is under `/api`.** Route prefixes:

| Prefix | Controller | Module |
|---|---|---|
| `/api/auth` | `AuthController` | Users |
| `/api/users` | `UsersController` | Users |
| `/api/content/content-types` | `ContentTypesController` | Content |
| `/api/content/levels`, `/api/content/lessons`, `/api/content/contents` | `LevelsController`, `LessonsController`, `LessonContentsController` | Content |
| `/api/content/media` | `MediaController` | Content |
| `/api/placement` | `PlacementController` | Assessment (learner) |
| `/api/quizzes/for-lesson/{lessonId}` | `LessonQuizController` | Assessment (learner) |
| `/api/quiz-attempts` | `QuizAttemptController` | Assessment (learner) |
| `/api/user-topic-stats` | `UserTopicStatController` | Assessment (learner) |
| `/api/quizzes` | `QuizController` | Assessment (admin) |
| `/api/questions` | `QuestionController` | Assessment (admin) |
| `/api/question-options` | `QuestionOptionController` | Assessment (admin) |
| `/api/assessment/categories` | `AssessmentCategoryController` | Assessment (admin) |
| `/api/assessment/topics` | `AssessmentTopicController` | Assessment (admin) |

- **Route constraints.** Path ids are declared as `{id:int}` or `{attemptId:long}`; an Assessment category id is `{categoryId:int:range(1,255)}`. A segment that is not a number, or a category id outside 1–255, matches no route: the response is a **404 with an empty body** (not the envelope).
- **Uploaded images** are static files under `/uploads/lessons/<guid>.<ext>` (`UseStaticFiles`). They are served **without authentication**. Every `imageUrl` / `mediaUrl` in the API is a server-relative path, so prefix it with the API origin to display it.
- **Content type.** A global `ProducesAttribute("application/json")` filter makes every controller response `application/json`. Requests with a body send `Content-Type: application/json`, except the image upload, which is `multipart/form-data`.
- **CORS.** `Program.cs` registers no CORS policy (`AddCors`/`UseCors` do not appear anywhere in the solution). Native mobile apps are unaffected. A browser-based dashboard served from another origin should be served from the same origin as the API, or a CORS policy should be added before that integration is attempted.

### JSON

- Serialization is System.Text.Json with the **web defaults** (MVC defaults and `new JsonSerializerOptions(JsonSerializerDefaults.Web)` in `ExceptionMiddleware` and `ApiResponseAuthWriter`). No API DTO has a `[JsonPropertyName]` attribute or a custom converter, and no `AddJsonOptions` call changes the defaults. In practice:
  - Response property names are **camelCase** (`attemptId`, `scorePercentage`).
  - Request property names are matched **case-insensitively**; unknown properties are ignored.
  - Numbers may also be sent as numeric strings (`"15"`).
  - `null` values are **written**, not omitted: an optional field is present with `null`.
- String "enums" (`quizType`, `questionType`, `difficulty`, `status`, `hintsStatus`, `role`, `authProvider`) are plain strings, not JSON enums. Values the server validates are **case-sensitive** unless an endpoint says otherwise.
- Messages (`message`) of envelopes and errors are always Arabic, whatever language the client asks for. The one exception is the encouraging `message` text of the progress map (`GET /api/user-topic-stats` and `GET /api/user-topic-stats/{topicId}`), which is English when the resolved language is `en`.

### Response shapes

A client meets four body shapes. Decode by status code first, then by shape.

**1. The envelope `ApiResponse<T>`** (`Shared/Common/Api/ApiResponse.cs`). Every success body of the **Auth, Users and Content** controllers uses it:

```json
{
  "success": true,
  "message": "تمت العملية بنجاح",
  "data": { "id": 1, "title": "مقدمة في الكهرباء", "description": "مفاهيم أساسية", "order": 1 }
}
```

(`تمت العملية بنجاح` = "the operation succeeded".) The non-generic `ApiResponse` has **no `data` key at all**. It is used where nothing is returned, for example logout, forgot/reset password, deletes and swap-order in Content, and some failures that those controllers write themselves:

```json
{ "success": true, "message": "تم تسجيل الخروج بنجاح" }
```

**2. Bare DTOs.** Every success body of the **Assessment** controllers (`PlacementController`, `LessonQuizController`, `QuizAttemptController`, `UserTopicStatController`, `QuizController`, `QuestionController`, `QuestionOptionController`, `AssessmentCategoryController`, `AssessmentTopicController`) is the DTO itself, with no envelope:

```json
{ "attemptId": 42, "quizId": 15, "startedAt": "2026-09-10T18:30:00.1234567Z", "language": "ar", "languageFallbackApplied": false, "questions": [] }
```

A `201 Created` carries a `Location` header. A `204 No Content` has no body.

**3. The error envelope.** Every error raised as an exception (`ElectroWorld/Middleware/ExceptionMiddleware`) and every 401/403 from JWT authentication (`ElectroWorld/Swagger/ApiResponseAuthWriter`) uses `ApiResponse<object>.Fail`, in **every** module:

```json
{ "success": false, "message": "المحاولة رقم 42 غير موجودة", "data": null }
```

(`المحاولة رقم 42 غير موجودة` = "attempt 42 does not exist".) Failures that Auth, Users and Content controllers build themselves use the same keys. When they are built with the non-generic `ApiResponse`, the `data` key is absent.

**4. ASP.NET Core ProblemDetails.** The framework writes these without going through the envelope: model-validation 400s ([below](#validation-errors-framework-400)). A 415 Unsupported Media Type is also written by the framework and never uses the envelope. No endpoint returns a bare `NotFound()` any more: the only one, `GET /api/user-topic-stats/{topicId}/{difficulty}`, was removed in this release.

**Suggested decoding.** If the body is empty, use the status alone. If it has a boolean `success`, it is an envelope: read `message`, and `data` when present. If it has `title` and `status`, it is ProblemDetails. Otherwise, on a 2xx from an Assessment endpoint, it is the DTO.

Mocks for the shared error bodies: `docs/mocks/mock_unauthorized.json` (401), `docs/mocks/mock_forbidden.json` (403), `docs/mocks/mock_server_error.json` (500) and `docs/mocks/mock_validation_error.json` (framework 400).

### Authentication and sessions

- **Scheme.** JWT bearer, HMAC-SHA256 (`Shared/Users/JwtTokenGenerator`). Send `Authorization: Bearer <accessToken>` on every endpoint that is not anonymous. The server validates issuer, audience, lifetime and signing key (`Program.cs`). `ClockSkew` is not set, so the library default tolerance of 5 minutes applies.
- **Getting tokens.** `POST /api/auth/guest`, `/register`, `/login`, `/google` and `/refresh` all return an `AuthResponse`:
  - `userId`, `fullName`, `role`, `authProvider`, `age`
  - `accessToken`, `refreshToken`, `accessTokenExpiresAt`

  Use `role` and `userId` from this response (or from `GET /api/users/me`) rather than decoding the JWT. The token carries `sub` (user id), a role claim, `authProvider` and `jti`.
- **Lifetimes.**
  - **Access token:** `Jwt:AccessTokenExpirationMinutes`, default **15 minutes** (`Shared/Users/JwtSettings`). None of the repository's `appsettings*.json` files sets it. Read the actual expiry from `accessTokenExpiresAt` (UTC).
  - **Refresh token:** an opaque base64 string, valid **30 days**. That value is hard-coded in `AuthService.IssueTokensAsync`; `JwtSettings.RefreshTokenExpirationDays` is not used.
- **Refresh flow.**
  1. Refresh shortly before `accessTokenExpiresAt`, or when an authenticated call returns 401.
  2. Call `POST /api/auth/refresh` with the stored refresh token. The response carries **both** a new access token and a new refresh token, and the old refresh token is revoked (rotation). Replace both atomically.
  3. Allow only **one refresh in flight** and queue other requests behind it; a second refresh with the already-rotated token returns 401.
  4. Retry the original request once with the new access token.
  5. If refresh itself returns 401, the session is over: clear the tokens and go to login. (A Guest who loses their tokens cannot get back into that account.)
- **Login 401 is not an expired session.** `POST /api/auth/login` returns 401 for wrong credentials, a bad email format and an inactive account. Do not start the refresh flow on it.
- **Logout.** `POST /api/auth/logout` (bearer required) with the refresh token revokes that refresh token only. The access token stays valid until it expires, so delete both locally.
- **Guest upgrade.** Send the guest's `userId` as `existingGuestUserId` to `POST /api/auth/register` or `POST /api/auth/google`. The same user id is kept, so progress is kept. The old guest refresh token is not revoked automatically, so the client should log it out explicitly as part of the upgrade flow.

### Roles: 401, 403 and 404

- **Roles** are `Child`, `Parent` and `Admin`.
  - `Child` and `Parent` are chosen at sign-up (`role`; anything other than `"Parent"` becomes `Child`).
  - No endpoint creates an `Admin` or changes a role.
- **401 Unauthorized:** the token is missing, malformed, expired or signed wrongly. Body: `{"success":false,"message":"غير مصرح لك بالوصول","data":null}` ("you are not authorized to access"). The login and refresh endpoints also return 401 with their own messages.
- **403 Forbidden now means only one thing: the token's role is not allowed on that endpoint.** Body: `{"success":false,"message":"ليس لديك صلاحية","data":null}` ("you do not have permission"). Role-restricted endpoints:
  - **Admin only:** every Content write (levels, lessons, lesson contents, image upload), and all of `/api/quizzes` except `for-lesson`, `/api/questions`, `/api/question-options`, `/api/assessment/categories` and `/api/assessment/topics`.
  - **Child only:** `/api/placement`.
- **Another user's record is a 404, never a 403.** For another user's attempt, the get, result, submit, retry (`previousAttemptId`) and hint endpoints return **404** with exactly the same message as an id that does not exist, for example `المحاولة رقم 42 غير موجودة`. A response therefore never confirms that someone else's id exists. Profile and topic statistics are always read for the token's own user.

### Content language

Only these endpoints are localized: `POST /api/placement/start`, `GET /api/quizzes/for-lesson/{lessonId}`, `POST /api/quiz-attempts`, `GET /api/quiz-attempts/{attemptId}`, `POST /api/quiz-attempts/{attemptId}/submit`, `GET /api/quiz-attempts/{attemptId}/result`, `GET /api/quiz-attempts/latest`, `POST /api/quiz-attempts/{attemptId}/questions/{questionId}/hint`, `GET /api/user-topic-stats` and `GET /api/user-topic-stats/{topicId}`. Every other endpoint (Auth, Users, Content, placement status, all admin endpoints) ignores language and returns text as stored.

The language is resolved by `ContentLanguageRequestExtensions.ResolveContentLanguage`, then `ContentLanguages.Normalize`:

1. A non-blank `?language=` query value wins. It is trimmed, lower-cased and cut to its primary subtag (`ar-EG` becomes `ar`). An unsupported value such as `fr` becomes `ar`; it does **not** fall through to `Accept-Language`.
2. Otherwise, the highest-weighted `Accept-Language` entry whose primary subtag is `en` or `ar`.
3. Otherwise `ar`.

Each text field then falls back in this order: requested language, then `en`, then the base column. Responses echo the resolved `language`. `languageFallbackApplied: true` means at least one field was not available in that language. The two topic-statistics endpoints apply the same fallback to topic and category names and descriptions, but have no `languageFallbackApplied`. A bad language value never fails a request.

### Dates, ids and numbers

- **Dates are UTC, ISO-8601, but the suffix varies.**
  - A `DateTime` the server set during the same request serializes with `Z` and up to 7 fractional digits (System.Text.Json drops trailing zeros), for example `"2026-09-13T08:30:12.3456789Z"`. Examples: `accessTokenExpiresAt`, `resetTokenExpiresAt`, `startedAt` of a new attempt, `completedAt` and `placement.placedAt` of a fresh submit, `createdAt` of a quiz or lesson in its create response, `updatedAt` of a quiz in its update response.
  - A value read back from SQL Server `datetime2` has no offset, and trailing zeros are dropped too, for example `"2026-09-10T18:42:10.123"` or `"2026-08-01T10:00:00"`. Assessment columns are `DATETIME2(3)`, so their values carry at most 3 fractional digits.
  - **Always parse both forms as UTC.**
- **Ids.**
  - User ids are GUID strings.
  - Levels, lessons, content items, content types, quizzes, questions, options and topics are 32-bit integers.
  - Assessment category ids are `byte` values (1–255).
  - Attempt ids are 64-bit integers (`long`).
- **Small integers.** `displayOrder` and a category's `sortOrder` are `short` values (−32768…32767). `points`, `awardedPoints`, `maxPoints` and `stars` are `byte` values (0–255; `stars` is 0–3).
- **Decimals** such as `scorePercentage` can arrive as `50` (computed in the same request) or `50.00` (read from `DECIMAL(5,2)`). Decode them as a generic number (Dart: `num` then `.toDouble()`), never as a strict double.

### Pagination

Only `GET /api/quizzes` is paged. It returns `PagedResult<T>` (`AssessmentBL/DTOs/Quiz/Common/PagedResult.cs`), bare (no envelope):

```json
{ "items": [ ], "totalCount": 37, "pageNumber": 2, "pageSize": 20 }
```

- Query parameters are `pageNumber` (default 1; values below 1 become 1) and `pageSize` (default 20, clamped to 1–100).
- The response echoes the values actually applied.
- A page past the end returns `items: []` with the real `totalCount`. Page count = `ceil(totalCount / pageSize)`.

Every other list endpoint returns the full list.

### Validation errors (framework 400)

`[ApiController]` validates the request before the action runs, and `Program.cs` registers **no** custom `InvalidModelStateResponseFactory`. Validation failures therefore return ASP.NET Core's **`ValidationProblemDetails`**, not the envelope. In the endpoint tables this is written **400 (framework)** or "400 `ValidationProblemDetails`".

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "traceId": "00-8f1c2b3a6b4d4e2a9c1f3d7e5a2b1c0d-1a2b3c4d5e6f7a8b-00",
  "errors": { "quizId": [ "The value 'abc' is not valid." ] }
}
```

What triggers it:

- A missing or unparsable JSON body.
- A value of the wrong JSON type, or `null` for a non-nullable number or bool.
- A query or route value that cannot be converted (`?pageSize=abc`).
- A number out of range for its type (for example `points: 300` for a `byte`).
- A missing, empty or whitespace-only **non-nullable `string`** property. Every project has `<Nullable>enable</Nullable>`, so each `string` without `?` is implicitly `[Required]`.

What does **not** trigger it: a *missing* `int`, `short`, `byte` or `bool` property. It silently binds to `0` / `false`, and the service then decides (often "id 0 not found", or a flag switched off). The endpoint sections point out where this matters.

Business-rule failures detected by services are **400 with the envelope** and a specific Arabic message.

### Status codes

| Status | Body | When |
|---|---|---|
| 200 OK | Envelope (Auth, Users, Content) or bare DTO (Assessment) | Success. |
| 201 Created | Bare DTO + `Location` | `POST /api/quiz-attempts`, `POST /api/quizzes`, `POST /api/questions`, `POST /api/question-options`, `POST /api/assessment/categories`, `POST /api/assessment/topics`. |
| 204 No Content | Empty | `PATCH /api/quizzes/{quizId}/active`, `PATCH /api/questions/{questionId}/active`, `DELETE /api/question-options/{optionId}`, `PATCH /api/assessment/categories/{categoryId}/active`, `PATCH /api/assessment/topics/{topicId}/active`. |
| 400 Bad Request | Envelope | `BusinessRuleException` or `ArgumentException` from a service. Also `Result` failures that Users and Content controllers map to 400, including some "not found" cases there. |
| 400 Bad Request | `ValidationProblemDetails` | Model binding / validation ([above](#validation-errors-framework-400)). |
| 401 Unauthorized | Envelope, `غير مصرح لك بالوصول` | Missing, invalid or expired token. Login and refresh failures also return 401, with their own messages. |
| 403 Forbidden | Envelope, `ليس لديك صلاحية` | **Only** a role restriction. |
| 404 Not Found | Envelope | `KeyNotFoundException`, including **another user's attempt**. Also not-found results of some Users and Content GETs. |
| 404 Not Found | Empty | No route matched (for example a non-numeric id, or a category id outside 1–255). |
| 409 Conflict | Envelope | `ConflictException`: state conflicts, lost races, already placed, hints exhausted, attempt not submitted yet. |
| 410 Gone | Envelope | `GoneException`: the attempt expired before it was submitted. |
| 415 Unsupported Media Type | Framework response, not the envelope (the body may be empty) | `POST /api/quiz-attempts/{attemptId}/submit` without `Content-Type: application/json`. |
| 500 Internal Server Error | Envelope, `حدث خطأ داخلي في الخادم` ("an internal server error occurred") | Any other exception, including `InvalidOperationException` and EF Core `DbUpdateException` (for example a string longer than its column). The internal message is never sent. |

The exception mapping lives in `ExceptionMiddleware.MapStatusCode`. For 4xx, the exception's message is sent as `message`; the ` (Parameter 'x')` suffix that .NET appends to `ArgumentException` messages is stripped. If the client aborts the request, nothing is written.

---

## Endpoint index

Every controller action in `ElectroWorld/Controllers`, once each (66 endpoints). Auth values:

- **Anonymous:** no token needed.
- **Any signed-in:** any valid access token, whatever the role.
- **Admin / Child:** that role is required; any other role gets 403.
- **(own attempts):** another user's attempt returns 404, exactly like an attempt that does not exist.

Click a route to jump to its section.

<div style="overflow-x:auto">

| # | Area | Method | Route | Auth | Purpose |
|---|---|---|---|---|---|
| 1 | Auth & Users | `POST` | [`/api/auth/guest`](#register-guest) | Anonymous | Create a guest account and its tokens |
| 2 | Auth & Users | `POST` | [`/api/auth/register`](#register-with-email) | Anonymous | Register with email and password, optionally upgrading a guest |
| 3 | Auth & Users | `POST` | [`/api/auth/login`](#login-with-email) | Anonymous | Log in with email and password |
| 4 | Auth & Users | `POST` | [`/api/auth/google`](#google-sign-in--sign-up) | Anonymous | Sign in or sign up with a Google ID token, optionally upgrading a guest |
| 5 | Auth & Users | `POST` | [`/api/auth/refresh`](#refresh-tokens) | Anonymous | Rotate the access and refresh tokens |
| 6 | Auth & Users | `POST` | [`/api/auth/logout`](#logout) | Any signed-in | Revoke a refresh token |
| 7 | Auth & Users | `POST` | [`/api/auth/forgot-password`](#forgot-password-request-otp) | Anonymous | Email a 6-digit password reset code |
| 8 | Auth & Users | `POST` | [`/api/auth/verify-reset-otp`](#verify-reset-otp) | Anonymous | Exchange the code for a reset token |
| 9 | Auth & Users | `POST` | [`/api/auth/reset-password`](#reset-password) | Anonymous | Set a new password with the reset token |
| 10 | Auth & Users | `PATCH` | [`/api/auth/age`](#set-age) | Any signed-in | Set the caller's age |
| 11 | Auth & Users | `GET` | [`/api/users/me`](#get-my-profile) | Any signed-in | Read the caller's profile |
| 12 | Auth & Users | `PUT` | [`/api/users/me`](#update-my-profile) | Any signed-in | Update the caller's name and age |
| 13 | Content | `GET` | [`/api/content/content-types`](#list-content-types) | Any signed-in | List lesson content types |
| 14 | Content | `GET` | [`/api/content/levels`](#list-levels) | Any signed-in | List levels in order |
| 15 | Content | `GET` | [`/api/content/levels/{id}`](#get-level) | Any signed-in | Get one level |
| 16 | Content | `POST` | [`/api/content/levels`](#create-level) | Admin | Create a level |
| 17 | Content | `PUT` | [`/api/content/levels/{id}`](#update-level) | Admin | Update a level's title and description |
| 18 | Content | `DELETE` | [`/api/content/levels/{id}`](#delete-level) | Admin | Delete a level that has no lessons |
| 19 | Content | `POST` | [`/api/content/levels/swap-order`](#swap-level-order) | Admin | Swap the order of two levels |
| 20 | Content | `GET` | [`/api/content/levels/{levelId}/lessons`](#list-lessons-of-a-level) | Any signed-in | List a level's lessons (drafts included) |
| 21 | Content | `GET` | [`/api/content/lessons/{id}`](#get-lesson-detail) | Any signed-in | Get a lesson with its content items |
| 22 | Content | `POST` | [`/api/content/lessons`](#create-lesson) | Admin | Create a draft lesson |
| 23 | Content | `PUT` | [`/api/content/lessons/{id}`](#update-lesson) | Admin | Update a lesson or move it to another level |
| 24 | Content | `PATCH` | [`/api/content/lessons/{id}/publish`](#publish--unpublish-lesson) | Admin | Publish or hide a lesson |
| 25 | Content | `DELETE` | [`/api/content/lessons/{id}`](#delete-lesson) | Admin | Delete a lesson and its content items |
| 26 | Content | `POST` | [`/api/content/lessons/swap-order`](#swap-lesson-order) | Admin | Swap two lessons of the same level |
| 27 | Content | `POST` | [`/api/content/lessons/{lessonId}/contents`](#add-lesson-content-item) | Admin | Add a content item to a lesson |
| 28 | Content | `PUT` | [`/api/content/contents/{contentId}`](#update-lesson-content-item) | Admin | Update a content item |
| 29 | Content | `DELETE` | [`/api/content/contents/{contentId}`](#delete-lesson-content-item) | Admin | Delete a content item |
| 30 | Content | `POST` | [`/api/content/contents/swap-order`](#swap-lesson-content-order) | Admin | Swap two content items of the same lesson |
| 31 | Content | `POST` | [`/api/content/media/images`](#upload-image) | Admin | Upload an image (lesson content, question, option) and get its URL |
| 32 | Assessment (learner) | `GET` | [`/api/placement`](#placement-status) | Child | The caller's placement status and result |
| 33 | Assessment (learner) | `POST` | [`/api/placement/start`](#start-or-resume-placement-test) | Child | Start or resume the placement test |
| 34 | Assessment (learner) | `GET` | [`/api/quizzes/for-lesson/{lessonId}`](#lesson-quiz-preview) | Any signed-in | Preview a published lesson's quiz |
| 35 | Assessment (learner) | `POST` | [`/api/quiz-attempts?quizId=&previousAttemptId=`](#start-attempt-first-attempt-or-retry) | Any signed-in | Start an attempt, or retry the wrong answers of a finished one |
| 36 | Assessment (learner) | `GET` | [`/api/quiz-attempts/{attemptId}`](#get-attempt-resume) | Any signed-in (own attempts) | Redraw an attempt (resume) |
| 37 | Assessment (learner) | `POST` | [`/api/quiz-attempts/{attemptId}/submit`](#submit-attempt) | Any signed-in (own attempts) | Submit every answer and get the result |
| 38 | Assessment (learner) | `GET` | [`/api/quiz-attempts/{attemptId}/result`](#get-saved-result-recovery-and-essay-polling) | Any signed-in (own attempts) | Saved result: recovery and essay polling |
| 39 | Assessment (learner) | `GET` | [`/api/quiz-attempts/latest`](#get-latest-result) | Any signed-in (own attempts) | Result of the caller's most recently completed attempt |
| 40 | Assessment (learner) | `POST` | [`/api/quiz-attempts/{attemptId}/questions/{questionId}/hint`](#hint-button) | Any signed-in (own attempts) | Hint button (escalating levels) |
| 41 | Assessment (learner) | `GET` | [`/api/user-topic-stats?language=`](#my-topic-statistics) | Any signed-in | The caller's progress map: XP, mastery and stars for every topic, by category |
| 42 | Assessment (learner) | `GET` | [`/api/user-topic-stats/{topicId}?language=`](#one-topics-progress) | Any signed-in | The caller's progress in one topic, with a breakdown per difficulty |
| 43 | Assessment (admin) | `GET` | [`/api/quizzes/{quizId}`](#get-quiz-by-id) | Admin | Get a quiz |
| 44 | Assessment (admin) | `GET` | [`/api/quizzes`](#list-quizzes-paged) | Admin | List quizzes with filters (paged) |
| 45 | Assessment (admin) | `POST` | [`/api/quizzes`](#create-quiz) | Admin | Create a quiz (always active) |
| 46 | Assessment (admin) | `PUT` | [`/api/quizzes/{quizId}`](#update-quiz) | Admin | Replace a quiz's title, description and active flag |
| 47 | Assessment (admin) | `PATCH` | [`/api/quizzes/{quizId}/active?isActive=`](#activate--deactivate-quiz) | Admin | Activate or deactivate a quiz |
| 48 | Assessment (admin) | `GET` | [`/api/questions?quizId=`](#list-questions-of-a-quiz) | Admin | List a quiz's questions with options and answer key |
| 49 | Assessment (admin) | `GET` | [`/api/questions/{questionId}`](#get-question-by-id) | Admin | Get a question with its options |
| 50 | Assessment (admin) | `POST` | [`/api/questions`](#create-question) | Admin | Create a question (inactive) |
| 51 | Assessment (admin) | `PUT` | [`/api/questions/{questionId}`](#update-question) | Admin | Replace a question, optionally activating it |
| 52 | Assessment (admin) | `PATCH` | [`/api/questions/{questionId}/active?isActive=`](#activate--deactivate-question) | Admin | Activate or deactivate a question |
| 53 | Assessment (admin) | `GET` | [`/api/question-options?questionId=`](#list-options-of-a-question) | Admin | List a question's options |
| 54 | Assessment (admin) | `POST` | [`/api/question-options`](#create-option) | Admin | Add an option to a question |
| 55 | Assessment (admin) | `PUT` | [`/api/question-options/{optionId}`](#update-option) | Admin | Replace an option |
| 56 | Assessment (admin) | `DELETE` | [`/api/question-options/{optionId}`](#delete-option) | Admin | Delete an option that no attempt references |
| 57 | Assessment (admin) | `GET` | [`/api/assessment/categories?isActive=`](#list-assessment-categories) | Admin | List categories with their translations and topic counts |
| 58 | Assessment (admin) | `GET` | [`/api/assessment/categories/{categoryId}`](#get-assessment-category) | Admin | Get a category |
| 59 | Assessment (admin) | `POST` | [`/api/assessment/categories`](#create-assessment-category) | Admin | Create a category (active, id assigned by the server) |
| 60 | Assessment (admin) | `PUT` | [`/api/assessment/categories/{categoryId}`](#update-assessment-category) | Admin | Replace a category's name, order, active flag and (when sent) translations |
| 61 | Assessment (admin) | `PATCH` | [`/api/assessment/categories/{categoryId}/active?isActive=`](#activate--deactivate-assessment-category) | Admin | Activate or deactivate a category |
| 62 | Assessment (admin) | `GET` | [`/api/assessment/topics?categoryId=&learningLevel=&isActive=`](#list-assessment-topics) | Admin | List topics with filters (the question form's topic picker) |
| 63 | Assessment (admin) | `GET` | [`/api/assessment/topics/{topicId}`](#get-assessment-topic) | Admin | Get a topic |
| 64 | Assessment (admin) | `POST` | [`/api/assessment/topics`](#create-assessment-topic) | Admin | Create a topic (active) in an existing category |
| 65 | Assessment (admin) | `PUT` | [`/api/assessment/topics/{topicId}`](#update-assessment-topic) | Admin | Replace a topic and (when sent) its translations |
| 66 | Assessment (admin) | `PATCH` | [`/api/assessment/topics/{topicId}/active?isActive=`](#activate--deactivate-assessment-topic) | Admin | Activate or deactivate a topic |

</div>

---

## Auth & Users

Controllers: `AuthController` and `UsersController` (`ElectroWorld/Controllers/Users`), backed by `UsersBL.Services.AuthService` and `UsersBL.Services.UserService`.

- Success bodies use the envelope. Failures written by these controllers use the envelope with or without `data` (see [Response shapes](#response-shapes)).
- Not localized: `?language=` and `Accept-Language` are ignored.
- The controllers turn service `Result` failures into a fixed status per endpoint, so the same kind of failure can be 400 on one endpoint and 401 or 404 on another. Each endpoint lists its exact statuses.
- String lengths are limited only by the database (`FullName` 150, `Email` 255). An over-length value returns 500.

---

### Register guest

`POST /api/auth/guest`: called by the Child app (or the Parent app) on first launch, so a learner can start without an account.

- **Auth:** anonymous. Neither the class nor the action has `[Authorize]`.
- **Headers / language:** `Content-Type: application/json`. Not localized.
- **Path / query parameters:** none.
- **Request body:** `application/json`, type `RegisterGuestRequest`.

| Field | Type | Required | Rules |
|---|---|---|---|
| `fullName` | string | yes | Must not be empty or whitespace (framework 400). No length check in code; the database column is `nvarchar(150)`, so a longer value causes a 500. |
| `role` | string \| null | no | `"Parent"` (any letter case) creates a Parent. Anything else, including `null`, `"Admin"` or a typo, creates a **Child** (`AuthService.NormalizeRole`). |

```json
{ "fullName": "Youssef", "role": "Child" }
```

- **Success:** `200`

```json
{
  "success": true,
  "message": "تم إنشاء حساب Guest بنجاح",
  "data": {
    "userId": "8b1f3c2e-4d5a-4e6f-9a7b-1c2d3e4f5a6b",
    "fullName": "Youssef",
    "role": "Child",
    "authProvider": "Guest",
    "age": null,
    "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
    "refreshToken": "o2V7cQ0m1p0bQy8m3kR0eYj3y0h5Xl8m5nq0m2mYg3v1w3o6h8V0yq3m1r2d5s8t4u7w9x0z1a2b3c4d5e6f7g==",
    "accessTokenExpiresAt": "2026-09-13T10:15:42.1234567Z"
  }
}
```

| Field | Meaning |
|---|---|
| `userId` | The new user's GUID. **Store it**: guest conversion (`existingGuestUserId` on register or google) needs it. |
| `role` | `"Child"` or `"Parent"`. This endpoint never returns `"Admin"`. |
| `authProvider` | Always `"Guest"` here. Other endpoints return `"Email"` or `"Google"`. |
| `age` | Always `null` for a new guest. |
| `accessToken` | JWT to send as `Authorization: Bearer <token>`. It carries `sub` (the user id), the role claim, `authProvider` and `jti`. |
| `refreshToken` | Opaque base64 string (64 random bytes). The server stores only its hash. Valid for 30 days (hard-coded in `AuthService.IssueTokensAsync`). |
| `accessTokenExpiresAt` | UTC expiry of `accessToken`, set by `Jwt:AccessTokenExpirationMinutes` (default 15). |

- **Errors:**

| Status | When | Example |
|---|---|---|
| 400 (framework) | Body missing, or `fullName` missing, empty or whitespace. | ValidationProblemDetails |
| 500 | Database failure, for example `fullName` longer than 150 characters. | `حدث خطأ داخلي في الخادم` |

`RegisterGuestAsync` has no business-rule failure path, so no envelope 400 is possible.

- **Frontend notes:** This call is **not idempotent**: every call creates another guest user. Call it once and persist `userId`, `accessToken` and `refreshToken` securely. Mock: `docs/mocks/mock_auth_guest.json`.

---

### Register with email

`POST /api/auth/register`: called by the Parent app and the Child app sign-up screen, including a guest upgrading to a real account.

- **Auth:** anonymous.
- **Headers / language:** `Content-Type: application/json`. Not localized.
- **Path / query parameters:** none.
- **Request body:** `application/json`, type `RegisterEmailRequest`.

| Field | Type | Required | Rules |
|---|---|---|---|
| `email` | string | yes | Must parse with `System.Net.Mail.MailAddress`, **and** the parsed address must equal the input exactly. Leading or trailing spaces, or a display-name form, are rejected. Must not already exist. No trim or lower-casing on the server. DB column `nvarchar(255)`. |
| `password` | string | yes | Must not be empty or whitespace (framework 400). **No length or strength rule in code.** |
| `fullName` | string | yes | Must not be empty or whitespace. DB column `nvarchar(150)`. |
| `role` | string \| null | no | `"Parent"` (any case) creates a Parent; anything else creates a Child. **Ignored when converting a guest**: the guest keeps its role. |
| `age` | int \| null | no | `null`, or **7 to 18 inclusive**. The range applies to every role, Parent included (`AuthService.IsValidAge`). |
| `existingGuestUserId` | GUID \| null | no | When set, the server upgrades that Guest user in place (same `userId`, so progress is kept) instead of creating a new user. |

```json
{
  "email": "mona@example.com",
  "password": "S3cure-pass",
  "fullName": "Mona Adel",
  "role": "Child",
  "age": 11,
  "existingGuestUserId": "8b1f3c2e-4d5a-4e6f-9a7b-1c2d3e4f5a6b"
}
```

- **Success:** `200`. `data` is an `AuthResponse` (fields as in *Register guest*), with `authProvider: "Email"`.

```json
{
  "success": true,
  "message": "تم التسجيل بنجاح",
  "data": {
    "userId": "8b1f3c2e-4d5a-4e6f-9a7b-1c2d3e4f5a6b",
    "fullName": "Mona Adel",
    "role": "Child",
    "authProvider": "Email",
    "age": 11,
    "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
    "refreshToken": "Zm9vYmFyYmF6cXV4...==",
    "accessTokenExpiresAt": "2026-09-13T10:15:42.1234567Z"
  }
}
```

When converting a guest, `userId` equals `existingGuestUserId`. The server overwrites the guest's `email`, password, `fullName` and `age` (a `null` age clears it) and sets `convertedFromGuestAt`.

- **Errors:** checked in this order (`AuthService.RegisterWithEmailAsync`).

| Status | When | Example |
|---|---|---|
| 400 (framework) | Body missing, or `email`, `password` or `fullName` missing or blank, or `existingGuestUserId` not a GUID, or `age` not an integer. | ValidationProblemDetails |
| 400 | Email fails the format check. | `صيغة الإيميل غير صحيحة` ("Email format is invalid") |
| 400 | `age` outside 7 to 18. | `السن لازم يكون بين 7 و 18 سنة` ("Age must be between 7 and 18") |
| 400 | Email already registered. This is checked **before** the guest conversion. | `البريد الإلكتروني مستخدم بالفعل` ("Email already in use") |
| 400 | `existingGuestUserId` does not exist, or that user is no longer a Guest (already converted). | `حساب الـ Guest غير موجود أو اتحول قبل كده` ("Guest account not found or already converted") |
| 500 | Database failure: over-length strings, or two concurrent registrations with the same email hitting the unique index `UQ_Users_Email`. | `حدث خطأ داخلي في الخادم` |

- **Frontend notes:**
  - Not idempotent. A retry after a lost response returns `البريد الإلكتروني مستخدم بالفعل`. Recover by calling login.
  - Trim the email on the client before sending, because the server rejects surrounding whitespace.
  - Do not collect an age outside 7 to 18; the API validates against that range.
  - After a guest conversion, replace the stored tokens with the new ones. The old guest refresh token is **not** revoked by the server, so call `POST /api/auth/logout` with it to revoke it.
  - Mock: `docs/mocks/mock_auth_register.json`.

---

### Login with email

`POST /api/auth/login`: called by the Parent app and the Child app login screen.

- **Auth:** anonymous.
- **Headers / language:** `Content-Type: application/json`. Not localized.
- **Path / query parameters:** none.
- **Request body:** `application/json`, type `LoginEmailRequest`.

| Field | Type | Required | Rules |
|---|---|---|---|
| `email` | string | yes | Same format check as register. |
| `password` | string | yes | Must not be blank. |

```json
{ "email": "mona@example.com", "password": "S3cure-pass" }
```

- **Success:** `200`, `data` = `AuthResponse`.

```json
{
  "success": true,
  "message": "تم تسجيل الدخول بنجاح",
  "data": {
    "userId": "8b1f3c2e-4d5a-4e6f-9a7b-1c2d3e4f5a6b",
    "fullName": "Mona Adel",
    "role": "Child",
    "authProvider": "Email",
    "age": 11,
    "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
    "refreshToken": "Zm9vYmFyYmF6cXV4...==",
    "accessTokenExpiresAt": "2026-09-13T10:15:42.1234567Z"
  }
}
```

- **Errors:** the controller maps **every** service failure to **401**.

| Status | When | Example |
|---|---|---|
| 400 (framework) | Body missing, or `email` or `password` blank. | ValidationProblemDetails |
| 401 | Email fails the format check. | `صيغة الإيميل غير صحيحة` |
| 401 | Unknown email, wrong password, or an account with no password (Google or Guest). | `بيانات الدخول غير صحيحة` ("Invalid login credentials") |
| 401 | Account has `IsActive = false`. | `الحساب غير مفعّل` ("Account is not active") |

- **Frontend notes:**
  - A 401 from this endpoint is a login failure, not an expired session. Do not start the refresh-token flow on it; show `message`.
  - Every successful login issues a **new** refresh token; earlier ones stay valid until they expire or are logged out.
  - No lockout or rate limiting exists in code.
  - Mocks: `docs/mocks/mock_auth_login.json`, `docs/mocks/mock_auth_login_invalid.json`.

---

### Google sign-in / sign-up

`POST /api/auth/google`: called by the Parent app and the Child app after the native Google Sign-In returns an ID token.

- **Auth:** anonymous.
- **Headers / language:** `Content-Type: application/json`. Not localized.
- **Path / query parameters:** none.
- **Request body:** `application/json`, type `GoogleAuthRequest`.

| Field | Type | Required | Rules |
|---|---|---|---|
| `idToken` | string | yes | Google ID token. It is validated with `GoogleJsonWebSignature.ValidateAsync`, and its audience must be the configured `Google:ClientId`. |
| `role` | string \| null | no | Used **only** when a brand-new user is created. `"Parent"` (any case) creates a Parent; anything else creates a Child. |
| `existingGuestUserId` | GUID \| null | no | Used only when no user is linked to this Google account yet. The Guest is converted in place. |

```json
{
  "idToken": "eyJhbGciOiJSUzI1NiIsImtpZCI6Ij...",
  "role": "Parent",
  "existingGuestUserId": null
}
```

- **Success:** `200`, `data` = `AuthResponse` with `authProvider: "Google"`. The service handles three cases, in this order (`AuthService.LoginOrRegisterWithGoogleAsync`):
  1. **A user is already linked to this Google account.** This is a normal login. `role` and `existingGuestUserId` are **ignored**, and a guest's progress is **not** merged.
  2. **`existingGuestUserId` is set.** That Guest becomes a Google user with the same `userId`. The server stores the Google `email`. `fullName` stays the guest's name unless it was blank, and `age` is unchanged.
  3. **Otherwise** a new user is created. `fullName` is the Google name, or the email when Google sends no name. `age` is `null`.

```json
{
  "success": true,
  "message": "تم الدخول عن طريق Google بنجاح",
  "data": {
    "userId": "c7a1d2e3-5f60-4b71-8c92-a3b4c5d6e7f8",
    "fullName": "Ahmed Ali",
    "role": "Parent",
    "authProvider": "Google",
    "age": null,
    "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
    "refreshToken": "YmF6cXV4Zm9vYmFy...==",
    "accessTokenExpiresAt": "2026-09-13T10:15:42.1234567Z"
  }
}
```

- **Errors:** the controller maps every service failure to **400**.

| Status | When | Example |
|---|---|---|
| 400 (framework) | Body missing, or `idToken` blank, or `existingGuestUserId` not a GUID. | ValidationProblemDetails |
| 400 | Google rejected the token (`InvalidJwtException`): bad signature, wrong audience, or expired. | `Google Token غير صالح` ("Invalid Google token") |
| 400 | An already-linked account has `IsActive = false`. | `الحساب غير مفعّل` |
| 400 | `existingGuestUserId` not found, or that user is not a Guest. | `حساب الـ Guest غير موجود أو اتحول قبل كده` |
| 500 | The Google email already belongs to another user, for example an Email-registered account. There is no uniqueness check in this path, so the unique index `UQ_Users_Email` fails. Also any validator error other than `InvalidJwtException`, such as a network failure while fetching Google certificates. | `حدث خطأ داخلي في الخادم` |

- **Frontend notes:**
  - The Google flow never collects an age. If `data.age` is `null` and the product needs one (the Child app), show the age screen next and call `PATCH /api/auth/age`.
  - The email-collision 500 is not recoverable by retrying. Tell the user to log in with email and password instead.
  - After a guest conversion, revoke the old guest refresh token with `POST /api/auth/logout`, as in register.
  - No mock file exists for this endpoint.

---

### Refresh tokens

`POST /api/auth/refresh`: called by every client's HTTP layer when the access token has expired or is about to expire.

- **Auth:** anonymous. The expired access token is not needed.
- **Headers / language:** `Content-Type: application/json`. Not localized.
- **Path / query parameters:** none.
- **Request body:** `application/json`, type `RefreshTokenRequest`.

| Field | Type | Required | Rules |
|---|---|---|---|
| `refreshToken` | string | yes | A refresh token from an earlier `AuthResponse`. It must exist, must not be revoked, and must not be expired. |

```json
{ "refreshToken": "Zm9vYmFyYmF6cXV4...==" }
```

- **Success:** `200`, `data` = `AuthResponse` with a **new** `accessToken` **and a new `refreshToken`**. `role`, `fullName` and `age` reflect the current database values.

```json
{
  "success": true,
  "message": "تم تجديد التوكن بنجاح",
  "data": {
    "userId": "8b1f3c2e-4d5a-4e6f-9a7b-1c2d3e4f5a6b",
    "fullName": "Mona Adel",
    "role": "Child",
    "authProvider": "Email",
    "age": 11,
    "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.rotated...",
    "refreshToken": "bmV3bHlSb3RhdGVk...==",
    "accessTokenExpiresAt": "2026-09-13T10:30:42.1234567Z"
  }
}
```

- **Errors:**

| Status | When | Example |
|---|---|---|
| 400 (framework) | Body missing or `refreshToken` blank. | ValidationProblemDetails |
| 401 | The token is unknown, already revoked (including by an earlier refresh, because of rotation), or expired. | `Refresh Token غير صالح أو منتهي` ("Refresh token invalid or expired") |
| 401 | The token's user no longer exists or has `IsActive = false`. | `المستخدم غير موجود أو غير مفعّل` ("User not found or not active") |

- **Frontend notes:**
  - **Rotation.** Every successful call revokes the token you sent. Always replace both stored tokens atomically.
  - **Serialize refreshes.** Use a single in-flight refresh shared by all failing requests. Once one refresh has saved, a second call with the same old token gets 401. The code has no concurrency guard, so two truly simultaneous calls are not reliably rejected either.
  - A 401 from this endpoint means the session is over: clear the tokens and go to login. For a Guest, that means the guest's progress is unreachable unless you still have a valid token.
  - Mock: `docs/mocks/mock_auth_refresh.json`.

---

### Logout

`POST /api/auth/logout`: called by any signed-in user (Child app, Parent app, Admin dashboard).

- **Auth:** Bearer token (any role). The action has `[Authorize]`.
- **Headers / language:** `Authorization: Bearer <accessToken>`, `Content-Type: application/json`. Not localized.
- **Path / query parameters:** none.
- **Request body:** `application/json`, type `RefreshTokenRequest`.

| Field | Type | Required | Rules |
|---|---|---|---|
| `refreshToken` | string | yes | The refresh token to revoke. The server does **not** check that it belongs to the caller. |

```json
{ "refreshToken": "Zm9vYmFyYmF6cXV4...==" }
```

- **Success:** `200`. The non-generic envelope has no `data` key.

```json
{ "success": true, "message": "تم تسجيل الخروج بنجاح" }
```

- **Errors:**

| Status | When | Example |
|---|---|---|
| 400 (framework) | Body missing or `refreshToken` blank. | ValidationProblemDetails |
| 400 | No stored refresh token matches. | `{"success":false,"message":"Token غير موجود"}` ("Token not found"), with no `data` key |
| 401 | Access token missing, invalid or expired. | `غير مصرح لك بالوصول` |

- **Frontend notes:**
  - Only the refresh token is revoked. The access token is a stateless JWT and stays valid until `accessTokenExpiresAt`, so delete it locally.
  - Logging out a token that is already revoked returns 200 again, which makes retries safe. An unknown token returns 400; treat that as already logged out.
  - If the access token has expired, the call returns 401 before reaching the service. Either refresh first, or just clear local state.
  - Mock: `docs/mocks/mock_auth_logout.json`.

---

### Forgot password (request OTP)

`POST /api/auth/forgot-password`: called by the Parent app and the Child app "forgot password" screen.

- **Auth:** anonymous.
- **Headers / language:** `Content-Type: application/json`. Not localized. The email content is Arabic.
- **Path / query parameters:** none.
- **Request body:** `application/json`, type `ForgotPasswordRequest`.

| Field | Type | Required | Rules |
|---|---|---|---|
| `email` | string | yes | Must not be blank. No format check in this endpoint. |

```json
{ "email": "mona@example.com" }
```

- **Success:** always `200`, whether or not the email is registered (`AuthService.ForgotPasswordAsync`).

```json
{ "success": true, "message": "لو الإيميل مسجل، هيوصلك كود إعادة التعيين" }
```

The message means "If the email is registered, you will receive a reset code". Only users with `authProvider = "Email"` get a code. Google and Guest users get the same 200, but no email is sent. For an Email user, the server:

1. Deletes that user's previous unverified, unexpired codes, so only the newest code works.
2. Creates a 6-digit code valid for **15 minutes**.
3. Emails the code (subject `كود إعادة تعيين كلمة المرور - ElectroWorld`, "Password reset code - ElectroWorld").

If SMTP fails, the request still returns 200 and no email arrives.

- **Errors:**

| Status | When | Example |
|---|---|---|
| 400 (framework) | Body missing or `email` blank. | ValidationProblemDetails |
| 500 | Database failure while saving the code. SMTP failures are swallowed. | `حدث خطأ داخلي في الخادم` |

- **Frontend notes:**
  - Always move to the OTP screen after 200.
  - A "resend code" button can call this again; it invalidates the previous code.
  - There is no server-side rate limit, so throttle the resend button on the client (for example a 60-second cooldown).
  - Mock: `docs/mocks/mock_auth_forgot_password.json`.

---

### Verify reset OTP

`POST /api/auth/verify-reset-otp`: called by the Parent app and the Child app OTP screen.

- **Auth:** anonymous.
- **Headers / language:** `Content-Type: application/json`. Not localized.
- **Path / query parameters:** none.
- **Request body:** `application/json`, type `VerifyResetOtpRequest`.

| Field | Type | Required | Rules |
|---|---|---|---|
| `email` | string | yes | The same email that was sent to forgot-password. |
| `otp` | string | yes | The 6-digit code, compared by exact hash. Strip spaces on the client. |

```json
{ "email": "mona@example.com", "otp": "482913" }
```

- **Success:** `200`

```json
{
  "success": true,
  "message": "الكود صحيح",
  "data": {
    "resetToken": "k3J9x2Q7m1Z8p4R6t0Y5w2E8u1I3o7P9a4S6d8F0g2H5j7K9l1Z3x5C7v9B1n3M5q7W9e1R3t5Y7u9I1o3P5a7S9d==",
    "resetTokenExpiresAt": "2026-09-13T10:10:42.1234567Z"
  }
}
```

| Field | Meaning |
|---|---|
| `resetToken` | Single-use token for `POST /api/auth/reset-password`. The server stores only its hash. |
| `resetTokenExpiresAt` | UTC, **10 minutes** after verification. |

- **Errors:** all are 400, checked in this order.

| Status | When | Example |
|---|---|---|
| 400 (framework) | Body missing or a field blank. | ValidationProblemDetails |
| 400 | No user with this email. | `بيانات غير صحيحة` ("Incorrect data") |
| 400 | No usable code: never requested, older than 15 minutes, already verified once, or **5 wrong attempts reached**. | `الكود منتهي أو غير صالح، اطلبي كود جديد` ("Code expired or invalid, request a new one") |
| 400 | Wrong code. Each wrong attempt increments the counter. | `الكود غير صحيح` ("Incorrect code") |

- **Frontend notes:**
  - On `الكود غير صحيح` let the user try again. After the 5th wrong attempt every further try returns `الكود منتهي أو غير صالح، اطلبي كود جديد`; then offer "send a new code" (forgot-password).
  - A code verifies **once**. If the response is lost, verifying again fails, so start over with forgot-password.
  - Keep `resetToken` in memory only, and move to the new-password screen immediately (10-minute window).
  - Mocks: `docs/mocks/mock_auth_verify_otp.json`, `docs/mocks/mock_auth_verify_otp_invalid.json`.

---

### Reset password

`POST /api/auth/reset-password`: called by the Parent app and the Child app new-password screen.

- **Auth:** anonymous.
- **Headers / language:** `Content-Type: application/json`. Not localized.
- **Path / query parameters:** none.
- **Request body:** `application/json`, type `ResetPasswordRequest`.

| Field | Type | Required | Rules |
|---|---|---|---|
| `email` | string | yes | The same email used to verify the code. |
| `resetToken` | string | yes | From verify-reset-otp. Must belong to this user and be unexpired and unused. |
| `newPassword` | string | yes | Must not be blank. **No length or strength rule in code.** |

```json
{
  "email": "mona@example.com",
  "resetToken": "k3J9x2Q7m1Z8p4R6t0Y5w2E8u1I3o7P9...==",
  "newPassword": "N3w-secure-pass"
}
```

- **Success:** `200`. No `data` key and **no tokens**.

```json
{ "success": true, "message": "تم تغيير كلمة المرور بنجاح" }
```

- **Errors:**

| Status | When | Example |
|---|---|---|
| 400 (framework) | Body missing or a field blank. | ValidationProblemDetails |
| 400 | No user with this email. | `{"success":false,"message":"بيانات غير صحيحة"}` |
| 400 | Reset token wrong, belongs to another email, expired (older than 10 minutes), or already used. | `{"success":false,"message":"جلسة إعادة التعيين منتهية أو غير صالحة، ابدئي من كود جديد"}` ("Reset session expired or invalid, start again from a new code") |

- **Frontend notes:**
  - The reset token is single-use. A retry after a lost 200 returns the session-expired error, but the password **was** changed. On that error after a timeout, offer login before "start over".
  - After success, send the user to login; this endpoint does not sign them in.
  - Existing refresh tokens on other devices are **not** revoked by the server.
  - Apply client-side password rules, because the server has none.
  - No mock file exists.

---

### Set age

`PATCH /api/auth/age`: called by the Child app right after Google sign-in (which never collects an age). Any signed-in user can call it.

- **Auth:** Bearer token (any role). The action has `[Authorize]`.
- **Headers / language:** `Authorization: Bearer <accessToken>`, `Content-Type: application/json`. Not localized. The user id comes from the token's `sub` claim, never from the body.
- **Path / query parameters:** none.
- **Request body:** `application/json`, type `SetAgeRequest`.

| Field | Type | Required | Rules |
|---|---|---|---|
| `age` | int | yes | **7 to 18 inclusive**, for every role. A missing `age` binds as `0` and fails the range check; `null` is a framework 400. |

```json
{ "age": 12 }
```

- **Success:** `200`, `data` = `UserProfileResponse` (fields described under *Get my profile*).

```json
{
  "success": true,
  "message": "تم تحديث السن بنجاح",
  "data": {
    "id": "c7a1d2e3-5f60-4b71-8c92-a3b4c5d6e7f8",
    "email": "ahmed@example.com",
    "fullName": "Ahmed Ali",
    "role": "Child",
    "authProvider": "Google",
    "age": 12,
    "isActive": true,
    "convertedFromGuestAt": null,
    "createdAt": "2026-08-01T10:00:00"
  }
}
```

- **Errors:**

| Status | When | Example |
|---|---|---|
| 400 (framework) | Body missing, or `age` is `null` or not an integer. | ValidationProblemDetails |
| 400 | `age` outside 7 to 18, including a missing `age` (bound as `0`). | `السن لازم يكون بين 7 و 18 سنة` |
| 400 | The token's user no longer exists. | `المستخدم غير موجود` ("User not found") |
| 401 | Token missing, invalid or expired. | `غير مصرح لك بالوصول` |

- **Frontend notes:**
  - This is idempotent: setting the same age twice gives the same result.
  - The current access token's claims do not include age, so no refresh is needed afterwards.
  - Use the `data` object to update the cached profile.
  - No mock file exists.

---

### Get my profile

`GET /api/users/me`: called by any signed-in user (Child app, Parent app, Admin dashboard).

- **Auth:** Bearer token (any role). The class has `[Authorize]`.
- **Headers / language:** `Authorization: Bearer <accessToken>`. Not localized.
- **Path / query parameters:** none.
- **Request body:** none.
- **Success:** `200`

```json
{
  "success": true,
  "message": "تمت العملية بنجاح",
  "data": {
    "id": "8b1f3c2e-4d5a-4e6f-9a7b-1c2d3e4f5a6b",
    "email": "mona@example.com",
    "fullName": "Mona Adel",
    "role": "Child",
    "authProvider": "Email",
    "age": 11,
    "isActive": true,
    "convertedFromGuestAt": "2026-09-12T18:03:11.523",
    "createdAt": "2026-09-10T09:41:27.117"
  }
}
```

| Field | Meaning |
|---|---|
| `id` | User GUID. It matches `userId` in `AuthResponse`. |
| `email` | `null` for a Guest. |
| `role` | `"Child"`, `"Parent"` or `"Admin"`. The database allows `Admin`, but no endpoint creates one. |
| `authProvider` | `"Email"`, `"Google"` or `"Guest"`. |
| `age` | `null` until it is set. |
| `isActive` | Account flag. No endpoint changes it. |
| `convertedFromGuestAt` | UTC time of the guest-to-Email or guest-to-Google upgrade, or `null`. Read from the database, so it has no `Z` suffix. |
| `createdAt` | UTC, read from the database, so it has no `Z` suffix. |

- **Errors:**

| Status | When | Example |
|---|---|---|
| 401 | Token missing, invalid or expired. | `غير مصرح لك بالوصول` |
| 404 | The token is valid but its user no longer exists. | `{"success":false,"message":"المستخدم غير موجود","data":null}` |

- **Frontend notes:**
  - Safe to cache and re-fetch at app start.
  - Use `authProvider === "Guest"` to show a "create an account to save your progress" prompt.
  - This endpoint does not check `isActive`.
  - Mock: `docs/mocks/mock_users_me.json`.

---

### Update my profile

`PUT /api/users/me`: called by any signed-in user from the profile/settings screen.

- **Auth:** Bearer token (any role). The class has `[Authorize]`.
- **Headers / language:** `Authorization: Bearer <accessToken>`, `Content-Type: application/json`. Not localized.
- **Path / query parameters:** none.
- **Request body:** `application/json`, type `UpdateProfileRequest`. This is a full replacement of both fields.

| Field | Type | Required | Rules |
|---|---|---|---|
| `fullName` | string | yes | Must not be blank. DB column `nvarchar(150)`, so a longer value causes a 500. |
| `age` | int \| null | no, but see note | `null`, or 7 to 18. **A missing or `null` age clears the stored age** (`UserService.UpdateProfileAsync` assigns it unconditionally). |

Email, role and password cannot be changed here.

```json
{ "fullName": "Mona A. Adel", "age": 12 }
```

- **Success:** `200`, `data` = `UserProfileResponse`.

```json
{
  "success": true,
  "message": "تم تحديث البيانات بنجاح",
  "data": {
    "id": "8b1f3c2e-4d5a-4e6f-9a7b-1c2d3e4f5a6b",
    "email": "mona@example.com",
    "fullName": "Mona A. Adel",
    "role": "Child",
    "authProvider": "Email",
    "age": 12,
    "isActive": true,
    "convertedFromGuestAt": null,
    "createdAt": "2026-09-10T09:41:27.117"
  }
}
```

- **Errors:**

| Status | When | Example |
|---|---|---|
| 400 (framework) | Body missing, `fullName` blank, or `age` not an integer. | ValidationProblemDetails |
| 400 | The token's user no longer exists. This returns **400**, not 404 as in GET. | `المستخدم غير موجود` |
| 400 | `age` outside 7 to 18. | `السن لازم يكون بين 7 و 18 سنة` |
| 401 | Token missing, invalid or expired. | `غير مصرح لك بالوصول` |
| 500 | Database failure, for example `fullName` longer than 150 characters. | `حدث خطأ داخلي في الخادم` |

- **Frontend notes:**
  - Always send the current `age` back, even when only the name changes; otherwise the age is erased.
  - Idempotent (a PUT with the same body gives the same result).
  - Parent accounts cannot store an adult age (the 7 to 18 range applies to every role).
  - No mock file exists.

---

## Content

Controllers in `ElectroWorld/Controllers/Content` (`ContentTypesController`, `LevelsController`, `LessonsController`, `LessonContentsController`, `MediaController`), backed by `ContentBL.Services`.

- Reads (`GET`) are open to any signed-in role. Every write, and the image upload, is Admin-only.
- Success bodies use the envelope. Several "not found" cases on writes return **400**, not 404; each endpoint lists its statuses.
- Not localized: titles and texts come back exactly as the admin stored them.
- String lengths are limited only by the database (`Title` 200, `Description` 1000, `MediaUrl` 500). An over-length value returns 500.

---

### List content types

`GET /api/content/content-types`: called by the Admin dashboard (lesson-content editor) and readable by the Child app.

- **Auth:** Bearer token (any role). The class has `[Authorize]`.
- **Headers / language:** `Authorization: Bearer <accessToken>`. Not localized.
- **Path / query parameters:** none.
- **Request body:** none.
- **Success:** `200`. Sorted by `id` (`ContentTypeRepository.GetAllAsync`).

```json
{
  "success": true,
  "message": "تمت العملية بنجاح",
  "data": [
    { "id": 1, "name": "Text" },
    { "id": 2, "name": "Image" },
    { "id": 3, "name": "TextAndImage" }
  ]
}
```

The rows come from the `LearningContent.ContentTypes` table. The repository contains no seed data for it. The ids and names above are taken from the Swagger example and the mock, not from a seed script.

- **Errors:**

| Status | When | Example |
|---|---|---|
| 401 | Token missing, invalid or expired. | `غير مصرح لك بالوصول` |

- **Frontend notes:**
  - This is a lookup table: fetch it once per session and cache it.
  - Do not hard-code ids; match by `name`.
  - Mock: `docs/mocks/mock_content_types.json`.

---

### List levels

`GET /api/content/levels`: called by the Child app (level map) and the Admin dashboard.

- **Auth:** Bearer token (any role).
- **Headers / language:** `Authorization: Bearer <accessToken>`. Not localized.
- **Path / query parameters:** none.
- **Request body:** none.
- **Success:** `200`. Sorted by `order` ascending (`LevelRepository.GetAllAsync`). The list is empty when no levels exist.

```json
{
  "success": true,
  "message": "تمت العملية بنجاح",
  "data": [
    { "id": 1, "title": "مقدمة في الكهرباء", "description": "مفاهيم أساسية", "order": 1 },
    { "id": 2, "title": "الدوائر البسيطة", "description": null, "order": 2 }
  ]
}
```

| Field | Meaning |
|---|---|
| `order` | Display position, set by the server and changed only by swap-order. Values need not be contiguous (a delete leaves a gap). The placement test uses the same ordering (`LevelCatalogService`). |

- **Errors:**

| Status | When | Example |
|---|---|---|
| 401 | Token missing, invalid or expired. | `غير مصرح لك بالوصول` |

- **Frontend notes:**
  - Render in the order returned; do not compute positions from `order` values.
  - Cache-friendly.
  - Mock: `docs/mocks/mock_levels_list.json`.

---

### Get level

`GET /api/content/levels/{id}`: called by the Child app and the Admin dashboard.

- **Auth:** Bearer token (any role).
- **Headers / language:** `Authorization: Bearer <accessToken>`. Not localized.
- **Path / query parameters:**

| Name | Type | Required | Rules |
|---|---|---|---|
| `id` | int (route) | yes | A non-integer value gets a routing 404 with an empty body. |

- **Request body:** none.
- **Success:** `200`

```json
{
  "success": true,
  "message": "تمت العملية بنجاح",
  "data": { "id": 1, "title": "مقدمة في الكهرباء", "description": "مفاهيم أساسية", "order": 1 }
}
```

- **Errors:**

| Status | When | Example |
|---|---|---|
| 401 | Token missing, invalid or expired. | `غير مصرح لك بالوصول` |
| 404 | No level with this id. | `{"success":false,"message":"المستوى غير موجود","data":null}` ("Level not found") |

- **Frontend notes:** Mock: `docs/mocks/mock_level_detail.json`.

---

### Create level

`POST /api/content/levels`: called by the Admin dashboard.

- **Auth:** roles: `Admin`. The class has `[Authorize]` and the action has `[Authorize(Roles = "Admin")]`.
- **Headers / language:** `Authorization: Bearer <accessToken>`, `Content-Type: application/json`. Not localized.
- **Path / query parameters:** none.
- **Request body:** `application/json`, type `CreateLevelRequest`.

| Field | Type | Required | Rules |
|---|---|---|---|
| `title` | string | yes | Must not be blank. DB column `nvarchar(200)`, so a longer value causes a 500. |
| `description` | string \| null | no | DB column `nvarchar(1000)`. |

Do not send `order`. The server sets it to the current highest `order` + 1.

```json
{ "title": "المستشعرات", "description": "حساسات الحرارة والضوء" }
```

- **Success:** `200`

```json
{
  "success": true,
  "message": "تم إنشاء المستوى بنجاح",
  "data": { "id": 3, "title": "المستشعرات", "description": "حساسات الحرارة والضوء", "order": 3 }
}
```

- **Errors:**

| Status | When | Example |
|---|---|---|
| 400 (framework) | Body missing or `title` blank. | ValidationProblemDetails |
| 401 | Token missing, invalid or expired. | `غير مصرح لك بالوصول` |
| 403 | The caller is not an Admin. | `ليس لديك صلاحية` |
| 500 | Database failure, for example over-length `title` or `description`. | `حدث خطأ داخلي في الخادم` |

`LevelService.CreateAsync` has no business-rule failure path.

- **Frontend notes:**
  - Not idempotent: a double submit creates two levels. Disable the button while the request is in flight.
  - A new level is immediately visible to every signed-in user; levels have no publish flag.
  - It also becomes part of the placement ordering.

---

### Update level

`PUT /api/content/levels/{id}`: called by the Admin dashboard.

- **Auth:** roles: `Admin`.
- **Headers / language:** `Authorization: Bearer <accessToken>`, `Content-Type: application/json`. Not localized.
- **Path / query parameters:**

| Name | Type | Required | Rules |
|---|---|---|---|
| `id` | int (route) | yes | Must be an existing level. |

- **Request body:** `application/json`, type `UpdateLevelRequest`. This is a full replacement of `title` and `description`; `order` is not changeable here.

| Field | Type | Required | Rules |
|---|---|---|---|
| `title` | string | yes | Must not be blank. At most 200 characters (DB limit). |
| `description` | string \| null | no | At most 1000 characters (DB limit). Sending `null` or omitting it **clears** the description. |

```json
{ "title": "مقدمة في الكهرباء - محدّث", "description": "وصف جديد" }
```

- **Success:** `200`

```json
{
  "success": true,
  "message": "تم تعديل المستوى بنجاح",
  "data": { "id": 1, "title": "مقدمة في الكهرباء - محدّث", "description": "وصف جديد", "order": 1 }
}
```

- **Errors:**

| Status | When | Example |
|---|---|---|
| 400 (framework) | Body missing or `title` blank. | ValidationProblemDetails |
| 400 | No level with this id. This returns **400**, not 404. | `المستوى غير موجود` |
| 401 / 403 | No valid token / not an Admin. | `غير مصرح لك بالوصول` / `ليس لديك صلاحية` |
| 500 | Database failure (over-length strings). | `حدث خطأ داخلي في الخادم` |

- **Frontend notes:** Idempotent. Send the existing description back if it was not edited.

---

### Delete level

`DELETE /api/content/levels/{id}`: called by the Admin dashboard.

- **Auth:** roles: `Admin`.
- **Headers / language:** `Authorization: Bearer <accessToken>`. Not localized.
- **Path / query parameters:**

| Name | Type | Required | Rules |
|---|---|---|---|
| `id` | int (route) | yes | Must be an existing level with **no lessons**. |

- **Request body:** none.
- **Success:** `200`

```json
{ "success": true, "message": "تم مسح المستوى بنجاح" }
```

- **Errors:** error bodies have no `data` key.

| Status | When | Example |
|---|---|---|
| 400 | No level with this id. | `{"success":false,"message":"المستوى غير موجود"}` |
| 400 | Any database error while saving the delete (`DbUpdateException`), in practice lessons still attached through the `FK_Lessons_Levels` constraint. | `{"success":false,"message":"مينفعش تمسحي المستوى ده لأن فيه دروس مرتبطة بيه، امسحي الدروس الأول"}` ("Can't delete this level because it has lessons; delete the lessons first") |
| 401 / 403 | No valid token / not an Admin. | `غير مصرح لك بالوصول` / `ليس لديك صلاحية` |

- **Frontend notes:**
  - Show a confirmation dialog first.
  - A second delete of the same id returns 400 `المستوى غير موجود`; treat that as already deleted.
  - Assessment data (`Quizzes.LevelId`, `UserPlacements.PlacedLevelId`) points at levels with **no foreign key**, and this endpoint does not check it. Deleting a level used by a quiz or a placement result succeeds and leaves those records pointing at a missing level. Warn the admin in the UI.

---

### Swap level order

`POST /api/content/levels/swap-order`: called by the Admin dashboard (drag-and-drop reorder, one swap at a time).

- **Auth:** roles: `Admin`.
- **Headers / language:** `Authorization: Bearer <accessToken>`, `Content-Type: application/json`. Not localized.
- **Path / query parameters:** none.
- **Request body:** `application/json`, type `SwapLevelsOrderRequest`.

| Field | Type | Required | Rules |
|---|---|---|---|
| `firstLevelId` | int | yes | Must exist. A missing field binds as `0`, which does not exist. |
| `secondLevelId` | int | yes | Must exist, and must differ from `firstLevelId`. |

```json
{ "firstLevelId": 1, "secondLevelId": 3 }
```

- **Success:** `200`. The two levels exchange their `order` values.

```json
{ "success": true, "message": "تم تبديل الترتيب بنجاح" }
```

- **Errors:** error bodies have no `data` key.

| Status | When | Example |
|---|---|---|
| 400 (framework) | Body missing or an id is not an integer. | ValidationProblemDetails |
| 400 | Both ids are the same. | `مينفعش تبدلي ترتيب المستوى بنفسه` ("Can't swap a level with itself") |
| 400 | Either level does not exist. | `واحد من المستويين غير موجود` ("One of the two levels doesn't exist") |
| 401 / 403 | No valid token / not an Admin. | `غير مصرح لك بالوصول` / `ليس لديك صلاحية` |

- **Frontend notes:**
  - **Not idempotent.** Repeating the same swap swaps the two back, so never retry blindly.
  - After the call, re-fetch `GET /api/content/levels` instead of assuming the result.
  - Moving an item several positions takes several adjacent swaps.
  - Changing the order also changes placement ordering.

---

### List lessons of a level

`GET /api/content/levels/{levelId}/lessons`: called by the Child app (the lesson list inside a level) and the Admin dashboard.

- **Auth:** Bearer token (any role).
- **Headers / language:** `Authorization: Bearer <accessToken>`. Not localized.
- **Path / query parameters:**

| Name | Type | Required | Rules |
|---|---|---|---|
| `levelId` | int (route) | yes | An unknown level returns `200` with an empty list, not 404. |

- **Request body:** none.
- **Success:** `200`. Sorted by `sortOrder`. The list includes **unpublished** lessons.

```json
{
  "success": true,
  "message": "تمت العملية بنجاح",
  "data": [
    {
      "id": 5,
      "levelId": 1,
      "title": "مقدمة عن الدائرة الكهربية",
      "description": null,
      "sortOrder": 1,
      "isPublished": true,
      "createdAt": "2026-08-05T09:00:00"
    },
    {
      "id": 6,
      "levelId": 1,
      "title": "القياسات الكهربية",
      "description": "الفولت والأمبير",
      "sortOrder": 2,
      "isPublished": false,
      "createdAt": "2026-09-10T10:00:00"
    }
  ]
}
```

| Field | Meaning |
|---|---|
| `sortOrder` | Position inside the level. Set by the server and changed only by lesson swap-order or by moving the lesson to another level. Values can have gaps. |
| `isPublished` | `false` means a draft. The endpoint does **not** filter drafts out for the Child app. |

- **Errors:**

| Status | When | Example |
|---|---|---|
| 401 | Token missing, invalid or expired. | `غير مصرح لك بالوصول` |

- **Frontend notes:**
  - **Child app: hide lessons with `isPublished: false`.** The server returns them to every role.
  - Assessment treats an unpublished lesson's quiz as unavailable (`ILessonAvailability`), so listing a draft would lead to a dead end.
  - An empty list can mean either "no lessons" or "no such level".
  - Mock: `docs/mocks/mock_lessons_by_level.json`.

---

### Get lesson detail

`GET /api/content/lessons/{id}`: called by the Child app (the lesson reader) and the Admin dashboard (the lesson editor).

- **Auth:** Bearer token (any role).
- **Headers / language:** `Authorization: Bearer <accessToken>`. Not localized.
- **Path / query parameters:**

| Name | Type | Required | Rules |
|---|---|---|---|
| `id` | int (route) | yes | Lesson id. |

- **Request body:** none.
- **Success:** `200`. `contents` is sorted by `sortOrder`.

```json
{
  "success": true,
  "message": "تمت العملية بنجاح",
  "data": {
    "id": 5,
    "levelId": 1,
    "title": "مقدمة عن الدائرة الكهربية",
    "description": null,
    "sortOrder": 1,
    "isPublished": true,
    "createdAt": "2026-08-05T09:00:00",
    "contents": [
      {
        "id": 10,
        "lessonId": 5,
        "contentTypeId": 1,
        "contentTypeName": "Text",
        "content": "الدائرة الكهربية بتتكون من مصدر للجهد وموصلات وحمل.",
        "mediaUrl": null,
        "sortOrder": 1
      },
      {
        "id": 11,
        "lessonId": 5,
        "contentTypeId": 2,
        "contentTypeName": "Image",
        "content": null,
        "mediaUrl": "/uploads/lessons/8f1c2b3a-6b4d-4e2a-9c1f-3d7e5a2b1c0d.png",
        "sortOrder": 2
      },
      {
        "id": 12,
        "lessonId": 5,
        "contentTypeId": 3,
        "contentTypeName": "TextAndImage",
        "content": "المقاومة بتقلل شدة التيار المار في الدائرة.",
        "mediaUrl": "/uploads/lessons/2c4e6a8b-1d3f-5a7c-9e1b-4f6a8c0e2d4f.png",
        "sortOrder": 3
      }
    ]
  }
}
```

| Field | Meaning |
|---|---|
| `contents[].contentTypeName` | The name from the content-types table, for choosing a renderer. |
| `contents[].content` | Text body, or `null`. Stored as `nvarchar(max)`, returned as-is (no markup processing). |
| `contents[].mediaUrl` | Server-relative path such as `/uploads/lessons/<guid>.png`, or whatever string the admin saved. Prefix it with the API base URL. Files under `/uploads` are served by static-file middleware **without authentication**. |
| `contents[].sortOrder` | Position inside the lesson. |

- **Errors:**

| Status | When | Example |
|---|---|---|
| 401 | Token missing, invalid or expired. | `غير مصرح لك بالوصول` |
| 404 | No lesson with this id. | `{"success":false,"message":"الدرس غير موجود","data":null}` ("Lesson not found") |

- **Frontend notes:**
  - This endpoint returns unpublished lessons too. The Child app should not open a lesson whose `isPublished` is `false`.
  - The server does not check that `contentTypeName` matches which of `content` and `mediaUrl` are filled, so render defensively: show text if `content` is non-empty and an image if `mediaUrl` is non-empty.
  - Image URLs are random GUID names that never change, so they are safe to cache for a long time.
  - Mocks: `docs/mocks/mock_lesson_detail.json`, `docs/mocks/mock_lesson_not_found.json`.

---

### Create lesson

`POST /api/content/lessons`: called by the Admin dashboard.

- **Auth:** roles: `Admin`.
- **Headers / language:** `Authorization: Bearer <accessToken>`, `Content-Type: application/json`. Not localized.
- **Path / query parameters:** none.
- **Request body:** `application/json`, type `CreateLessonRequest`.

| Field | Type | Required | Rules |
|---|---|---|---|
| `levelId` | int | yes | Must be an existing level. A missing field binds as `0`, which does not exist. |
| `title` | string | yes | Must not be blank. At most 200 characters (DB limit). |
| `description` | string \| null | no | At most 1000 characters (DB limit). |

Do not send `sortOrder` or `isPublished`. The server sets `sortOrder` to the highest in that level + 1, and `isPublished` to `false`.

```json
{ "levelId": 1, "title": "القياسات الكهربية", "description": "الفولت والأمبير" }
```

- **Success:** `200`. `createdAt` has a `Z` suffix here because the server just set it.

```json
{
  "success": true,
  "message": "تم إنشاء الدرس بنجاح",
  "data": {
    "id": 6,
    "levelId": 1,
    "title": "القياسات الكهربية",
    "description": "الفولت والأمبير",
    "sortOrder": 2,
    "isPublished": false,
    "createdAt": "2026-09-13T10:00:12.3456789Z"
  }
}
```

- **Errors:**

| Status | When | Example |
|---|---|---|
| 400 (framework) | Body missing or `title` blank. | ValidationProblemDetails |
| 400 | The level does not exist. | `المستوى المحدد غير موجود` ("The selected level doesn't exist") |
| 401 / 403 | No valid token / not an Admin. | `غير مصرح لك بالوصول` / `ليس لديك صلاحية` |
| 500 | Database failure (over-length strings). | `حدث خطأ داخلي في الخادم` |

- **Frontend notes:**
  - Not idempotent; guard against double submit.
  - The lesson starts as a draft. Add content, then publish with `PATCH /api/content/lessons/{id}/publish`.

---

### Update lesson

`PUT /api/content/lessons/{id}`: called by the Admin dashboard.

- **Auth:** roles: `Admin`.
- **Headers / language:** `Authorization: Bearer <accessToken>`, `Content-Type: application/json`. Not localized.
- **Path / query parameters:**

| Name | Type | Required | Rules |
|---|---|---|---|
| `id` | int (route) | yes | Must be an existing lesson. |

- **Request body:** `application/json`, type `UpdateLessonRequest`. This is a full replacement of `levelId`, `title` and `description`.

| Field | Type | Required | Rules |
|---|---|---|---|
| `levelId` | int | yes | Must be an existing level. **Always send the current level** unless you are moving the lesson (a missing value binds as `0`, which fails). If it differs from the current level, the lesson moves and gets `sortOrder` = the new level's highest + 1. |
| `title` | string | yes | Must not be blank. At most 200 characters (DB limit). |
| `description` | string \| null | no | `null` or omitted **clears** it. At most 1000 characters. |

`isPublished` is not changed by this endpoint.

```json
{ "levelId": 1, "title": "القياسات الكهربية - محدّث", "description": "وصف جديد" }
```

- **Success:** `200`. `createdAt` comes from the database, so it has no `Z` suffix.

```json
{
  "success": true,
  "message": "تم تعديل الدرس بنجاح",
  "data": {
    "id": 6,
    "levelId": 1,
    "title": "القياسات الكهربية - محدّث",
    "description": "وصف جديد",
    "sortOrder": 2,
    "isPublished": false,
    "createdAt": "2026-09-13T10:00:12.3456789"
  }
}
```

- **Errors:** checked in this order.

| Status | When | Example |
|---|---|---|
| 400 (framework) | Body missing or `title` blank. | ValidationProblemDetails |
| 400 | No lesson with this id. | `الدرس غير موجود` |
| 400 | The target level does not exist. | `المستوى المحدد غير موجود` |
| 401 / 403 | No valid token / not an Admin. | `غير مصرح لك بالوصول` / `ليس لديك صلاحية` |
| 500 | Database failure (over-length strings). | `حدث خطأ داخلي في الخادم` |

- **Frontend notes:** Idempotent when the level is unchanged. Moving a lesson back to its original level does **not** restore its old position.

---

### Publish / unpublish lesson

`PATCH /api/content/lessons/{id}/publish`: called by the Admin dashboard.

- **Auth:** roles: `Admin`.
- **Headers / language:** `Authorization: Bearer <accessToken>`, `Content-Type: application/json`. Not localized.
- **Path / query parameters:**

| Name | Type | Required | Rules |
|---|---|---|---|
| `id` | int (route) | yes | Must be an existing lesson. |

- **Request body:** `application/json`, type `SetPublishedRequest`.

| Field | Type | Required | Rules |
|---|---|---|---|
| `isPublished` | bool | yes | `true` publishes, `false` hides. **A missing field binds as `false` and hides the lesson.** `null` is a framework 400. |

```json
{ "isPublished": true }
```

- **Success:** `200`. The message depends on the value: `تم نشر الدرس` ("Lesson published") for `true`, `تم إخفاء الدرس` ("Lesson hidden") for `false`.

```json
{
  "success": true,
  "message": "تم نشر الدرس",
  "data": {
    "id": 6,
    "levelId": 1,
    "title": "القياسات الكهربية",
    "description": "الفولت والأمبير",
    "sortOrder": 2,
    "isPublished": true,
    "createdAt": "2026-09-13T10:00:12.3456789"
  }
}
```

- **Errors:**

| Status | When | Example |
|---|---|---|
| 400 (framework) | Body missing, or `isPublished` not a boolean. | ValidationProblemDetails |
| 400 | No lesson with this id. | `الدرس غير موجود` |
| 401 / 403 | No valid token / not an Admin. | `غير مصرح لك بالوصول` / `ليس لديك صلاحية` |

- **Frontend notes:**
  - Idempotent; safe to retry.
  - No server-side check that the lesson has content or a quiz before publishing.
  - Publishing is what makes the lesson's quiz available in Assessment (`GET /api/quizzes/for-lesson` and attempt start go through `ILessonAvailability`).

---

### Delete lesson

`DELETE /api/content/lessons/{id}`: called by the Admin dashboard.

- **Auth:** roles: `Admin`.
- **Headers / language:** `Authorization: Bearer <accessToken>`. Not localized.
- **Path / query parameters:**

| Name | Type | Required | Rules |
|---|---|---|---|
| `id` | int (route) | yes | Must be an existing lesson. |

- **Request body:** none.
- **Success:** `200`. The lesson and **all its content items** are deleted. Every content image under `/uploads/lessons/` is **removed from disk**.

```json
{ "success": true, "message": "تم مسح الدرس بنجاح" }
```

- **Errors:**

| Status | When | Example |
|---|---|---|
| 400 | No lesson with this id. | `{"success":false,"message":"الدرس غير موجود"}` |
| 401 / 403 | No valid token / not an Admin. | `غير مصرح لك بالوصول` / `ليس لديك صلاحية` |
| 500 | Database failure while saving. The image files may already be gone at that point, because they are deleted before the save. | `حدث خطأ داخلي في الخادم` |

- **Frontend notes:**
  - Irreversible; confirm first.
  - A repeat call returns 400 `الدرس غير موجود`; treat that as already deleted.
  - A quiz linked through `Quizzes.LessonId` is **not** checked or removed (no foreign key), so warn the admin to handle the lesson's quiz.
  - Image files are deleted even if the same URL is also used elsewhere, for example a question image uploaded once and reused.

---

### Swap lesson order

`POST /api/content/lessons/swap-order`: called by the Admin dashboard.

- **Auth:** roles: `Admin`.
- **Headers / language:** `Authorization: Bearer <accessToken>`, `Content-Type: application/json`. Not localized.
- **Path / query parameters:** none.
- **Request body:** `application/json`, type `SwapLessonsOrderRequest`.

| Field | Type | Required | Rules |
|---|---|---|---|
| `firstLessonId` | int | yes | Must exist. |
| `secondLessonId` | int | yes | Must exist, differ from `firstLessonId`, and be **in the same level**. |

```json
{ "firstLessonId": 5, "secondLessonId": 6 }
```

- **Success:** `200`. The two lessons exchange their `sortOrder` values.

```json
{ "success": true, "message": "تم تبديل الترتيب بنجاح" }
```

- **Errors:** error bodies have no `data` key.

| Status | When | Example |
|---|---|---|
| 400 (framework) | Body missing or an id is not an integer. | ValidationProblemDetails |
| 400 | Both ids are the same. | `مينفعش تبدلي ترتيب الدرس بنفسه` ("Can't swap a lesson with itself") |
| 400 | Either lesson does not exist. | `واحد من الدرسين غير موجود` ("One of the two lessons doesn't exist") |
| 400 | The lessons are in different levels. | `مينفعش تبدلي ترتيب درسين من مستويين مختلفين` ("Can't swap lessons from two different levels") |
| 401 / 403 | No valid token / not an Admin. | `غير مصرح لك بالوصول` / `ليس لديك صلاحية` |

- **Frontend notes:** **Not idempotent.** A repeat swaps the lessons back. Re-fetch the level's lessons after each call.

---

### Add lesson content item

`POST /api/content/lessons/{lessonId}/contents`: called by the Admin dashboard.

- **Auth:** roles: `Admin`.
- **Headers / language:** `Authorization: Bearer <accessToken>`, `Content-Type: application/json`. Not localized.
- **Path / query parameters:**

| Name | Type | Required | Rules |
|---|---|---|---|
| `lessonId` | int (route) | yes | Must be an existing lesson. |

- **Request body:** `application/json`, type `CreateLessonContentRequest`.

| Field | Type | Required | Rules |
|---|---|---|---|
| `contentTypeId` | int | yes | Must exist in content types. A missing field binds as `0`, which does not exist. |
| `content` | string \| null | conditional | At least one of `content` and `mediaUrl` must be non-blank. Stored as `nvarchar(max)`. |
| `mediaUrl` | string \| null | conditional | Normally the `url` returned by `POST /api/content/media/images`. **Not validated**: any string is accepted. At most 500 characters (DB limit). |

Do not send `sortOrder`; the server sets it to the highest in that lesson + 1. The server does **not** check that the content type matches the fields, so the dashboard should enforce that Text has `content`, Image has `mediaUrl`, and TextAndImage has both.

```json
{ "contentTypeId": 3, "content": "المقاومة بتقلل شدة التيار المار في الدائرة.", "mediaUrl": "/uploads/lessons/2c4e6a8b-1d3f-5a7c-9e1b-4f6a8c0e2d4f.png" }
```

- **Success:** `200`

```json
{
  "success": true,
  "message": "تم إضافة المحتوى بنجاح",
  "data": {
    "id": 12,
    "lessonId": 5,
    "contentTypeId": 3,
    "contentTypeName": "TextAndImage",
    "content": "المقاومة بتقلل شدة التيار المار في الدائرة.",
    "mediaUrl": "/uploads/lessons/2c4e6a8b-1d3f-5a7c-9e1b-4f6a8c0e2d4f.png",
    "sortOrder": 3
  }
}
```

- **Errors:** checked in this order.

| Status | When | Example |
|---|---|---|
| 400 (framework) | Body missing, or a field has the wrong JSON type. | ValidationProblemDetails |
| 400 | No lesson with this id. | `الدرس غير موجود` |
| 400 | Unknown `contentTypeId`. | `نوع المحتوى غير موجود` ("Content type doesn't exist") |
| 400 | Both `content` and `mediaUrl` are blank. | `لازم يكون في نص أو صورة على الأقل` ("There must be at least text or an image") |
| 401 / 403 | No valid token / not an Admin. | `غير مصرح لك بالوصول` / `ليس لديك صلاحية` |
| 500 | Database failure, for example `mediaUrl` longer than 500 characters. | `حدث خطأ داخلي في الخادم` |

- **Frontend notes:**
  - Not idempotent: a double submit adds two items.
  - Typical flow: upload the image, then create the item with the returned `url`.
  - If creation fails after an upload, the uploaded file is left orphaned on disk; the server never cleans those up.

---

### Update lesson content item

`PUT /api/content/contents/{contentId}`: called by the Admin dashboard.

- **Auth:** roles: `Admin`.
- **Headers / language:** `Authorization: Bearer <accessToken>`, `Content-Type: application/json`. Not localized.
- **Path / query parameters:**

| Name | Type | Required | Rules |
|---|---|---|---|
| `contentId` | int (route) | yes | Must be an existing content item. No `lessonId` is needed. |

- **Request body:** `application/json`, type `UpdateLessonContentRequest`. This is a full replacement of the three fields; `sortOrder` and `lessonId` do not change.

| Field | Type | Required | Rules |
|---|---|---|---|
| `contentTypeId` | int | yes | Must exist. |
| `content` | string \| null | conditional | At least one of `content` and `mediaUrl` must be non-blank. `null` clears it. |
| `mediaUrl` | string \| null | conditional | At most 500 characters (DB limit). **If this differs from the stored value, the old file under `/uploads/lessons/` is deleted from disk**, even if the database save then fails. |

```json
{ "contentTypeId": 2, "content": null, "mediaUrl": "/uploads/lessons/9a7b6c5d-4e3f-4a2b-8c1d-0e9f8a7b6c5d.png" }
```

- **Success:** `200`

```json
{
  "success": true,
  "message": "تم تعديل المحتوى بنجاح",
  "data": {
    "id": 12,
    "lessonId": 5,
    "contentTypeId": 2,
    "contentTypeName": "Image",
    "content": null,
    "mediaUrl": "/uploads/lessons/9a7b6c5d-4e3f-4a2b-8c1d-0e9f8a7b6c5d.png",
    "sortOrder": 3
  }
}
```

- **Errors:** checked in this order.

| Status | When | Example |
|---|---|---|
| 400 (framework) | Body missing or wrong JSON types. | ValidationProblemDetails |
| 400 | No content item with this id. | `عنصر المحتوى غير موجود` ("Content item not found") |
| 400 | Unknown `contentTypeId`. | `نوع المحتوى غير موجود` |
| 400 | Both `content` and `mediaUrl` are blank. | `لازم يكون في نص أو صورة على الأقل` |
| 401 / 403 | No valid token / not an Admin. | `غير مصرح لك بالوصول` / `ليس لديك صلاحية` |
| 500 | Database failure (over-length `mediaUrl`). | `حدث خطأ داخلي في الخادم` |

- **Frontend notes:**
  - Always send the current `mediaUrl` back unchanged when only the text is edited. Sending `null` or a different string permanently deletes the old image file.
  - A retry with the same body is safe: the URL no longer differs, so nothing more is deleted.
  - Never point two items (or a question) at the same uploaded URL. Upload the file again instead.

---

### Delete lesson content item

`DELETE /api/content/contents/{contentId}`: called by the Admin dashboard.

- **Auth:** roles: `Admin`.
- **Headers / language:** `Authorization: Bearer <accessToken>`. Not localized.
- **Path / query parameters:**

| Name | Type | Required | Rules |
|---|---|---|---|
| `contentId` | int (route) | yes | Must be an existing content item. |

- **Request body:** none.
- **Success:** `200`. The item is removed and its image (if under `/uploads/lessons/`) is deleted from disk. The remaining items keep their `sortOrder` values, so a gap remains.

```json
{ "success": true, "message": "تم مسح المحتوى بنجاح" }
```

- **Errors:**

| Status | When | Example |
|---|---|---|
| 400 | No content item with this id. | `{"success":false,"message":"عنصر المحتوى غير موجود"}` |
| 401 / 403 | No valid token / not an Admin. | `غير مصرح لك بالوصول` / `ليس لديك صلاحية` |

- **Frontend notes:** Irreversible. A repeat call returns 400 `عنصر المحتوى غير موجود`; treat that as already deleted.

---

### Swap lesson content order

`POST /api/content/contents/swap-order`: called by the Admin dashboard.

- **Auth:** roles: `Admin`.
- **Headers / language:** `Authorization: Bearer <accessToken>`, `Content-Type: application/json`. Not localized.
- **Path / query parameters:** none.
- **Request body:** `application/json`, type `SwapLessonContentsOrderRequest`.

| Field | Type | Required | Rules |
|---|---|---|---|
| `firstContentId` | int | yes | Must exist. |
| `secondContentId` | int | yes | Must exist, differ from `firstContentId`, and be **in the same lesson**. |

```json
{ "firstContentId": 11, "secondContentId": 12 }
```

- **Success:** `200`. The two items exchange their `sortOrder` values.

```json
{ "success": true, "message": "تم تبديل الترتيب بنجاح" }
```

- **Errors:** error bodies have no `data` key.

| Status | When | Example |
|---|---|---|
| 400 (framework) | Body missing or an id is not an integer. | ValidationProblemDetails |
| 400 | Both ids are the same. | `مينفعش تبدلي ترتيب العنصر بنفسه` ("Can't swap an item with itself") |
| 400 | Either item does not exist. | `واحد من العنصرين غير موجود` ("One of the two items doesn't exist") |
| 400 | The items are in different lessons. | `مينفعش تبدلي ترتيب عنصرين من درسين مختلفين` ("Can't swap items from two different lessons") |
| 401 / 403 | No valid token / not an Admin. | `غير مصرح لك بالوصول` / `ليس لديك صلاحية` |

- **Frontend notes:** **Not idempotent.** A repeat swaps the items back. Re-fetch `GET /api/content/lessons/{id}` after each call.

---

### Upload image

`POST /api/content/media/images`: called by the Admin dashboard, for lesson content images and for Assessment question and option images (`CreateQuestionDto`, `CreateQuestionOptionDto`).

- **Auth:** roles: `Admin`. The class has `[Authorize(Roles = "Admin")]`.
- **Headers / language:** `Authorization: Bearer <accessToken>`, `Content-Type: multipart/form-data`. Not localized.
- **Path / query parameters:** none.
- **Request body:** `multipart/form-data`.

| Field | Type | Required | Rules |
|---|---|---|---|
| `file` | file | yes | Not empty. At most **5 MB** (checked by the service); the whole request is capped at 6 MB by `[RequestSizeLimit]`. The file-name extension must be `.jpg`, `.jpeg`, `.png` or `.webp` (any case). **Only the extension is checked, not the file content.** |

```
POST /api/content/media/images
Authorization: Bearer eyJhbGciOi...
Content-Type: multipart/form-data; boundary=----b

------b
Content-Disposition: form-data; name="file"; filename="circuit.png"
Content-Type: image/png

<binary>
------b--
```

- **Success:** `200`

```json
{
  "success": true,
  "message": "تم رفع الصورة بنجاح",
  "data": { "url": "/uploads/lessons/8f1c2b3a-6b4d-4e2a-9c1f-3d7e5a2b1c0d.png" }
}
```

| Field | Meaning |
|---|---|
| `url` | Server-relative path `/uploads/lessons/<random-guid><ext>`. Store it exactly as returned (as `mediaUrl`, or a question or option image URL). Prefix it with the API base URL to display it. The file is publicly readable without a token. |

- **Errors:**

| Status | When | Example |
|---|---|---|
| 400 (framework) | No `file` part, or the request is not multipart. `file` is a non-nullable `IFormFile`, so it is implicitly required. | ValidationProblemDetails |
| 400 | The file is 0 bytes. | `الملف فاضي` ("The file is empty") |
| 400 | The file is over 5 MB (and the request is still within 6 MB). | `حجم الصورة أكبر من 5 ميجا` ("Image is larger than 5 MB") |
| 400 | The extension is not allowed. | `امتداد الصورة غير مسموح - المسموح بس jpg, jpeg, png, webp` ("Image extension not allowed; only jpg, jpeg, png, webp") |
| 401 / 403 | No valid token / not an Admin. | `غير مصرح لك بالوصول` / `ليس لديك صلاحية` |
| non-200 without envelope | The whole request is over 6 MB: the server stops reading the body before the action runs. The exact status is not set by this code. | (no envelope) |
| 500 | Server misconfiguration (`wwwroot` missing) or a disk write failure. The internal message is hidden. | `حدث خطأ داخلي في الخادم` |

- **Frontend notes:**
  - Check the size (5 MB or less) and the extension on the client before uploading.
  - Not idempotent: each upload creates a new file with a new URL, and unused uploads are never deleted by the server. Upload only when the admin confirms the image.
  - For Assessment questions and options, the image also needs an admin-written image description; see [Assessment authoring concepts](#assessment-authoring-concepts).
  - Mock: `docs/mocks/mock_image_upload.json`.

---

## Assessment (learner)

Placement, lesson quiz preview, attempts, hints and topic statistics: `PlacementController`, `LessonQuizController`, `QuizAttemptController` and `UserTopicStatController` (`ElectroWorld/Controllers/AssessmentModule`), backed by `AssessmentBL.Services`.

- Success bodies are **bare DTOs**. Errors use the envelope, or ProblemDetails for framework errors (see [Response shapes](#response-shapes)).
- Most endpoints here are localized; see [Content language](#content-language).
- Timestamps and decimals can arrive in two formats; see [Dates, ids and numbers](#dates-ids-and-numbers).
- `imageDescription` is admin-only and never appears in these responses.

---

### Placement status

`GET /api/placement`: called by the child app after register or login, to decide whether to show the placement test.

- **Auth:** roles: `Child` (class-level `[Authorize(Roles = "Child")]` on `PlacementController`). A Parent or Admin token gets 403.
- **Headers / language:** `Authorization: Bearer <accessToken>`. Not localized: there is no `language` parameter, and level titles come from the Content module as stored.
- **Path / query parameters:** none.
- **Request body:** none.
- **Success:** `200 OK`. The body is a bare `PlacementStatusDto`.

New learner:
```json
{
  "status": "Required",
  "placementQuizId": 7,
  "attemptId": null,
  "result": null
}
```
Test already open:
```json
{
  "status": "InProgress",
  "placementQuizId": 7,
  "attemptId": 58,
  "result": null
}
```
Already placed:
```json
{
  "status": "Completed",
  "placementQuizId": null,
  "attemptId": 58,
  "result": {
    "levelId": 2,
    "levelTitle": "المستوى الثاني: الدوائر الكهربية",
    "levelOrder": 2,
    "scorePercentage": 58.33,
    "passPercentage": 75,
    "placedAt": "2026-09-11T16:05:42.317",
    "levels": [
      { "levelId": 1, "levelTitle": "المستوى الأول: أساسيات الكهرباء", "questionsAsked": 4, "correctAnswers": 4, "totalPoints": 4, "earnedPoints": 4, "scorePercentage": 100, "mastered": true },
      { "levelId": 2, "levelTitle": "المستوى الثاني: الدوائر الكهربية", "questionsAsked": 4, "correctAnswers": 2, "totalPoints": 4, "earnedPoints": 2, "scorePercentage": 50, "mastered": false },
      { "levelId": 3, "levelTitle": "المستوى الثالث: القياسات", "questionsAsked": 4, "correctAnswers": 1, "totalPoints": 4, "earnedPoints": 1, "scorePercentage": 25, "mastered": false }
    ]
  }
}
```

| Field | Meaning |
|---|---|
| `status` | Decided in this order by `PlacementService.GetStatusAsync`: **`Completed`**: a placement is stored for the user. **`Unavailable`**: no active Placement quiz. **`InProgress`**: an open placement attempt that has not expired exists. **`Unavailable`**: no level has an active LevelAssessment quiz with active, answerable MCQ/TrueFalse questions. **`Optional`**: the user already has at least one Completed attempt of any quiz (joined before placement existed). **`Required`**: everyone else. |
| `placementQuizId` | Set for `Required`, `Optional` and `InProgress`. `null` for `Completed` and `Unavailable`. |
| `attemptId` | The open attempt (`InProgress`) or the attempt that placed the learner (`Completed`). Otherwise `null`. |
| `result` | Only for `Completed`. |
| `result.levelId` / `levelTitle` / `levelOrder` | The level to start the learner at. `levelTitle` and `levelOrder` are `null` if that level no longer exists in the Content module. |
| `result.scorePercentage` | Score of the whole placement attempt: points of correct answers ÷ points of all questions × 100, 2dp. The points are frozen at attempt start. Placement has no essays. |
| `result.passPercentage` | Per-level mastery threshold stored with the placement (config `Assessment:PlacementPassPercentage`, default 75). |
| `result.levels[]` | Only levels that had placement questions, in learning order. The per-level numbers are recomputed from the saved wrong answers and frozen points. |
| `levels[].questionsAsked` / `correctAnswers` | Question **counts**, whatever each question's points. |
| `levels[].totalPoints` / `earnedPoints` | Sum of the frozen points of the level's questions, and of those answered correctly. |
| `levels[].scorePercentage` / `mastered` | `earnedPoints ÷ totalPoints × 100` (2dp, midpoint away from zero). `mastered` is true when that is `>= passPercentage` (inclusive). |

- **Errors:**

| Status | When | Example body |
|---|---|---|
| 401 | Missing, invalid or expired token | `{"success":false,"message":"غير مصرح لك بالوصول","data":null}` |
| 403 | Token role is not `Child` | `{"success":false,"message":"ليس لديك صلاحية","data":null}` |
| 500 | Unexpected server fault | `{"success":false,"message":"حدث خطأ داخلي في الخادم","data":null}` |

- **Frontend notes:**
  - Read-only with no side effects, so it is safe to call on every app launch. Suggested routing:
    - `Required` → show the test.
    - `Optional` → offer it.
    - `InProgress` → call `POST /api/placement/start`, which resumes the same `attemptId`.
    - `Completed` → show `result` or go to `result.levelId`.
    - `Unavailable` → skip placement.
  - Placement rule (`PlacementEngine.Decide`): the learner is placed at the **first level, in learning order, that is not mastered**. Mastering every level places them at the last level. A level with no placement questions cannot be mastered, so placement stops there. That level is then **not listed** in `levels` (only levels with `questionsAsked > 0` are listed), so `result.levelId` may not match any entry of `levels`.
  - An open attempt stops counting as `InProgress` once it is older than `Assessment:InProgressAttemptTimeoutMinutes` (default 180, minimum 30). The status then falls back to `Required` or `Optional`, and start creates a new attempt.
  - Mocks: `mock_placement_status_required.json` and `mock_placement_status_completed.json`.

---

### Start or resume placement test

`POST /api/placement/start`: called by the child app when status is `Required`, `Optional` or `InProgress`.

- **Auth:** roles: `Child` (class-level on `PlacementController`).
- **Headers / language:** `Authorization: Bearer <accessToken>`. Question and option text are localized: `?language=` wins, then `Accept-Language`, then `ar` (see [Content language](#content-language)).
- **Path / query parameters:**

| Name | Type | Required | Rules |
|---|---|---|---|
| `language` | string | no | `en` or `ar`. Regional tags are accepted (`en-US`). Anything else resolves to `ar`. |

- **Request body:** none.
- **Success:** `200 OK` (not 201). The body is a bare `QuizAttemptResponseDto`.
```json
{
  "attemptId": 58,
  "quizId": 7,
  "startedAt": "2026-09-11T16:01:03.4567891Z",
  "language": "ar",
  "languageFallbackApplied": false,
  "questions": [
    {
      "questionId": 201,
      "questionText": "ما هو التيار الكهربي؟",
      "questionType": "MultipleChoice",
      "imageUrl": null,
      "difficulty": "Easy",
      "displayOrder": 1,
      "points": 1,
      "currentHint": null,
      "options": [
        { "optionId": 2001, "optionText": "سريان الشحنات الكهربية", "imageUrl": null, "displayOrder": 1 },
        { "optionId": 2002, "optionText": "فرق الجهد", "imageUrl": null, "displayOrder": 2 }
      ]
    },
    {
      "questionId": 305,
      "questionText": "المقاومات على التوالي تُجمع قيمها.",
      "questionType": "TrueFalse",
      "imageUrl": null,
      "difficulty": "Medium",
      "displayOrder": 2,
      "points": 2,
      "currentHint": null,
      "options": [
        { "optionId": 3050, "optionText": "صح", "imageUrl": null, "displayOrder": 1 },
        { "optionId": 3051, "optionText": "خطأ", "imageUrl": null, "displayOrder": 2 }
      ]
    }
  ]
}
```

| Field | Meaning |
|---|---|
| `attemptId` | Submit it with `POST /api/quiz-attempts/{attemptId}/submit`. |
| `quizId` | The active Placement quiz. |
| `startedAt` | Has a `Z` for a new attempt. A **resumed** attempt returns the original start time read from the DB, with no offset. |
| `questions[]` | MultipleChoice and TrueFalse only (placement never serves essays). Up to `Assessment:PlacementQuestionsPerLevel` (default 4) per level, taken from each level's newest active LevelAssessment quiz in its display order. Easiest level first. |
| `questions[].displayOrder` | Renumbered `1..n` in serving order, because each level's quiz starts at 1. |
| `questions[].points` | Frozen for this attempt (1 unless the admin set another value). |
| `questions[].currentHint` | Always `null`. The Hint button is refused on placement. |
| `questions[].options` | Exactly 2 for TrueFalse, 2 or more for MultipleChoice. Never an answer key. |

- **Errors:**

| Status | When | Example message |
|---|---|---|
| 400 | No level has placement questions to sample (`QuizAttemptService.StartPlacementAttemptAsync`) | `"اختبار تحديد المستوى غير متاح حاليًا: لا توجد أسئلة تقييم مفعّلة للمستويات"` (placement test currently unavailable: no active assessment questions for the levels) |
| 400 | A selected question has no correct option (`LoadQuestionSnapshotsAsync`; should not happen because the engine filters these out) | `"لا يمكن بدء المحاولة: الأسئلة أرقام 201, 305 ليس لها إجابة صحيحة"` (cannot start: questions … have no correct answer) |
| 401 | Missing or invalid token | `"غير مصرح لك بالوصول"` |
| 403 | Role is not `Child` | `"ليس لديك صلاحية"` |
| 404 | No active Placement quiz (`PlacementService.StartAsync`) | `"لا يوجد اختبار تحديد مستوى متاح حاليًا"` (no placement test available right now) |
| 409 | Learner is already placed | `"تم تحديد مستواك بالفعل، لا يمكن إعادة اختبار تحديد المستوى"` (your level is already set; the placement test cannot be retaken) |
| 500 | Unexpected fault | `"حدث خطأ داخلي في الخادم"` |

- **Frontend notes:**
  - **Resumable, not strictly idempotent.** If an open placement attempt exists that has not expired, the same attempt is returned (same `attemptId`, same questions). Otherwise a new attempt is created. Nothing stops two simultaneous calls from creating two attempts: the newest is the one resumed later, and once either is submitted the other's submit gets 409. Disable the button while the request is in flight.
  - Submit with `POST /api/quiz-attempts/{attemptId}/submit`, answering **every** question in `mistakes`. The result carries `placement` (see Submit attempt).
  - Placement cannot be retried: `POST /api/quiz-attempts?quizId=…&previousAttemptId=…` returns 400. It has no hints: the Hint button returns 409.
  - To redraw the test after an app restart, `GET /api/quiz-attempts/{attemptId}` returns the same order and numbering.
  - After `InProgressAttemptTimeoutMinutes` (default 180) the attempt expires. Submit then returns 410, and calling start again creates a fresh attempt.
  - Mocks: no placement-specific start mock. `mock_start_attempt_mixed_ar.json` shows the current `QuizAttemptResponseDto` shape.

---

### Lesson quiz preview

`GET /api/quizzes/for-lesson/{lessonId}`: called by the child app on the lesson screen, to preview the lesson's quiz before starting it.

- **Auth:** Bearer token (any role). Class-level `[Authorize]` on `LessonQuizController` has no roles.
- **Headers / language:** `Authorization`. Localized: `?language=` → `Accept-Language` → `ar`. The quiz title and description use the same fallback chain.
- **Path / query parameters:**

| Name | Type | Required | Rules |
|---|---|---|---|
| `lessonId` | int (path) | yes | Route constraint `:int`. A non-integer value matches no route and returns a 404 with no envelope. |
| `language` | string (query) | no | `en` or `ar`. Anything else → `ar`. |

- **Request body:** none.
- **Success:** `200 OK`. The body is a bare `LessonQuizResponseDto`.
```json
{
  "quizId": 15,
  "lessonId": 5,
  "title": "اختبار الدائرة الكهربية",
  "description": null,
  "totalQuestions": 3,
  "language": "ar",
  "languageFallbackApplied": false,
  "questions": [
    {
      "questionId": 101,
      "questionText": "ما هو الجهد الكهربي؟",
      "questionType": "MultipleChoice",
      "imageUrl": null,
      "difficulty": "Easy",
      "displayOrder": 1,
      "points": 1,
      "currentHint": null,
      "options": [
        { "optionId": 1001, "optionText": "فرق الجهد بين نقطتين", "imageUrl": null, "displayOrder": 1 },
        { "optionId": 1002, "optionText": "مقاومة مرور التيار", "imageUrl": null, "displayOrder": 2 }
      ]
    },
    {
      "questionId": 103,
      "questionText": "المصباح يضيء بدون مصدر كهربي.",
      "questionType": "TrueFalse",
      "imageUrl": null,
      "difficulty": "Easy",
      "displayOrder": 2,
      "points": 1,
      "currentHint": null,
      "options": [
        { "optionId": 1010, "optionText": "صح", "imageUrl": null, "displayOrder": 1 },
        { "optionId": 1011, "optionText": "خطأ", "imageUrl": null, "displayOrder": 2 }
      ]
    },
    {
      "questionId": 104,
      "questionText": "اشرح بأسلوبك لماذا يضيء المصباح عند غلق الدائرة الكهربية.",
      "questionType": "Essay",
      "imageUrl": null,
      "difficulty": "Hard",
      "displayOrder": 3,
      "points": 3,
      "currentHint": null,
      "options": []
    }
  ]
}
```

| Field | Meaning |
|---|---|
| `quizId` | Pass to `POST /api/quiz-attempts?quizId={quizId}`. |
| `totalQuestions` | Count of **active** questions, which is what a first attempt would contain. |
| `languageFallbackApplied` | True if the quiz title/description or any question or option text is not in the requested language. |
| `questions[].questionType` | `MultipleChoice`, `TrueFalse` or `Essay`. It decides the widget and which submit list the answer goes into. |
| `questions[].points` | The question's **current** points. The attempt freezes the points at start, so they can differ if an admin changes them in between. |
| `questions[].currentHint` | Always `null` here. |
| `questions[].options` | Empty for `Essay`, exactly 2 for `TrueFalse`. Each option has `optionText`, `imageUrl` or both. Never `isCorrect`. |

- **Errors:**

| Status | When | Example message |
|---|---|---|
| 401 | Missing or invalid token | `"غير مصرح لك بالوصول"` |
| 404 | Lesson does not exist **or** is not published (`QuizService.GetForLessonAsync`; the two cases are indistinguishable) | `"الدرس رقم 5 غير موجود"` (lesson 5 does not exist) |
| 404 | The lesson has no active `LessonQuiz` with at least one active question | `"لا يوجد اختبار متاح للدرس رقم 5"` (no quiz available for lesson 5) |
| 500 | Unexpected fault | `"حدث خطأ داخلي في الخادم"` |

- **Frontend notes:**
  - This is a **preview**. The questions to answer and submit are the ones returned by `POST /api/quiz-attempts`. Render from that response, not from this one.
  - If a lesson has several active LessonQuizzes, the newest is used.
  - Treat a 404 as "no quiz for this lesson" and hide the quiz entry point. Caching per `(lessonId, language)` for the lesson screen's lifetime is reasonable. The server does not cache.
  - Mocks: `mock_lesson_quiz.json` and `mock_lesson_quiz_not_found.json`.

---

### Start attempt (first attempt or retry)

`POST /api/quiz-attempts?quizId={quizId}[&previousAttemptId={attemptId}][&language=]`: called by the child app to start answering a quiz, or to retry the wrong answers of a finished attempt.

- **Auth:** Bearer token (any role). Class-level `[Authorize]` with no roles, and no action-level attribute.
- **Headers / language:** `Authorization`. Localized: `?language=` → `Accept-Language` → `ar`.
- **Path / query parameters:**

| Name | Type | Required | Rules |
|---|---|---|---|
| `quizId` | int | yes (in practice) | Must be an existing, active quiz. A lesson's quiz also needs its lesson to be published. If omitted, it binds to `0` and returns 404 `"الاختبار رقم 0 غير موجود"`. A non-numeric value returns a ProblemDetails 400. |
| `previousAttemptId` | long | no | Omit for a first attempt. For a retry: must be **your own** attempt of the **same** `quizId`, with status `Completed`, not already retried, and with at least one wrong MCQ/TF answer. Not allowed for the Placement quiz. |
| `language` | string | no | `en` or `ar`. |

- **Request body:** none.
- **Success:** `201 Created`. The `Location` header points to `GET /api/quiz-attempts/{attemptId}`. The body is a bare `QuizAttemptResponseDto`.

First attempt:
```json
{
  "attemptId": 42,
  "quizId": 15,
  "startedAt": "2026-09-10T18:30:00.1234567Z",
  "language": "ar",
  "languageFallbackApplied": false,
  "questions": [
    {
      "questionId": 101,
      "questionText": "ما هو الجهد الكهربي؟",
      "questionType": "MultipleChoice",
      "imageUrl": null,
      "difficulty": "Easy",
      "displayOrder": 1,
      "points": 1,
      "currentHint": null,
      "options": [
        { "optionId": 1001, "optionText": "فرق الجهد بين نقطتين", "imageUrl": null, "displayOrder": 1 },
        { "optionId": 1002, "optionText": "مقاومة مرور التيار", "imageUrl": null, "displayOrder": 2 }
      ]
    },
    {
      "questionId": 102,
      "questionText": "ما وحدة قياس المقاومة الكهربية؟",
      "questionType": "MultipleChoice",
      "imageUrl": "/uploads/lessons/8f1c2b3a-6b4d-4e2a-9c1f-3d7e5a2b1c0d.png",
      "difficulty": "Medium",
      "displayOrder": 2,
      "points": 2,
      "currentHint": null,
      "options": [
        { "optionId": 1004, "optionText": "الأوم", "imageUrl": null, "displayOrder": 1 },
        { "optionId": 1005, "optionText": "الفولت", "imageUrl": null, "displayOrder": 2 },
        { "optionId": 1006, "optionText": null, "imageUrl": "/uploads/lessons/9b1d3f5a-7c9e-4b2d-8f6a-1c3e5a7b9d0f.png", "displayOrder": 3 }
      ]
    },
    {
      "questionId": 103,
      "questionText": "المصباح يضيء بدون مصدر كهربي.",
      "questionType": "TrueFalse",
      "imageUrl": null,
      "difficulty": "Easy",
      "displayOrder": 3,
      "points": 1,
      "currentHint": null,
      "options": [
        { "optionId": 1010, "optionText": "صح", "imageUrl": null, "displayOrder": 1 },
        { "optionId": 1011, "optionText": "خطأ", "imageUrl": null, "displayOrder": 2 }
      ]
    },
    {
      "questionId": 104,
      "questionText": "اشرح بأسلوبك لماذا يضيء المصباح عند غلق الدائرة الكهربية.",
      "questionType": "Essay",
      "imageUrl": null,
      "difficulty": "Hard",
      "displayOrder": 4,
      "points": 3,
      "currentHint": null,
      "options": []
    }
  ]
}
```
Retry of attempt 42, where question 102 was wrong (`?quizId=15&previousAttemptId=42`):
```json
{
  "attemptId": 43,
  "quizId": 15,
  "startedAt": "2026-09-10T18:45:00.7654321Z",
  "language": "ar",
  "languageFallbackApplied": false,
  "questions": [
    {
      "questionId": 102,
      "questionText": "ما وحدة قياس المقاومة الكهربية؟",
      "questionType": "MultipleChoice",
      "imageUrl": "/uploads/lessons/8f1c2b3a-6b4d-4e2a-9c1f-3d7e5a2b1c0d.png",
      "difficulty": "Medium",
      "displayOrder": 2,
      "points": 2,
      "currentHint": "افتكر إن الوحدة اسمها على اسم العالم الألماني.",
      "options": [
        { "optionId": 1004, "optionText": "الأوم", "imageUrl": null, "displayOrder": 1 },
        { "optionId": 1005, "optionText": "الفولت", "imageUrl": null, "displayOrder": 2 },
        { "optionId": 1006, "optionText": null, "imageUrl": "/uploads/lessons/9b1d3f5a-7c9e-4b2d-8f6a-1c3e5a7b9d0f.png", "displayOrder": 3 }
      ]
    }
  ]
}
```

| Field | Meaning |
|---|---|
| `attemptId` | New attempt id. Needed for get, submit, hint and result. |
| `startedAt` | UTC, with `Z`. The attempt expires `Assessment:InProgressAttemptTimeoutMinutes` (default 180, minimum 30) after this. |
| `questions[]` | First attempt: every **active** question of the quiz, ordered by `displayOrder`. Retry: only questions that were **wrong** in `previousAttemptId` and are still active, ordered by `displayOrder` (the original numbers are kept, e.g. `2`). A retry never contains essays. |
| `questions[].points` | Frozen into this attempt (`QuizAttemptQuestions.Points`). The submit is scored with these values, even if an admin changes the question later. |
| `questions[].currentHint` | First attempt: `null`. Retry: the latest hint saved on that question in the previous attempt, in the requested language, or else the latest in any language. It can be a post-submit AI hint or a Hint-button hint. `null` if there is none. |
| `languageFallbackApplied` | True if any question or option text fell back. |

- **Errors** (in the order the service checks them, `QuizAttemptService.StartAsync`):

| Status | When | Example message |
|---|---|---|
| 400 (ProblemDetails) | `quizId` or `previousAttemptId` not a number | see `mock_validation_error.json` |
| 401 | Missing or invalid token | `"غير مصرح لك بالوصول"` |
| 404 | Quiz does not exist | `"الاختبار رقم 15 غير موجود"` (quiz 15 does not exist) |
| 400 | Quiz is inactive | `"الاختبار رقم 15 غير مفعّل"` (quiz 15 is not active) |
| 404 | Quiz belongs to a lesson that is missing or unpublished | `"الاختبار رقم 15 غير متاح"` (quiz 15 not available) |
| 400 | `previousAttemptId` sent for the Placement quiz | `"اختبار تحديد المستوى لا تتم إعادته"` (the placement test is not retried) |
| 409 | Placement quiz id and the user is already placed | `"تم تحديد مستواك بالفعل، لا يمكن إعادة اختبار تحديد المستوى"` |
| 400 | First attempt: quiz has no active questions | `"الاختبار رقم 15 لا يحتوي على أسئلة مفعّلة"` (quiz has no active questions) |
| 404 | Retry: `previousAttemptId` does not exist **or belongs to someone else** | `"المحاولة رقم 42 غير موجودة"` (attempt 42 does not exist) |
| 400 | Retry: previous attempt is of another quiz | `"المحاولة رقم 42 لا تخص الاختبار رقم 15"` (attempt 42 does not belong to quiz 15) |
| 400 | Retry: previous attempt not Completed (still InProgress or Abandoned) | `"لا يمكن إعادة المحاولة رقم 42 لأنها لم تكتمل"` (cannot retry attempt 42 because it was not completed) |
| 400 | Retry: previous attempt was already retried | `"تمت إعادة المحاولة رقم 42 من قبل"` (attempt 42 was already retried) |
| 400 | Retry: previous attempt had no wrong answers | `"المحاولة رقم 42 لا تحتوي على إجابات خاطئة لإعادتها"` (no wrong answers to retry) |
| 400 | Retry: every wrong question has since been deactivated | `"أسئلة المحاولة رقم 42 الخاطئة لم تعد متاحة لإعادتها"` (the wrong questions are no longer available) |
| 400 | A MCQ/TF question has no correct option | `"لا يمكن بدء المحاولة: الأسئلة أرقام 102 ليس لها إجابة صحيحة"` |
| 409 | Retry: a concurrent request retried the same attempt first (`UQ_QuizAttempts_PreviousAttemptId`) | `"تمت إعادة المحاولة رقم 42 بالفعل"` (attempt 42 has already been retried) |
| 500 | Unexpected fault | `"حدث خطأ داخلي في الخادم"` |

- **Frontend notes:**
  - **Not idempotent.** Every call without `previousAttemptId` creates a new attempt, and there is no "one open attempt per quiz" rule. Keep the `attemptId`, and resume it with `GET /api/quiz-attempts/{attemptId}` instead of starting again. Disable the Start button while the request is in flight.
  - **Retry flow:**
    1. Submit (or `GET …/result`) returns `retryQuestions` and `quizId`.
    2. `POST /api/quiz-attempts?quizId={quizId}&previousAttemptId={attemptId}` creates a new attempt holding only the still-active wrong questions, with their latest hints.
    3. Submit that attempt like any other, answering every question it contains.
    4. Each attempt can be retried **once**. A retry can itself be retried if it still has wrong answers, which makes a chain.
    5. A result with `wrongAnswers: 0` (or only essays) cannot be retried (400).
  - `retryQuestions` in a result may list a question an admin has deactivated since. The retry attempt leaves it out, so the retry can have fewer questions than `retryQuestions`. Render the retry from **this** response.
  - **Keep the retry hints from this response.** `GET /api/quiz-attempts/{retryAttemptId}` reads hints saved on the retry attempt itself, so the hints carried over from the previous attempt do not come back there.
  - A Placement quiz id also works here: it follows the placement rules (resume, 409 if placed). The app should still use `POST /api/placement/start`.
  - The server does not refuse Parent or Admin tokens here.
  - Mocks: `mock_start_attempt.json`, `mock_start_retry.json`, `mock_start_attempt_mixed_ar.json`, `mock_start_attempt_mixed_en.json`, `mock_start_attempt_fallback.json`.

---

### Get attempt (resume)

`GET /api/quiz-attempts/{attemptId}`: called by the child app to redraw an attempt (after an app restart, or to reopen a quiz screen).

- **Auth:** Bearer token (any role). Only the caller's own attempts are visible.
- **Headers / language:** `Authorization`. Localized: `?language=` → `Accept-Language` → `ar`.
- **Path / query parameters:**

| Name | Type | Required | Rules |
|---|---|---|---|
| `attemptId` | long (path) | yes | Route constraint `:long`. Must be one of **your** attempts. |
| `language` | string (query) | no | `en` or `ar`. |

- **Request body:** none.
- **Success:** `200 OK`. The body is a bare `QuizAttemptResponseDto`, the same shape as Start. `startedAt` is read from the DB and has no offset.
```json
{
  "attemptId": 42,
  "quizId": 15,
  "startedAt": "2026-09-10T18:30:00.123",
  "language": "ar",
  "languageFallbackApplied": false,
  "questions": [
    {
      "questionId": 101,
      "questionText": "ما هو الجهد الكهربي؟",
      "questionType": "MultipleChoice",
      "imageUrl": null,
      "difficulty": "Easy",
      "displayOrder": 1,
      "points": 1,
      "currentHint": null,
      "options": [
        { "optionId": 1001, "optionText": "فرق الجهد بين نقطتين", "imageUrl": null, "displayOrder": 1 },
        { "optionId": 1002, "optionText": "مقاومة مرور التيار", "imageUrl": null, "displayOrder": 2 }
      ]
    },
    {
      "questionId": 102,
      "questionText": "ما وحدة قياس المقاومة الكهربية؟",
      "questionType": "MultipleChoice",
      "imageUrl": "/uploads/lessons/8f1c2b3a-6b4d-4e2a-9c1f-3d7e5a2b1c0d.png",
      "difficulty": "Medium",
      "displayOrder": 2,
      "points": 2,
      "currentHint": "الوحدة دي مش بتقيس الجهد ولا شدة التيار.",
      "options": [
        { "optionId": 1004, "optionText": "الأوم", "imageUrl": null, "displayOrder": 1 },
        { "optionId": 1005, "optionText": "الفولت", "imageUrl": null, "displayOrder": 2 },
        { "optionId": 1006, "optionText": null, "imageUrl": "/uploads/lessons/9b1d3f5a-7c9e-4b2d-8f6a-1c3e5a7b9d0f.png", "displayOrder": 3 }
      ]
    }
  ]
}
```

| Field | Meaning |
|---|---|
| `questions[]` | Exactly the questions frozen into this attempt, including any deactivated since start. Each one must still be answered on submit. Text comes from the live question (localized). Placement attempts are re-sorted by level, then display order, and renumbered `1..n` as they were served. Other attempts are ordered by `displayOrder`. |
| `questions[].points` | The points **frozen at attempt start**, not the live value. |
| `questions[].currentHint` | Latest hint saved **on this attempt** for that question: a Hint-button hint while the attempt is live, or a post-submit AI hint afterwards. Requested language first, otherwise the latest in any language. `null` if none. |

- **Errors:**

| Status | When | Example message |
|---|---|---|
| 401 | Missing or invalid token | `"غير مصرح لك بالوصول"` |
| 404 | Attempt does not exist **or belongs to another user** (same message) | `"المحاولة رقم 42 غير موجودة"` |
| 500 | Unexpected fault | `"حدث خطأ داخلي في الخادم"` |

- **Frontend notes:**
  - Returns 200 for an attempt in **any** state: InProgress, Completed or Abandoned/expired. The response has **no status or expiry field**. To find out, submit (410 if expired) or call `GET …/result` (409 means not submitted yet, 410 means expired). You can also estimate expiry locally as `startedAt` + the server's timeout (default 180 min), but that value is server configuration.
  - For a **retry** attempt, hints carried over from the previous attempt are not returned here (see Start attempt). Keep them from the Start response.
  - Mocks: `mock_get_attempt.json`, `mock_attempt_not_found.json`.

---

### Submit attempt

`POST /api/quiz-attempts/{attemptId}/submit`: called by the child app when the child finishes a quiz, a retry, or the placement test.

- **Auth:** Bearer token (any role). Only the caller's own attempts.
- **Headers / language:** `Authorization`, `Content-Type: application/json` (the action has `[Consumes("application/json")]`; any other type gets 415). Localized: `?language=` → `Accept-Language` → `ar`. The resolved language sets the text of `retryQuestions`, the language of the post-submit AI hints, and the language the AI writes **essay feedback** in. That language is stored with each essay, so the feedback keeps it even if the result is later fetched in another language.
- **Path / query parameters:**

| Name | Type | Required | Rules |
|---|---|---|---|
| `attemptId` | long (path) | yes | Must be one of **your** attempts. |
| `language` | string (query) | no | `en` or `ar`. |

- **Request body:** `application/json`, `SubmitQuizAttemptDto`. **Every question of the attempt must be answered exactly once.**

| Field | Type | Required | Rules |
|---|---|---|---|
| `mistakes` | array | yes when the attempt has MCQ/TF questions | Despite the name, this holds the answer to **every** `MultipleChoice` and `TrueFalse` question, right or wrong. The server decides correctness against the answer key frozen at attempt start. Omitting the property is treated as an empty list. Sending `null` fails model validation (400). |
| `mistakes[].questionId` | int | yes | A MCQ/TF question of this attempt. No duplicates. An Essay id here is rejected. |
| `mistakes[].selectedOptionId` | int | yes | An existing option **of that question**. |
| `essayAnswers` | array | yes when the attempt has essays | One entry per `Essay` question. Omitting it is treated as an empty list. Sending `null` fails model validation (400). |
| `essayAnswers[].questionId` | int | yes | An Essay question of this attempt. No duplicates. |
| `essayAnswers[].answerText` | string | yes | Not blank. At most `Assessment:EssayAnswerMaxLength` characters after trimming (default 4000, clamped 100–20000). Stored trimmed. |

```json
{
  "mistakes": [
    { "questionId": 101, "selectedOptionId": 1001 },
    { "questionId": 102, "selectedOptionId": 1005 },
    { "questionId": 103, "selectedOptionId": 1011 }
  ],
  "essayAnswers": [
    { "questionId": 104, "answerText": "لأن غلق الدائرة يعمل مسار كامل للتيار فيعدّي في المصباح وينوّره." }
  ]
}
```

- **Success:** `200 OK`. The body is a bare `QuizAttemptResultDto`.

Example: 101 (MCQ, 1 pt) right, 102 (MCQ, 2 pts) wrong, 103 (TF, 1 pt) right, 104 (Essay, 3 pts) not yet graded by the AI:
```json
{
  "attemptId": 42,
  "quizId": 15,
  "completedAt": "2026-09-10T18:42:10.1234567Z",
  "totalQuestions": 4,
  "autoGradedQuestions": 3,
  "pendingEssayQuestions": 1,
  "essayResults": [
    { "questionId": 104, "status": "Pending", "awardedPoints": null, "maxPoints": 3, "feedback": null }
  ],
  "correctAnswers": 2,
  "wrongAnswers": 1,
  "scorePercentage": 50,
  "totalPoints": 7,
  "earnedPoints": 2,
  "pendingPoints": 3,
  "language": "ar",
  "languageFallbackApplied": false,
  "hintsStatus": "Generated",
  "retryQuestions": [
    {
      "questionId": 102,
      "questionText": "ما وحدة قياس المقاومة الكهربية؟",
      "questionType": "MultipleChoice",
      "imageUrl": "/uploads/lessons/8f1c2b3a-6b4d-4e2a-9c1f-3d7e5a2b1c0d.png",
      "difficulty": "Medium",
      "displayOrder": 2,
      "points": 2,
      "currentHint": "افتكر إن الوحدة اسمها على اسم العالم الألماني.",
      "options": [
        { "optionId": 1004, "optionText": "الأوم", "imageUrl": null, "displayOrder": 1 },
        { "optionId": 1005, "optionText": "الفولت", "imageUrl": null, "displayOrder": 2 },
        { "optionId": 1006, "optionText": null, "imageUrl": "/uploads/lessons/9b1d3f5a-7c9e-4b2d-8f6a-1c3e5a7b9d0f.png", "displayOrder": 3 }
      ]
    }
  ],
  "placement": null
}
```
Placement attempt (12 one-point questions, 7 right). `placement` is filled in, there are no retry questions and no hints:
```json
{
  "attemptId": 58,
  "quizId": 7,
  "completedAt": "2026-09-11T16:05:42.3171234Z",
  "totalQuestions": 12,
  "autoGradedQuestions": 12,
  "pendingEssayQuestions": 0,
  "essayResults": [],
  "correctAnswers": 7,
  "wrongAnswers": 5,
  "scorePercentage": 58.33,
  "totalPoints": 12,
  "earnedPoints": 7,
  "pendingPoints": 0,
  "language": "ar",
  "languageFallbackApplied": false,
  "hintsStatus": "NotRequired",
  "retryQuestions": [],
  "placement": {
    "levelId": 2,
    "levelTitle": "المستوى الثاني: الدوائر الكهربية",
    "levelOrder": 2,
    "scorePercentage": 58.33,
    "passPercentage": 75,
    "placedAt": "2026-09-11T16:05:42.3171234Z",
    "levels": [
      { "levelId": 1, "levelTitle": "المستوى الأول: أساسيات الكهرباء", "questionsAsked": 4, "correctAnswers": 4, "totalPoints": 4, "earnedPoints": 4, "scorePercentage": 100, "mastered": true },
      { "levelId": 2, "levelTitle": "المستوى الثاني: الدوائر الكهربية", "questionsAsked": 4, "correctAnswers": 2, "totalPoints": 4, "earnedPoints": 2, "scorePercentage": 50, "mastered": false },
      { "levelId": 3, "levelTitle": "المستوى الثالث: القياسات", "questionsAsked": 4, "correctAnswers": 1, "totalPoints": 4, "earnedPoints": 1, "scorePercentage": 25, "mastered": false }
    ]
  }
}
```

**Scoring and points** (`AttemptScoring.ScorePercentage` / `AttemptScoring.CountPoints`). Every question weighs the `points` frozen when the attempt started (1 unless the admin set another value).

| Field | Exactly | Essays included? | Can change after submit? |
|---|---|---|---|
| `totalQuestions` | Questions in the attempt | yes | no |
| `autoGradedQuestions` | MultipleChoice + TrueFalse questions | no | no |
| `correctAnswers` | **Count** of MCQ/TF answered correctly | no | no |
| `wrongAnswers` | **Count** of MCQ/TF answered wrong (the only answers stored) | no | no |
| `scorePercentage` | Points of correct MCQ/TF ÷ points of all MCQ/TF × 100, 2dp, midpoint away from zero. `0` when the attempt has no MCQ/TF (essay-only quiz). | **no, never** | **no**: saved at submit |
| `totalPoints` | Sum of points of **every** question | yes | no |
| `earnedPoints` | Points of correct MCQ/TF + `awardedPoints` of **Graded** essays | yes | **grows** while essays are Pending |
| `pendingPoints` | Sum of `maxPoints` of essays still **Pending** | yes | shrinks to 0 |
| `pendingEssayQuestions` | Essays still **Pending** | yes | shrinks to 0 |

In the example: score = 2 (101 + 103) ÷ 4 (101 + 102 + 103) = 50. `totalPoints` = 1 + 2 + 1 + 3 = 7. `earnedPoints` = 2. `pendingPoints` = 3. If the AI later awards 2 of 3, `earnedPoints` becomes 4 and `pendingPoints` becomes 0, while `scorePercentage` stays 50. A **NotGraded** essay stays in `totalPoints`, earns 0, and is no longer pending. `earnedPoints` is final once `pendingEssayQuestions` is 0.

| Other field | Meaning |
|---|---|
| `completedAt` | When the result was saved (UTC). Has a `Z` here; no offset from `GET …/result` or a replayed submit. |
| `essayResults[]` | Every essay answer of the attempt, ordered by question `displayOrder`. |
| `essayResults[].status` | `Pending`: the AI has not finished; ask again later. `Graded`: final, with `awardedPoints` (0…`maxPoints`) and `feedback` (AI text for the child, in the submit language). `NotGraded`: final, the AI declined or never produced a usable grade; `awardedPoints` and `feedback` are `null` and nobody else will grade it. |
| `essayResults[].maxPoints` | The essay question's frozen points. |
| `hintsStatus` | Whether `retryQuestions` carry hints. `NotRequired`: no wrong answers (always the case for placement). `Generated`: every retry question has `currentHint`. `Partial`: some do. `Unavailable`: none do (AI not configured, down, timed out, or every hint was rejected because it gave the answer away). The score is final whatever this says. |
| `retryQuestions[]` | The wrong MCQ/TF questions, ordered by `displayOrder`, localized, with `currentHint` or `null`. Their `points` come from the live question (see Frontend notes). |
| `languageFallbackApplied` | Refers to the `retryQuestions` text only. `false` when there are none. |
| `placement` | `null` for every quiz except Placement. For placement it is a `PlacementResultDto` (fields explained under Placement status), and the placement is saved in the same transaction as the score. |

- **Errors** (roughly in check order, `QuizAttemptService.SubmitAsync` / `GradeAndCommitAsync`):

| Status | When | Example message |
|---|---|---|
| 400 (ProblemDetails) | Missing or malformed JSON body, wrong field types, `"mistakes": null`, `"essayAnswers": null`, or a missing/empty `answerText` caught by ASP.NET Core model validation (non-nullable properties are implicitly required) | framework `errors` object |
| 415 (framework) | `Content-Type` not `application/json` | framework response, not the envelope; the body may be empty |
| 401 | Missing or invalid token | `"غير مصرح لك بالوصول"` |
| 404 | Attempt does not exist **or belongs to another user** | `"المحاولة رقم 42 غير موجودة"` |
| 200 (replay) | Attempt is already `Completed`: the saved result is returned, and the body is **not** re-validated or re-graded | — |
| 410 | Attempt is Abandoned, or still InProgress but older than the timeout (it is marked Abandoned now) | `"المحاولة رقم 42 انتهت صلاحيتها قبل تسليمها، برجاء بدء محاولة جديدة"` (attempt 42 expired before it was submitted; please start a new attempt) |
| 409 | Placement attempt, and the learner is already placed | `"تم تحديد مستواك بالفعل، لا يمكن إعادة اختبار تحديد المستوى"` |
| 400 | A `mistakes` item is `null` | `"قائمة الإجابات تحتوي على عنصر فارغ"` (the answers list contains an empty item) |
| 400 | Same `questionId` twice in `mistakes` | `"السؤال رقم 102 مكرر في قائمة الإجابات"` (question 102 is duplicated in the answers) |
| 400 | `mistakes` item for a question not in the attempt, or for an Essay | `"السؤال رقم 104 لا يخص هذه المحاولة أو لا يُصحّح تلقائيًا"` (question 104 is not in this attempt or is not auto-graded) |
| 400 | An `essayAnswers` item is `null` | `"قائمة الإجابات المقالية تحتوي على عنصر فارغ"` |
| 400 | Same `questionId` twice in `essayAnswers` | `"السؤال رقم 104 مكرر في قائمة الإجابات المقالية"` |
| 400 | `essayAnswers` item for a non-essay question, or one not in the attempt | `"السؤال رقم 101 ليس سؤالًا مقاليًا في هذه المحاولة"` (question 101 is not an essay question in this attempt) |
| 400 | Blank `answerText` (service check; model validation usually rejects it first) | `"إجابة السؤال رقم 104 فارغة"` (the answer to question 104 is empty) |
| 400 | `answerText` too long | `"إجابة السؤال رقم 104 تتجاوز 4000 حرفًا"` (answer exceeds 4000 characters) |
| 400 | At least one question unanswered (ids listed ascending) | `"يجب الإجابة على كل أسئلة الاختبار؛ الأسئلة بدون إجابة: 101, 104"` (every question must be answered; unanswered: 101, 104) |
| 400 | `selectedOptionId` does not exist | `"الاختيار رقم 1099 غير موجود"` (option 1099 does not exist) |
| 400 | `selectedOptionId` belongs to another question | `"الاختيار رقم 1004 لا يخص السؤال رقم 101"` (option 1004 does not belong to question 101) |
| 400 | Placement attempt, but the Content module has no levels | `"لا توجد مستويات متاحة لتحديد مستوى الطالب"` (no levels available to place the learner) |
| 409 | A concurrent request interfered (another submit, the expiry sweep, a statistics row update or a deadlock). Nothing was saved and the attempt is still InProgress. | `"تعذّر تسليم المحاولة رقم 42 بسبب طلب متزامن، برجاء إعادة المحاولة"` (could not submit attempt 42 because of a concurrent request; please retry) |
| 409 | Two placement attempts of the same learner submitted at once; the other one won | `"تم تحديد مستواك بالفعل، لا يمكن إعادة اختبار تحديد المستوى"` |
| 500 | Unexpected fault | `"حدث خطأ داخلي في الخادم"` |

If a concurrent submit of the **same** attempt wins the race, this request returns that saved result with 200 instead of an error. The same happens if the attempt turns out Completed after a conflict. If the sweep abandoned it, the answer is 410.

- **Frontend notes:**
  - **How the request runs.**
    1. **Phase A** validates, grades and commits the score, wrong answers, essay answers (Pending), topic statistics, and the placement if any.
    2. **Phase B** runs only after that commit, inside one AI budget of `Assessment:AiHintTimeoutSeconds` (default 15 s, clamped 1–60). It first generates hints for the wrong answers, then asks the AI to grade the essays with whatever time is left.
    3. AI failures never fail the request. The one budget covers hints **and** inline essay grading together: when it runs out, the essay request is abandoned uncounted and left to the background worker. Expect up to about 15 s of AI waiting plus database work. Set the client timeout well above that, e.g. 30–45 s (suggestion).
  - **Safe to repeat.** Resubmitting a Completed attempt returns the saved result with 200 and no second grading. **Recovery after a lost response or a client timeout:** call `GET /api/quiz-attempts/{attemptId}/result`.
    - 200: the submit went through; show it.
    - 409 "not submitted yet": nothing was saved; submit again.
    - 410: expired; start a new attempt.
  - On **409 "concurrent request"**, just resubmit the same body. On **410**, start a new attempt (the answers are lost). On a **400 listing unanswered ids**, send the child back to those questions.
  - **Build the body from the attempt's questions.** Every `MultipleChoice`/`TrueFalse` question goes into `mistakes`, every `Essay` into `essayAnswers`. Do not filter to wrong answers: the client does not know the key, and a missing answer is a 400. Only the wrong answers are stored (`QuizAttemptMistakes`); correct answers leave no row.
  - **Essays: poll `GET …/result` while `pendingEssayQuestions > 0`.**
    - Fields that change while polling: `essayResults[].status`, `awardedPoints`, `feedback`, `pendingEssayQuestions`, `earnedPoints`, `pendingPoints`.
    - Fields that never change: `scorePercentage`, `correctAnswers`, `wrongAnswers`, `totalPoints`.
    - Grading timeline, from `EssayEvaluationService` and `EssayEvaluationWorker` with default settings:
      1. **Inline attempt** inside this submit's AI budget. If it succeeds, the response already shows `Graded`/`NotGraded`.
      2. **Nothing changes for at least `EssayInlineGraceMinutes` (5 min)** after the submit. The background worker leaves younger essays alone.
      3. **Background worker:** runs every `EssayEvaluationIntervalMinutes` (2 min), one AI request per attempt with a `EssayEvaluationTimeoutSeconds` (30 s) timeout.
      4. **Retries after a failed AI attempt:** the wait grows by `EssayEvaluationRetryMinutes` (10 min) × attempts made (10, 20, 30, 40 min).
      5. **Final state:** an AI `Skipped` becomes `NotGraded` immediately. After `EssayEvaluationMaxAttempts` (5) failures the essay becomes `NotGraded`. The worst case is therefore well over an hour and a half. An essay whose question has neither text nor an image description is closed as `NotGraded` on its first evaluation, without asking the AI.
    - If the essay AI endpoint is **not configured**, essays stay `Pending` indefinitely: no attempt is counted and no final state is reached.
    - Suggested UI: show the MCQ score at once and an "essay being graded" state. Refresh when the result screen is opened, on pull-to-refresh, or about every 1–2 min while the screen is visible. Stop when `pendingEssayQuestions == 0`. Polling faster than the worker interval gains nothing.
  - **Hints.** `retryQuestions[].currentHint` in this response contains only hints generated by this submit. `GET …/result` also shows hints the child took with the Hint button during the attempt. For the same attempt, `hintsStatus` and `currentHint` can therefore be richer in the GET than in the submit response.
  - `retryQuestions[].points` is the question's **live** points. The retry attempt freezes whatever value is live when it starts.
  - `placement` is always present in the JSON (`null` for non-placement quizzes).
  - Mocks: `mock_submit_mixed_request.json`, `mock_submit_attempt_request.json` (valid only for an attempt without essays), `mock_submit_attempt_result.json`, `mock_submit_perfect_score.json`, `mock_submit_mixed_result_ar.json`, `mock_submit_mixed_result_en.json`, `mock_submit_essay_only_result.json`, `mock_submit_essay_graded_result.json`, `mock_submit_placement_result.json`, `mock_submit_missing_answers.json`, `mock_attempt_ai_failure.json`, `mock_attempt_already_completed.json`, `mock_attempt_abandoned.json`, `mock_essay_empty_answer.json`, `mock_essay_not_essay_question.json`, `mock_mistake_on_essay_question.json`.

---

### Get saved result (recovery and essay polling)

`GET /api/quiz-attempts/{attemptId}/result`: called by the child app to recover a lost submit response, refresh essay grades, or reopen a past result.

- **Auth:** Bearer token (any role). The owner comes from the token's `sub` claim, never from the request.
- **Headers / language:** `Authorization`. Localized: `?language=` → `Accept-Language` → `ar`. The language applies to `retryQuestions` text and to which hint is shown (requested language first, otherwise the latest in any language). Essay `feedback` stays in the language used at submit.
- **Path / query parameters:**

| Name | Type | Required | Rules |
|---|---|---|---|
| `attemptId` | long (path) | yes | One of **your** attempts. |
| `language` | string (query) | no | `en` or `ar`. |

- **Request body:** none.
- **Success:** `200 OK`. The body is a bare `QuizAttemptResultDto`, the same fields as Submit. Here is the earlier attempt after the AI graded the essay:
```json
{
  "attemptId": 42,
  "quizId": 15,
  "completedAt": "2026-09-10T18:42:10.123",
  "totalQuestions": 4,
  "autoGradedQuestions": 3,
  "pendingEssayQuestions": 0,
  "essayResults": [
    {
      "questionId": 104,
      "status": "Graded",
      "awardedPoints": 2,
      "maxPoints": 3,
      "feedback": "إجابة جميلة! اذكر كمان إن الدائرة لازم تكون مقفولة."
    }
  ],
  "correctAnswers": 2,
  "wrongAnswers": 1,
  "scorePercentage": 50.00,
  "totalPoints": 7,
  "earnedPoints": 4,
  "pendingPoints": 0,
  "language": "ar",
  "languageFallbackApplied": false,
  "hintsStatus": "Generated",
  "retryQuestions": [
    {
      "questionId": 102,
      "questionText": "ما وحدة قياس المقاومة الكهربية؟",
      "questionType": "MultipleChoice",
      "imageUrl": "/uploads/lessons/8f1c2b3a-6b4d-4e2a-9c1f-3d7e5a2b1c0d.png",
      "difficulty": "Medium",
      "displayOrder": 2,
      "points": 2,
      "currentHint": "افتكر إن الوحدة اسمها على اسم العالم الألماني.",
      "options": [
        { "optionId": 1004, "optionText": "الأوم", "imageUrl": null, "displayOrder": 1 },
        { "optionId": 1005, "optionText": "الفولت", "imageUrl": null, "displayOrder": 2 },
        { "optionId": 1006, "optionText": null, "imageUrl": "/uploads/lessons/9b1d3f5a-7c9e-4b2d-8f6a-1c3e5a7b9d0f.png", "displayOrder": 3 }
      ]
    }
  ],
  "placement": null
}
```
A NotGraded essay would read `{ "questionId": 104, "status": "NotGraded", "awardedPoints": null, "maxPoints": 3, "feedback": null }`, with `earnedPoints: 2` and `pendingPoints: 0`.

| Field | Differences from the Submit response |
|---|---|
| `scorePercentage` / `correctAnswers` / `completedAt` | The values saved at submit, never recomputed. `completedAt` has no offset. `scorePercentage` comes from `DECIMAL(5,2)`, e.g. `50.00`. |
| `wrongAnswers` | Count of stored wrong answers. |
| `essayResults` / `pendingEssayQuestions` / `earnedPoints` / `pendingPoints` | Re-read on every call, so they include grades that arrived after the submit. |
| `hintsStatus` / `retryQuestions[].currentHint` | Derived from **every hint saved on this attempt**: post-submit AI hints **and** Hint-button hints taken during the attempt. Placement: always `NotRequired` and `[]`. |
| `retryQuestions[]` | Every wrong question, **including ones deactivated since**. The retry attempt will skip those. |
| `placement` | For placement attempts: rebuilt from the stored placement (stored `passPercentage`, frozen points, current level list). `null` otherwise. |
| `languageFallbackApplied` | Refers to `retryQuestions` text only. |

- **Errors** (`QuizAttemptService.GetResultAsync`):

| Status | When | Example message |
|---|---|---|
| 401 | Missing or invalid token | `"غير مصرح لك بالوصول"` |
| 404 | Attempt does not exist **or belongs to another user** | `"المحاولة رقم 42 غير موجودة"` |
| 409 | Attempt is still InProgress (not expired): not submitted yet | `"المحاولة رقم 42 لم يتم تسليمها بعد، برجاء إرسال الإجابات"` (attempt 42 has not been submitted yet; please send the answers) |
| 410 | Attempt is Abandoned, or InProgress past the timeout | `"المحاولة رقم 42 انتهت صلاحيتها قبل تسليمها، برجاء بدء محاولة جديدة"` |
| 500 | Unexpected fault | `"حدث خطأ داخلي في الخادم"` |

- **Frontend notes:**
  - Read-only and idempotent. Safe to call as often as needed. It is the **essay polling endpoint**: repeat while `pendingEssayQuestions > 0`, and stop once it is `0` (timeline and cadence under Submit attempt).
  - **Recovery algorithm after a submit with no response:** 200 → show it. 409 → resubmit the same body. 410 → start a new attempt.
  - **Start a retry from here:** `POST /api/quiz-attempts?quizId={quizId}&previousAttemptId={attemptId}`. Only possible when `wrongAnswers > 0` and this attempt has not been retried before. The result does not say whether it was already retried; a second retry returns 400 `"تمت إعادة المحاولة رقم 42 من قبل"`.
  - Do not cache this response while essays are Pending. Once `pendingEssayQuestions == 0`, it no longer changes, except for `retryQuestions` text and hints if language or content changes.
  - Mocks: `mock_attempt_result.json`, `mock_attempt_result_essay_not_graded.json`, `mock_attempt_result_not_submitted.json`, `mock_attempt_abandoned.json`, `mock_attempt_not_found.json`.
  - To reopen the caller's last result without an id, use [`GET /api/quiz-attempts/latest`](#get-latest-result).

---

### Get latest result

`GET /api/quiz-attempts/latest`: called by the child app to show the last result (for example a "your last quiz" card) without keeping an attempt id.

- **Auth:** Bearer token (any role). The user comes from the token's `sub` claim; the request carries no id.
- **Headers / language:** `Authorization`. Localized like `GET …/result`: `?language=` → `Accept-Language` → `ar`.
- **Path / query parameters:** none required.

| Name | Type | Required | Rules |
|---|---|---|---|
| `language` | string (query) | no | `en` or `ar`. |

- **Request body:** none.
- **Which attempt:** the caller's `Completed` attempt with the latest `completedAt` (on a tie, the higher `attemptId`), of any quiz type, placement included. InProgress and Abandoned attempts have no result and are skipped, so starting a new quiz does not hide the last result.
- **Success:** `200 OK`. The body is a bare `QuizAttemptResultDto`, exactly what [`GET /api/quiz-attempts/{attemptId}/result`](#get-saved-result-recovery-and-essay-polling) returns for that attempt: same fields, same rules (essay grades re-read, committed `scorePercentage`, `retryQuestions` with the latest hints, `placement` for a placement attempt). See that section for the example and the field table.

- **Errors** (`QuizAttemptService.GetLatestResultAsync`):

| Status | When | Example message |
|---|---|---|
| 401 | Missing or invalid token | `"غير مصرح لك بالوصول"` |
| 404 | The caller has no completed attempt yet | `"لا توجد محاولات مكتملة بعد، أكمل اختبارًا لتظهر نتيجتك هنا"` (no completed attempts yet; complete a quiz to see your result here) |
| 500 | Unexpected fault | `"حدث خطأ داخلي في الخادم"` |

- **Frontend notes:**
  - A 404 is the normal state for a new user. Show an empty state inviting a first quiz, not an error. The body is the envelope, so branch on the status code.
  - Use the response's `attemptId` for the follow-ups: poll essays with `GET /api/quiz-attempts/{attemptId}/result` while `pendingEssayQuestions > 0`, or retry with `POST /api/quiz-attempts?quizId={quizId}&previousAttemptId={attemptId}` when `wrongAnswers > 0`.
  - Right after a submit returns, this returns that attempt's result: the submit commits before it responds.
  - No clash with `GET /api/quiz-attempts/{attemptId}`: that route only matches numeric ids.
  - Mocks: `mock_attempt_result.json` (200), `mock_attempt_latest_not_found.json` (404).

---

### Hint button

`POST /api/quiz-attempts/{attemptId}/questions/{questionId}/hint`: called by the child app when the child presses "Hint" on a question of a live attempt.

- **Auth:** Bearer token (any role). Only the caller's own attempts.
- **Headers / language:** `Authorization`. Localized: `?language=` → `Accept-Language` → `ar`. The hint is generated and saved in the resolved language.
- **Path / query parameters:**

| Name | Type | Required | Rules |
|---|---|---|---|
| `attemptId` | long (path) | yes | One of **your** attempts. Must be InProgress, not expired, and not a placement attempt. |
| `questionId` | int (path) | yes | Must be a question of that attempt (any type, Essay included). |
| `language` | string (query) | no | `en` or `ar`. |

- **Request body:** none. The escalation level cannot be chosen by the client.
- **Success:** `200 OK`. The body is a bare `HintResponseDto`.

First press:
```json
{
  "questionId": 102,
  "attemptNumber": 1,
  "hint": "افتكر إن الوحدة اسمها على اسم عالم ألماني.",
  "hintsStatus": "Generated",
  "hintsRemaining": 1,
  "language": "ar"
}
```
Second press:
```json
{
  "questionId": 102,
  "attemptNumber": 2,
  "hint": "الوحدة دي مش بتقيس الجهد ولا شدة التيار — دي بتقيس اللي بيعاكس مرور التيار.",
  "hintsStatus": "Generated",
  "hintsRemaining": 0,
  "language": "ar"
}
```
No usable hint this time (the level is not used up):
```json
{
  "questionId": 102,
  "attemptNumber": 1,
  "hint": null,
  "hintsStatus": "Partial",
  "hintsRemaining": 2,
  "language": "ar"
}
```

| Field | Meaning |
|---|---|
| `attemptNumber` | The escalation level this press was for: `1` is a soft nudge, `2` is more direct. It equals the number of Hint-button hints already **saved** for this attempt and question (counted across **all languages**) plus 1. The maximum is `Assessment:MaxHintLevels` (default 2, clamped 1–5). |
| `hint` | The hint text (at most `MaxHintLength`, default 400 chars), or `null`. |
| `hintsStatus` | See the table below. |
| `hintsRemaining` | Levels still available for this question in this attempt **after** this press: `MaxHintLevels` minus the levels used, never below 0 (`HintService.HintsRemaining`). A `Generated` press used its level, so it is `MaxHintLevels − attemptNumber`. A `Partial` or `Unavailable` press used none, so it is `MaxHintLevels − attemptNumber + 1`, the same as before the press. `0` means the next press returns 409. |
| `language` | The language the hint was requested in. |

What each `hintsStatus` means, from `HintService.RequestHintAsync`:

| `hintsStatus` | `hint` | Saved and uses a level? | When |
|---|---|---|---|
| `Generated` | text | yes | The AI answered `Ok` with a hint that passed the checks: non-blank, not too long, and it does not name or nearly copy the correct option (TrueFalse: does not name either answer). Essays have no answer key, so no leak check applies. |
| `Partial` | `null` | **no** | The AI call **failed, timed out** (`AiHintTimeoutSeconds`, default 15 s), answered a non-`Ok` status, returned a blank or over-long hint, or a hint that gave the answer away. |
| `Unavailable` | `null` | **no** | The hint AI is **not configured**, or the question or its correct option has neither text nor an image description, so a hint could not be checked. |

**A press with no hint is not counted.** After `Partial` or `Unavailable` nothing is saved: no level is used, `hintsRemaining` does not change, and the next press asks for the same `attemptNumber` again. It never shows in `hintsUsed` of the progress map either, because only saved hints are counted and a hint is saved only when it is returned to the child. The client still cannot tell whether a `Partial` came from an AI failure or from a hint the checks refused.

- **Errors** (in check order):

| Status | When | Example message |
|---|---|---|
| 401 | Missing or invalid token | `"غير مصرح لك بالوصول"` |
| 404 | Attempt does not exist **or belongs to another user** | `"المحاولة رقم 42 غير موجودة"` |
| 410 | Attempt is Abandoned, or InProgress past the timeout | `"المحاولة رقم 42 انتهت صلاحيتها قبل تسليمها، برجاء بدء محاولة جديدة"` |
| 409 | Attempt already submitted | `"المحاولة رقم 42 تم تسليمها، ولا يمكن طلب تلميح بعد التسليم"` (attempt 42 was submitted; hints cannot be requested after submitting) |
| 409 | Placement attempt | `"اختبار تحديد المستوى لا يحتوي على تلميحات"` (the placement test has no hints) |
| 404 | `questionId` is not part of the attempt | `"السؤال رقم 102 ليس ضمن المحاولة رقم 42"` (question 102 is not part of attempt 42) |
| 409 | Both levels already used for this question in this attempt | `"لا توجد تلميحات إضافية للسؤال رقم 102 في هذه المحاولة"` (no more hints for question 102 in this attempt) |
| 404 | The question row was deleted (checked only when the AI is configured) | `"السؤال رقم 102 غير موجود"` |
| 409 | Two presses for the same question at the same moment, in the same language or in different ones. Both computed the same level; only one keeps it (`UQ_QuestionHints_AttemptId_QuestionId_AttemptNumber`, or `UQ_QuestionHints_AttemptId_QuestionId_Language_Sequence`). This press saved nothing and used no level. | `"تم طلب تلميح لنفس السؤال بالفعل، برجاء المحاولة مرة أخرى"` (a hint was already requested for this question; please try again) |
| 500 | Unexpected fault | `"حدث خطأ داخلي في الخادم"` |

- **Frontend notes:**
  - **Escalation UI:**
    1. Press 1 returns `attemptNumber: 1`, `hintsRemaining: 1`.
    2. Press 2 returns `attemptNumber: 2`, `hintsRemaining: 0`.
    3. Press 3 returns 409. Disable the button as soon as a response has `hintsRemaining: 0`, or when you receive that 409.
    - Only `Generated` uses up a level. After `Partial` or `Unavailable` the child can press again and gets the same `attemptNumber`; `hintsRemaining` stays the same. For `Unavailable` caused by missing AI configuration, pressing again will not help; consider hiding the button for the session (suggestion).
  - **Disable the button while a request is in flight.** Responses can take up to about 15 s.
  - **Two presses at once** (a double tap, or the same child on two devices) end with one 200 and one 409 `تم طلب تلميح لنفس السؤال بالفعل، برجاء المحاولة مرة أخرى`. Show the 200's hint and keep its `hintsRemaining`. If you only have the 409, the other press got the level: a later press returns the next level, or 409 when none is left.
  - A saved hint later shows as `currentHint` in `GET /api/quiz-attempts/{attemptId}`, as the latest hint per question. After submit it can appear in `GET …/result` and in the next retry's questions.
  - Hints never affect scoring. Every saved hint counts toward `hintsUsed` on the [progress map](#my-topic-statistics); a `Partial` or `Unavailable` press never does.
  - Asking again in another language does not grant new levels.
  - Mocks: `mock_hint_level1.json`, `mock_hint_level2.json`, `mock_hint_exhausted.json`, `mock_hint_partial.json` (what an AI failure or timeout returns), `mock_hint_unavailable.json`, `mock_hint_concurrent_press.json` (409 when two presses race).

---

### My topic statistics

`GET /api/user-topic-stats?language={en|ar}`: called by the child app for the progress map (the progress screen). Works for any signed-in user, but only returns the caller's own progress.

- **Auth:** Bearer token (any role). Class-level `[Authorize]` on `UserTopicStatController`.
- **Headers / language:** `Authorization`. **Localized** (`UserTopicStatController.GetMine` → `UserTopicStatService.GetMyProgressAsync`): `?language=` → `Accept-Language` → `ar`. Topic names, topic descriptions and category names fall back from the requested language to `en`, then to the base column. Every `message` is in English when the resolved language is `en`, and in Arabic otherwise (`TopicProgress`). There is no `languageFallbackApplied`.
- **Path / query parameters:**

| Name | Type | Required | Rules |
|---|---|---|---|
| `language` | string (query) | no | `en` or `ar`. Any other value resolves to `ar`. |

- **Request body:** none.
- **Success:** `200 OK`. The body is a bare `MyProgressResponseDto` **object**. It used to be an array of rows; see [Changes in this release](#changes-in-this-release-breaking-for-clients). It is never empty: a child who has done nothing gets every topic as `NotStarted` (`docs/mocks/mock_user_topic_stats_empty.json`).

```json
{
  "language": "ar",
  "totalXp": 23,
  "totalTopics": 6,
  "topicsStarted": 3,
  "topicsMastered": 1,
  "questionsAnswered": 26,
  "correctAnswers": 21,
  "accuracyPercentage": 81,
  "hintsUsed": 2,
  "message": "أحسنت! جمعت 23 من نقاط الخبرة وأتقنت 1 من أصل 6 من المواضيع. واصل التقدّم!",
  "categories": [
    {
      "categoryId": 1,
      "name": "أساسيات الكهرباء",
      "xp": 15,
      "totalTopics": 3,
      "topicsStarted": 2,
      "topicsMastered": 1,
      "topics": [
        {
          "topicId": 1,
          "name": "مفهوم الكهرباء",
          "description": "ما هي الكهرباء وأين نستخدمها في حياتنا.",
          "categoryId": 1,
          "categoryName": "أساسيات الكهرباء",
          "learningLevel": "Beginner",
          "mastery": "Mastered",
          "stars": 3,
          "xp": 13,
          "questionsAnswered": 13,
          "correctAnswers": 13,
          "wrongAnswers": 0,
          "accuracyPercentage": 100,
          "hintsUsed": 0,
          "lastPracticedAt": "2026-09-08T17:15:42.517",
          "message": "مذهل! لقد أتقنت هذا الموضوع، أنت بطل الكهرباء.",
          "difficulties": [
            { "difficulty": "Easy", "xp": 8, "questionsAnswered": 8, "correctAnswers": 8, "wrongAnswers": 0, "accuracyPercentage": 100, "hintsUsed": 0 },
            { "difficulty": "Medium", "xp": 5, "questionsAnswered": 5, "correctAnswers": 5, "wrongAnswers": 0, "accuracyPercentage": 100, "hintsUsed": 0 }
          ]
        },
        {
          "topicId": 2,
          "name": "الدائرة الكهربية",
          "description": "الدائرة المغلقة والمفتوحة ولماذا يضيء المصباح.",
          "categoryId": 1,
          "categoryName": "أساسيات الكهرباء",
          "learningLevel": "Beginner",
          "mastery": "Learning",
          "stars": 1,
          "xp": 2,
          "questionsAnswered": 3,
          "correctAnswers": 1,
          "wrongAnswers": 2,
          "accuracyPercentage": 33,
          "hintsUsed": 0,
          "lastPracticedAt": "2026-09-09T16:05:31.873",
          "message": "بداية رائعة! كل سؤال تجيب عنه يقرّبك من إتقان هذا الموضوع.",
          "difficulties": [
            { "difficulty": "Easy", "xp": 1, "questionsAnswered": 3, "correctAnswers": 1, "wrongAnswers": 2, "accuracyPercentage": 33, "hintsUsed": 0 },
            { "difficulty": "Medium", "xp": 1, "questionsAnswered": 0, "correctAnswers": 0, "wrongAnswers": 0, "accuracyPercentage": 0, "hintsUsed": 0 }
          ]
        },
        {
          "topicId": 3,
          "name": "السلامة الكهربية",
          "description": "كيف نتعامل مع الكهرباء بأمان في البيت والمدرسة.",
          "categoryId": 1,
          "categoryName": "أساسيات الكهرباء",
          "learningLevel": "Beginner",
          "mastery": "NotStarted",
          "stars": 0,
          "xp": 0,
          "questionsAnswered": 0,
          "correctAnswers": 0,
          "wrongAnswers": 0,
          "accuracyPercentage": 0,
          "hintsUsed": 0,
          "lastPracticedAt": null,
          "message": "موضوع جديد في انتظارك! ابدأ أول اختبار فيه لتجمع نقاط الخبرة.",
          "difficulties": []
        }
      ]
    },
    {
      "categoryId": 2,
      "name": "المكونات الإلكترونية",
      "xp": 7,
      "totalTopics": 3,
      "topicsStarted": 1,
      "topicsMastered": 0,
      "topics": [
        {
          "topicId": 4,
          "name": "الثنائي الباعث للضوء",
          "description": "كيف يعمل الـ LED ولماذا له طرف طويل وطرف قصير.",
          "categoryId": 2,
          "categoryName": "المكونات الإلكترونية",
          "learningLevel": "Intermediate",
          "mastery": "Practicing",
          "stars": 2,
          "xp": 7,
          "questionsAnswered": 10,
          "correctAnswers": 7,
          "wrongAnswers": 3,
          "accuracyPercentage": 70,
          "hintsUsed": 2,
          "lastPracticedAt": "2026-09-10T18:42:10.123",
          "message": "أنت تتقدّم بسرعة! راجع الأسئلة التي أخطأت فيها لتتقن هذا الموضوع.",
          "difficulties": [
            { "difficulty": "Easy", "xp": 6, "questionsAnswered": 8, "correctAnswers": 6, "wrongAnswers": 2, "accuracyPercentage": 75, "hintsUsed": 2 },
            { "difficulty": "Medium", "xp": 1, "questionsAnswered": 2, "correctAnswers": 1, "wrongAnswers": 1, "accuracyPercentage": 50, "hintsUsed": 0 }
          ]
        },
        {
          "topicId": 5,
          "name": "المقاومة",
          "description": "لماذا نحتاج المقاومة في الدائرة وكيف نقرأ قيمتها.",
          "categoryId": 2,
          "categoryName": "المكونات الإلكترونية",
          "learningLevel": "Intermediate",
          "mastery": "NotStarted",
          "stars": 0,
          "xp": 0,
          "questionsAnswered": 0,
          "correctAnswers": 0,
          "wrongAnswers": 0,
          "accuracyPercentage": 0,
          "hintsUsed": 0,
          "lastPracticedAt": null,
          "message": "موضوع جديد في انتظارك! ابدأ أول اختبار فيه لتجمع نقاط الخبرة.",
          "difficulties": []
        },
        {
          "topicId": 6,
          "name": "زر الضغط",
          "description": "كيف يفتح زر الضغط الدائرة ويغلقها.",
          "categoryId": 2,
          "categoryName": "المكونات الإلكترونية",
          "learningLevel": "Intermediate",
          "mastery": "NotStarted",
          "stars": 0,
          "xp": 0,
          "questionsAnswered": 0,
          "correctAnswers": 0,
          "wrongAnswers": 0,
          "accuracyPercentage": 0,
          "hintsUsed": 0,
          "lastPracticedAt": null,
          "message": "موضوع جديد في انتظارك! ابدأ أول اختبار فيه لتجمع نقاط الخبرة.",
          "difficulties": []
        }
      ]
    }
  ]
}
```

What the example shows, with the default settings (mastery at 80% over at least 5 answers):
- Topic 1 is `Mastered`: 13 of 13 answers correct.
- Topic 2 is `Learning`: 1 of 3 correct (33%). Its `Medium` entry has 1 XP and no answers. That point came from a graded essay: essays earn XP but are never counted as answered.
- Topic 4 is `Practicing`: 7 of 10 correct (70%), below the 80% needed for `Mastered`.
- Topics 3, 5 and 6 are `NotStarted`, and they are still listed.
- The topics' `xp` adds up to 22 (13 + 2 + 7), but `totalXp` is 23. The extra point came from a question that has no topic.
- `accuracyPercentage` 81 is 21 ÷ 26 = 80.77%, rounded.

**`MyProgressResponseDto`** (top level)

| Field | Meaning |
|---|---|
| `language` | The language names and messages were resolved in: `en` or `ar`. |
| `totalXp` | Every point the child has earned in submitted attempts: the frozen `points` of each correct MultipleChoice/TrueFalse answer, plus the `awardedPoints` of each `Graded` essay. Questions with no topic are included, so this can be **more than the sum of the topics' `xp`**. See *XP* below. |
| `totalTopics` | Topics on the map, across all categories. |
| `topicsStarted` | Topics whose `mastery` is not `NotStarted`. |
| `topicsMastered` | Topics whose `mastery` is `Mastered`. |
| `questionsAnswered` / `correctAnswers` | Sums over the topics on the map. MultipleChoice/TrueFalse answers only. |
| `accuracyPercentage` | `correctAnswers` ÷ `questionsAnswered` × 100 of those sums, as a whole number rounded half away from zero; `0` before any answer. It is not an average of the topics' percentages. |
| `hintsUsed` | Sum of the topics' `hintsUsed`. |
| `message` | The headline for the top of the screen (table below). |
| `categories` | Ordered by the category's `sortOrder`, then its id. |

**`CategoryProgressDto`** (`categories[]`)

| Field | Meaning |
|---|---|
| `categoryId` | `byte` (1–255). |
| `name` | Category name in `language`. |
| `xp` | Sum of its topics' `xp`. |
| `totalTopics` / `topicsStarted` / `topicsMastered` | As above, within this category. |
| `topics` | Ordered by `learningLevel` (`Beginner`, `Intermediate`, `Advanced`), then topic id. |

**`TopicProgressDto`** (`categories[].topics[]`, and the body of [One topic's progress](#one-topics-progress))

| Field | Meaning |
|---|---|
| `topicId` | Topic id. |
| `name` / `description` | In `language`. `description` can be `null`. |
| `categoryId` / `categoryName` | The topic's category; `categoryName` is in `language`. |
| `learningLevel` | `Beginner`, `Intermediate` or `Advanced`. |
| `mastery` | `NotStarted`, `Learning`, `Practicing` or `Mastered`. Computed on every read, never stored (see *Mastery and stars*). |
| `stars` | `0`–`3`, determined by `mastery`. |
| `xp` | XP earned in this topic: the sum of `difficulties[].xp`. |
| `questionsAnswered` / `correctAnswers` / `wrongAnswers` | **Counts, not points**, of MultipleChoice/TrueFalse answers in this topic over every submitted attempt, retries and placement included (a retried question counts again). Essays are never counted. `wrongAnswers` = answered − correct. |
| `accuracyPercentage` | `correctAnswers` ÷ `questionsAnswered` × 100, whole number, rounded half away from zero; `0` before any answer. |
| `hintsUsed` | Saved hints the child received on this topic's questions: Hint-button hints and post-submit hints alike. A Hint-button press that returned `Partial` or `Unavailable` saved nothing, so it is never counted. |
| `lastPracticedAt` | `completedAt` of the last submitted attempt that counted an answer in this topic (UTC, no offset). `null` when no answer was ever counted, even if a graded essay earned XP. |
| `message` | A sentence that matches `mastery` (table below). |
| `difficulties` | Only the difficulties the child has touched (counted answers, XP, or both), in the order `Easy`, `Medium`, `Hard`, `Advanced`. `[]` for a topic not started. |

**`DifficultyProgressDto`** (`difficulties[]`) has `difficulty` (`Easy` \| `Medium` \| `Hard` \| `Advanced`), `xp`, `questionsAnswered`, `correctAnswers`, `wrongAnswers`, `accuracyPercentage` and `hintsUsed`. Each means the same as the topic field, for that difficulty only. Topic and difficulty come from the attempt snapshot, so re-classifying a question later does not move past counts or XP.

**Which topics are listed** (`UserTopicStatService.GetMyProgressAsync`):
- Every active topic of an active category, practised or not.
- Every topic the child has counts or XP in, even after an admin deactivated the topic or its category. Earned progress never disappears from the map.
- A question with no topic is on no topic. Its answers are counted nowhere, and its XP counts only in `totalXp`.

**XP** (`UserTopicStatService.LoadXpAsync`). XP is not stored. It is read from the attempt history on every call:
- Each correct MultipleChoice/TrueFalse answer in a `Completed` attempt earns the `points` frozen for that attempt (`QuizAttemptQuestions.Points`).
- Each `Graded` essay earns its `awardedPoints`. `Pending` and `NotGraded` essays earn nothing, so XP grows when a pending essay is graded later.
- Every submitted attempt counts: retries, repeated attempts of the same quiz, and placement.
- `totalXp` is therefore the sum of `earnedPoints` over all of the child's submitted results.
- XP never decides mastery.

**Mastery and stars** (`TopicProgress.Mastery`, `TopicProgress.Stars`). The rows are checked in this order, on the topic's counts:

| `mastery` | `stars` | When |
|---|---|---|
| `NotStarted` | 0 | No answer counted, and the topic has no XP and no hint. |
| `Learning` | 1 | No answer counted, but the topic has XP (a graded essay) or a hint. Or fewer than 50% of the answers are correct. |
| `Mastered` | 3 | At least `Assessment:TopicMasteryPercentage` percent of the answers are correct (default 80, clamped 50–100), **and** at least `Assessment:TopicMasteryMinQuestions` answers were counted (default 5, clamped 1–50). |
| `Practicing` | 2 | Everything else: at least 50% correct, but below the mastery percentage or with too few answers. |

The thresholds are compared exactly on the counts (`correct × 100 ≥ percentage × answered`), not on the rounded `accuracyPercentage`. 79.5% is not 80%, even though `accuracyPercentage` shows `80`.

**Topic `message`** (`TopicProgress.TopicMessage`):

| When | `ar` | `en` |
|---|---|---|
| `Mastered` | `مذهل! لقد أتقنت هذا الموضوع، أنت بطل الكهرباء.` | `Amazing! You've mastered this topic. You're an electricity hero!` |
| `Practicing` and every answer correct (only more answers are missing) | `إجابات رائعة! أجب عن مزيد من الأسئلة في هذا الموضوع لتتقنه.` | `Perfect answers! Answer a few more questions in this topic to master it.` |
| `Practicing` with some wrong answers | `أنت تتقدّم بسرعة! راجع الأسئلة التي أخطأت فيها لتتقن هذا الموضوع.` | `You're making great progress! Review the questions you missed to master this topic.` |
| `Learning` | `بداية رائعة! كل سؤال تجيب عنه يقرّبك من إتقان هذا الموضوع.` | `Great start! Every question you answer brings you closer to mastering this topic.` |
| `NotStarted` | `موضوع جديد في انتظارك! ابدأ أول اختبار فيه لتجمع نقاط الخبرة.` | `A new topic is waiting for you! Take its first quiz to start earning XP.` |

**Headline `message`** (`TopicProgress.Headline`). The first matching row wins. `{xp}` is `totalXp`, `{mastered}` is `topicsMastered` and `{total}` is `totalTopics`:

| When | `ar` | `en` |
|---|---|---|
| Nothing started: `topicsStarted` is 0 and `totalXp` is 0 | `رحلتك في عالم الكهرباء تبدأ الآن! حُلّ أول اختبار لتجمع أولى نقاط الخبرة.` | `Your electricity adventure starts now! Take your first quiz to earn your first XP.` |
| Every topic on the map mastered | `بطل حقيقي! أتقنت كل المواضيع وجمعت {xp} من نقاط الخبرة.` | `True champion! You've mastered every topic and earned {xp} XP.` |
| At least one topic mastered | `أحسنت! جمعت {xp} من نقاط الخبرة وأتقنت {mastered} من أصل {total} من المواضيع. واصل التقدّم!` | `Well done! You've earned {xp} XP and mastered {mastered} of {total} topics. Keep going!` |
| Some XP, nothing mastered | `أنت على الطريق الصحيح! جمعت {xp} من نقاط الخبرة، واصل التعلّم لتتقن أول موضوع.` | `You're on the right track! You've earned {xp} XP. Keep learning to master your first topic.` |
| Started, but no XP yet (for example, every answer so far was wrong) | `كل محاولة تعلّمك شيئًا جديدًا! راجع أخطاءك وحاول مرة أخرى لتجمع نقاط الخبرة.` | `Every try teaches you something new! Review your mistakes and try again to earn XP.` |

- **Errors:**

| Status | When | Example body |
|---|---|---|
| 401 | Missing or invalid token | `{"success":false,"message":"غير مصرح لك بالوصول","data":null}` |
| 500 | Unexpected fault | `{"success":false,"message":"حدث خطأ داخلي في الخادم","data":null}` |

- **Frontend notes:**
  - **Rendering.** Draw `stars` as filled stars out of 3, and use `mastery` for the badge or colour. Show `message` as it is. Do not work out mastery from `accuracyPercentage`: the thresholds are server settings and the comparison is exact.
  - **Show `NotStarted` topics.** They are listed on purpose, so the child can see what is still ahead. Show them as upcoming (0 stars), not as errors or blanks.
  - **Keep the server order** of categories and topics. It follows the category `sortOrder` and the topics' learning level.
  - **Show `totalXp` as it is.** Do not add up the topics' `xp`, because questions with no topic earn XP that belongs to no topic. A category's `xp`, on the other hand, is exactly the sum of its topics.
  - **When to refetch.** Counts are written in the same transaction as a submit, hint counts right after the post-submit hints, and XP is read live. Refetch after a submit returns. Refetch again when essay polling (`GET /api/quiz-attempts/{attemptId}/result`) shows a newly `Graded` essay, because its points add XP. Nothing changes while an attempt is in progress, so do not refetch then.
  - Send the same `language` as the rest of the app; names and messages come back in it.
  - A `difficulties` entry can have `xp` and no answers: that is essay XP.
  - There is intentionally **no** recalculate endpoint.
  - Mocks: `mock_user_topic_stats.json` (the example above) and `mock_user_topic_stats_empty.json` (a child who has done nothing yet).

---

### One topic's progress

`GET /api/user-topic-stats/{topicId}?language={en|ar}`: called by the child app for a topic detail screen.

- **Auth:** Bearer token (any role). Only the caller's own progress.
- **Headers / language:** `Authorization`. Localized exactly like [My topic statistics](#my-topic-statistics).
- **Path / query parameters:**

| Name | Type | Required | Rules |
|---|---|---|---|
| `topicId` | int (path) | yes | Route constraint `:int`. Any existing topic, active or not. |
| `language` | string (query) | no | `en` or `ar`. |

- **Request body:** none.
- **Success:** `200 OK`. The body is a bare `TopicProgressDto`, with the same fields and rules as a topic of the progress map (`UserTopicStatService.GetTopicProgressAsync`). A topic the child has not practised yet is a **200** with `mastery: "NotStarted"`, zeros and `difficulties: []`, not a 404.

```json
{
  "topicId": 4,
  "name": "الثنائي الباعث للضوء",
  "description": "كيف يعمل الـ LED ولماذا له طرف طويل وطرف قصير.",
  "categoryId": 2,
  "categoryName": "المكونات الإلكترونية",
  "learningLevel": "Intermediate",
  "mastery": "Practicing",
  "stars": 2,
  "xp": 7,
  "questionsAnswered": 10,
  "correctAnswers": 7,
  "wrongAnswers": 3,
  "accuracyPercentage": 70,
  "hintsUsed": 2,
  "lastPracticedAt": "2026-09-10T18:42:10.123",
  "message": "أنت تتقدّم بسرعة! راجع الأسئلة التي أخطأت فيها لتتقن هذا الموضوع.",
  "difficulties": [
    { "difficulty": "Easy", "xp": 6, "questionsAnswered": 8, "correctAnswers": 6, "wrongAnswers": 2, "accuracyPercentage": 75, "hintsUsed": 2 },
    { "difficulty": "Medium", "xp": 1, "questionsAnswered": 2, "correctAnswers": 1, "wrongAnswers": 1, "accuracyPercentage": 50, "hintsUsed": 0 }
  ]
}
```

- **Errors:**

| Status | When | Example body |
|---|---|---|
| 401 | Missing or invalid token | `{"success":false,"message":"غير مصرح لك بالوصول","data":null}` |
| 404 | No topic with this id | `{"success":false,"message":"الموضوع رقم 9 غير موجود","data":null}` ("topic 9 does not exist") |
| 404 | `topicId` is not a number, or the path has an extra segment (such as the removed `/{topicId}/{difficulty}` route) | Empty body: no route matched |
| 500 | Unexpected fault | `{"success":false,"message":"حدث خطأ داخلي في الخادم","data":null}` |

- **Frontend notes:**
  - A 404 now means the topic does not exist (for example, a stale id). It never means "not practised yet", and it uses the envelope like the rest of Assessment.
  - The per-difficulty breakdown is in `difficulties`. The old `GET /api/user-topic-stats/{topicId}/{difficulty}` endpoint is removed.
  - For the whole map, prefer one call to `GET /api/user-topic-stats` over one call per topic. Both apply the same rules (`TopicProgress`), so a topic reads the same in both.
  - Mocks: `mock_user_topic_progress.json`, `mock_topic_not_found.json`.

---

## Assessment (admin)

These endpoints are how the Admin dashboard builds assessment content: a **quiz** holds **questions**, and a MultipleChoice or TrueFalse question holds **options**. A question can be classified under a **topic**, and topics are grouped in **categories**. The controllers are `QuizController`, `QuestionController` (file `QuestionController .cs`, with a space), `QuestionOptionController`, `AssessmentCategoryController` and `AssessmentTopicController`, all `[Authorize(Roles = "Admin")]` at class level. The rules live in `AssessmentBL/Services/QuizService.cs`, `QuestionService.cs`, `QuestionOptionService.cs`, `CategoryService.cs` and `TopicService.cs` (shared name and translation checks in `TaxonomyInput.cs`).

- Success bodies are **bare DTOs** (`Ok(dto)`, `CreatedAtAction(..., dto)` or `NoContent()`). Errors use the envelope.
- Not localized: these endpoints read and write the base columns (`Quizzes.Title`, `Questions.QuestionText`, `QuestionOptions.OptionText`, `Categories.Name`, `Topics.Name` / `Description`). The category and topic endpoints also read and write their translations (`CategoryTranslations`, `TopicTranslations`) as a `translations` list. No endpoint manages `QuizTranslations`, `QuestionTranslations` or `QuestionOptionTranslations`.
- `imageDescription` is returned only by these admin DTOs. The child-facing projections (`LocalizedQuestionQuery`) never include it.
- Nothing here deletes a quiz, a question, a category or a topic. They are deactivated instead, which keeps attempt history, snapshots, wrong answers, essay answers, hints and statistics intact. Only options can be deleted, and only while no attempt references them.

### Assessment authoring concepts

**Quiz types** (`AssessmentBL/Services/Constants/QuizTypes.cs`, mirrors `CK_Quizzes_TypeMatchesReference`). Values are case-sensitive.

| `quizType` | Must have | Must NOT have | How children reach it | Active-slot rule |
|---|---|---|---|---|
| `LevelAssessment` | `levelId` | `lessonId` | `POST /api/quiz-attempts?quizId=…`. Its active auto-graded questions also feed the placement test (the newest active LevelAssessment quiz per level; up to `AssessmentSettings.PlacementQuestionsPerLevel` questions, in `displayOrder`). | Several can be active; placement uses the newest (highest id). |
| `LessonQuiz` | `lessonId` | `levelId` | `GET /api/quizzes/for-lesson/{lessonId}`, then an attempt. Only while the lesson is published. | **At most one active per lesson** (409 otherwise). |
| `LessonReview` | `lessonId` | `levelId` | By quiz id | No limit |
| `Standalone` | nothing | `levelId`, `lessonId` | By quiz id | No limit |
| `Placement` | nothing | `levelId`, `lessonId` | `/api/placement` | **At most one active overall** (409 otherwise). **Owns no questions**: adding one is refused. |

**Question types** (`QuestionTypes.cs`, case-sensitive): `MultipleChoice` (the default when blank on create), `TrueFalse`, `Essay`.

**Difficulty** (`QuestionDifficulties.cs`, case-sensitive): `Easy`, `Medium`, `Hard`, `Advanced`.

**Categories and topics.** A topic is what a question is about, and the unit of the child's progress map. Each topic belongs to one category and has a `learningLevel` (`TopicConstants.cs`: `Beginner`, `Intermediate`, `Advanced`; case-insensitive on input, stored in this casing). A question's `topicId` is **optional**. A question with no topic is asked, graded and earns XP like any other, but its answers count toward no topic. The topic is frozen per attempt like the difficulty, including "no topic". Manage them with [`/api/assessment/categories`](#list-assessment-categories) and [`/api/assessment/topics`](#list-assessment-topics).

**Activation rules for a question.** They are checked by `QuestionService.EnsureAnswerableAsync` both on `PATCH /api/questions/{id}/active?isActive=true` and on `PUT /api/questions/{id}` with `isActive: true`.

| Type | Options | Correct options |
|---|---|---|
| `MultipleChoice` | ≥ 2 | exactly 1 |
| `TrueFalse` | exactly 2 | exactly 1 |
| `Essay` | 0 | — |

In every case the question image, if any, needs a description, and every option image needs a description. New questions start **inactive** (DB default `IsActive = 0`). New quizzes start **active**.

**Points.** `points` is a `byte` (1–255). Sending `0` or omitting it stores **1** (`QuestionService.NormalizePoints`). Points are copied into `QuizAttemptQuestions.Points` when an attempt starts (`QuizAttemptService.LoadQuestionSnapshotsAsync` / `PersistAttemptAsync`), so editing points affects only attempts started afterwards.
- For MultipleChoice/TrueFalse, points weight `scorePercentage`.
- For an Essay, points are the most the AI can award. Essays count in `totalPoints`/`earnedPoints`, never in `scorePercentage`.

**Images.**
1. Upload first: `POST /api/content/media/images`, Admin, `multipart/form-data` with field `file`; jpg/jpeg/png/webp; ≤ 5 MB. The response is the enveloped `{ "success": true, "message": "تم رفع الصورة بنجاح", "data": { "url": "/uploads/lessons/<guid>.png" } }` — see `docs/mocks/mock_image_upload.json`.
2. Send `data.url` as `imageUrl` together with a mandatory `imageDescription`: non-blank, ≤ 1000 characters after trimming, and saying in words what the image shows.

The AI never looks at images, only at this text. When `imageUrl` is null or blank, the description is silently discarded and stored as `null`.

---

### Get quiz by id

`GET /api/quizzes/{quizId}`: called by the Admin dashboard.

- **Auth:** roles: `Admin` (class-level `[Authorize(Roles = "Admin")]`; no action-level attribute).
- **Headers / language:** `Authorization: Bearer <token>`. Not localized; returns the base `title`/`description`.
- **Path / query parameters:**

| Name | Type | Required | Rules |
|---|---|---|---|
| `quizId` | int | yes | Route constraint `:int`. A non-integer does not match the route, so the response is 404 with no body. |

- **Request body:** none.
- **Success:** `200 OK`. Bare `QuizResponseDto`, no envelope.

```json
{
  "id": 15,
  "title": "اختبار الدائرة الكهربية",
  "description": "أسئلة على مقدمة الدوائر",
  "quizType": "LessonQuiz",
  "levelId": null,
  "lessonId": 5,
  "isActive": true,
  "createdAt": "2026-08-10T09:00:00.123",
  "updatedAt": null
}
```

| Field | Notes |
|---|---|
| `quizType` | One of the five quiz types above. |
| `levelId` / `lessonId` | Exactly as the type requires (see the quiz type table); the other one is `null`. Neither has a database foreign key: they point into the Content module. |
| `isActive` | Inactive quizzes cannot be started by children (`QuizAttemptService.StartAsync` → 400 `"الاختبار رقم {id} غير مفعّل"`, "quiz {id} is not active"). |
| `createdAt` / `updatedAt` | UTC. Values read from the database carry no `Z` suffix. `updatedAt` is `null` until the first update or activation change. |

- **Errors:**

| Status | When | Example body |
|---|---|---|
| 401 | Missing, invalid or expired token | `{"success":false,"message":"غير مصرح لك بالوصول","data":null}` |
| 403 | Signed in but not `Admin` | `{"success":false,"message":"ليس لديك صلاحية","data":null}` |
| 404 | No quiz with this id (`QuizService.GetByIdAsync`) | `{"success":false,"message":"الاختبار رقم 15 غير موجود","data":null}` ("quiz 15 does not exist") |
| 500 | Unexpected server fault | `{"success":false,"message":"حدث خطأ داخلي في الخادم","data":null}` |

- **Frontend notes:** A safe read that can be cached until the admin edits the quiz. Every mutating quiz endpoint returns (or implies) the new state, so update the cache from that response. The child-facing `GET /api/quizzes/for-lesson/{lessonId}` shares the `/api/quizzes` prefix but lives in `LessonQuizController`; the routes do not overlap.

---

### List quizzes (paged)

`GET /api/quizzes`: called by the Admin dashboard.

- **Auth:** roles: `Admin` (class level).
- **Headers / language:** `Authorization: Bearer <token>`. Not localized.
- **Path / query parameters** (bound from `QuizFilterDto`):

| Name | Type | Required | Rules |
|---|---|---|---|
| `quizType` | string | no | Exact match on `QuizType`. Not validated: an unknown value just filters to no rows. |
| `levelId` | int | no | Exact match. |
| `lessonId` | int | no | Exact match. |
| `isActive` | bool | no | `true` / `false`. Omit to get both. |
| `pageNumber` | int | no | Default `1`; values `< 1` become `1`. |
| `pageSize` | int | no | Default `20`; values `< 1` become `1`; values `> 100` become `100` (`QuizService.MaxPageSize`). |

A non-numeric `levelId`/`lessonId`/`pageNumber`/`pageSize` or a non-boolean `isActive` fails model binding → 400 `ValidationProblemDetails`.

- **Request body:** none.
- **Success:** `200 OK`. Bare `PagedResult<QuizResponseDto>`, no envelope. Items are ordered by `id` descending (newest first).

```json
{
  "items": [
    {
      "id": 16,
      "title": "تقييم المستوى الأول",
      "description": null,
      "quizType": "LevelAssessment",
      "levelId": 1,
      "lessonId": null,
      "isActive": true,
      "createdAt": "2026-08-12T11:20:00",
      "updatedAt": "2026-09-01T14:05:00"
    },
    {
      "id": 15,
      "title": "اختبار الدائرة الكهربية",
      "description": "أسئلة على مقدمة الدوائر",
      "quizType": "LessonQuiz",
      "levelId": null,
      "lessonId": 5,
      "isActive": true,
      "createdAt": "2026-08-10T09:00:00",
      "updatedAt": null
    }
  ],
  "totalCount": 2,
  "pageNumber": 1,
  "pageSize": 20
}
```

| Field | Notes |
|---|---|
| `totalCount` | Number of quizzes matching the filters, across all pages. |
| `pageNumber` / `pageSize` | The values actually applied **after clamping**. Show these, not the ones you sent. |

- **Errors:**

| Status | When | Example body |
|---|---|---|
| 400 | A query value cannot be bound (e.g. `pageSize=abc`) | `ValidationProblemDetails` (see `mock_validation_error.json`) |
| 401 | Missing, invalid or expired token | `{"success":false,"message":"غير مصرح لك بالوصول","data":null}` |
| 403 | Not `Admin` | `{"success":false,"message":"ليس لديك صلاحية","data":null}` |
| 500 | Unexpected server fault | `{"success":false,"message":"حدث خطأ داخلي في الخادم","data":null}` |

- **Frontend notes:**
  - A page past the end returns `items: []` with the real `totalCount`, so compute the page count as `ceil(totalCount / pageSize)`.
  - There is no title search and no sort option.
  - Offer `quizType` as a fixed dropdown of the five values (case-sensitive when creating).
  - Mock: `docs/mocks/mock_admin_quizzes_paged.json` has the right shape (bare, no envelope).

---

### Create quiz

`POST /api/quizzes`: called by the Admin dashboard.

- **Auth:** roles: `Admin` (class level).
- **Headers / language:** `Authorization: Bearer <token>`, `Content-Type: application/json`. Not localized. The title is stored as sent (trimmed).
- **Path / query parameters:** none.
- **Request body** (`CreateQuizDto`):

| Field | Type | Required | Rules |
|---|---|---|---|
| `title` | string | yes | Non-blank (implicit required); trimmed; ≤ 300 characters after trimming. |
| `description` | string \| null | no | Stored as sent (not trimmed); no length limit (`NVARCHAR(MAX)`). |
| `quizType` | string | yes | One of `LevelAssessment`, `LessonQuiz`, `LessonReview`, `Standalone`, `Placement` (case-sensitive). The DTO property is a non-nullable `string`, so a missing or blank value is rejected by model validation (400 `ValidationProblemDetails`). The service's "blank → `Standalone`" fallback in `QuizService.CreateAsync` is therefore not reachable over HTTP. |
| `levelId` | int \| null | depends | Required for `LevelAssessment` and must be `null` for every other type. The level must exist (`ILevelCatalog.GetLevelsInOrderAsync`). |
| `lessonId` | int \| null | depends | Required for `LessonQuiz` / `LessonReview` and must be `null` for every other type. The lesson must exist; **it may be unpublished** (authoring before release is allowed). |

Validation order in `QuizService.CreateAsync`:
1. Title.
2. Type.
3. Type ↔ level/lesson match.
4. The referenced lesson/level exists.
5. Active slot is free (a new quiz is always created **active**).

```json
{
  "title": "اختبار الدائرة الكهربية",
  "description": "أسئلة على مقدمة الدوائر",
  "quizType": "LessonQuiz",
  "levelId": null,
  "lessonId": 5
}
```

- **Success:** `201 Created`. Header `Location: /api/quizzes/{id}`. Body is a bare `QuizResponseDto`:

```json
{
  "id": 17,
  "title": "اختبار الدائرة الكهربية",
  "description": "أسئلة على مقدمة الدوائر",
  "quizType": "LessonQuiz",
  "levelId": null,
  "lessonId": 5,
  "isActive": true,
  "createdAt": "2026-09-13T08:30:12.3456789Z",
  "updatedAt": null
}
```

| Field | Notes |
|---|---|
| `isActive` | Always `true` on create. |
| `createdAt` | Here it is the server clock value (`DateTime.UtcNow`), so it is serialized **with** a `Z` suffix and full precision. The same quiz read back later shows millisecond precision and no `Z`. Parse both as UTC. |

- **Errors:**

| Status | When | Example body |
|---|---|---|
| 400 | Missing/blank `title` or `quizType`, or an unparsable JSON value | `ValidationProblemDetails` |
| 400 | Title longer than 300 characters | `{"success":false,"message":"عنوان الاختبار لا يتجاوز 300 حرف","data":null}` ("title must not exceed 300 characters") |
| 400 | Unknown `quizType` (including a wrong case, e.g. `lessonquiz`) | `{"success":false,"message":"نوع الاختبار 'lessonquiz' غير صالح","data":null}` ("quiz type … is invalid") |
| 400 | `levelId`/`lessonId` do not match the type (e.g. `LessonQuiz` without `lessonId`, or `Standalone` with `levelId`) | `{"success":false,"message":"نوع الاختبار 'LessonQuiz' لا يتوافق مع LevelId/LessonId المرسلة","data":null}` ("quiz type does not match the LevelId/LessonId sent") |
| 401 | Missing, invalid or expired token | `{"success":false,"message":"غير مصرح لك بالوصول","data":null}` |
| 403 | Not `Admin` | `{"success":false,"message":"ليس لديك صلاحية","data":null}` |
| 404 | `lessonId` does not exist | `{"success":false,"message":"الدرس رقم 5 غير موجود","data":null}` ("lesson 5 does not exist") |
| 404 | `levelId` does not exist | `{"success":false,"message":"المستوى رقم 1 غير موجود","data":null}` ("level 1 does not exist") |
| 409 | `Placement`: another Placement quiz is already active | `{"success":false,"message":"يوجد اختبار تحديد مستوى مفعّل بالفعل، أوقفه أولًا","data":null}` ("an active placement quiz already exists, deactivate it first") |
| 409 | `LessonQuiz`: this lesson already has an active LessonQuiz | `{"success":false,"message":"يوجد اختبار مفعّل بالفعل للدرس رقم 5، أوقفه أولًا","data":null}` ("lesson 5 already has an active quiz, deactivate it first") |
| 409 | Lost a race on the filtered unique index (`UQ_Quizzes_OneActivePlacement` / `UQ_Quizzes_OneActiveLessonQuizPerLesson`) | `{"success":false,"message":"يوجد اختبار مفعّل آخر لنفس الغرض، أوقفه أولًا","data":null}` ("another active quiz serves the same purpose, deactivate it first") |
| 500 | Unexpected server fault | `{"success":false,"message":"حدث خطأ داخلي في الخادم","data":null}` |

- **Frontend notes:**
  - **Not idempotent.** A double submit creates two quizzes (except for the slot-limited types, where the second gets 409). Disable the submit button while the request is in flight.
  - Because a new quiz is always active, you cannot create a second Placement quiz, or a second LessonQuiz for the same lesson, as a draft. Deactivate the live one first (`PATCH /api/quizzes/{id}/active?isActive=false`). While it is deactivated, children see no placement test or lesson quiz. Consider warning the admin.
  - `quizType`, `levelId` and `lessonId` **cannot be changed later**, so confirm them in the UI before submitting.
  - For the lesson and level pickers use the Content endpoints (`GET /api/content/levels`, `GET /api/content/levels/{levelId}/lessons`).
  - A new quiz has no questions. Children cannot start it until at least one active question exists: `StartAsync` → 400 `"الاختبار رقم {id} لا يحتوي على أسئلة مفعّلة"` ("quiz has no active questions").
  - For a `Placement` quiz, do not offer "add question" at all.

---

### Update quiz

`PUT /api/quizzes/{quizId}`: called by the Admin dashboard.

- **Auth:** roles: `Admin` (class level).
- **Headers / language:** `Authorization: Bearer <token>`, `Content-Type: application/json`. Not localized.
- **Path / query parameters:**

| Name | Type | Required | Rules |
|---|---|---|---|
| `quizId` | int | yes | Route constraint `:int`. |

- **Request body** (`UpdateQuizDto`). This is a **full replacement** of the editable fields:

| Field | Type | Required | Rules |
|---|---|---|---|
| `title` | string | yes | Non-blank; trimmed; ≤ 300 characters. |
| `description` | string \| null | no | Replaces the stored value; **omitting it clears the description**. |
| `isActive` | bool | effectively yes | **Omitting it binds `false` and deactivates the quiz.** Going from inactive to active runs the same active-slot check as create. |

`quizType`, `levelId` and `lessonId` are not part of this DTO. If sent, they are ignored: a quiz can never be re-pointed at another level or lesson (see the comment in `QuizService.UpdateAsync`). The slot check runs before the title check.

```json
{
  "title": "اختبار الدائرة الكهربية (مُحدَّث)",
  "description": "أسئلة على مقدمة الدوائر والمقاومة",
  "isActive": true
}
```

- **Success:** `200 OK`. Bare `QuizResponseDto`:

```json
{
  "id": 15,
  "title": "اختبار الدائرة الكهربية (مُحدَّث)",
  "description": "أسئلة على مقدمة الدوائر والمقاومة",
  "quizType": "LessonQuiz",
  "levelId": null,
  "lessonId": 5,
  "isActive": true,
  "createdAt": "2026-08-10T09:00:00.123",
  "updatedAt": "2026-09-13T08:41:03.1234567Z"
}
```

| Field | Notes |
|---|---|
| `updatedAt` | Always set to the server clock on PUT, even when nothing changed. It carries a `Z` in this response; `createdAt` (read from the database) does not. |

- **Errors:**

| Status | When | Example body |
|---|---|---|
| 400 | Missing/blank `title`, or an unparsable value (e.g. `"isActive": "maybe"`) | `ValidationProblemDetails` |
| 400 | Title longer than 300 characters | `{"success":false,"message":"عنوان الاختبار لا يتجاوز 300 حرف","data":null}` |
| 401 | Missing, invalid or expired token | `{"success":false,"message":"غير مصرح لك بالوصول","data":null}` |
| 403 | Not `Admin` | `{"success":false,"message":"ليس لديك صلاحية","data":null}` |
| 404 | No quiz with this id | `{"success":false,"message":"الاختبار رقم 15 غير موجود","data":null}` |
| 409 | Activating a Placement quiz while another is active | `{"success":false,"message":"يوجد اختبار تحديد مستوى مفعّل بالفعل، أوقفه أولًا","data":null}` |
| 409 | Activating a LessonQuiz while the lesson has another active one | `{"success":false,"message":"يوجد اختبار مفعّل بالفعل للدرس رقم 5، أوقفه أولًا","data":null}` |
| 409 | Lost the activation race at the database | `{"success":false,"message":"يوجد اختبار مفعّل آخر لنفس الغرض، أوقفه أولًا","data":null}` |
| 500 | Unexpected server fault | `{"success":false,"message":"حدث خطأ داخلي في الخادم","data":null}` |

- **Frontend notes:**
  - Always send `isActive` explicitly, pre-filled from the loaded quiz. A form that forgets it silently takes the quiz offline.
  - Always send the current `description`, or it is cleared.
  - Idempotent in effect: repeating the same body gives the same state, only `updatedAt` moves.
  - Deactivating a quiz does not end attempts already in progress (submit does not re-check `Quiz.IsActive`), but new attempts and retries on it are refused.

---

### Activate / deactivate quiz

`PATCH /api/quizzes/{quizId}/active?isActive={true|false}`: called by the Admin dashboard.

- **Auth:** roles: `Admin` (class level).
- **Headers / language:** `Authorization: Bearer <token>`. Not localized.
- **Path / query parameters:**

| Name | Type | Required | Rules |
|---|---|---|---|
| `quizId` | int | yes | Route constraint `:int`. |
| `isActive` | bool | effectively yes | `[FromQuery] bool`. **If omitted it binds `false`, so the call deactivates.** A non-boolean value → 400 `ValidationProblemDetails`. |

- **Request body:** none.
- **Success:** `204 No Content`, empty body. If the quiz is already in the requested state, nothing is written (`updatedAt` does not move) and the response is still 204.
- **Errors:**

| Status | When | Example body |
|---|---|---|
| 400 | `isActive` is not a boolean | `ValidationProblemDetails` |
| 401 | Missing, invalid or expired token | `{"success":false,"message":"غير مصرح لك بالوصول","data":null}` |
| 403 | Not `Admin` | `{"success":false,"message":"ليس لديك صلاحية","data":null}` |
| 404 | No quiz with this id | `{"success":false,"message":"الاختبار رقم 15 غير موجود","data":null}` |
| 409 | Activating would create a second active Placement quiz, or a second active LessonQuiz for the lesson | `{"success":false,"message":"يوجد اختبار تحديد مستوى مفعّل بالفعل، أوقفه أولًا","data":null}` / `{"success":false,"message":"يوجد اختبار مفعّل بالفعل للدرس رقم 5، أوقفه أولًا","data":null}` |
| 409 | Lost the race at the database | `{"success":false,"message":"يوجد اختبار مفعّل آخر لنفس الغرض، أوقفه أولًا","data":null}` |
| 500 | Unexpected server fault | `{"success":false,"message":"حدث خطأ داخلي في الخادم","data":null}` |

- **Frontend notes:**
  - Idempotent and safe to retry.
  - The body is empty, so after 204 either flip `isActive` locally or refetch `GET /api/quizzes/{quizId}` (which also gives the new `updatedAt`).
  - On a 409 when activating, offer a shortcut: find the active one with `GET /api/quizzes?quizType=Placement&isActive=true` (or `quizType=LessonQuiz&lessonId=…&isActive=true`), deactivate it, then retry.
  - Activation does **not** check that the quiz has active questions.
  - Effects on children:
    - A deactivated LessonQuiz disappears from `GET /api/quizzes/for-lesson/{lessonId}` (404).
    - With no active Placement quiz, the placement status becomes `Unavailable`.
    - A deactivated LevelAssessment quiz stops feeding placement.

---

### List questions of a quiz

`GET /api/questions?quizId={quizId}`: called by the Admin dashboard.

- **Auth:** roles: `Admin` (class level).
- **Headers / language:** `Authorization: Bearer <token>`. Not localized; returns the base `questionText` and `optionText`.
- **Path / query parameters:**

| Name | Type | Required | Rules |
|---|---|---|---|
| `quizId` | int | effectively yes | `[FromQuery] int`. If omitted it binds `0` → 404 `"الاختبار رقم 0 غير موجود"`. Non-numeric → 400 `ValidationProblemDetails`. |

- **Request body:** none.
- **Success:** `200 OK`. Bare JSON array of `AdminQuestionResponseDto`, ordered by `displayOrder`. **Inactive questions are included.** Options are nested and ordered by `displayOrder`. A quiz with no questions returns `[]`.

```json
[
  {
    "id": 101,
    "quizId": 15,
    "topicId": 3,
    "questionText": "ما هو الجهد الكهربي؟",
    "questionType": "MultipleChoice",
    "imageUrl": null,
    "imageDescription": null,
    "difficulty": "Easy",
    "displayOrder": 1,
    "points": 1,
    "isActive": true,
    "createdAt": "2026-08-10T09:05:00",
    "options": [
      { "id": 1001, "optionText": "فرق الجهد بين نقطتين", "imageUrl": null, "imageDescription": null, "isCorrect": true, "displayOrder": 1 },
      { "id": 1002, "optionText": "مقاومة مرور التيار", "imageUrl": null, "imageDescription": null, "isCorrect": false, "displayOrder": 2 },
      { "id": 1003, "optionText": "شدة التيار المار", "imageUrl": null, "imageDescription": null, "isCorrect": false, "displayOrder": 3 }
    ]
  },
  {
    "id": 103,
    "quizId": 15,
    "topicId": 3,
    "questionText": "أي مكوّن يظهر في الصورة؟",
    "questionType": "MultipleChoice",
    "imageUrl": "/uploads/lessons/8f1c2b3a-6b4d-4e2a-9c1f-3d7e5a2b1c0d.png",
    "imageDescription": "صورة لمكوّن إلكتروني صغير أسطواني عليه أربعة أشرطة ملونة: أحمر، أحمر، بني، ذهبي.",
    "difficulty": "Medium",
    "displayOrder": 3,
    "points": 2,
    "isActive": true,
    "createdAt": "2026-09-11T09:00:00",
    "options": [
      { "id": 1006, "optionText": "مقاومة", "imageUrl": "/uploads/lessons/2c4e6a8b-1d3f-5a7c-9e1b-4f6a8c0e2d4f.png", "imageDescription": "رمز المقاومة: خط متعرّج بين طرفين.", "isCorrect": true, "displayOrder": 1 },
      { "id": 1007, "optionText": null, "imageUrl": "/uploads/lessons/9b1d3f5a-7c9e-4b2d-8f6a-1c3e5a7b9d0f.png", "imageDescription": "رمز المكثّف: خطان متوازيان قصيران بينهما فراغ.", "isCorrect": false, "displayOrder": 2 },
      { "id": 1008, "optionText": "مكثّف", "imageUrl": null, "imageDescription": null, "isCorrect": false, "displayOrder": 3 }
    ]
  },
  {
    "id": 104,
    "quizId": 15,
    "topicId": 3,
    "questionText": "اشرح بأسلوبك لماذا يضيء المصباح عند غلق الدائرة الكهربية.",
    "questionType": "Essay",
    "imageUrl": null,
    "imageDescription": null,
    "difficulty": "Hard",
    "displayOrder": 4,
    "points": 3,
    "isActive": false,
    "createdAt": "2026-09-12T10:00:00",
    "options": []
  }
]
```

| Field | Notes |
|---|---|
| `topicId` | `int \| null`. Classifies the question for topic statistics and the progress map. `null` when the question has no topic. |
| `imageUrl` | Server-relative path. Prefix it with the API origin to display it (served by `UseStaticFiles`). |
| `imageDescription` | Admin-only. Non-null exactly when `imageUrl` is non-null, except for legacy rows left by migration 001, which may have an image without a description; such questions were deactivated. |
| `displayOrder` | `short`. Unique within the quiz. Not required to start at 1 or be contiguous; `0` and negative values are accepted. |
| `points` | 1–255 (see Points above). |
| `isActive` | `false` = draft/hidden; never served in new attempts. |
| `options` | Always `[]` for `Essay`. For each option, `isCorrect` is the answer key; at most one option per question has `true`. |

- **Errors:**

| Status | When | Example body |
|---|---|---|
| 400 | `quizId` is not an integer | `ValidationProblemDetails` |
| 401 | Missing, invalid or expired token | `{"success":false,"message":"غير مصرح لك بالوصول","data":null}` |
| 403 | Not `Admin` | `{"success":false,"message":"ليس لديك صلاحية","data":null}` |
| 404 | The quiz does not exist, or `quizId` is omitted (binds 0) | `{"success":false,"message":"الاختبار رقم 15 غير موجود","data":null}` |
| 500 | Unexpected server fault | `{"success":false,"message":"حدث خطأ داخلي في الخادم","data":null}` |

- **Frontend notes:**
  - This is the main editor view: one call returns questions and options, with no N+1 queries.
  - Show draft/active badges, and warn on active questions whose options no longer satisfy the activation rules (they can only be in that state through legacy data).
  - Placement quizzes always return `[]`.
  - Mocks: `docs/mocks/mock_admin_questions.json`, `mock_admin_question_truefalse.json`, `mock_admin_question_essay.json`, `mock_admin_question_with_images.json`.

---

### Get question by id

`GET /api/questions/{questionId}`: called by the Admin dashboard.

- **Auth:** roles: `Admin` (class level).
- **Headers / language:** `Authorization: Bearer <token>`. Not localized.
- **Path / query parameters:**

| Name | Type | Required | Rules |
|---|---|---|---|
| `questionId` | int | yes | Route constraint `:int`. |

- **Request body:** none.
- **Success:** `200 OK`. Bare `AdminQuestionResponseDto` (same shape as one element of the list above):

```json
{
  "id": 102,
  "quizId": 15,
  "topicId": 3,
  "questionText": "التيار يسري من الطرف السالب إلى الطرف الموجب داخل الدائرة.",
  "questionType": "TrueFalse",
  "imageUrl": null,
  "imageDescription": null,
  "difficulty": "Medium",
  "displayOrder": 2,
  "points": 2,
  "isActive": true,
  "createdAt": "2026-09-11T09:00:00",
  "options": [
    { "id": 1004, "optionText": "صح", "imageUrl": null, "imageDescription": null, "isCorrect": false, "displayOrder": 1 },
    { "id": 1005, "optionText": "خطأ", "imageUrl": null, "imageDescription": null, "isCorrect": true, "displayOrder": 2 }
  ]
}
```

- **Errors:**

| Status | When | Example body |
|---|---|---|
| 401 | Missing, invalid or expired token | `{"success":false,"message":"غير مصرح لك بالوصول","data":null}` |
| 403 | Not `Admin` | `{"success":false,"message":"ليس لديك صلاحية","data":null}` |
| 404 | No question with this id | `{"success":false,"message":"السؤال رقم 102 غير موجود","data":null}` ("question 102 does not exist") |
| 500 | Unexpected server fault | `{"success":false,"message":"حدث خطأ داخلي في الخادم","data":null}` |

- **Frontend notes:**
  - Use this to refresh one question after option edits: the option endpoints return only the option, not the question's activation readiness.
  - The TrueFalse option texts are ordinary option texts. The API does not require them to be "صح"/"خطأ" ("true"/"false") and does not generate them.
  - Mock: `docs/mocks/mock_admin_question_truefalse.json`.

---

### Create question

`POST /api/questions`: called by the Admin dashboard.

- **Auth:** roles: `Admin` (class level).
- **Headers / language:** `Authorization: Bearer <token>`, `Content-Type: application/json`. Not localized.
- **Path / query parameters:** none.
- **Request body** (`CreateQuestionDto`):

| Field | Type | Required | Rules |
|---|---|---|---|
| `quizId` | int | yes | The quiz must exist (404 otherwise; omitted binds `0`). It **must not be a `Placement` quiz** (400). Any other type is fine, active or inactive. |
| `topicId` | int \| null | no | Omitted or `null` → the question has **no topic**. A given value must be an existing topic, active or not (404 otherwise; `0` is a value, so it is a 404 too). Pick it from `GET /api/assessment/topics`. |
| `questionText` | string | yes | Non-blank (implicit required); trimmed; no length limit. |
| `questionType` | string \| null | no | `MultipleChoice` \| `TrueFalse` \| `Essay` (case-sensitive). Null/blank → `MultipleChoice`. |
| `imageUrl` | string \| null | no | Use the `data.url` from `POST /api/content/media/images`. Trimmed; blank → no image. Not validated beyond that. The column is `NVARCHAR(500)`, and a longer value surfaces as a 500. |
| `imageDescription` | string \| null | **required when `imageUrl` is set** | Non-blank, trimmed, ≤ 1000 characters. Ignored and stored as `null` when there is no image. Never shown to children. |
| `difficulty` | string | yes | `Easy` \| `Medium` \| `Hard` \| `Advanced` (case-sensitive). The DTO property is a non-nullable `string`, so missing/blank is rejected by model validation (400 `ValidationProblemDetails`). The service's "blank → `Medium`" fallback is not reachable over HTTP. |
| `displayOrder` | short | yes | −32768…32767; must be unused among this quiz's questions, **including inactive ones**. Omitted binds `0`. |
| `points` | byte | no | 0–255 (outside that range → `ValidationProblemDetails`). `0` or omitted → **1**. |

There is no `isActive` field: a new question is always created **inactive**.

Validation order in `QuestionService.CreateQuestionAsync`:
1. Quiz exists.
2. Not a Placement quiz.
3. Topic exists (only when `topicId` is given).
4. Text.
5. Difficulty.
6. Type.
7. Points.
8. Image + description.
9. `displayOrder` free.
10. Save.

```json
{
  "quizId": 15,
  "topicId": 3,
  "questionText": "أي مكوّن يظهر في الصورة؟",
  "questionType": "MultipleChoice",
  "imageUrl": "/uploads/lessons/8f1c2b3a-6b4d-4e2a-9c1f-3d7e5a2b1c0d.png",
  "imageDescription": "صورة لمكوّن إلكتروني صغير أسطواني عليه أربعة أشرطة ملونة: أحمر، أحمر، بني، ذهبي.",
  "difficulty": "Medium",
  "displayOrder": 3,
  "points": 2
}
```

- **Success:** `201 Created`. Header `Location: /api/questions/{id}`. The body is the bare `AdminQuestionResponseDto`, re-read from the database:

```json
{
  "id": 103,
  "quizId": 15,
  "topicId": 3,
  "questionText": "أي مكوّن يظهر في الصورة؟",
  "questionType": "MultipleChoice",
  "imageUrl": "/uploads/lessons/8f1c2b3a-6b4d-4e2a-9c1f-3d7e5a2b1c0d.png",
  "imageDescription": "صورة لمكوّن إلكتروني صغير أسطواني عليه أربعة أشرطة ملونة: أحمر، أحمر، بني، ذهبي.",
  "difficulty": "Medium",
  "displayOrder": 3,
  "points": 2,
  "isActive": false,
  "createdAt": "2026-09-13T08:45:00.512",
  "options": []
}
```

- **Errors:**

| Status | When | Example body |
|---|---|---|
| 400 | Missing/blank `questionText` or `difficulty`; `points` outside 0–255; `displayOrder` outside the `short` range; malformed JSON | `ValidationProblemDetails` |
| 400 | The quiz is a `Placement` quiz | `{"success":false,"message":"اختبار تحديد المستوى يأخذ أسئلته من اختبارات تقييم المستويات، أضف السؤال إلى اختبار تقييم المستوى المناسب","data":null}` ("the placement test takes its questions from the level assessment quizzes; add the question to the appropriate level assessment quiz") |
| 400 | Unknown `difficulty` | `{"success":false,"message":"مستوى الصعوبة 'hard' غير صالح","data":null}` ("difficulty 'hard' is invalid") |
| 400 | Unknown `questionType` | `{"success":false,"message":"نوع السؤال 'Mcq' غير صالح","data":null}` ("question type 'Mcq' is invalid") |
| 400 | `imageUrl` set but `imageDescription` null or blank | `{"success":false,"message":"السؤال الذي يحتوي على صورة يجب أن يحتوي على وصف للصورة حتى يتمكن النظام من فهم السؤال","data":null}` ("a question with an image must have an image description so the system can understand the question") |
| 400 | `imageDescription` longer than 1000 characters after trimming | `{"success":false,"message":"وصف الصورة لا يتجاوز 1000 حرف","data":null}` ("the image description must not exceed 1000 characters") |
| 400 | `displayOrder` already used in this quiz (pre-check) | `{"success":false,"message":"الترتيب 3 مستخدم بالفعل في الاختبار رقم 15","data":null}` ("order 3 is already used in quiz 15") |
| 401 | Missing, invalid or expired token | `{"success":false,"message":"غير مصرح لك بالوصول","data":null}` |
| 403 | Not `Admin` | `{"success":false,"message":"ليس لديك صلاحية","data":null}` |
| 404 | The quiz does not exist | `{"success":false,"message":"الاختبار رقم 15 غير موجود","data":null}` |
| 404 | `topicId` is given and the topic does not exist | `{"success":false,"message":"الموضوع رقم 3 غير موجود","data":null}` ("topic 3 does not exist") |
| 409 | Another admin took the same `displayOrder` between the pre-check and the save (`UQ_Questions_QuizId_DisplayOrder`) | `{"success":false,"message":"الترتيب 3 مستخدم بالفعل في الاختبار رقم 15","data":null}` |
| 500 | Unexpected server fault, e.g. `imageUrl` longer than 500 characters | `{"success":false,"message":"حدث خطأ داخلي في الخادم","data":null}` |

- **Frontend notes:**
  - **Display order: 400 vs 409.** Both carry the same message. 400 means the order was already taken when the request arrived. 409 means a concurrent request won the race. Handle both the same way: show the message, refetch `GET /api/questions?quizId=…`, and suggest `max(displayOrder) + 1`. Compute that over **all** questions, inactive ones included.
  - Not idempotent. A double submit either creates two questions (different orders) or gets 400/409 (same order). Disable the button while the request is in flight.
  - **Recommended flow:**
    1. Upload the image, if any.
    2. `POST /api/questions`.
    3. For MultipleChoice/TrueFalse, add options with `POST /api/question-options`.
    4. Publish with `PATCH /api/questions/{id}/active?isActive=true`.
  - For `Essay`, skip step 3. `points` is the essay's maximum grade.
  - Make `imageDescription` a required field in the form as soon as an image is attached, with a 1000-character counter. Explain that it is what the AI "sees".
  - Fill the topic picker from [`GET /api/assessment/topics?isActive=true`](#list-assessment-topics), and offer a "no topic" choice that sends `topicId: null`. A question with no topic is asked, graded and earns XP, but counts toward no topic on the [progress map](#my-topic-statistics).
  - Mocks: `docs/mocks/mock_admin_question_create_request.json`, `mock_admin_question_image_description_required.json`, `mock_admin_conflict.json` (the 409 race), `mock_admin_question_without_topic.json` (a question with `topicId: null`), `mock_topic_not_found.json`.

---

### Update question

`PUT /api/questions/{questionId}`: called by the Admin dashboard.

- **Auth:** roles: `Admin` (class level).
- **Headers / language:** `Authorization: Bearer <token>`, `Content-Type: application/json`. Not localized.
- **Path / query parameters:**

| Name | Type | Required | Rules |
|---|---|---|---|
| `questionId` | int | yes | Route constraint `:int`. |

- **Request body** (`UpdateQuestionDto`). This is a **full replacement**: every omitted field is overwritten with its default.

| Field | Type | Required | Rules |
|---|---|---|---|
| `topicId` | int \| null | no | **Null or omitted clears the topic** (full replacement, like every other field). A non-null value that differs from the current topic must exist (404); an unchanged value is not re-checked. |
| `questionText` | string | yes | Non-blank; trimmed. |
| `questionType` | string \| null | no | `MultipleChoice` \| `TrueFalse` \| `Essay`. **Null/blank keeps the current type.** Changing type is allowed; the new type is validated against the existing options only when `isActive` is `true`. |
| `imageUrl` | string \| null | no | **Null/blank removes the image, and its description is cleared too.** Send the current value to keep it. |
| `imageDescription` | string \| null | required when `imageUrl` is set | Same rules as create. |
| `difficulty` | string | yes | `Easy` \| `Medium` \| `Hard` \| `Advanced`. |
| `displayOrder` | short | yes | If changed, it must be free among the quiz's other questions. **Omitted binds `0`.** |
| `points` | byte | no | `0`/omitted → **resets to 1**. |
| `isActive` | bool | effectively yes | `true` runs the full activation check (see below) on every save, even if the question was already active. `false` or **omitted deactivates it** without any check. |

`quizId` is not part of the DTO: a question can never move to another quiz. When `isActive` is `true`, `EnsureAnswerableAsync` checks the **type being saved** against the options currently stored: counts, exactly one correct, no options for Essay, and every option image described.

```json
{
  "topicId": 3,
  "questionText": "أي مكوّن يظهر في الصورة؟",
  "questionType": "MultipleChoice",
  "imageUrl": "/uploads/lessons/8f1c2b3a-6b4d-4e2a-9c1f-3d7e5a2b1c0d.png",
  "imageDescription": "صورة لمقاومة كهربية أسطوانية عليها أربعة أشرطة ملونة: أحمر، أحمر، بني، ذهبي.",
  "difficulty": "Medium",
  "displayOrder": 3,
  "points": 3,
  "isActive": true
}
```

- **Success:** `200 OK`. Bare `AdminQuestionResponseDto` with its options, re-read from the database. It has the same shape as "Get question by id" and reflects the saved values (for example `"points": 3`, `"isActive": true`).
- **Errors:**

| Status | When | Example body |
|---|---|---|
| 400 | Missing/blank `questionText` or `difficulty`; values out of range; malformed JSON | `ValidationProblemDetails` |
| 400 | Unknown `difficulty` / `questionType` | `{"success":false,"message":"مستوى الصعوبة 'hard' غير صالح","data":null}` / `{"success":false,"message":"نوع السؤال 'Mcq' غير صالح","data":null}` |
| 400 | Image without description, or description > 1000 characters | `{"success":false,"message":"السؤال الذي يحتوي على صورة يجب أن يحتوي على وصف للصورة حتى يتمكن النظام من فهم السؤال","data":null}` / `{"success":false,"message":"وصف الصورة لا يتجاوز 1000 حرف","data":null}` |
| 400 | Changed `displayOrder` already used in this quiz | `{"success":false,"message":"الترتيب 2 مستخدم بالفعل في الاختبار رقم 15","data":null}` |
| 400 | `isActive: true` and type `Essay` but options exist | `{"success":false,"message":"السؤال المقالي رقم 104 لا يجب أن يحتوي على اختيارات","data":null}` ("essay question 104 must not have options") |
| 400 | `isActive: true`, `TrueFalse`, option count ≠ 2 | `{"success":false,"message":"سؤال الصح والخطأ رقم 102 يجب أن يحتوي على اختيارين بالضبط","data":null}` ("true/false question 102 must have exactly two options") |
| 400 | `isActive: true`, `MultipleChoice`, fewer than 2 options | `{"success":false,"message":"السؤال رقم 101 يجب أن يحتوي على اختيارين على الأقل قبل تفعيله","data":null}` ("question 101 must have at least two options before activation") |
| 400 | `isActive: true`, MultipleChoice/TrueFalse, correct options ≠ 1 | `{"success":false,"message":"السؤال رقم 101 يجب أن يحتوي على إجابة صحيحة واحدة بالضبط قبل تفعيله","data":null}` ("question 101 must have exactly one correct answer before activation") |
| 400 | `isActive: true` and some option images have no description (legacy rows) | `{"success":false,"message":"لا يمكن تفعيل السؤال رقم 103: صور الاختيارات رقم 1006، 1007 بدون وصف، أضف وصفًا لكل صورة أولًا","data":null}` ("cannot activate question 103: option images 1006, 1007 have no description; add one to each image first") |
| 401 | Missing, invalid or expired token | `{"success":false,"message":"غير مصرح لك بالوصول","data":null}` |
| 403 | Not `Admin` | `{"success":false,"message":"ليس لديك صلاحية","data":null}` |
| 404 | The question does not exist | `{"success":false,"message":"السؤال رقم 103 غير موجود","data":null}` |
| 404 | A changed, non-null `topicId` does not exist | `{"success":false,"message":"الموضوع رقم 9 غير موجود","data":null}` |
| 409 | Lost the `displayOrder` race at the database | `{"success":false,"message":"الترتيب 2 مستخدم بالفعل في الاختبار رقم 15","data":null}` |
| 500 | Unexpected server fault (e.g. `imageUrl` > 500 characters) | `{"success":false,"message":"حدث خطأ داخلي في الخادم","data":null}` |

- **Frontend notes:**
  - **Always send the whole object**, pre-filled from `GET /api/questions/{id}`. Omitting any of these silently changes the question:
    - `isActive` → deactivates.
    - `points` → becomes 1.
    - `displayOrder` → 0.
    - `imageUrl` → the image is removed.
    - `topicId` → the topic is removed.
  - To swap two questions' orders, use three PUTs through a temporary free value (A → free, B → A's old value, A → B's old value). The unique constraint forbids a direct swap, and there is no swap endpoint for questions.
  - **Editing an active question is safe for children mid-quiz.** An attempt that has already started keeps its frozen `QuizAttemptQuestions` row (type, correct option, points, topic or no topic, difficulty). Changes to `points`, `questionType`, `topicId` or the answer key apply to attempts started afterwards.
  - The type can only be changed to one the existing options satisfy, or to anything while saving with `isActive: false`. To turn an option-based question into an Essay, all its options must be deleted first, and that is impossible once an attempt has referenced them (see "Delete option").
  - Replacing or removing an image does not delete the old file from `wwwroot/uploads/lessons`.
  - Mocks: `docs/mocks/mock_truefalse_wrong_option_count.json`, `mock_admin_question_image_description_required.json`, `mock_admin_question_activation_undescribed_images.json`.

---

### Activate / deactivate question

`PATCH /api/questions/{questionId}/active?isActive={true|false}`: called by the Admin dashboard.

- **Auth:** roles: `Admin` (class level).
- **Headers / language:** `Authorization: Bearer <token>`. Not localized.
- **Path / query parameters:**

| Name | Type | Required | Rules |
|---|---|---|---|
| `questionId` | int | yes | Route constraint `:int`. |
| `isActive` | bool | effectively yes | `[FromQuery] bool`. **If omitted it binds `false`, so the call deactivates.** A non-boolean value → 400 `ValidationProblemDetails`. |

- **Request body:** none.
- **Success:** `204 No Content`, empty body. If the question is already in the requested state, nothing happens and no check runs (still 204). Deactivation never runs checks.
- **Errors** (activation checks run only when switching from inactive to active, against the **stored** image, type and options):

| Status | When | Example body |
|---|---|---|
| 400 | `isActive` is not a boolean | `ValidationProblemDetails` |
| 400 | The stored question image has no description (legacy row, e.g. one deactivated by migration 001) | `{"success":false,"message":"لا يمكن تفعيل السؤال رقم 103: صورة السؤال بدون وصف، أضف وصفًا للصورة أولًا","data":null}` ("cannot activate question 103: the question image has no description; add one first") |
| 400 | `Essay` with options | `{"success":false,"message":"السؤال المقالي رقم 104 لا يجب أن يحتوي على اختيارات","data":null}` |
| 400 | `TrueFalse` without exactly 2 options | `{"success":false,"message":"سؤال الصح والخطأ رقم 102 يجب أن يحتوي على اختيارين بالضبط","data":null}` |
| 400 | `MultipleChoice` with fewer than 2 options | `{"success":false,"message":"السؤال رقم 101 يجب أن يحتوي على اختيارين على الأقل قبل تفعيله","data":null}` |
| 400 | MultipleChoice/TrueFalse without exactly one correct option | `{"success":false,"message":"السؤال رقم 101 يجب أن يحتوي على إجابة صحيحة واحدة بالضبط قبل تفعيله","data":null}` |
| 400 | Option images without description (lists the option ids in display order) | `{"success":false,"message":"لا يمكن تفعيل السؤال رقم 103: صور الاختيارات رقم 1006، 1007 بدون وصف، أضف وصفًا لكل صورة أولًا","data":null}` |
| 401 | Missing, invalid or expired token | `{"success":false,"message":"غير مصرح لك بالوصول","data":null}` |
| 403 | Not `Admin` | `{"success":false,"message":"ليس لديك صلاحية","data":null}` |
| 404 | The question does not exist | `{"success":false,"message":"السؤال رقم 103 غير موجود","data":null}` |
| 500 | Unexpected server fault | `{"success":false,"message":"حدث خطأ داخلي في الخادم","data":null}` |

- **Frontend notes:**
  - Idempotent and safe to retry.
  - After 204, flip the badge locally or refetch the question.
  - Show the activation rules as a checklist next to the "Publish" button: option count, one correct answer, every image described. Most 400s can then be prevented before the call.
  - To fix a legacy question whose image lacks a description, `PUT` it with the description. A PUT with `isActive: true` validates and activates in one step. Mock: `docs/mocks/mock_admin_question_activation_undescribed_images.json`.
  - **Effects on children:**
    - An active question is served in new attempts of its quiz.
    - Deactivating removes it from new attempts and from retries (`StartRetryAttemptAsync` filters `IsActive`).
    - Attempts already started keep it.
    - An active, non-Essay question with a correct option in the newest active `LevelAssessment` quiz of a level can be sampled by placement.

---

### List options of a question

`GET /api/question-options?questionId={questionId}`: called by the Admin dashboard.

- **Auth:** roles: `Admin` (class level).
- **Headers / language:** `Authorization: Bearer <token>`. Not localized; returns the base `optionText`.
- **Path / query parameters:**

| Name | Type | Required | Rules |
|---|---|---|---|
| `questionId` | int | effectively yes | `[FromQuery] int`. Omitted binds `0` → 404 `"السؤال رقم 0 غير موجود"`. Non-numeric → 400 `ValidationProblemDetails`. |

- **Request body:** none.
- **Success:** `200 OK`. Bare JSON array of `AdminQuestionOptionResponseDto`, ordered by `displayOrder`; `[]` for an Essay or a question with no options yet.

```json
[
  {
    "id": 1006,
    "optionText": "مقاومة",
    "imageUrl": "/uploads/lessons/2c4e6a8b-1d3f-5a7c-9e1b-4f6a8c0e2d4f.png",
    "imageDescription": "رمز المقاومة: خط متعرّج بين طرفين.",
    "isCorrect": true,
    "displayOrder": 1
  },
  {
    "id": 1007,
    "optionText": null,
    "imageUrl": "/uploads/lessons/9b1d3f5a-7c9e-4b2d-8f6a-1c3e5a7b9d0f.png",
    "imageDescription": "رمز المكثّف: خطان متوازيان قصيران بينهما فراغ.",
    "isCorrect": false,
    "displayOrder": 2
  }
]
```

| Field | Notes |
|---|---|
| `optionText` | `null` for an image-only option. |
| `imageDescription` | Admin-only; non-null whenever `imageUrl` is non-null (except unfixed legacy rows). |
| `isCorrect` | At most one `true` per question (`UQ_QuestionOptions_OneCorrectPerQuestion`). |
| (absent) | The option DTO has no `questionId` and no `createdAt`. |

- **Errors:**

| Status | When | Example body |
|---|---|---|
| 400 | `questionId` is not an integer | `ValidationProblemDetails` |
| 401 | Missing, invalid or expired token | `{"success":false,"message":"غير مصرح لك بالوصول","data":null}` |
| 403 | Not `Admin` | `{"success":false,"message":"ليس لديك صلاحية","data":null}` |
| 404 | The question does not exist, or `questionId` is omitted | `{"success":false,"message":"السؤال رقم 103 غير موجود","data":null}` |
| 500 | Unexpected server fault | `{"success":false,"message":"حدث خطأ داخلي في الخادم","data":null}` |

- **Frontend notes:** The same options already come nested in `GET /api/questions` and `GET /api/questions/{id}`, so this call is only needed to refresh one question's options. There is no "get single option" endpoint.

---

### Create option

`POST /api/question-options`: called by the Admin dashboard.

- **Auth:** roles: `Admin` (class level).
- **Headers / language:** `Authorization: Bearer <token>`, `Content-Type: application/json`. Not localized.
- **Path / query parameters:** none.
- **Request body** (`CreateQuestionOptionDto`):

| Field | Type | Required | Rules |
|---|---|---|---|
| `questionId` | int | yes | The question must exist (404). It must **not** be an `Essay`. For a `TrueFalse` question, it must currently have fewer than 2 options. |
| `optionText` | string \| null | one of text/image | Trimmed; blank → `null`. No length limit. |
| `imageUrl` | string \| null | one of text/image | From `POST /api/content/media/images`. Trimmed; blank → no image. Not validated beyond that (a value over 500 characters → 500). |
| `imageDescription` | string \| null | **required when `imageUrl` is set, even if `optionText` is also set** | Non-blank, trimmed, ≤ 1000 characters. Silently dropped when there is no image. |
| `isCorrect` | bool | no | Default `false`. `true` is refused if another option of the question is already correct. |
| `displayOrder` | short | yes | Must be unused among this question's options. Omitted binds `0`. |

The question may be active or inactive. Adding a non-correct option to an active MultipleChoice question is allowed.

Validation order in `QuestionOptionService.CreateOptionAsync`:
1. Question exists.
2. Text/image/description.
3. Question accepts options (not Essay; TrueFalse has < 2).
4. `displayOrder` free.
5. No other correct option (only if `isCorrect`).
6. Save.

```json
{
  "questionId": 103,
  "optionText": null,
  "imageUrl": "/uploads/lessons/9b1d3f5a-7c9e-4b2d-8f6a-1c3e5a7b9d0f.png",
  "imageDescription": "رمز المكثّف: خطان متوازيان قصيران بينهما فراغ.",
  "isCorrect": false,
  "displayOrder": 2
}
```

- **Success:** `201 Created`. The `Location` header points to the **list** endpoint `/api/question-options?questionId={questionId}`, not to the new option: there is no single-option GET. Body is a bare `AdminQuestionOptionResponseDto`:

```json
{
  "id": 1007,
  "optionText": null,
  "imageUrl": "/uploads/lessons/9b1d3f5a-7c9e-4b2d-8f6a-1c3e5a7b9d0f.png",
  "imageDescription": "رمز المكثّف: خطان متوازيان قصيران بينهما فراغ.",
  "isCorrect": false,
  "displayOrder": 2
}
```

- **Errors:**

| Status | When | Example body |
|---|---|---|
| 400 | Malformed JSON / value out of range | `ValidationProblemDetails` |
| 400 | Neither text nor image | `{"success":false,"message":"الاختيار يجب أن يحتوي على نص أو صورة على الأقل","data":null}` ("an option must have text or an image, at least one") |
| 400 | Image without description | `{"success":false,"message":"الاختيار الذي يحتوي على صورة يجب أن يحتوي على وصف للصورة حتى يتمكن النظام من تحليل إجابة الطالب","data":null}` ("an option with an image must have an image description so the system can analyse the student's answer") |
| 400 | Description > 1000 characters | `{"success":false,"message":"وصف الصورة لا يتجاوز 1000 حرف","data":null}` |
| 400 | The question is an Essay | `{"success":false,"message":"السؤال رقم 104 سؤال مقالي ولا يقبل اختيارات","data":null}` ("question 104 is an essay question and does not accept options") |
| 400 | TrueFalse question already has 2 options | `{"success":false,"message":"سؤال الصح والخطأ رقم 102 له اختياران بالفعل","data":null}` ("true/false question 102 already has two options") |
| 400 | `displayOrder` already used on this question (pre-check) | `{"success":false,"message":"الترتيب 2 مستخدم بالفعل في السؤال رقم 103","data":null}` ("order 2 is already used in question 103") |
| 400 | `isCorrect: true` but another option is already correct (pre-check) | `{"success":false,"message":"السؤال رقم 103 له إجابة صحيحة بالفعل","data":null}` ("question 103 already has a correct answer") |
| 401 | Missing, invalid or expired token | `{"success":false,"message":"غير مصرح لك بالوصول","data":null}` |
| 403 | Not `Admin` | `{"success":false,"message":"ليس لديك صلاحية","data":null}` |
| 404 | The question does not exist | `{"success":false,"message":"السؤال رقم 103 غير موجود","data":null}` |
| 409 | Lost a race: same `displayOrder` (`UQ_QuestionOptions_QuestionId_DisplayOrder`) | `{"success":false,"message":"الترتيب 2 مستخدم بالفعل في السؤال رقم 103","data":null}` |
| 409 | Lost a race: a second correct option (`UQ_QuestionOptions_OneCorrectPerQuestion`) | `{"success":false,"message":"السؤال رقم 103 له إجابة صحيحة بالفعل","data":null}` |
| 500 | Unexpected server fault (e.g. `imageUrl` > 500 characters) | `{"success":false,"message":"حدث خطأ داخلي في الخادم","data":null}` |

- **Frontend notes:**
  - Not idempotent. Disable the button while the request is in flight. On 400 or 409 about the order, refetch the options and suggest `max(displayOrder) + 1`. The 400 and 409 variants carry identical messages; treat them the same.
  - Adding the option does not activate the question. After the last option, call `PATCH /api/questions/{id}/active?isActive=true`.
  - For a TrueFalse question, stop offering "add option" once it has 2.
  - For an image option, require the description even when there is a text label ("A", "B"). The AI uses the description to understand what the child picked.
  - Mocks: `docs/mocks/mock_admin_option_create_request.json`, `mock_admin_option_image_description_required.json`, `mock_option_neither_text_nor_image.json`, `mock_option_on_essay_question.json`.

---

### Update option

`PUT /api/question-options/{optionId}`: called by the Admin dashboard.

- **Auth:** roles: `Admin` (class level).
- **Headers / language:** `Authorization: Bearer <token>`, `Content-Type: application/json`. Not localized.
- **Path / query parameters:**

| Name | Type | Required | Rules |
|---|---|---|---|
| `optionId` | int | yes | Route constraint `:int`. |

- **Request body** (`UpdateQuestionOptionDto`). This is a **full replacement**; the option always stays on its question.

| Field | Type | Required | Rules |
|---|---|---|---|
| `optionText` | string \| null | one of text/image | As on create. Null/blank removes the text. |
| `imageUrl` | string \| null | one of text/image | As on create. **Null/blank removes the image and clears its description.** |
| `imageDescription` | string \| null | required when `imageUrl` is set | Non-blank, trimmed, ≤ 1000 characters. |
| `isCorrect` | bool | effectively yes | **Omitted binds `false`.** Rules:<br>- Turning it `true` is refused if another option is correct.<br>- Turning the only correct option `false` is refused while the question is **active**. |
| `displayOrder` | short | yes | If changed, it must be free on this question. **Omitted binds `0`.** |

Validation order in `QuestionOptionService.UpdateOptionAsync`:
1. Option exists.
2. Text/image/description.
3. `displayOrder` free (only if changed).
4. No other correct option (only when changing to correct).
5. Not removing the only correct answer of an active question.
6. Save.

```json
{
  "optionText": "مكثّف",
  "imageUrl": "/uploads/lessons/9b1d3f5a-7c9e-4b2d-8f6a-1c3e5a7b9d0f.png",
  "imageDescription": "رمز المكثّف: خطان متوازيان قصيران بينهما فراغ.",
  "isCorrect": false,
  "displayOrder": 2
}
```

- **Success:** `200 OK`. Bare `AdminQuestionOptionResponseDto`:

```json
{
  "id": 1007,
  "optionText": "مكثّف",
  "imageUrl": "/uploads/lessons/9b1d3f5a-7c9e-4b2d-8f6a-1c3e5a7b9d0f.png",
  "imageDescription": "رمز المكثّف: خطان متوازيان قصيران بينهما فراغ.",
  "isCorrect": false,
  "displayOrder": 2
}
```

- **Errors:**

| Status | When | Example body |
|---|---|---|
| 400 | Malformed JSON / value out of range | `ValidationProblemDetails` |
| 400 | Neither text nor image | `{"success":false,"message":"الاختيار يجب أن يحتوي على نص أو صورة على الأقل","data":null}` |
| 400 | Image without description | `{"success":false,"message":"الاختيار الذي يحتوي على صورة يجب أن يحتوي على وصف للصورة حتى يتمكن النظام من تحليل إجابة الطالب","data":null}` |
| 400 | Description > 1000 characters | `{"success":false,"message":"وصف الصورة لا يتجاوز 1000 حرف","data":null}` |
| 400 | Changed `displayOrder` already used | `{"success":false,"message":"الترتيب 1 مستخدم بالفعل في السؤال رقم 103","data":null}` |
| 400 | Setting `isCorrect: true` while another option is correct | `{"success":false,"message":"السؤال رقم 103 له إجابة صحيحة بالفعل","data":null}` |
| 400 | Setting `isCorrect: false` (or omitting it) on the only correct option of an active question | `{"success":false,"message":"لا يمكن إلغاء الإجابة الصحيحة الوحيدة من السؤال رقم 103 وهو مفعّل","data":null}` ("cannot unset the only correct answer of question 103 while it is active") |
| 401 | Missing, invalid or expired token | `{"success":false,"message":"غير مصرح لك بالوصول","data":null}` |
| 403 | Not `Admin` | `{"success":false,"message":"ليس لديك صلاحية","data":null}` |
| 404 | The option does not exist | `{"success":false,"message":"الاختيار رقم 1007 غير موجود","data":null}` ("option 1007 does not exist") |
| 409 | Lost a race on `displayOrder` or on the one-correct index | `{"success":false,"message":"الترتيب 1 مستخدم بالفعل في السؤال رقم 103","data":null}` / `{"success":false,"message":"السؤال رقم 103 له إجابة صحيحة بالفعل","data":null}` |
| 500 | Unexpected server fault (e.g. `imageUrl` > 500 characters) | `{"success":false,"message":"حدث خطأ داخلي في الخادم","data":null}` |

- **Frontend notes:**
  - **Always send the whole option**, pre-filled from the loaded data. Omitting `isCorrect` unmarks it (or fails on an active question); omitting `imageUrl` removes the picture; omitting `displayOrder` moves it to `0`.
  - **Changing which option is correct:**
    1. Deactivate the question: `PATCH /api/questions/{id}/active?isActive=false`.
    2. Set the old correct option to `isCorrect: false`.
    3. Set the new one to `isCorrect: true`.
    4. Reactivate the question.

    Order matters, because at most one option may be correct at any moment. While the question is active, both single-step paths are refused (unset → "only correct answer", set → "already has a correct answer"). Offer this as one "change correct answer" action and warn that the question is hidden from new attempts while it runs.
  - Attempts already started keep their frozen correct option id (`QuizAttemptQuestions.CorrectOptionId`). Option text and images are **not** frozen per attempt, so past wrong answers that point at this option show the edited content wherever they are rendered from live options.
  - Swapping orders needs a temporary free value, as for questions.
  - Mocks: `docs/mocks/mock_admin_option_image_description_required.json`, `mock_option_neither_text_nor_image.json`.

---

### Delete option

`DELETE /api/question-options/{optionId}`: called by the Admin dashboard.

- **Auth:** roles: `Admin` (class level).
- **Headers / language:** `Authorization: Bearer <token>`. Not localized.
- **Path / query parameters:**

| Name | Type | Required | Rules |
|---|---|---|---|
| `optionId` | int | yes | Route constraint `:int`. |

- **Request body:** none.
- **Success:** `204 No Content`, empty body. The option and its translations (`ON DELETE CASCADE`) are removed permanently. The image file is not deleted from disk.
- **Errors** (checked in this order by `QuestionOptionService.DeleteOptionAsync`):

| Status | When | Example body |
|---|---|---|
| 401 | Missing, invalid or expired token | `{"success":false,"message":"غير مصرح لك بالوصول","data":null}` |
| 403 | Not `Admin` | `{"success":false,"message":"ليس لديك صلاحية","data":null}` |
| 404 | The option does not exist (including a second DELETE of the same id) | `{"success":false,"message":"الاختيار رقم 1007 غير موجود","data":null}` |
| 400 | A child once picked this option as a **wrong** answer (`QuizAttemptMistakes.SelectedOptionId`) | `{"success":false,"message":"لا يمكن حذف الاختيار رقم 1007 لأنه مستخدم في محاولات سابقة","data":null}` ("cannot delete option 1007 because it is used in previous attempts") |
| 400 | This option was the answer key frozen into any attempt (`QuizAttemptQuestions.CorrectOptionId`). Any attempt that included the question while this option was correct counts, whatever the child answered. | `{"success":false,"message":"لا يمكن حذف الاختيار رقم 1006 لأنه الإجابة الصحيحة المسجّلة في محاولات سابقة","data":null}` ("cannot delete option 1006 because it is the correct answer recorded in previous attempts") |
| 400 | The question is active and has 2 or fewer options. This applies to MultipleChoice too. | `{"success":false,"message":"لا يمكن حذف الاختيار رقم 1004: السؤال رقم 102 مفعّل ويحتاج إلى اختيارين على الأقل","data":null}` ("cannot delete option 1004: question 102 is active and needs at least two options") |
| 400 | The question is active and this is its only correct option | `{"success":false,"message":"لا يمكن حذف الإجابة الصحيحة الوحيدة من السؤال رقم 103 وهو مفعّل","data":null}` ("cannot delete the only correct answer of question 103 while it is active") |
| 500 | Unexpected server fault | `{"success":false,"message":"حدث خطأ داخلي في الخادم","data":null}` |

- **Frontend notes:**
  - **Why deletes are refused:** attempt history. Wrong answers point at the option the child chose, and every attempt freezes the correct option id. Deleting either would break past results, so those options can never be deleted. Editing them (`PUT`) is still allowed. Where the goal is to "remove" a wrong option from a question with history, change its text instead, or deactivate the question and author a new one.
  - While the question is active it must stay answerable, so deactivate it first to drop below 3 options or to remove the correct one.
  - A retry after a 204 returns 404. Treat 404 after a delete as "already gone" and remove the row from the UI.
  - Ask for confirmation: the delete cannot be undone.

---

### List assessment categories

`GET /api/assessment/categories?isActive={true|false}`: called by the Admin dashboard (the category list, and the category picker of the topic form).

- **Auth:** roles: `Admin` (class-level `[Authorize(Roles = "Admin")]` on `AssessmentCategoryController`).
- **Headers / language:** `Authorization: Bearer <token>`. Not localized: `name` is the base column (`Categories.Name`), and `translations` lists every stored translation.
- **Path / query parameters:**

| Name | Type | Required | Rules |
|---|---|---|---|
| `isActive` | bool | no | `true` or `false` filters on the flag; omitted returns every category. A non-boolean value → 400 `ValidationProblemDetails`. |

- **Request body:** none.
- **Success:** `200 OK`. Bare JSON array of `AdminCategoryResponseDto`, ordered by `sortOrder`, then `id` (`CategoryService.GetAllAsync`). `[]` when nothing matches. No paging.

```json
[
  {
    "id": 1,
    "name": "أساسيات الكهرباء",
    "sortOrder": 1,
    "isActive": true,
    "topicsCount": 3,
    "translations": [
      { "languageCode": "ar", "name": "أساسيات الكهرباء" },
      { "languageCode": "en", "name": "Electricity basics" }
    ]
  },
  {
    "id": 2,
    "name": "المكونات الإلكترونية",
    "sortOrder": 2,
    "isActive": true,
    "topicsCount": 3,
    "translations": [
      { "languageCode": "ar", "name": "المكونات الإلكترونية" },
      { "languageCode": "en", "name": "Electronic components" }
    ]
  }
]
```

| Field | Notes |
|---|---|
| `id` | `byte` (1–255), assigned by the server on create. |
| `name` | Base name, at most 100 characters, unique among categories. The child sees it when a language has no translation. |
| `sortOrder` | `short`. Position on the child's progress map, ascending. Not required to be unique or contiguous. |
| `isActive` | `false` takes the category's topics off the progress map of children who never practised them. |
| `topicsCount` | Topics in this category, active or not. |
| `translations` | At most one per language (`ar`, `en`), ordered by `languageCode`. |

- **Errors:**

| Status | When | Example body |
|---|---|---|
| 400 | `isActive` is not a boolean | `ValidationProblemDetails` |
| 401 | Missing, invalid or expired token | `{"success":false,"message":"غير مصرح لك بالوصول","data":null}` |
| 403 | Not `Admin` | `{"success":false,"message":"ليس لديك صلاحية","data":null}` |
| 500 | Unexpected server fault | `{"success":false,"message":"حدث خطأ داخلي في الخادم","data":null}` |

- **Frontend notes:**
  - A category is not a Content level (`/api/content/levels`): a level is where something is taught, a category is what questions are about. It groups topics on the child's progress map.
  - There is no delete endpoint. Deactivate instead; `FK_Topics_Categories` would refuse deleting a category that has topics anyway.
  - Mock: `docs/mocks/mock_admin_categories.json`.

---

### Get assessment category

`GET /api/assessment/categories/{categoryId}`: called by the Admin dashboard (category editor).

- **Auth:** roles: `Admin` (class level).
- **Headers / language:** `Authorization: Bearer <token>`. Not localized.
- **Path / query parameters:**

| Name | Type | Required | Rules |
|---|---|---|---|
| `categoryId` | byte (path) | yes | Route constraint `{categoryId:int:range(1,255)}`. A value outside 1–255, or not a number, matches no route: 404 with an empty body. |

- **Request body:** none.
- **Success:** `200 OK`. Bare `AdminCategoryResponseDto`, the same shape as one element of the list above.
- **Errors:**

| Status | When | Example body |
|---|---|---|
| 401 | Missing, invalid or expired token | `{"success":false,"message":"غير مصرح لك بالوصول","data":null}` |
| 403 | Not `Admin` | `{"success":false,"message":"ليس لديك صلاحية","data":null}` |
| 404 | No category with this id | `{"success":false,"message":"التصنيف رقم 9 غير موجود","data":null}` ("category 9 does not exist") |
| 404 | `categoryId` outside 1–255 or not a number | Empty body (no route matched) |
| 500 | Unexpected server fault | `{"success":false,"message":"حدث خطأ داخلي في الخادم","data":null}` |

- **Frontend notes:**
  - Pre-fill the edit form from this response: `PUT` replaces every field.

---

### Create assessment category

`POST /api/assessment/categories`: called by the Admin dashboard.

- **Auth:** roles: `Admin` (class level).
- **Headers / language:** `Authorization: Bearer <token>`, `Content-Type: application/json`. Not localized.
- **Path / query parameters:** none.
- **Request body** (`CreateCategoryDto`):

| Field | Type | Required | Rules |
|---|---|---|---|
| `name` | string | yes | Non-blank (implicit required); trimmed; at most 100 characters after trimming. Must not be the name of another category (409). The comparison runs in the database, so its case sensitivity follows the collation. |
| `sortOrder` | short | no | −32768…32767. Omitted binds `0`. |
| `translations` | array \| null | no | Omitted, `null` or `[]` → no translations. Each item: `languageCode` (`ar` \| `en`, case-insensitive, trimmed and stored lower-case; at most once per language) and `name` (non-blank, trimmed, at most 100 characters). |

There is no `id` and no `isActive`. The id is the highest existing category id + 1 (`Categories.Id` is a `TINYINT`, not an identity), and a new category is always created **active**.

Validation order in `CategoryService.CreateAsync`:
1. Name.
2. Translations, in list order.
3. Name not taken.
4. Id limit.
5. Save.

```json
{
  "name": "القياسات الكهربية",
  "sortOrder": 3,
  "translations": [
    { "languageCode": "ar", "name": "القياسات الكهربية" },
    { "languageCode": "en", "name": "Electrical measurements" }
  ]
}
```

- **Success:** `201 Created`. Header `Location: /api/assessment/categories/{id}`. The body is the bare `AdminCategoryResponseDto`, re-read from the database:

```json
{
  "id": 3,
  "name": "القياسات الكهربية",
  "sortOrder": 3,
  "isActive": true,
  "topicsCount": 0,
  "translations": [
    { "languageCode": "ar", "name": "القياسات الكهربية" },
    { "languageCode": "en", "name": "Electrical measurements" }
  ]
}
```

- **Errors:**

| Status | When | Example body |
|---|---|---|
| 400 | Missing/blank `name`, missing/blank `translations[].languageCode` or `translations[].name`, a wrong JSON type, `sortOrder` outside the `short` range, malformed JSON | `ValidationProblemDetails` |
| 400 | `name` blank when it reaches the service (model validation normally answers first) | `{"success":false,"message":"اسم التصنيف مطلوب","data":null}` ("the category name is required") |
| 400 | `name` longer than 100 characters after trimming | `{"success":false,"message":"اسم التصنيف لا يتجاوز 100 حرف","data":null}` ("the category name must not exceed 100 characters") |
| 400 | A `null` item in `translations` | `{"success":false,"message":"قائمة الترجمات تحتوي على عنصر فارغ","data":null}` ("the translations list contains an empty item") |
| 400 | Unsupported `languageCode` | `{"success":false,"message":"لغة الترجمة 'fr' غير مدعومة، اللغات المتاحة: en, ar","data":null}` ("translation language 'fr' is not supported; available languages: en, ar") |
| 400 | The same language twice | `{"success":false,"message":"الترجمة بلغة 'en' مكررة","data":null}` ("the 'en' translation is duplicated") |
| 400 | A translation name blank (when it reaches the service) or longer than 100 characters | `{"success":false,"message":"اسم الترجمة بلغة 'en' مطلوب","data":null}` ("the 'en' translation name is required") / `{"success":false,"message":"اسم الترجمة بلغة 'en' لا يتجاوز 100 حرف","data":null}` ("the 'en' translation name must not exceed 100 characters") |
| 400 | The highest category id is already 255 | `{"success":false,"message":"تم الوصول إلى الحد الأقصى لعدد التصنيفات (255)","data":null}` ("the maximum number of categories (255) has been reached") |
| 401 | Missing, invalid or expired token | `{"success":false,"message":"غير مصرح لك بالوصول","data":null}` |
| 403 | Not `Admin` | `{"success":false,"message":"ليس لديك صلاحية","data":null}` |
| 409 | Another category has this name (pre-check, or `UQ_Categories_Name` when two requests race) | `{"success":false,"message":"يوجد تصنيف آخر بالاسم 'القياسات الكهربية'","data":null}` ("another category is named 'القياسات الكهربية'") |
| 409 | Another create took the same id at the same moment (`PK_Categories`) | `{"success":false,"message":"تم إنشاء تصنيف آخر في نفس اللحظة، برجاء إعادة المحاولة","data":null}` ("another category was created at the same moment, please try again") |
| 500 | Unexpected server fault | `{"success":false,"message":"حدث خطأ داخلي في الخادم","data":null}` |

- **Frontend notes:**
  - Not idempotent. A double submit with the same name gets 409 on the second request. Disable the button while the request is in flight.
  - The id race 409 is safe to retry as is: nothing was saved.
  - A new active category with no topics adds nothing to the progress map. Its topics appear once they exist.
  - Mocks: `docs/mocks/mock_admin_category_create_request.json`, `mock_validation_error.json` (framework 400).

---

### Update assessment category

`PUT /api/assessment/categories/{categoryId}`: called by the Admin dashboard.

- **Auth:** roles: `Admin` (class level).
- **Headers / language:** `Authorization: Bearer <token>`, `Content-Type: application/json`. Not localized.
- **Path / query parameters:**

| Name | Type | Required | Rules |
|---|---|---|---|
| `categoryId` | byte (path) | yes | Route constraint `{categoryId:int:range(1,255)}`. |

- **Request body** (`UpdateCategoryDto`). This is a **full replacement** of the category's fields:

| Field | Type | Required | Rules |
|---|---|---|---|
| `name` | string | yes | Same rules as create. Uniqueness is checked only when the name changed (exact, case-sensitive comparison with the stored name). |
| `sortOrder` | short | effectively yes | **Omitted binds `0`.** |
| `isActive` | bool | effectively yes | **Omitted binds `false` and deactivates the category.** No check either way. |
| `translations` | array \| null | no | **`null` or omitted keeps the stored translations.** A list replaces them: a language in the list is updated or added, and a stored language missing from it is **deleted** (`[]` deletes every translation). Same item rules as create. |

Validation order in `CategoryService.UpdateAsync`:
1. Category exists.
2. Name.
3. Translations.
4. Name not taken (only when it changed).
5. Save.

```json
{
  "name": "أساسيات الكهرباء",
  "sortOrder": 1,
  "isActive": true,
  "translations": [
    { "languageCode": "ar", "name": "أساسيات الكهرباء" },
    { "languageCode": "en", "name": "Basics of electricity" }
  ]
}
```

- **Success:** `200 OK`. Bare `AdminCategoryResponseDto`, re-read from the database (same shape as "Get assessment category").
- **Errors:** every 400 and 409 of "Create assessment category" except the id limit and the id race, plus:

| Status | When | Example body |
|---|---|---|
| 404 | No category with this id | `{"success":false,"message":"التصنيف رقم 9 غير موجود","data":null}` |
| 404 | `categoryId` outside 1–255 or not a number | Empty body (no route matched) |

- **Frontend notes:**
  - **Always send the whole object**, pre-filled from `GET /api/assessment/categories/{categoryId}`. Omitting `isActive` takes the category off the map, and omitting `sortOrder` moves it to position 0.
  - Send `translations: null` to leave translations alone. Send the full list to change them; a list without `en` deletes the English name.
  - Renaming a category renames it on every child's progress map at once (names are read live).

---

### Activate / deactivate assessment category

`PATCH /api/assessment/categories/{categoryId}/active?isActive={true|false}`: called by the Admin dashboard.

- **Auth:** roles: `Admin` (class level).
- **Headers / language:** `Authorization: Bearer <token>`. Not localized.
- **Path / query parameters:**

| Name | Type | Required | Rules |
|---|---|---|---|
| `categoryId` | byte (path) | yes | Route constraint `{categoryId:int:range(1,255)}`. |
| `isActive` | bool | effectively yes | `[FromQuery] bool`. **If omitted it binds `false`, so the call deactivates.** A non-boolean value → 400 `ValidationProblemDetails`. |

- **Request body:** none.
- **Success:** `204 No Content`, empty body. If the category is already in the requested state, nothing is written (still 204).
- **Errors:**

| Status | When | Example body |
|---|---|---|
| 400 | `isActive` is not a boolean | `ValidationProblemDetails` |
| 401 | Missing, invalid or expired token | `{"success":false,"message":"غير مصرح لك بالوصول","data":null}` |
| 403 | Not `Admin` | `{"success":false,"message":"ليس لديك صلاحية","data":null}` |
| 404 | No category with this id | `{"success":false,"message":"التصنيف رقم 9 غير موجود","data":null}` |
| 404 | `categoryId` outside 1–255 or not a number | Empty body (no route matched) |
| 500 | Unexpected server fault | `{"success":false,"message":"حدث خطأ داخلي في الخادم","data":null}` |

- **Frontend notes:**
  - Idempotent and safe to retry.
  - **Effects on children.** An inactive category takes all its topics off the progress map of children who never practised them, even topics that are still active. A topic a child practised stays on that child's map. Nothing else changes: topics keep their own `isActive`, questions keep their topic and are still asked, and statistics are untouched.

---

### List assessment topics

`GET /api/assessment/topics?categoryId=&learningLevel=&isActive=`: called by the Admin dashboard (topic list, and the topic picker of the question form).

- **Auth:** roles: `Admin` (class-level `[Authorize(Roles = "Admin")]` on `AssessmentTopicController`).
- **Headers / language:** `Authorization: Bearer <token>`. Not localized: `name`, `description` and `categoryName` are base columns, and `translations` lists every stored topic translation.
- **Path / query parameters** (`TopicFilterDto`, every one optional and combined with AND):

| Name | Type | Required | Rules |
|---|---|---|---|
| `categoryId` | byte | no | Only topics of this category. An id with no category returns `[]`, not 404. A non-number or a value outside 0–255 → 400 `ValidationProblemDetails`. |
| `learningLevel` | string | no | `Beginner` \| `Intermediate` \| `Advanced`, **case-insensitive**. Blank is ignored. Any other value → 400 with the envelope. |
| `isActive` | bool | no | `true` or `false`. A non-boolean value → 400 `ValidationProblemDetails`. |

- **Request body:** none.
- **Success:** `200 OK`. Bare JSON array of `AdminTopicResponseDto`, ordered by the category's `sortOrder`, then `categoryId`, then `id` (`TopicService.GetAsync`). No paging.

```json
[
  {
    "id": 1,
    "name": "مفهوم الكهرباء",
    "description": "ما هي الكهرباء وأين نستخدمها في حياتنا.",
    "categoryId": 1,
    "categoryName": "أساسيات الكهرباء",
    "learningLevel": "Beginner",
    "isActive": true,
    "questionsCount": 8,
    "createdAt": "2026-08-01T10:00:00",
    "updatedAt": null,
    "translations": [
      { "languageCode": "ar", "name": "مفهوم الكهرباء", "description": "ما هي الكهرباء وأين نستخدمها في حياتنا." },
      { "languageCode": "en", "name": "What is electricity", "description": "What electricity is and where we use it." }
    ]
  },
  {
    "id": 4,
    "name": "الثنائي الباعث للضوء",
    "description": "كيف يعمل الـ LED ولماذا له طرف طويل وطرف قصير.",
    "categoryId": 2,
    "categoryName": "المكونات الإلكترونية",
    "learningLevel": "Intermediate",
    "isActive": true,
    "questionsCount": 10,
    "createdAt": "2026-08-01T10:00:00",
    "updatedAt": null,
    "translations": [
      { "languageCode": "ar", "name": "الثنائي الباعث للضوء", "description": "كيف يعمل الـ LED ولماذا له طرف طويل وطرف قصير." },
      { "languageCode": "en", "name": "The LED", "description": "How an LED works and why it has a long leg and a short leg." }
    ]
  }
]
```

(Two of the six topics in `docs/mocks/mock_admin_topics.json`.)

| Field | Notes |
|---|---|
| `id` | 32-bit integer (identity). |
| `name` | Base name, at most 200 characters, unique across **all** topics (not only within the category). |
| `description` | Base description or `null`. No length limit. |
| `categoryId` / `categoryName` | The topic's category. `categoryName` is the category's base name, not a translation. |
| `learningLevel` | `Beginner`, `Intermediate` or `Advanced`, always in this casing. It orders topics within a category on the progress map. |
| `isActive` | `false` takes the topic off the progress map of children who never practised it. Its questions keep the topic and are still asked. |
| `questionsCount` | Questions whose current `topicId` is this topic, active or not. |
| `createdAt` / `updatedAt` | UTC, read from the database (no offset, at most 3 fractional digits). `updatedAt` is `null` until the first `PUT` or active-flag change. |
| `translations` | At most one per language (`ar`, `en`), ordered by `languageCode`. `description` can be `null`. |

- **Errors:**

| Status | When | Example body |
|---|---|---|
| 400 | `categoryId` or `isActive` cannot be converted | `ValidationProblemDetails` |
| 400 | Unknown `learningLevel` | `{"success":false,"message":"مستوى التعلّم 'Expert' غير صالح (Beginner \| Intermediate \| Advanced)","data":null}` ("learning level 'Expert' is invalid") |
| 401 | Missing, invalid or expired token | `{"success":false,"message":"غير مصرح لك بالوصول","data":null}` |
| 403 | Not `Admin` | `{"success":false,"message":"ليس لديك صلاحية","data":null}` |
| 500 | Unexpected server fault | `{"success":false,"message":"حدث خطأ داخلي في الخادم","data":null}` |

- **Frontend notes:**
  - **Topic picker of the question form:** call `GET /api/assessment/topics?isActive=true`, group the options by `categoryName`, and add a "no topic" choice that sends `topicId: null`. `QuestionService` accepts an inactive topic too, but a new question is rarely meant for a retired topic.
  - There is no delete endpoint. Deactivate instead; `FK_Questions_Topics`, `FK_QuizAttemptQuestions_Topics` and `FK_UserTopicStats_Topics` would refuse deleting a topic in use anyway.
  - Mock: `docs/mocks/mock_admin_topics.json`.

---

### Get assessment topic

`GET /api/assessment/topics/{topicId}`: called by the Admin dashboard (topic editor).

- **Auth:** roles: `Admin` (class level).
- **Headers / language:** `Authorization: Bearer <token>`. Not localized.
- **Path / query parameters:**

| Name | Type | Required | Rules |
|---|---|---|---|
| `topicId` | int (path) | yes | Route constraint `:int`. |

- **Request body:** none.
- **Success:** `200 OK`. Bare `AdminTopicResponseDto`, the same shape as one element of the list above.
- **Errors:**

| Status | When | Example body |
|---|---|---|
| 401 | Missing, invalid or expired token | `{"success":false,"message":"غير مصرح لك بالوصول","data":null}` |
| 403 | Not `Admin` | `{"success":false,"message":"ليس لديك صلاحية","data":null}` |
| 404 | No topic with this id | `{"success":false,"message":"الموضوع رقم 9 غير موجود","data":null}` ("topic 9 does not exist") |
| 500 | Unexpected server fault | `{"success":false,"message":"حدث خطأ داخلي في الخادم","data":null}` |

- **Frontend notes:**
  - Pre-fill the edit form from this response: `PUT` replaces every field.
  - Mock: `docs/mocks/mock_topic_not_found.json` (404).

---

### Create assessment topic

`POST /api/assessment/topics`: called by the Admin dashboard.

- **Auth:** roles: `Admin` (class level).
- **Headers / language:** `Authorization: Bearer <token>`, `Content-Type: application/json`. Not localized.
- **Path / query parameters:** none.
- **Request body** (`CreateTopicDto`):

| Field | Type | Required | Rules |
|---|---|---|---|
| `name` | string | yes | Non-blank (implicit required); trimmed; at most 200 characters after trimming. Must not be the name of another topic in **any** category (409). The comparison runs in the database, so its case sensitivity follows the collation. |
| `description` | string \| null | no | Trimmed; blank → `null`. No length limit. |
| `categoryId` | byte | yes | The category must exist (404; omitted binds `0` → `التصنيف رقم 0 غير موجود`). An inactive category is accepted. A value outside 0–255 → 400 `ValidationProblemDetails`. |
| `learningLevel` | string | yes | `Beginner` \| `Intermediate` \| `Advanced`, **case-insensitive** (`beginner` is stored as `Beginner`). Missing or blank → 400 `ValidationProblemDetails`. |
| `translations` | array \| null | no | Omitted, `null` or `[]` → none. Each item: `languageCode` (`ar` \| `en`, case-insensitive, stored lower-case; at most once per language), `name` (non-blank, trimmed, at most 200 characters) and `description` (optional; trimmed; blank → `null`). |

There is no `isActive`: a new topic is always created **active**, with `createdAt` = the server clock and `updatedAt` = `null`.

Validation order in `TopicService.CreateAsync`:
1. Name.
2. Learning level.
3. Translations, in list order.
4. Category exists.
5. Name not taken.
6. Save.

```json
{
  "name": "الجرس الكهربي",
  "description": "كيف يُصدر الجرس صوتًا عندما يمر فيه التيار.",
  "categoryId": 2,
  "learningLevel": "Intermediate",
  "translations": [
    { "languageCode": "ar", "name": "الجرس الكهربي", "description": "كيف يُصدر الجرس صوتًا عندما يمر فيه التيار." },
    { "languageCode": "en", "name": "The buzzer", "description": "How a buzzer makes a sound when current flows through it." }
  ]
}
```

- **Success:** `201 Created`. Header `Location: /api/assessment/topics/{id}`. The body is the bare `AdminTopicResponseDto`, **re-read from the database**, so `createdAt` has no `Z` and millisecond precision:

```json
{
  "id": 7,
  "name": "الجرس الكهربي",
  "description": "كيف يُصدر الجرس صوتًا عندما يمر فيه التيار.",
  "categoryId": 2,
  "categoryName": "المكونات الإلكترونية",
  "learningLevel": "Intermediate",
  "isActive": true,
  "questionsCount": 0,
  "createdAt": "2026-09-14T08:30:12.345",
  "updatedAt": null,
  "translations": [
    { "languageCode": "ar", "name": "الجرس الكهربي", "description": "كيف يُصدر الجرس صوتًا عندما يمر فيه التيار." },
    { "languageCode": "en", "name": "The buzzer", "description": "How a buzzer makes a sound when current flows through it." }
  ]
}
```

- **Errors:**

| Status | When | Example body |
|---|---|---|
| 400 | Missing/blank `name` or `learningLevel`, missing/blank `translations[].languageCode` or `translations[].name`, `categoryId` outside 0–255, a wrong JSON type, malformed JSON | `ValidationProblemDetails` |
| 400 | `name` blank when it reaches the service (model validation normally answers first) | `{"success":false,"message":"اسم الموضوع مطلوب","data":null}` ("the topic name is required") |
| 400 | `name` longer than 200 characters after trimming | `{"success":false,"message":"اسم الموضوع لا يتجاوز 200 حرف","data":null}` ("the topic name must not exceed 200 characters") |
| 400 | `learningLevel` blank when it reaches the service | `{"success":false,"message":"مستوى التعلّم مطلوب (Beginner \| Intermediate \| Advanced)","data":null}` ("the learning level is required") |
| 400 | Unknown `learningLevel` | `{"success":false,"message":"مستوى التعلّم 'Expert' غير صالح (Beginner \| Intermediate \| Advanced)","data":null}` ("learning level 'Expert' is invalid") |
| 400 | A `null` item in `translations` | `{"success":false,"message":"قائمة الترجمات تحتوي على عنصر فارغ","data":null}` |
| 400 | Unsupported `languageCode` | `{"success":false,"message":"لغة الترجمة 'fr' غير مدعومة، اللغات المتاحة: en, ar","data":null}` |
| 400 | The same language twice | `{"success":false,"message":"الترجمة بلغة 'en' مكررة","data":null}` |
| 400 | A translation name blank (when it reaches the service) or longer than 200 characters | `{"success":false,"message":"اسم الترجمة بلغة 'en' مطلوب","data":null}` / `{"success":false,"message":"اسم الترجمة بلغة 'en' لا يتجاوز 200 حرف","data":null}` |
| 401 | Missing, invalid or expired token | `{"success":false,"message":"غير مصرح لك بالوصول","data":null}` |
| 403 | Not `Admin` | `{"success":false,"message":"ليس لديك صلاحية","data":null}` |
| 404 | The category does not exist (or `categoryId` omitted) | `{"success":false,"message":"التصنيف رقم 9 غير موجود","data":null}` ("category 9 does not exist") |
| 409 | Another topic has this name (pre-check, or `UQ_Topics_Name` when two requests race) | `{"success":false,"message":"يوجد موضوع آخر بالاسم 'الثنائي الباعث للضوء'","data":null}` ("another topic is named 'الثنائي الباعث للضوء'") |
| 500 | Unexpected server fault | `{"success":false,"message":"حدث خطأ داخلي في الخادم","data":null}` |

- **Frontend notes:**
  - Not idempotent. A double submit gets 409 on the second request. Disable the button while the request is in flight.
  - **Effect on children.** A new topic in an active category appears at once on every child's progress map as `NotStarted`. It raises `totalTopics`, so a child who had mastered every topic no longer gets the "every topic mastered" headline.
  - Mocks: `docs/mocks/mock_admin_topic_create_request.json`, `mock_admin_topic_conflict.json` (409).

---

### Update assessment topic

`PUT /api/assessment/topics/{topicId}`: called by the Admin dashboard.

- **Auth:** roles: `Admin` (class level).
- **Headers / language:** `Authorization: Bearer <token>`, `Content-Type: application/json`. Not localized.
- **Path / query parameters:**

| Name | Type | Required | Rules |
|---|---|---|---|
| `topicId` | int (path) | yes | Route constraint `:int`. |

- **Request body** (`UpdateTopicDto`). This is a **full replacement** of the topic's fields:

| Field | Type | Required | Rules |
|---|---|---|---|
| `name` | string | yes | Same rules as create. Uniqueness is checked only when the name changed (exact, case-sensitive comparison with the stored name). |
| `description` | string \| null | no | **Omitted or blank clears the description.** |
| `categoryId` | byte | yes | When it differs from the current category, the new one must exist (404). **Omitted binds `0`**, which differs and does not exist → 404. |
| `learningLevel` | string | yes | Same rules as create. |
| `isActive` | bool | effectively yes | **Omitted binds `false` and deactivates the topic.** No check either way. |
| `translations` | array \| null | no | **`null` or omitted keeps the stored translations.** A list replaces them: a language in the list is updated (name and description) or added, and a stored language missing from it is **deleted**. Same item rules as create. |

`updatedAt` is set to the server clock on every PUT, even when nothing changed.

Validation order in `TopicService.UpdateAsync`:
1. Topic exists.
2. Name.
3. Learning level.
4. Translations.
5. Category exists (only when it changed).
6. Name not taken (only when it changed).
7. Save.

```json
{
  "name": "الثنائي الباعث للضوء",
  "description": "كيف يعمل الـ LED ولماذا له طرف طويل وطرف قصير.",
  "categoryId": 2,
  "learningLevel": "Intermediate",
  "isActive": true,
  "translations": [
    { "languageCode": "ar", "name": "الثنائي الباعث للضوء", "description": "كيف يعمل الـ LED ولماذا له طرف طويل وطرف قصير." },
    { "languageCode": "en", "name": "The LED (light-emitting diode)", "description": "How an LED works and why it has a long leg and a short leg." }
  ]
}
```

- **Success:** `200 OK`. Bare `AdminTopicResponseDto`, re-read from the database (same shape as "Get assessment topic"), with `updatedAt` set, for example `"updatedAt": "2026-09-14T09:12:05.482"`.
- **Errors:** every 400, 404 and 409 of "Create assessment topic", plus:

| Status | When | Example body |
|---|---|---|
| 404 | No topic with this id | `{"success":false,"message":"الموضوع رقم 9 غير موجود","data":null}` |

- **Frontend notes:**
  - **Always send the whole object**, pre-filled from `GET /api/assessment/topics/{topicId}`. Omitting a field silently changes the topic:
    - `isActive` → deactivates.
    - `description` → cleared.
    - `categoryId` → 404.
  - Send `translations: null` to leave translations alone.
  - **Effects on children.** Names are read live, so a rename shows on every progress map at once and in AI requests. Moving a topic to another category moves the children's progress in it too, because statistics are kept per topic. Attempts that already started keep the topic id in their snapshot.

---

### Activate / deactivate assessment topic

`PATCH /api/assessment/topics/{topicId}/active?isActive={true|false}`: called by the Admin dashboard.

- **Auth:** roles: `Admin` (class level).
- **Headers / language:** `Authorization: Bearer <token>`. Not localized.
- **Path / query parameters:**

| Name | Type | Required | Rules |
|---|---|---|---|
| `topicId` | int (path) | yes | Route constraint `:int`. |
| `isActive` | bool | effectively yes | `[FromQuery] bool`. **If omitted it binds `false`, so the call deactivates.** A non-boolean value → 400 `ValidationProblemDetails`. |

- **Request body:** none.
- **Success:** `204 No Content`, empty body. A change of state also sets `updatedAt`. If the topic is already in the requested state, nothing is written (still 204).
- **Errors:**

| Status | When | Example body |
|---|---|---|
| 400 | `isActive` is not a boolean | `ValidationProblemDetails` |
| 401 | Missing, invalid or expired token | `{"success":false,"message":"غير مصرح لك بالوصول","data":null}` |
| 403 | Not `Admin` | `{"success":false,"message":"ليس لديك صلاحية","data":null}` |
| 404 | No topic with this id | `{"success":false,"message":"الموضوع رقم 9 غير موجود","data":null}` |
| 500 | Unexpected server fault | `{"success":false,"message":"حدث خطأ داخلي في الخادم","data":null}` |

- **Frontend notes:**
  - Idempotent and safe to retry.
  - **Effects on children.** An inactive topic leaves the progress map of children who never practised it; children who practised it keep seeing their progress. Its questions keep the topic, are still asked, and still count in its statistics. Deactivating does not remove it from `GET /api/user-topic-stats/{topicId}`.

---
