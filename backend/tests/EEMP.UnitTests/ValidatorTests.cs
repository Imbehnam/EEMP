using EEMP.SharedKernel.Errors;
using EEMP.SharedKernel.Paging;
using EEMP.SharedKernel.Validation;

namespace EEMP.UnitTests;

public class ValidatorTests
{
    [Fact]
    public void Collects_stable_codes_per_camelCase_field()
    {
        var v = new Validator()
            .RequiredText("firstName", "  ", 10)
            .RequiredText("lastName", new string('x', 11), 10)
            .Email("email", "not-an-email")
            .Phone("phone", "abc")
            .Locale("locale", "de")
            .TimeZone("timeZone", "Mars/Olympus");

        var ex = Assert.Throws<ValidationException>(v.ThrowIfInvalid);
        Assert.Equal(ErrorCodes.ValidationFailed, ex.Code);
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal([Validator.Required], ex.Errors["firstName"]);
        Assert.Equal([Validator.MaxLength], ex.Errors["lastName"]);
        Assert.Equal([Validator.InvalidFormat], ex.Errors["email"]);
        Assert.Equal([Validator.InvalidFormat], ex.Errors["phone"]);
        Assert.Equal([Validator.UnsupportedLocale], ex.Errors["locale"]);
        Assert.Equal([Validator.InvalidTimeZone], ex.Errors["timeZone"]);
    }

    [Fact]
    public void Valid_input_passes()
    {
        var v = new Validator()
            .RequiredText("name", "Ali", 10)
            .Email("email", "a@b.ir")
            .Phone("phone", "+98 912 000 0000")
            .Locale("locale", "fa-IR")
            .TimeZone("timeZone", "Asia/Tehran")
            .Slug("slug", "demo-1")
            .RowVersion("rowVersion", Convert.ToBase64String([1, 2, 3, 4, 5, 6, 7, 8]));
        Assert.True(v.IsValid);
        v.ThrowIfInvalid();
    }

    [Theory]
    [InlineData("Demo")]
    [InlineData("-demo")]
    [InlineData("demo_x")]
    [InlineData("")]
    public void Rejects_bad_slugs(string slug) => Assert.False(new Validator().Slug("slug", slug).IsValid);

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("not base64!", false)]
    public void RowVersion_is_required_and_base64(string? value, bool requiredError)
    {
        var ex = Assert.Throws<ValidationException>(new Validator().RowVersion("rowVersion", value).ThrowIfInvalid);
        Assert.Equal(requiredError ? Validator.Required : Validator.InvalidFormat, ex.Errors["rowVersion"][0]);
    }

    [Fact]
    public void ParseDate_accepts_iso_dates_only()
    {
        var v = new Validator();
        Assert.Equal(new DateOnly(2012, 3, 21), Validator.ParseDate("2012-03-21", "birthDate", v));
        Assert.Null(Validator.ParseDate("21/03/2012", "birthDate", v));
        Assert.False(v.IsValid);
    }

    [Theory]
    [InlineData(null, null, 1, 20)]
    [InlineData(0, 0, 1, 1)]
    [InlineData(3, 500, 3, 100)]
    public void Paging_is_normalized(int? page, int? size, int expectedPage, int expectedSize)
    {
        var p = PageRequest.Normalize(page, size, "  ");
        Assert.Equal(expectedPage, p.Page);
        Assert.Equal(expectedSize, p.PageSize);
        Assert.Null(p.Search);
    }
}
