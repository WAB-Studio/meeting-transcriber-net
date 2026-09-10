using System.Reflection;

using MeetingTranscriber.Infrastructure.Storage;

namespace MeetingTranscriber.Testing;

/// <summary>
/// The one rule a corpus is: a database and the folder it is in, never two things a caller keeps
/// pointing at the same place.
/// </summary>
/// <remarks>
/// It lives here rather than in a suite because the rule is not one assembly's. It was written when
/// the live mismatch sat in the Python corpus importer, which <c>src/</c> may not reference and the
/// CLI's suite therefore could not see; that tool is gone, and what is left is one rule every suite
/// that can reach a corpus asserts over what it can reach — written once, so the second suite to
/// need it copies nothing.
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
