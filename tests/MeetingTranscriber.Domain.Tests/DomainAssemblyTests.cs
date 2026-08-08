using System.Reflection;
using System.Runtime.Versioning;

namespace MeetingTranscriber.Domain.Tests;

public class DomainAssemblyTests
{
    [Fact]
    public void Domain_is_reachable_from_the_tests()
    {
        DomainAssembly.Reference.GetName().Name.ShouldBe("MeetingTranscriber.Domain");
    }

    [Fact]
    public void Domain_targets_a_framework_without_the_windows_flavour()
    {
        var framework = DomainAssembly.Reference
            .GetCustomAttribute<TargetFrameworkAttribute>()!
            .FrameworkName;

        framework.ShouldNotContain("windows", Case.Insensitive);
    }

    [Fact]
    public void Domain_references_no_windows_assembly()
    {
        var referenced = DomainAssembly.Reference
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name!)
            .ToArray();

        referenced.ShouldNotContain(
            name => name.Contains("Windows", StringComparison.OrdinalIgnoreCase)
                 || name.Contains("WinUI", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Everything this suite can reach, not only what it names: the shared test support reaches
    /// Infrastructure because a temporary corpus is a database, and a helper it grew that wrapped
    /// the parser but returned domain types would be invisible to a check of direct references.
    /// A domain rule proved against the parser's own output only proves the two agree, and the
    /// domain cannot reference the parser, which is what makes that worth a test rather than a
    /// convention. The set is exact so a layer entering this suite's reach is a decision.
    /// </summary>
    [Fact]
    public void Nothing_the_domain_suite_reaches_knows_how_a_response_is_parsed()
    {
        var reached = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<Assembly>([typeof(DomainAssemblyTests).Assembly]);

        while (pending.Count > 0)
        {
            var ours = pending.Dequeue().GetReferencedAssemblies()
                .Where(assembly => assembly.Name!.StartsWith("MeetingTranscriber.", StringComparison.Ordinal));

            foreach (var assembly in ours.Where(assembly => reached.Add(assembly.Name!)))
            {
                pending.Enqueue(Assembly.Load(assembly));
            }
        }

        reached.ShouldBe(
            [
                "MeetingTranscriber.Domain",
                "MeetingTranscriber.Infrastructure",
                "MeetingTranscriber.Testing",
            ],
            ignoreOrder: true);
    }
}
