namespace AssessmentBL.DTOs.Taxonomy
{
    /// <summary>A topic's name and description in one content language.</summary>
    public class TopicTranslationDto
    {
        /// <summary>"ar" or "en".</summary>
        public string LanguageCode { get; set; } = null!;

        /// <summary>At most 200 characters.</summary>
        public string Name { get; set; } = null!;

        public string? Description { get; set; }
    }

    public class AdminTopicResponseDto
    {
        public int Id { get; set; }

        /// <summary>The base name, used when a language has no translation.</summary>
        public string Name { get; set; } = null!;

        public string? Description { get; set; }

        public byte CategoryId { get; set; }

        public string CategoryName { get; set; } = null!;

        /// <summary>Beginner | Intermediate | Advanced.</summary>
        public string LearningLevel { get; set; } = null!;

        public bool IsActive { get; set; }

        /// <summary>Questions classified under this topic, active or not.</summary>
        public int QuestionsCount { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? UpdatedAt { get; set; }

        public IReadOnlyList<TopicTranslationDto> Translations { get; set; } = [];
    }

    public class TopicFilterDto
    {
        public byte? CategoryId { get; set; }

        public string? LearningLevel { get; set; }

        public bool? IsActive { get; set; }
    }

    public class CreateTopicDto
    {
        /// <summary>Required, unique across all topics, at most 200 characters.</summary>
        public string Name { get; set; } = null!;

        public string? Description { get; set; }

        /// <summary>Must be an existing category.</summary>
        public byte CategoryId { get; set; }

        /// <summary>Beginner | Intermediate | Advanced (case-insensitive).</summary>
        public string LearningLevel { get; set; } = null!;

        /// <summary>Optional; at most one per language.</summary>
        public List<TopicTranslationDto>? Translations { get; set; }
    }

    public class UpdateTopicDto
    {
        /// <summary>Required, unique across all topics, at most 200 characters.</summary>
        public string Name { get; set; } = null!;

        public string? Description { get; set; }

        /// <summary>
        /// Must be an existing category. Moving a topic moves its statistics with
        /// it; attempts keep the topic, not the category.
        /// </summary>
        public byte CategoryId { get; set; }

        /// <summary>Beginner | Intermediate | Advanced (case-insensitive).</summary>
        public string LearningLevel { get; set; } = null!;

        /// <summary>
        /// An inactive topic leaves the progress map of children who never
        /// practised it. Its questions keep it, and are still asked.
        /// </summary>
        public bool IsActive { get; set; }

        /// <summary>
        /// Null keeps the stored translations. A list replaces them: languages not
        /// in it are removed.
        /// </summary>
        public List<TopicTranslationDto>? Translations { get; set; }
    }
}
