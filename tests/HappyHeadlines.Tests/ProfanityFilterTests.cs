using ProfanityService;

namespace HappyHeadlines.Tests;

public class ProfanityFilterTests
{
    private static readonly string[] Words = ["darn", "heck"];

    [Fact]
    public void Masks_listed_words_with_asterisks_of_same_length()
    {
        var result = ProfanityFilter.Apply("What the heck is this darn thing", Words);

        Assert.Equal("What the **** is this **** thing", result.Text);
        Assert.True(result.ContainedProfanity);
    }

    [Fact]
    public void Matches_case_insensitively_and_reports_each_word_once_in_lower_case()
    {
        var result = ProfanityFilter.Apply("HECK, Heck and heck", Words);

        Assert.Equal("****, **** and ****", result.Text);
        Assert.Equal(["heck"], result.Matches);
    }

    [Fact]
    public void Leaves_words_that_only_contain_a_listed_word_untouched()
    {
        var result = ProfanityFilter.Apply("Darnell checked the hecklers", Words);

        Assert.Equal("Darnell checked the hecklers", result.Text);
        Assert.False(result.ContainedProfanity);
    }

    [Fact]
    public void Treats_regex_characters_in_the_word_list_literally()
    {
        var result = ProfanityFilter.Apply("a.b and axb", ["a.b"]);

        Assert.Equal("*** and axb", result.Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Returns_empty_text_unchanged(string? text)
    {
        var result = ProfanityFilter.Apply(text!, Words);

        Assert.Equal(text, result.Text);
        Assert.Empty(result.Matches);
    }

    [Fact]
    public void Returns_text_unchanged_when_word_list_is_empty()
    {
        var result = ProfanityFilter.Apply("What the heck", []);

        Assert.Equal("What the heck", result.Text);
        Assert.False(result.ContainedProfanity);
    }
}
