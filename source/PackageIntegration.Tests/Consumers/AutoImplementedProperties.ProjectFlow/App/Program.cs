using System;
using AutoImplementedProperties.Attributes;
using Contracts;

var hello = new Hello { A = 1, B = "ok" };

if (hello.A != 1 || hello.B != "ok")
{
    throw new InvalidOperationException("Generated properties did not behave correctly.");
}

[AutoImplementProperties]
public sealed partial class Hello : IStuff { }
