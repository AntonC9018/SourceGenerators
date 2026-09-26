using AutoImplementedProperties.Attributes;

public interface IStuff
{
    int A { get; set; }
    string B { get; set; }
}

[AutoImplementProperties]
public sealed partial class Hello : IStuff {}
