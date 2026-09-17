using System.IO;

namespace MeetingTranscriber.UiProbe.Tests;

/// <summary>
/// The walk both of the probe's staleness refusals are computed from.
/// </summary>
/// <remarks>
/// The probe's whole job is refusing once what Windows started is older than what is written, and
/// every one of those refusals is a comparison against the newest source this finds. An input that
/// silently stops watching hands an agent yesterday's tree described as today's — which is the one
/// failure the probe cannot report, because nothing about a green answer says what it stopped
/// looking at.
/// </remarks>
public class SourcesTests
{
    /// <summary>
    /// A props file at the root of the checkout counts as a source of what is running, even though
    /// it is above every folder the walk descends from.
    /// </summary>
    /// <remarks>
    /// Red when the walk up to the root stops finding one — a rename of either props file, or a
    /// tree it finds no solution above — which is the silent green this suite exists against. A
    /// bump in <c>Directory.Packages.props</c> changes what the application contains and edits no
    /// file under any project folder, so without this the probe would go on saying a stale window
    /// was current.
    /// </remarks>
    [Fact]
    public void The_newest_thing_a_project_is_built_from_can_be_a_props_file_at_the_root()
    {
        using var checkout = new ACheckout();

        var project = checkout.Folder("src", "MeetingTranscriber.Something");
        checkout.Write(Path.Combine(project.FullName, "Thing.cs"), "class Thing;");

        var props = checkout.Write(
            Path.Combine(checkout.Root.FullName, "Directory.Packages.props"), "<Project />");

        // Stamped newer than everything under the project folder, which is the whole question: the
        // walk below the project can never reach this file at any depth.
        File.SetLastWriteTimeUtc(props, DateTime.UtcNow.AddHours(1));

        var newest = Sources.NewestUnder([project.FullName], Sources.OfThisTool);

        newest.ShouldNotBeNull();
        newest.Value.Path.ShouldBe(props);
    }

    /// <summary>
    /// A checkout with no props file above it answers with what is under the project, and never
    /// with nothing.
    /// </summary>
    /// <remarks>
    /// The other direction of the fact above. Without it a walk that had stopped finding the root
    /// would still pass the first one if the root file happened to be the newest by accident, and
    /// this says the ordinary answer is still the ordinary answer.
    /// </remarks>
    [Fact]
    public void With_nothing_above_it_the_answer_is_the_newest_file_under_the_project()
    {
        using var checkout = new ACheckout();

        var project = checkout.Folder("src", "MeetingTranscriber.Something");
        checkout.Write(Path.Combine(project.FullName, "Thing.cs"), "class Thing;");

        var newer = checkout.Write(
            Path.Combine(project.FullName, "MeetingTranscriber.Something.csproj"), "<Project />");

        File.SetLastWriteTimeUtc(newer, DateTime.UtcNow.AddHours(1));

        var newest = Sources.NewestUnder([project.FullName], Sources.OfThisTool);

        newest.ShouldNotBeNull();
        newest.Value.Path.ShouldBe(newer);
    }

    /// <summary>
    /// What a kind list is for: the application compiles XAML and this tool does not, so the two
    /// readers ask about different files and the difference is a fact and not a coincidence.
    /// </summary>
    [Fact]
    public void A_screen_counts_as_a_source_of_the_application_and_not_of_the_tool()
    {
        using var checkout = new ACheckout();

        var project = checkout.Folder("src", "MeetingTranscriber.Something");
        checkout.Write(Path.Combine(project.FullName, "Thing.cs"), "class Thing;");

        var screen = checkout.Write(Path.Combine(project.FullName, "Screen.xaml"), "<Page />");
        File.SetLastWriteTimeUtc(screen, DateTime.UtcNow.AddHours(1));

        Sources.NewestUnder([project.FullName], Sources.OfTheApplication)!.Value.Path.ShouldBe(screen);
        Sources.NewestUnder([project.FullName], Sources.OfThisTool)!.Value.Path.ShouldNotBe(screen);
    }
}
