using System.Globalization;
using System.Reflection;
using EEMP.Infrastructure.Localization;
using EEMP.SharedKernel.Errors;

namespace EEMP.UnitTests;

public class LocalizationCatalogTests
{
    private static readonly ServerTextLocalizer Localizer = new();

    public static TheoryData<string> Languages() => new(ServerTextLocalizer.Languages);

    private static IEnumerable<string> ErrorCodeValues() =>
        new[] { typeof(ErrorCodes), typeof(EEMP.Modules.Academic.Domain.AcademicErrors), typeof(EEMP.Modules.Finance.Domain.FinanceErrors) }
        new[] { typeof(ErrorCodes), typeof(EEMP.Modules.Academic.Domain.AcademicErrors), typeof(EEMP.Modules.Workflow.Domain.WorkflowErrors) }
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(f => f.IsLiteral && f.FieldType == typeof(string)).Select(f => (string)f.GetRawConstantValue()!);

    [Theory]
    [MemberData(nameof(Languages))]
    public void Every_error_code_is_translated(string language)
    {
        var missing = ErrorCodeValues().Where(c => !Localizer.HasKey(language, c)).ToList();
        Assert.True(missing.Count == 0, $"{language} is missing: {string.Join(", ", missing)}");
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Every_catalog_has_the_same_keys_as_the_default(string language)
    {
        var reference = Localizer.Keys(ServerTextLocalizer.DefaultLanguage).Order().ToList();
        Assert.Equal(reference, Localizer.Keys(language).Order().ToList());
        Assert.All(Localizer.Keys(language), k => Assert.False(string.IsNullOrWhiteSpace(Localizer.Get(k, new CultureInfo(language)))));
    }

    [Fact]
    public void Falls_back_to_default_language_for_unsupported_culture()
    {
        Assert.Equal(Localizer.Get(ErrorCodes.NotFound, new CultureInfo("fa")), Localizer.Get(ErrorCodes.NotFound, new CultureInfo("de")));
        Assert.NotEqual(Localizer.Get(ErrorCodes.NotFound, new CultureInfo("fa")), Localizer.Get(ErrorCodes.NotFound, new CultureInfo("en")));
    }
}
