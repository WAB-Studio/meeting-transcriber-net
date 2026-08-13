using System.Reflection;

using MeetingTranscriber.Infrastructure.Storage;

namespace MeetingTranscriber.Testing;

/// <summary>
/// The one rule a corpus is: a database and the folder it is in, never two things a caller keeps
/// pointing at the same place.
/// </summary>
/// <remarks>
/// It lives here rather than in a suite because the assembly it has to hold over is not the one
/// any single suite can reach. <c>src/</c> may not reference <c>tools/</c>, so the CLI's suite
/// cannot see the importer — which is exactly where the live mismatch was — and the importer's
/// suite cannot see the CLI. Each asserts it over what it can reach, and the rule is written once.
/// </remarks>
public static class CorpusPairing
{
    /// <summary>
    /// Every public member of these assemblies that takes a corpus and a folder as two arguments,
    /// which is the pairing nothing may ask for. Empty is the only acceptable answer.
    /// </summary>
    /// <remarks>
    /// It sees parameters and not the whole of the idea. A member taking a corpus and some record
    /// that happens to carry a <see cref="DirectoryInfo"/> would pass, and so would a path passed
    /// as a string. What it does hold is the shape every writer in this product actually had, and
    /// it names the offender rather than only failing.
    /// </remarks>
    public static IReadOnlyList<string> WhereACorpusMeetsAFolder(params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        return
        [
            .. assemblies
                .SelectMany(assembly => assembly.GetTypes())
                .Where(type => type.IsPublic || type.IsNestedPublic)
                .SelectMany(type => type
                    .GetMembers(
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .OfType<MethodBase>())
                .Where(TakesBoth)
                .Select(method => $"{method.DeclaringType!.FullName}.{method.Name}")
                .Order(StringComparer.Ordinal),
        ];
    }

    private static bool TakesBoth(MethodBase method)
    {
        var parameters = method.GetParameters();
        return parameters.Any(parameter => parameter.ParameterType == typeof(CorpusDbContext))
            && parameters.Any(parameter => parameter.ParameterType == typeof(DirectoryInfo));
    }
}
