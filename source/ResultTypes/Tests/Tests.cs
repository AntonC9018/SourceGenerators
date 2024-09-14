using System.Threading.Tasks;
using AutoImplementedProperties.Tests;
using ResultTypes.Shared;
using ResultTypes.SourceGenerator;
using Xunit;

namespace ResultTypes.Tests;

public class Tests
{
    private readonly TestHelper<ResultTypesGenerator> _helper = new(
        TestHelper.GetAllMetadataReferences(typeof(ResultBaseAttribute)));

    // Basic interface implementations.
    // Not runnable at runtime.
    private const string Config = """
        using ResultTypes.Shared;
        using System;

        [assembly: ResultBase(typeof(ResultBase))]
        [assembly: WellKnownResult(typeof(WellKnownResult))]

        namespace System.Runtime.CompilerServices
        {
              internal static class IsExternalInit {}
        }

        public readonly record struct ResultBase(int i)
        {
            public bool IsNone => i == 0;
            public static ResultBase None => default;
            public static ResultSet ResultSetOf<T>() => throw new NotImplementedException();
            public static ResultSet ResultSetOf<T>(ReadOnlySpan<T> values) => throw new NotImplementedException();

            public static ResultBase Create<T>(T t)
                where T : struct, Enum
                => new ResultBase((int) (object) t);

            public T As<T>()
                where T : struct, Enum
                => throw new NotImplementedException();

            public static void Declare<T>()
                where T : struct, Enum
                => throw new NotImplementedException();

            public static void DeclareSubset<T, TBase>()
                where T : struct, Enum
                where TBase : struct, Enum
                => throw new NotImplementedException();
        }

        public readonly record struct ResultSet
        {
        }

        public enum WellKnownResult
        {
            None,
            Ok,
            GenericFailure,
        }
    """;

    [Fact]
    public Task BasicTest()
    {
        return _helper.Verify(Config + """
            public enum Result1
            {
                None,
                Hello,
                World,
            }

            public static partial class Helper0
            {
                [GenerateResultType]
                public static Result ComputeThing(int i)
                {
                    if (i == 0)
                    {
                        return Result.Ok(Result1.Hello);
                    }

                    if (i == 1)
                    {
                        return Result.Ok(Result1.World);
                    }

                    return Result.Failure();
                }

                public static void Usage()
                {
                    ResultTag.Declare();

                    var ret = ComputeThing(0);

                    bool isOk = ret.Tag.IsOk;
                    _ = isOk;

                    var resultSet = ResultTag.ResultSets;
                    _ = resultSet;

                    var as1 = ret.Tag.As<Result1>();
                    _ = as1;
                }
            }
        """);
    }

    [Fact]
    public Task UsesExistingResultType()
    {
        return _helper.Verify(Config + """
            public readonly partial record struct Result1;

            public static partial class Helper1
            {
                [GenerateResultType]
                public static Result1 Stuff(int i)
                {
                    return Result1.Ok();
                }
            }
        """);
    }

    private static string PayloadTestCode(
        string definitions,
        string methodBody)
    {
        return $$"""
            {{Config}}
            {{definitions}}
            public static partial class Helper
            {
                [GenerateResultType]
                public static MyResult Stuff(int i)
                {
                    {{methodBody}}
                }
            }
        """;
    }

    [Fact]
    public Task StructPayload()
    {
        var source = PayloadTestCode(
            "public record struct Things(int p);",
            "return MyResult.Ok(new Things(5));");
        return _helper.Verify(source);
    }

    [Fact]
    public Task ExceptionNotPayload_IfError()
    {
        var source = PayloadTestCode(
            """
            public sealed class Exception1 : System.Exception
            {
            }
            """,
            "return MyResult.Failure(new Exception1());");
        return _helper.Verify(source);
    }

    [Fact]
    public Task ExceptionNotPayload_IfError_WithTag()
    {
        var source = PayloadTestCode(
            """
            public sealed class Exception1 : System.Exception
            {
            }
            public enum Tag1
            {
                None,
                A,
                B,
            }
            """,
            """
            var tag = Tag1.A;
            var exception = new Exception1();
            return MyResult.Failure(tag, exception);
            """);
        return _helper.Verify(source);
    }

    [Fact]
    public Task ExceptionNotPayload_IfError_WithNonEnumTag()
    {
        var source = PayloadTestCode(
            """
            public sealed class Exception1 : System.Exception
            {
            }
            public static class Helper1
            {
                [GenerateResultType]
                public static MyResult1 Thing()
                {
                    return MyResult1.Failure();
                }
            }
            """,
            """
            var tag = Helper1.Thing().Tag;
            var exception = new Exception1();
            return MyResult.Failure(tag, exception);
            """);
        return _helper.Verify(source);
    }

    [Fact]
    public Task ExceptionIsPayload_IfOk()
    {
        var source = PayloadTestCode(
            """
            public sealed class Exception1 : System.Exception
            {
            }
            """,
            "return MyResult.Ok(new Exception1());");
        return _helper.Verify(source);
    }

    [Fact]
    public Task ExceptionAndStructPayload()
    {
        var source = PayloadTestCode(
            """
            public sealed class Exception1 : System.Exception
            {
            }
            public sealed class Payload
            {
            }
            """,
            "return MyResult.Failure(new Payload(), new Exception1());");
        return _helper.Verify(source);
    }

    [Fact]
    public Task ConstTag()
    {
        var source = PayloadTestCode(
            """
            public enum Tag
            {
                None,
                A,
                B,
            }
            """,
            """
            if (i == 0)
            {
                return MyResult.Ok(Tag.A);
            }
            else
            {
                return MyResult.Failure(Tag.B);
            }
            """);
        return _helper.Verify(source);
    }

    [Fact]
    public Task ConstTagAndPayload()
    {
        var source = PayloadTestCode(
            """
            public enum Tag
            {
                None,
                A,
                B,
            }
            public struct PayloadA
            {
            }
            public struct PayloadB
            {
            }
            """,
            """
            if (i == 0)
            {
                return MyResult.Ok(Tag.A, new PayloadA());
            }
            else
            {
                return MyResult.Failure(Tag.B, new PayloadB());
            }
            """);
        return _helper.Verify(source);
    }

    [Fact]
    public Task MultipleTagsSinglePayload()
    {
        var source = PayloadTestCode(
            """
            public enum Tag1
            {
                None,
                A,
                B,
            }
            public enum Tag2
            {
                None,
                C,
                D,
            }
            public struct PayloadA
            {
            }
            public struct PayloadB
            {
            }
            """,
            """
            if (i == 0)
            {
                return MyResult.Ok(Tag1.A, new PayloadA());
            }
            else
            {
                return MyResult.Failure(Tag2.C, new PayloadB());
            }
            """);
        return _helper.Verify(source);
    }

    [Fact]
    public Task TagNonConst()
    {
        var source = PayloadTestCode(
            """
            public enum Tag1
            {
                None,
                A,
                B,
            }
            public static class TagHelper
            {
                public static Tag1 NonConst() => Tag1.A;
            }
            """,
            """
            if (i == 0)
            {
                return MyResult.Failure(TagHelper.NonConst());
            }
            else
            {
                return MyResult.Ok(Tag1.B);
            }
            """);
        return _helper.Verify(source);
    }

    [Fact]
    public Task TagPayloadException()
    {
        var source = PayloadTestCode(
            """
            public enum Tag1
            {
                None,
                A,
                B,
            }
            public struct PayloadA
            {
            }
            public sealed class Exception1 : System.Exception
            {
            }
            """,
            """
            return MyResult.Failure(Tag1.A, new PayloadA(), new Exception1());
            """);
        return _helper.Verify(source);
    }
}
