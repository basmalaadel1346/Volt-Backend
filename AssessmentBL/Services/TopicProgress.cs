using AssessmentBL.Services.Constants;

namespace AssessmentBL.Services
{
    /// <summary>
    /// How a child's progress in a topic reads on the progress screen: accuracy,
    /// mastery, stars and the sentence that goes with them. Pure, so the rules are
    /// pinned by tests and the list and single-topic endpoints never disagree.
    ///
    /// Mastery is judged on MultipleChoice/TrueFalse answers only — the counts in
    /// UserTopicStats. XP never decides it: retrying keeps earning XP, and that is
    /// effort, not mastery.
    /// </summary>
    public static class TopicProgress
    {
        /// <summary>Below this share of correct answers a topic is still being learned.</summary>
        public const int PracticingFromPercentage = 50;

        /// <summary>Correct ÷ answered × 100, rounded half away from zero; 0 before any answer.</summary>
        public static int AccuracyPercentage(int answered, int correct) =>
            answered <= 0
                ? 0
                : (int)Math.Round(Math.Clamp(correct, 0, answered) * 100m / answered, MidpointRounding.AwayFromZero);

        /// <param name="answered">MultipleChoice/TrueFalse answers counted in the topic.</param>
        /// <param name="correct">How many of them were correct.</param>
        /// <param name="practised">
        /// Something else happened in the topic — XP from a graded essay, or a hint
        /// the child saw — so it has started even with no answer counted yet.
        /// </param>
        /// <param name="masteryPercentage">AssessmentSettings.EffectiveTopicMasteryPercentage.</param>
        /// <param name="minQuestions">AssessmentSettings.EffectiveTopicMasteryMinQuestions.</param>
        public static string Mastery(int answered, int correct, bool practised, int masteryPercentage, int minQuestions)
        {
            if (answered <= 0)
                return practised ? TopicMasteryLevels.Learning : TopicMasteryLevels.NotStarted;

            // Compared exactly rather than on the rounded percentage: 79.5% is not 80%.
            if (correct * 100 < PracticingFromPercentage * answered)
                return TopicMasteryLevels.Learning;

            return correct * 100 >= masteryPercentage * answered && answered >= minQuestions
                ? TopicMasteryLevels.Mastered
                : TopicMasteryLevels.Practicing;
        }

        /// <summary>0 before anything is done; at least 1 from then on, because trying counts.</summary>
        public static byte Stars(string mastery) => mastery switch
        {
            TopicMasteryLevels.Mastered => 3,
            TopicMasteryLevels.Practicing => 2,
            TopicMasteryLevels.Learning => 1,
            _ => 0
        };

        /// <summary>The sentence shown under a topic, in "en" or (otherwise) "ar".</summary>
        public static string TopicMessage(string mastery, int answered, int correct, string language)
        {
            var en = language == ContentLanguages.English;

            return mastery switch
            {
                TopicMasteryLevels.Mastered => en
                    ? "Amazing! You've mastered this topic. You're an electricity hero!"
                    : "مذهل! لقد أتقنت هذا الموضوع، أنت بطل الكهرباء.",

                // Practicing with nothing wrong only lacks enough answers.
                TopicMasteryLevels.Practicing when answered > 0 && correct >= answered => en
                    ? "Perfect answers! Answer a few more questions in this topic to master it."
                    : "إجابات رائعة! أجب عن مزيد من الأسئلة في هذا الموضوع لتتقنه.",

                TopicMasteryLevels.Practicing => en
                    ? "You're making great progress! Review the questions you missed to master this topic."
                    : "أنت تتقدّم بسرعة! راجع الأسئلة التي أخطأت فيها لتتقن هذا الموضوع.",

                TopicMasteryLevels.Learning => en
                    ? "Great start! Every question you answer brings you closer to mastering this topic."
                    : "بداية رائعة! كل سؤال تجيب عنه يقرّبك من إتقان هذا الموضوع.",

                _ => en
                    ? "A new topic is waiting for you! Take its first quiz to start earning XP."
                    : "موضوع جديد في انتظارك! ابدأ أول اختبار فيه لتجمع نقاط الخبرة."
            };
        }

        /// <summary>The sentence at the top of the progress screen, in "en" or (otherwise) "ar".</summary>
        public static string Headline(int totalXp, int topicsMastered, int totalTopics, bool started, string language)
        {
            var en = language == ContentLanguages.English;

            if (!started)
                return en
                    ? "Your electricity adventure starts now! Take your first quiz to earn your first XP."
                    : "رحلتك في عالم الكهرباء تبدأ الآن! حُلّ أول اختبار لتجمع أولى نقاط الخبرة.";

            if (totalTopics > 0 && topicsMastered >= totalTopics)
                return en
                    ? $"True champion! You've mastered every topic and earned {totalXp} XP."
                    : $"بطل حقيقي! أتقنت كل المواضيع وجمعت {totalXp} من نقاط الخبرة.";

            if (topicsMastered > 0)
                return en
                    ? $"Well done! You've earned {totalXp} XP and mastered {topicsMastered} of {totalTopics} topics. Keep going!"
                    : $"أحسنت! جمعت {totalXp} من نقاط الخبرة وأتقنت {topicsMastered} من أصل {totalTopics} من المواضيع. واصل التقدّم!";

            if (totalXp > 0)
                return en
                    ? $"You're on the right track! You've earned {totalXp} XP. Keep learning to master your first topic."
                    : $"أنت على الطريق الصحيح! جمعت {totalXp} من نقاط الخبرة، واصل التعلّم لتتقن أول موضوع.";

            return en
                ? "Every try teaches you something new! Review your mistakes and try again to earn XP."
                : "كل محاولة تعلّمك شيئًا جديدًا! راجع أخطاءك وحاول مرة أخرى لتجمع نقاط الخبرة.";
        }
    }
}
