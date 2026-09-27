namespace UsersBL;

/// <summary>
/// قواعد السن (6-14 سنة) وحساب العمر من تاريخ الميلاد - مكان واحد يستخدمه AuthService وUserService،
/// بدل ما يكون نفس المنطق مكرر بالظبط في الاتنين (كان فيه احتمال لو حد غيّر النطاق في مكان
/// وناسي التاني يبقى فيه تضارب بين قواعد التسجيل وتعديل البروفايل).
/// </summary>
public static class BirthDateRules
{
    public const int MinAge = 6;
    public const int MaxAge = 14;

    public static int? CalculateAge(DateOnly? birthDate, DateOnly today)
    {
        if (birthDate is not { } b)
            return null;

        var age = today.Year - b.Year;
        if (b > today.AddYears(-age))
            age--;

        return age;
    }

    /// <summary>تاريخ الميلاد null مسموح (يعني مش متحدد لسه/مش مطلوب - زي حساب Parent)، لكن
    /// لو محدد لازم يكون منطقي (مش في المستقبل) ويقع في نطاق سن الطفل (6-14).</summary>
    public static bool IsValidBirthDate(DateOnly? birthDate, DateOnly today)
    {
        if (birthDate is null)
            return true;

        if (birthDate > today)
            return false; // تاريخ ميلاد في المستقبل

        var age = CalculateAge(birthDate, today);
        return age >= MinAge && age <= MaxAge;
    }
}
