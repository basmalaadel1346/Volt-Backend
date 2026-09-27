namespace AssessmentBL.DTOs.UserTopicStat
{
    /// <summary>
    /// The child's progress screen (GET /api/user-topic-stats): XP, stars and
    /// mastery for every topic, grouped by category, with a sentence for the
    /// child. Names and messages are in <see cref="Language"/>.
    /// </summary>
    public class MyProgressResponseDto
    {
        /// <summary>The language names and messages were resolved in ("en" | "ar").</summary>
        public string Language { get; set; } = null!;

        /// <summary>
        /// Every point the child has earned: each correct MultipleChoice/TrueFalse
        /// answer is worth its Points, each graded essay its awarded points, over
        /// every submitted attempt. Questions that belong to no topic earn XP too,
        /// so this can be more than the sum of the topics' xp.
        /// </summary>
        public int TotalXp { get; set; }

        /// <summary>
        /// Topics on the map: every active topic of an active category, plus any
        /// retired topic (or topic of a retired category) the child practised.
        /// </summary>
        public int TotalTopics { get; set; }

        public int TopicsStarted { get; set; }

        public int TopicsMastered { get; set; }

        /// <summary>MultipleChoice/TrueFalse answers counted in topics. Essays are not counted.</summary>
        public int QuestionsAnswered { get; set; }

        public int CorrectAnswers { get; set; }

        /// <summary>CorrectAnswers ÷ QuestionsAnswered × 100, a whole number; 0 before any answer.</summary>
        public int AccuracyPercentage { get; set; }

        /// <summary>Hints the child has seen in these topics. A hint that was never shown is not counted.</summary>
        public int HintsUsed { get; set; }

        /// <summary>An encouraging sentence for the top of the screen.</summary>
        public string Message { get; set; } = null!;

        /// <summary>By category SortOrder; each category's topics Beginner → Advanced.</summary>
        public List<CategoryProgressDto> Categories { get; set; } = new();
    }

    public class CategoryProgressDto
    {
        public byte CategoryId { get; set; }

        public string Name { get; set; } = null!;

        public int Xp { get; set; }

        public int TotalTopics { get; set; }

        public int TopicsStarted { get; set; }

        public int TopicsMastered { get; set; }

        public List<TopicProgressDto> Topics { get; set; } = new();
    }

    /// <summary>One topic of the progress map (also GET /api/user-topic-stats/{topicId}).</summary>
    public class TopicProgressDto
    {
        public int TopicId { get; set; }

        public string Name { get; set; } = null!;

        public string? Description { get; set; }

        public byte CategoryId { get; set; }

        public string CategoryName { get; set; } = null!;

        /// <summary>Beginner | Intermediate | Advanced.</summary>
        public string LearningLevel { get; set; } = null!;

        /// <summary>NotStarted | Learning | Practicing | Mastered.</summary>
        public string Mastery { get; set; } = null!;

        /// <summary>0 (NotStarted) to 3 (Mastered).</summary>
        public byte Stars { get; set; }

        public int Xp { get; set; }

        public int QuestionsAnswered { get; set; }

        public int CorrectAnswers { get; set; }

        public int WrongAnswers { get; set; }

        public int AccuracyPercentage { get; set; }

        public int HintsUsed { get; set; }

        /// <summary>
        /// When the child last submitted a quiz with a MultipleChoice/TrueFalse
        /// question of this topic (UTC). Null if never — including a topic
        /// practised only through essays.
        /// </summary>
        public DateTime? LastPracticedAt { get; set; }

        /// <summary>An encouraging sentence that matches Mastery.</summary>
        public string Message { get; set; } = null!;

        /// <summary>Only difficulties the child has practised, Easy → Advanced.</summary>
        public List<DifficultyProgressDto> Difficulties { get; set; } = new();
    }

    public class DifficultyProgressDto
    {
        /// <summary>Easy | Medium | Hard | Advanced.</summary>
        public string Difficulty { get; set; } = null!;

        public int Xp { get; set; }

        public int QuestionsAnswered { get; set; }

        public int CorrectAnswers { get; set; }

        public int WrongAnswers { get; set; }

        public int AccuracyPercentage { get; set; }

        public int HintsUsed { get; set; }
    }
}
