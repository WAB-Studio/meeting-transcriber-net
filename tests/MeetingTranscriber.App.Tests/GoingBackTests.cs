using System.Text.RegularExpressions;

namespace MeetingTranscriber.App.Tests;

/// <summary>
/// How a sub-screen is left: through one public call the window's app bar makes, and never through
/// a press of its own.
/// </summary>
/// <remarks>
/// The window cannot be run here, so this reads source. What it holds is the seam and not the bar:
/// a screen that lost its back press and gained no way to be left is one nobody can leave, and the
/// corrections screen's refusal has to live in the call itself, because the app bar's press and
/// the keyboard shortcut reach the same door.
/// </remarks>
public class GoingBackTests
{
    private static readonly string[] SubScreens =
    [
        "Configuracion",
        "ReadingAMeeting",
        "ReadingANode",
        "ClassifyingAMeeting",
        "SayingWhoIsWho",
        "WordsThatComeOutWrong",
    ];

    [Fact]
    public void Every_sub_screen_is_left_through_one_public_call()
    {
        foreach (var screen in SubScreens)
        {
            Code(screen + ".xaml.cs").ShouldContain(
                "public void GoBack()", customMessage: $"{screen} declares no public GoBack, so the app bar cannot leave it.");

            Read(screen + ".xaml").ShouldNotContain(
                "x:Name=\"BackButton\"", customMessage: $"{screen} draws a back press of its own beside the app bar's.");
        }
    }

    [Fact]
    public void The_corrections_screen_says_when_it_may_not_be_left()
    {
        var code = Code("WordsThatComeOutWrong.xaml.cs");

        code.ShouldContain("public bool MayGoBack");
        code.ShouldContain("public event EventHandler? MayGoBackChanged");
        code.ShouldContain("MayGoBackChanged?.Invoke(");

        // The refusal is read in the call and before anything is left: the disabled press used to
        // be the only guard, and it is not the only door now.
        var body = Regex.Match(code, @"public void GoBack\(\)\s*\{(?<body>.*?)\n    \}", RegexOptions.Singleline);

        body.Success.ShouldBeTrue("WordsThatComeOutWrong declares no GoBack body to read.");
        body.Groups["body"].Value.ShouldMatch(@"^\s*if \(_saving\)\s*\{\s*return;\s*\}[\s\S]*Left\?\.Invoke");
    }

    [Fact]
    public void The_meeting_screen_commits_the_name_before_it_leaves()
    {
        // A title typed and then "back" is somebody who meant to keep it, and leaving over a
        // refusal would lose it silently.
        var body = Regex.Match(
            Code("ReadingAMeeting.xaml.cs"), @"public void GoBack\(\)\s*\{(?<body>.*?)
    \}", RegexOptions.Singleline);

        body.Success.ShouldBeTrue("ReadingAMeeting declares no GoBack body to read.");
        body.Groups["body"].Value.ShouldMatch(
            @"^\s*if \(!CommitTheName\(\)\)\s*\{\s*return;\s*\}[\s\S]*Close\(\);[\s\S]*Left\?\.Invoke");
    }

    private static string Read(string file) =>
        File.ReadAllText(AppSources.At(Path.Combine("MeetingTranscriber.App", file)).FullName);

    private static string Code(string file) => Regex.Replace(Read(file), @"//[^\r\n]*", string.Empty);
}
