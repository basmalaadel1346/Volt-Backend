using System.Reflection;
using System.Reflection.Emit;
using AssessmentBL.Services;
using AssessmentDA.Context;
using AssessmentDA.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Shared.Common.Exceptions;
using Xunit;

namespace Assessment.Tests;

/// <summary>
/// D5: the AI never looks at images, so every image — a question's or an
/// option's — must carry a description, and a description never outlives its
/// image. The normalizers and activation guards are private, so they are reached
/// by reflection (as in ImageSemanticTextTests) to pin the code that actually runs.
/// </summary>
public class ImageDescriptionRequirementTests
{
    private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;

    private static object? Invoke(Type type, string method, params object?[] args)
    {
        try
        {
            return type.GetMethod(method, PrivateStatic)!.Invoke(null, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            // Surface the real exception so Assert.Throws sees its exact type.
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static (string? Url, string? Description) NormalizeQuestionImage(string? url, string? description)
    {
        var result = (ValueTuple<string?, string?>)Invoke(typeof(QuestionService), "NormalizeImage", url, description)!;
        return (result.Item1, result.Item2);
    }

    private static (string? Text, string? Url, string? Description) NormalizeOption(
        string? text, string? url, string? description)
    {
        var result = (ValueTuple<string?, string?, string?>)Invoke(
            typeof(QuestionOptionService), "NormalizeContent", text, url, description)!;
        return (result.Item1, result.Item2, result.Item3);
    }

    // ---------------- Question create / update -------------------------------

    [Fact]
    public void Question_ImageWithDescription_IsKeptTrimmed()
    {
        var (url, description) = NormalizeQuestionImage("  /uploads/lessons/a.png ", "  A bulb wired to a battery.  ");

        Assert.Equal("/uploads/lessons/a.png", url);
        Assert.Equal("A bulb wired to a battery.", description);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \t\n")]
    public void Question_ImageWithoutDescription_IsRejectedAsFieldValidation(string? description)
    {
        var ex = Assert.Throws<ArgumentException>(() => NormalizeQuestionImage("/uploads/lessons/a.png", description));
        Assert.Equal("imageDescription", ex.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Question_WithoutImage_StoresNoDescription_EvenIfOneIsSent(string? url)
    {
        // A description must never outlive a removed image.
        var (storedUrl, description) = NormalizeQuestionImage(url, "a stale description of a removed image");

        Assert.Null(storedUrl);
        Assert.Null(description);
    }

    [Fact]
    public void Question_DescriptionLength_IsCappedAt1000AfterTrimming()
    {
        var atLimit = new string('x', 1000);
        Assert.Equal(atLimit, NormalizeQuestionImage("/i.png", "  " + atLimit + "  ").Description);

        Assert.Throws<ArgumentException>(() => NormalizeQuestionImage("/i.png", new string('x', 1001)));
    }

    // ---------------- Option create / update ---------------------------------

    [Fact]
    public void Option_WithTextAndImage_StillRequiresADescription()
    {
        // The old rule only covered image-ONLY options. The text may just label
        // the picture ("A"), so the AI still cannot tell what was chosen.
        var ex = Assert.Throws<ArgumentException>(() => NormalizeOption("A", "/uploads/lessons/a.png", null));
        Assert.Equal("imageDescription", ex.ParamName);
    }

    [Fact]
    public void Option_ImageOnlyWithoutDescription_IsRejected()
        => Assert.Throws<ArgumentException>(() => NormalizeOption(null, "/uploads/lessons/a.png", "   "));

    [Fact]
    public void Option_WithNeitherTextNorImage_IsRejected()
    {
        var ex = Assert.Throws<ArgumentException>(() => NormalizeOption("  ", null, "a description alone is not content"));
        Assert.Equal("optionText", ex.ParamName);
    }

    [Fact]
    public void Option_TextOnly_DropsAnyDescription()
    {
        var (text, url, description) = NormalizeOption(" 20 ", null, "describes an image that is not there");

        Assert.Equal("20", text);
        Assert.Null(url);
        Assert.Null(description);
    }

    [Fact]
    public void Option_ImageWithDescription_IsKeptTrimmed()
    {
        var (text, url, description) = NormalizeOption("A", " /uploads/lessons/a.png ", "  LED with a resistor.  ");

        Assert.Equal("A", text);
        Assert.Equal("/uploads/lessons/a.png", url);
        Assert.Equal("LED with a resistor.", description);
    }

    [Fact]
    public void Option_DescriptionLength_IsCappedAt1000()
    {
        Assert.Equal(1000, NormalizeOption(null, "/i.png", new string('d', 1000)).Description!.Length);
        Assert.Throws<ArgumentException>(() => NormalizeOption(null, "/i.png", new string('d', 1001)));
    }

    // ---------------- Activation catches legacy rows --------------------------

    [Theory]
    [InlineData("/uploads/lessons/a.png", null)]
    [InlineData("/uploads/lessons/a.png", "  ")]
    // A legacy blank ImageUrl is still "an image" to the CHECK (ImageUrl IS NOT
    // NULL), so activation must refuse it rather than let SQL fail with a 500.
    [InlineData("", null)]
    public void Activation_QuestionImageWithoutDescription_IsABusinessRule(string imageUrl, string? description)
    {
        var ex = Assert.Throws<BusinessRuleException>(() =>
            Invoke(typeof(QuestionService), "EnsureQuestionImageIsDescribed", 42, imageUrl, description));

        Assert.Contains("42", ex.Message);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("/uploads/lessons/a.png", "A described image.")]
    public void Activation_QuestionWithoutImageOrWithDescription_Passes(string? imageUrl, string? description)
        => Invoke(typeof(QuestionService), "EnsureQuestionImageIsDescribed", 42, imageUrl, description);

    [Fact]
    public void Activation_OptionImagesWithoutDescription_AreABusinessRule_NamingTheOptions()
    {
        var ex = Assert.Throws<BusinessRuleException>(() =>
            Invoke(typeof(QuestionService), "EnsureOptionImagesAreDescribed", 42, (IReadOnlyCollection<int>)new List<int> { 1001, 1003 }));

        Assert.Contains("1001", ex.Message);
        Assert.Contains("1003", ex.Message);
    }

    [Fact]
    public void Activation_WhenEveryOptionImageIsDescribed_Passes()
        => Invoke(typeof(QuestionService), "EnsureOptionImagesAreDescribed", 42, (IReadOnlyCollection<int>)new List<int>());

    // ---------------- The EF model mirrors the new CHECKs ---------------------

    private static AssessmentDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AssessmentDbContext>()
            .UseSqlServer("Server=none;Database=VoltDB;Trusted_Connection=True;")
            .Options);

    [Fact]
    public void Model_Questions_DeclareImageHasDescription()
    {
        using var db = CreateContext();
        // Check constraints live only in the design-time model; the runtime
        // (read-optimized) db.Model does not carry them.
        var checks = db.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(Question))!.GetCheckConstraints().ToList();

        var check = Assert.Single(checks, c => c.ModelName == "CK_Questions_ImageHasDescription");
        Assert.StartsWith("[ImageUrl] IS NULL OR", check.Sql);
    }

    [Fact]
    public void Model_QuestionOptions_ReplaceTheImageOnlyRuleWithImageHasDescription()
    {
        using var db = CreateContext();
        var names = db.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(QuestionOption))!
            .GetCheckConstraints()
            .Select(c => c.ModelName)
            .ToList();

        Assert.Contains("CK_QuestionOptions_ImageHasDescription", names);
        Assert.Contains("CK_QuestionOptions_TextOrImage", names);
        Assert.DoesNotContain("CK_QuestionOptions_ImageOptionHasDescription", names);
    }
}

/// <summary>
/// D4: business rules in the admin authoring services are BusinessRuleException
/// (400). InvalidOperationException is a 500 now, so these services must never
/// construct one. Checked on the compiled IL — including the compiler-generated
/// async state machines — so a new throw cannot slip in unnoticed.
/// </summary>
public class AuthoringServicesExceptionTests
{
    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(o => o.Value);

    [Theory]
    [InlineData(typeof(QuestionService))]
    [InlineData(typeof(QuestionOptionService))]
    [InlineData(typeof(QuizService))]
    public void NeverConstructsInvalidOperationOrUnauthorizedAccessException(Type service)
    {
        var constructed = ConstructedTypes(service).ToList();

        // Sanity: the scan really sees the throws inside async methods.
        if (service != typeof(QuizService))
            Assert.Contains(typeof(BusinessRuleException), constructed);

        Assert.DoesNotContain(typeof(InvalidOperationException), constructed);
        Assert.DoesNotContain(typeof(UnauthorizedAccessException), constructed);
    }

    private static IEnumerable<Type> ConstructedTypes(Type root)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic
                               | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        var types = new List<Type> { root };
        for (var i = 0; i < types.Count; i++)
            types.AddRange(types[i].GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic));

        foreach (var type in types)
        foreach (var method in type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all)))
        {
            var il = method.GetMethodBody()?.GetILAsByteArray();
            if (il is null)
                continue;

            foreach (var ctor in NewObjTargets(method, il))
                yield return ctor.DeclaringType!;
        }
    }

    // Walks the IL opcode by opcode (so operand bytes are never misread as
    // opcodes) and resolves every newobj target.
    private static IEnumerable<MethodBase> NewObjTargets(MethodBase method, byte[] il)
    {
        var position = 0;
        while (position < il.Length)
        {
            short value = il[position++];
            if (value == 0xFE)
                value = unchecked((short)(0xFE00 | il[position++]));

            var opCode = OpCodesByValue[value];
            var operandStart = position;

            position += opCode.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, operandStart),
                _ => 4
            };

            if (opCode == OpCodes.Newobj)
            {
                var token = BitConverter.ToInt32(il, operandStart);
                var genericTypeArgs = method.DeclaringType!.IsGenericType
                    ? method.DeclaringType.GetGenericArguments()
                    : null;
                var genericMethodArgs = method.IsGenericMethod ? method.GetGenericArguments() : null;

                yield return method.Module.ResolveMethod(token, genericTypeArgs, genericMethodArgs)!;
            }
        }
    }
}
