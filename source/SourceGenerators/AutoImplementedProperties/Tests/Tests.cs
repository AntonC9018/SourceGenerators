using System.Threading.Tasks;
using AutoImplementedProperties.Attributes;
using AutoImplementedProperties.SourceGenerator;
using SourceGeneration.Testing;
using Xunit;

namespace AutoImplementedProperties.Tests;

public class Tests
{
    private readonly TestHelper<AutoImplementedPropertyGenerator> _helper = new(
        TestHelper.GetAllMetadataReferences(typeof(AutoImplementPropertiesAttribute)));

    [Fact]
    public Task BasicTest()
    {
        return _helper.Verify("""
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
        return _helper.Verify("""
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

    [Fact]
    public Task GenericTypeTest()
    {
        return _helper.Verify("""
            using AutoImplementedProperties.Attributes;

            public interface IStuff<TKey, TValue>
            {
                TKey Key { get; set; }
                TValue Value { get; set; }
            }

            [AutoImplementProperties]
            public sealed partial class Hello<TKey, TValue> : IStuff<TKey, TValue> {}
        """);
    }
}
