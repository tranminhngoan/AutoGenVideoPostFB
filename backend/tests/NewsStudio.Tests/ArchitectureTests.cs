using FluentAssertions;
using NewsStudio.Domain.Enums;
using Xunit;

namespace NewsStudio.Tests;

public class ArchitectureTests
{
    [Fact]
    public void DomainLayer_ShouldNotDependOnInfrastructureOrApi()
    {
        var domainAssembly = typeof(VideoProjectStatus).Assembly;
        var referencedAssemblies = domainAssembly.GetReferencedAssemblies();

        referencedAssemblies.Should().NotContain(a => a.Name!.StartsWith("NewsStudio.Infrastructure"));
        referencedAssemblies.Should().NotContain(a => a.Name!.StartsWith("NewsStudio.Api"));
        referencedAssemblies.Should().NotContain(a => a.Name!.StartsWith("NewsStudio.Worker"));
    }

    [Fact]
    public void ApplicationLayer_ShouldNotDependOnInfrastructureOrApi()
    {
        var appAssembly = typeof(NewsStudio.Application.DTOs.SystemHealthDto).Assembly;
        var referencedAssemblies = appAssembly.GetReferencedAssemblies();

        referencedAssemblies.Should().NotContain(a => a.Name!.StartsWith("NewsStudio.Infrastructure"));
        referencedAssemblies.Should().NotContain(a => a.Name!.StartsWith("NewsStudio.Api"));
        referencedAssemblies.Should().NotContain(a => a.Name!.StartsWith("NewsStudio.Worker"));
    }

    [Fact]
    public void VideoProjectStatus_ShouldHaveAllMasterPlanLifecycleStates()
    {
        var expectedStates = new[]
        {
            "Pending", "Extracting", "Extracted", "Analyzing", "Scripting",
            "PlanningScenes", "GeneratingAudio", "Rendering", "QualityChecking",
            "AwaitingApproval", "Publishing", "Published", "Failed", "Cancelled"
        };

        var actualNames = Enum.GetNames<VideoProjectStatus>();
        actualNames.Should().Contain(expectedStates);
    }
}
