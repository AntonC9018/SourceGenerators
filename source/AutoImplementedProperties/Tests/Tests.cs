using System.Threading.Tasks;
using VerifyXunit;
using Xunit;

namespace AutoImplementedProperties.Tests;

[UsesVerify]
public class Tests
{
    [Fact]
    public Task GeneratesEnumExtensionsCorrectly()
    {
        return TestHelper.Verify("""
            using AutoImplementedProperties.Attributes;

            public interface IStuff
            {
                int A { get; set; }
                string B { get; set; }
            }

            [AutoImplementProperties]
            public sealed partial class Hello : IStuff {}
        """);
    }
}
