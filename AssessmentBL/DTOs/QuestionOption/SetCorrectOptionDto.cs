namespace AssessmentBL.DTOs.QuestionOption
{
    /// <summary>
    /// Move a question's correct answer to another option, in ONE request.
    ///
    /// It used to take four: deactivate the question, clear IsCorrect on the old
    /// option, set it on the new one, activate again — because
    /// UQ_QuestionOptions_OneCorrectPerQuestion refuses two correct options and
    /// the activation rules refuse none. Four requests means four chances to lose
    /// the connection, and the admin's question was left deactivated and broken
    /// halfway through. This does all of it inside one database transaction: it
    /// either moves, or nothing changed and the question is still answerable.
    /// </summary>
    public class SetCorrectOptionDto
    {
        /// <summary>The option that becomes the only correct one. It must belong to this question.</summary>
        public int CorrectOptionId { get; set; }
    }
}
