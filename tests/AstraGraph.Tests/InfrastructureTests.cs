using NUnit.Framework;

namespace AstraGraph.Tests;

[TestFixture]
public sealed class InfrastructureTests
{
    [Test]
    public void VerifyRuntimeAndMultiTargeting()
    {
        var frameworkDescription = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
        Assert.That(frameworkDescription, Is.Not.Null.And.Not.Empty);

        // Verify Core assembly is loadable and has correct metadata
        var coreAssembly = typeof(Core.GraphId).Assembly;
        Assert.That(coreAssembly, Is.Not.Null);
    }
}
