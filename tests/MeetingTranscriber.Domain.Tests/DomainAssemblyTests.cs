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
}
