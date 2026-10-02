using Xunit;

namespace Probe;

public sealed class ProbeGeneratorTests
{
    [Fact]
    public void FindsGeneratorAndSharedProject()
    {
        Assert.NotNull(new ProbeGenerator());
        Assert.Equal("GoodName", ProbeNames.GoodName);
    }
}
