#:package Anton.AutoImplementedProperties.SourceGenerator
#:property TargetFramework=net11.0
#:property PublishAot=false

using AutoImplementedProperties.Attributes;

var hello = new Hello { A = 1, B = "ok" };

if (hello.A != 1 || hello.B != "ok")
{
    throw new InvalidOperationException("Generated properties did not behave correctly.");
}

public interface IStuff
{
    int A { get; set; }
    string B { get; set; }
}

[AutoImplementProperties]
public sealed partial class Hello : IStuff { }
