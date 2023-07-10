using AutoConstructor.Attributes;

public interface Interface<T, V>
{
}

public abstract partial class Base<T, U, V>
{
    private Interface<T, V> _interface;

    public Base(Interface<T, V> @interface)
    {
        this._interface = @interface;
    }
}

[AutoConstructor]
public sealed partial class Derived : Base<object, int, string>
{
    private object _value;
}
