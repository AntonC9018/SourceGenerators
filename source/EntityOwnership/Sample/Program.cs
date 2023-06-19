using EntityOwnership;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace Namespace;

public class Company : IOwner
{
    public string Id { get; set; }
}

public class Project : IOwned<Company>
{
    public long Id { get; set; }
    public string CompanyId { get; set; }
    public Company Company { get; set; }
}

public class Task : IOwned<Project>
{
    public int Id { get; set; }
    public long ProjectId { get; set; }
    public Project Project { get; set; }
}

partial class Program
{
    static void Main()
    {
        var companies = new List<Company>
        {
            new() { Id = "Hello" },
            new() { Id = "World" },
        };
        var projects = new List<Project>
        {
            new() { Id = 1, CompanyId = "Hello" },
            new() { Id = 2, CompanyId = "World" },
        };
        var tasks = new List<Task>
        {
            new() { Id = 1, ProjectId = 1 },
            new() { Id = 2, ProjectId = 1 },
            new() { Id = 3, ProjectId = 2 },
        };

        var taskQueryable = tasks.AsQueryable();
        var projectQueryable = projects.AsQueryable();
        var companyQueryable = companies.AsQueryable();

        taskQueryable.DirectOwnerFilter("123"); // expected long, got string
        taskQueryable.RootOwnerFilter("Hello"); // works fine, filters the companies

        projectQueryable.DirectOwnerFilter(123); // expected string
        projectQueryable.RootOwnerFilter("Hello"); // works fine, filters the companies

        companyQueryable.DirectOwnerFilter(123); // this overload does not exist for root types
        companyQueryable.RootOwnerFilter("Hello"); // filters companies themselves.

        taskQueryable.RootOwnerFilterT("Hello"); // works fine, filters the companies
        taskQueryable.RootOwnerFilterT(123); // compiles, throws an exception at runtime

        EntityOwnershipHelper.GetRootOwnerType(typeof(Task)); // returns typeof(Company)
        EntityOwnershipHelper.GetDirectOwnerType(typeof(Task)); // returns typeof(Project)
        EntityOwnershipHelper.GetIdType(typeof(Task)); // returns typeof(int)
        EntityOwnershipHelper.SupportsDirectOwnerFilter(typeof(Task)); // returns true
    }
}

#pragma warning restore CS8618
