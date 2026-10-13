using IitAcademicPortal.Domain.Configuration;

namespace IitAcademicPortal.Api.Tests.Configuration;

public sealed class NameNormalizerTests
{
    [Theory]
    [InlineData("Computer Science", "COMPUTER SCIENCE")]
    [InlineData("  computer   science  ", "COMPUTER SCIENCE")]
    [InlineData("computer\tscience\n", "COMPUTER SCIENCE")]
    [InlineData("COMPUTER SCIENCE", "COMPUTER SCIENCE")]
    public void Names_are_trimmed_collapsed_and_compared_in_invariant_upper_case(string input, string expected) =>
        Assert.Equal(expected, NameNormalizer.Normalize(input));

    [Fact]
    public void The_display_name_is_only_trimmed_so_what_the_admin_typed_is_kept()
    {
        Assert.Equal("Computer   Science", NameNormalizer.Display("  Computer   Science  "));
        Assert.Equal(string.Empty, NameNormalizer.Display(null));
    }

    [Fact]
    public void Accents_are_not_folded()
    {
        Assert.NotEqual(NameNormalizer.Normalize("Cafe"), NameNormalizer.Normalize("Café"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_whitespace_only_code_is_absent(string? code)
    {
        Assert.Null(NameNormalizer.NormalizeCode(code));
        Assert.Null(NameNormalizer.DisplayCode(code));
    }

    [Fact]
    public void A_code_keeps_its_trimmed_display_value_and_compares_case_insensitively()
    {
        Assert.Equal("cse-01", NameNormalizer.DisplayCode("  cse-01 "));
        Assert.Equal("CSE-01", NameNormalizer.NormalizeCode("  cse-01 "));
        Assert.Equal(NameNormalizer.NormalizeCode("Cse-01"), NameNormalizer.NormalizeCode("cSE-01"));
    }

    [Fact]
    public void Renaming_a_record_sets_the_display_and_comparison_values_together()
    {
        var degree = new Degree();
        degree.Rename("  Master  of IT ");

        Assert.Equal("Master  of IT", degree.Name);
        Assert.Equal("MASTER OF IT", degree.NormalizedName);
    }
}
