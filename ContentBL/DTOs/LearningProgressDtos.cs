namespace ContentBL.DTOs;

public record LessonProgressResponse(int LessonId, DateTime CompletedAt);

public record LessonProgressStatusResponse(int LessonId, bool IsCompleted, DateTime? CompletedAt);
