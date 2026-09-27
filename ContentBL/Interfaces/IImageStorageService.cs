using Microsoft.AspNetCore.Http;

namespace ContentBL.Interfaces;

public interface IImageStorageService
{
    /// <summary>بترفع الصورة وترجع الـ URL النسبي بتاعها (زي /uploads/lessons/xxx.png).</summary>
    Task<string> SaveImageAsync(IFormFile file, CancellationToken ct = default);

    /// <summary>بتمسح الصورة من على السيرفر بناءً على الـ URL المخزّن في الداتابيز.</summary>
    void DeleteImage(string? mediaUrl);
}
