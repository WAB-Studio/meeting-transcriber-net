using System.Reflection;

namespace MeetingTranscriber.Domain;

/// <summary>Anchor for locating this assembly from tests and service registration.</summary>
public static class DomainAssembly
{
    public static Assembly Reference => typeof(DomainAssembly).Assembly;
}
