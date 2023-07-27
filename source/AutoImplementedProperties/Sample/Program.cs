using AutoImplementedProperties.Attributes;

public interface IName
{
    public string Name { get; set; }
}

public interface IName1
{
    public string Name { get; set; }
}

public interface ITest : IMeat
{
    public int Burger { get; set; }
}

public interface IMeat
{
    public int Meat { get; set; }
}

public interface IName2
{
    public int Name { get; set; }
}

#pragma warning disable CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.

[AutoImplementProperties]
public partial class A : IName, IName1, IName2, ITest
{
    string IName.Name { get; set; }
}

#pragma warning restore CS8618


[AutoImplementProperties]
public partial class B : IName, IName1
{

}
