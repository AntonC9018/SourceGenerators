using AutoConstructor.Attributes;

public interface Interface<T, V>
{
}

[AutoConstructor]
public abstract partial class Base<T, U, V>
{
    private Interface<T, V> _interface;
}

[AutoConstructor]
public sealed partial class Derived : Base<object, int, string>
{
    private object _value;
}
