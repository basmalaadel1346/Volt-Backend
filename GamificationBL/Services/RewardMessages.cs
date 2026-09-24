using GamificationBL.Services.Constants;

namespace GamificationBL.Services;

/// <summary>
/// The sentences the reward bar shows. Arabic by default, English when asked —
/// the same rule the rest of the API follows.
///
/// A reward the child cannot read is not a reward, so every line that grants
/// Sparks says in words what it was for.
/// </summary>
public static class RewardMessages
{
    private const string English = "en";

    /// <summary>One "+N Sparks, because…" line. Written in Arabic: it rides on a committed result.</summary>
    public static string Line(string reason, int sparks) => reason switch
    {
        SparkReasons.LessonCompleted => $"‏+{sparks} شرارة لإنهاء الدرس",
        SparkReasons.QuizCompleted => $"‏+{sparks} شرارة لإنهاء الاختبار",
        SparkReasons.PerfectScore => $"‏+{sparks} شرارة لإجابة مثالية بدون أخطاء",
        SparkReasons.PlacementCompleted => $"‏+{sparks} شرارة لاجتياز اختبار تحديد المستوى",
        SparkReasons.LevelSkipPassed => $"‏+{sparks} شرارة لتخطي المستوى",
        _ => $"‏+{sparks} شرارة"
    };

    public static string Milestone(int streakDays, int sparks) =>
        $"‏صندوق مكافأة! {streakDays} يومًا متتاليًا، +{sparks} شرارة";

    /// <summary>The line at the top of the rewards screen.</summary>
    public static string Headline(
        string language,
        int streakDays,
        bool activeToday,
        int freezes,
        int daysToMilestone)
    {
        var en = language == English;

        if (streakDays <= 0)
            return en
                ? "Start your streak today! Finish a lesson or a quiz to light the first flame."
                : "ابدأ سلسلتك اليوم! أنهِ درسًا أو اختبارًا لتشعل أول شعلة.";

        if (!activeToday)
            return freezes > 0
                ? en
                    ? $"Your {streakDays}-day streak is waiting! Learn something today, or one of your {freezes} freezes will be used."
                    : $"سلسلتك ({streakDays} يومًا) في انتظارك! تعلّم شيئًا اليوم، وإلا استُخدم واحد من تجميداتك ({freezes})."
                : en
                    ? $"Your {streakDays}-day streak breaks if you skip today. One quiz is enough!"
                    : $"سلسلتك ({streakDays} يومًا) ستنكسر لو فاتك اليوم. اختبار واحد يكفي!";

        if (daysToMilestone == 1)
            return en
                ? $"{streakDays} days in a row! One more day for a reward box."
                : $"‏{streakDays} يومًا متتاليًا! باقي يوم واحد على صندوق المكافأة.";

        return en
            ? $"{streakDays} days in a row — keep it going! {daysToMilestone} days to your next reward box."
            : $"‏{streakDays} يومًا متتاليًا، واصل! باقي {daysToMilestone} أيام على صندوق المكافأة التالي.";
    }

    public static string Purchased(string language, string itemName, int price) =>
        language == English
            ? $"You bought {itemName} for {price} Sparks."
            : $"اشتريت {itemName} مقابل {price} شرارة.";
}
