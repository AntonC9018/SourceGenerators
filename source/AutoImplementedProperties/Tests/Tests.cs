using System.Threading.Tasks;
using VerifyXunit;
using Xunit;

namespace AutoImplementedProperties.Tests;

[UsesVerify]
public class Tests
{
    [Fact]
    public Task BasicTest()
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

    [Fact]
    public Task CustomTypesTest()
    {
        return TestHelper.Verify("""
            using AutoImplementedProperties.Attributes;

            public interface IStuff
            {
                E E { get; set; }
                S S { get; set; }
            }

            public enum E : byte { A, B }
            public struct S { public string Test; }

            [AutoImplementProperties]
            public sealed partial class Hello : IStuff {}
        """);
    }
}
