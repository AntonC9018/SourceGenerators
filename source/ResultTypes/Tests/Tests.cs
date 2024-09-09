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

            public static T As<T>(ResultBase val)
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

    [Fact]
    public Task Payloads()
    {
        return _helper.Verify(Config + """
            public readonly record struct Things(int p);
            public sealed class Exception1 : System.Exception
            {
            }
            public enum TestTag
            {
                None,
                A = 1,
                B = 2,
                C = 3,
            }
            public enum TestTag1
            {
                None,
                E = 1,
                F = 2,
                G = 3,
            }

            public static partial class Helper1
            {
                // Payload
                [GenerateResultType]
                public static Result1 Stuff1(int i)
                {
                    return Result1.Ok(new Things(5));
                }

                // Const tag
                [GenerateResultType]
                public static Result2 Stuff2(int i)
                {
                    return Result2.Ok(TestTag.A);
                }

                // Const tag x 2
                [GenerateResultType]
                public static Result2 Stuff2(int i)
                {
                    return Result2.Ok(TestTag.A);
                }

                // Tag
                [GenerateResultType]
                public static Result2 Stuff2(int i)
                {
                    return Result2.Ok(TestTag.A);
                }

            }
        """);
    }
}
