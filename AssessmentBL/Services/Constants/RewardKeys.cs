namespace AssessmentBL.Services.Constants
{
    /// <summary>
    /// How an activity reported to the Gamification module is identified, so the
    /// same finished thing never pays twice. The prefixes keep two modules'
    /// numbering apart: lesson 42 and attempt 42 are different events.
    /// </summary>
    public static class RewardKeys
    {
        public static string Attempt(long attemptId) => $"attempt:{attemptId}";

        public static string Lesson(int lessonId) => $"lesson:{lessonId}";
    }
}
