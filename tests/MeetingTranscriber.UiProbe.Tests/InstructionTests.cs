namespace MeetingTranscriber.UiProbe.Tests;

/// <summary>
/// How a script is read, which is the one thing about the probe a build agent can hold: reading
/// words into steps opens no window and needs no desktop. Every fact goes through
/// <see cref="Instruction.Read"/> and nothing else, because the types that start a window are the
/// ones <c>ProbeIsNotDrivenTests</c> keeps this suite away from.
/// </summary>
public class InstructionTests
{
    [Theory]
    [InlineData(Verb.Hover, new[] { "hover", "SaveButton" }, "SaveButton", "")]
    [InlineData(Verb.Drag, new[] { "drag", "Track", "120,0" }, "Track", "120,0")]
    [InlineData(Verb.Select, new[] { "select", "Line", "some words" }, "Line", "some words")]
    [InlineData(Verb.Size, new[] { "size", "1100x700" }, "1100x700", "")]
    public void Each_new_verb_takes_the_words_it_says(Verb verb, string[] words, string subject, string detail)
    {
        var read = Instruction.Read(words);

        var only = read.ShouldHaveSingleItem();
        only.Verb.ShouldBe(verb);
        only.Subject.ShouldBe(subject);
        only.Detail.ShouldBe(detail);
    }

    [Fact]
    public void A_new_verb_missing_its_words_is_refused()
    {
        Should.Throw<ProbeFailed>(() => Instruction.Read(["drag", "Track"]));
        Should.Throw<ProbeFailed>(() => Instruction.Read(["select", "Line"]));
        Should.Throw<ProbeFailed>(() => Instruction.Read(["hover"]));
        Should.Throw<ProbeFailed>(() => Instruction.Read(["size"]));
    }

    /// <summary>
    /// A drag that read one number as two would press a button and carry it nowhere, and a script
    /// that said so after six minutes of recording would have cost the six minutes.
    /// </summary>
    [Theory]
    [InlineData("200")]
    [InlineData("a,b")]
    [InlineData("9000,0")]
    [InlineData("0,-4001")]
    [InlineData("1,2,3")]
    [InlineData("")]
    public void A_drag_that_is_not_two_numbers_is_refused_before_anything_starts(string by) =>
        Should.Throw<ProbeFailed>(() => Instruction.Read(["drag", "Track", by]));

    [Theory]
    [InlineData("0,0")]
    [InlineData("-40,0")]
    [InlineData("4000,-4000")]
    public void A_drag_of_two_numbers_within_the_bounds_is_read(string by) =>
        Instruction.Read(["drag", "Track", by]).ShouldHaveSingleItem().Detail.ShouldBe(by);

    [Theory]
    [InlineData("1100")]
    [InlineData("1100x")]
    [InlineData("x700")]
    [InlineData("axb")]
    [InlineData("199x700")]
    [InlineData("1100x8001")]
    [InlineData("-1100x700")]
    [InlineData("1100x700x3")]
    public void A_size_that_is_not_width_by_height_is_refused(string size) =>
        Should.Throw<ProbeFailed>(() => Instruction.Read(["size", size]));

    [Theory]
    [InlineData("200x200")]
    [InlineData("1100x700")]
    [InlineData("8000X8000")]
    public void A_size_inside_the_bounds_is_read(string size) =>
        Instruction.Read(["size", size]).ShouldHaveSingleItem().Subject.ShouldBe(size);
}
