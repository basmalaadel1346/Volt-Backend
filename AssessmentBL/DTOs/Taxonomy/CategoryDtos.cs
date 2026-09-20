namespace AssessmentBL.DTOs.Taxonomy
{
    /// <summary>A category's name in one content language.</summary>
    public class CategoryTranslationDto
    {
        /// <summary>"ar" or "en".</summary>
        public string LanguageCode { get; set; } = null!;

        /// <summary>At most 100 characters.</summary>
        public string Name { get; set; } = null!;
    }

    public class AdminCategoryResponseDto
    {
        public byte Id { get; set; }

        /// <summary>The base name, used when a language has no translation.</summary>
        public string Name { get; set; } = null!;

        public short SortOrder { get; set; }

        public bool IsActive { get; set; }

        /// <summary>Topics in this category, active or not.</summary>
        public int TopicsCount { get; set; }

        public IReadOnlyList<CategoryTranslationDto> Translations { get; set; } = [];
    }

    public class CreateCategoryDto
    {
        /// <summary>Required, unique, at most 100 characters.</summary>
        public string Name { get; set; } = null!;

        /// <summary>Position on the child's progress map (ascending).</summary>
        public short SortOrder { get; set; }

        /// <summary>Optional; at most one per language.</summary>
        public List<CategoryTranslationDto>? Translations { get; set; }
    }

    public class UpdateCategoryDto
    {
        /// <summary>Required, unique, at most 100 characters.</summary>
        public string Name { get; set; } = null!;

        public short SortOrder { get; set; }

        /// <summary>
        /// An inactive category and its topics leave the child's progress map;
        /// topics a child already practised stay on that child's map.
        /// </summary>
        public bool IsActive { get; set; }

        /// <summary>
        /// Null keeps the stored translations. A list replaces them: languages not
        /// in it are removed.
        /// </summary>
        public List<CategoryTranslationDto>? Translations { get; set; }
    }
}
