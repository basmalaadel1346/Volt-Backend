using ContentBL.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace ContentBL.Services;

public class LocalImageStorageService : IImageStorageService
{
    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp"];
    private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5MB
    internal const string UploadsFolder = "uploads/lessons";

    private readonly IWebHostEnvironment _environment;

    public LocalImageStorageService(IWebHostEnvironment environment) => _environment = environment;

    public async Task<string> SaveImageAsync(IFormFile file, CancellationToken ct = default)
    {
        if (file is null)
            throw new InvalidOperationException("لازم ترفع ملف صورة");

        if (file.Length == 0)
            throw new InvalidOperationException("الملف فاضي");

        if (file.Length > MaxFileSizeBytes)
            throw new InvalidOperationException("حجم الصورة أكبر من 5 ميجا");

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
            throw new InvalidOperationException("امتداد الصورة غير مسموح - المسموح بس jpg, jpeg, png, webp");

        // فحص الامتداد بس مش كفاية - أي حد يقدر يسمي ملف مش صورة أصلاً "x.png" ويعدي الفحص فوق.
        // هنا بنتأكد إن محتوى الملف الفعلي (أول Bytes/Magic Number) مطابق فعلاً لصيغة صورة حقيقية.
        if (!await HasValidImageSignatureAsync(file, extension, ct))
            throw new InvalidOperationException("محتوى الملف مش صورة صالحة");

        var webRootPath = _environment.WebRootPath
            ?? throw new InvalidOperationException("wwwroot مش متظبطة، شوف app.UseStaticFiles() في Program.cs");

        var targetDirectory = Path.Combine(webRootPath, UploadsFolder);
        Directory.CreateDirectory(targetDirectory);

        // اسم عشوائي تمامًا - محدش يقدر يخمّن أسماء الصور أو يستبدل صورة حد تاني
        var fileName = $"{Guid.NewGuid()}{extension}";
        var fullPath = Path.Combine(targetDirectory, fileName);

        await using (var stream = new FileStream(fullPath, FileMode.Create))
        {
            await file.CopyToAsync(stream, ct);
        }

        // الـ URL النسبي اللي هيتخزن في الداتابيز جوه MediaUrl
        return $"/{UploadsFolder}/{fileName}";
    }

    /// <summary>بيقرا أول Bytes من الملف (Magic Number) ويتأكد إنها مطابقة لصيغة صورة حقيقية
    /// (JPEG/PNG/WEBP) بدل ما نثق بامتداد اسم الملف بس، اللي أي حد يقدر يغيّره بسهولة.</summary>
    private static async Task<bool> HasValidImageSignatureAsync(IFormFile file, string extension, CancellationToken ct)
    {
        const int headerLength = 12; // أطول Signature هنحتاجه هو WEBP (12 بايت)
        var header = new byte[headerLength];

        await using var stream = file.OpenReadStream();

        // ReadAsync مش مضمون يملى الـ Buffer كامل من أول مرة (Partial Read وارد حتى لو الداتا
        // متوفرة) - بنكرر القراءة لحد ما نوصل لآخر الملف أو نملى الـ Buffer كامل
        var totalRead = 0;
        int bytesRead;
        while (totalRead < headerLength &&
               (bytesRead = await stream.ReadAsync(header.AsMemory(totalRead, headerLength - totalRead), ct)) > 0)
        {
            totalRead += bytesRead;
        }

        if (totalRead < 4)
            return false;

        var isJpeg = header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
        var isPng = header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
                    && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A;
        var isWebp = totalRead >= headerLength
                     && header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46 // "RIFF"
                     && header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50; // "WEBP"

        return extension switch
        {
            ".jpg" or ".jpeg" => isJpeg,
            ".png" => isPng,
            ".webp" => isWebp,
            _ => false
        };
    }

    public void DeleteImage(string? mediaUrl)
    {
        if (string.IsNullOrWhiteSpace(mediaUrl))
            return;

        // بنتعامل بس مع الصور اللي إحنا رفعناها (تحت /uploads/lessons)، مش أي رابط خارجي
        if (!mediaUrl.StartsWith($"/{UploadsFolder}/", StringComparison.OrdinalIgnoreCase))
            return;

        var webRootPath = _environment.WebRootPath;
        if (webRootPath is null)
            return;

        // MediaUrl مش شرط يكون جاي من SaveImageAsync - ممكن الأدمن يكتبه يدوي في
        // CreateLessonContentRequest/UpdateLessonContentRequest، فمينفعش نثق فيه 100%.
        // لو حد حط فيه "../" بهدف يهرب بره فولدر uploads/lessons (Path Traversal) ويمسح
        // ملف تاني على السيرفر، الـ GetFullPath هنا بيحل أي "../" فعليًا، وبعدين بنتأكد إن
        // المسار النهائي لسه داخل الفولدر المسموح بيه قبل ما نمسح أي حاجة.
        var relativePath = mediaUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(webRootPath, relativePath));

        var allowedBase = Path.GetFullPath(Path.Combine(webRootPath, UploadsFolder) + Path.DirectorySeparatorChar);
        if (!fullPath.StartsWith(allowedBase, StringComparison.OrdinalIgnoreCase))
            return; // محاولة خروج بره الفولدر المسموح - نتجاهلها بصمت

        if (File.Exists(fullPath))
            File.Delete(fullPath);
    }
}
