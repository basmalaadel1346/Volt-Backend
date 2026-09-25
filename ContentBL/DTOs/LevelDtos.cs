namespace ContentBL.DTOs;

public record LevelResponse(int Id, string Title, string? Description, int Order);

// مفيش Order هنا - السيرفر هو اللي بيحددها تلقائي (آخر ترتيب + 1)
public record CreateLevelRequest(string Title, string? Description);

// مفيش Order هنا برضو - التعديل بيغيّر البيانات بس، الترتيب ثابت لحد ما يتغيّر بـ Swap
public record UpdateLevelRequest(string Title, string? Description);

public record SwapLevelsOrderRequest(int FirstLevelId, int SecondLevelId);

/// <summary>
/// الترتيب الجديد للمستويات كلها: قايمة الـ IDs من الأول للآخر. بديل
/// SwapLevelsOrderRequest لأنه Idempotent - بعته عشر مرات نفس النتيجة.
/// </summary>
public record ReorderLevelsRequest(List<int> OrderedIds);
