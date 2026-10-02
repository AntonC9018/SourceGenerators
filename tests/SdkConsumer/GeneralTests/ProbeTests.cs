using SourceGeneration.Helpers;
using Xunit;

namespace Probe;

public sealed class ProbeTests
{
    [Fact]
    public void IncludesSharedProjectAndHelpers()
    {
        var pool = new ObjectPool<string>(() => ProbeNames.GoodName, 1);
        Assert.Equal("GoodName", pool.Allocate());
    }
}
