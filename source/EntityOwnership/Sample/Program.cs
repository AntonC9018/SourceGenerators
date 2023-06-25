using EntityOwnership;
using EntityOwnership.Sample.EntityOwnership;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace Namespace;

public class Company : IOwner
{
    public string Id { get; set; }
}

public class Project : IOwnedBy<Company>
{
    public long Id { get; set; }
    public string CompanyId { get; set; }
    public Company Company { get; set; }
}

public class Task : IOwnedBy<Project>
{
    public int Id { get; set; }
    public long ProjectId { get; set; }
    public Project Project { get; set; }
}

public class Task2 : IOwnedBy<Company>
{
    public int Id { get; set; }
    public string CompanyId { get; set; }
    public Company Company { get; set; }
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

        // taskQueryable.DirectOwnerFilter("123"); // expected long, got string
        taskQueryable.RootOwnerFilter("Hello"); // works fine, filters the companies

        // projectQueryable.DirectOwnerFilter(123); // expected string
        projectQueryable.RootOwnerFilter("Hello"); // works fine, filters the companies

        // companyQueryable.DirectOwnerFilter(123); // this overload does not exist for root types
        companyQueryable.RootOwnerFilter("Hello"); // filters companies themselves.

        taskQueryable.RootOwnerFilterT("Hello"); // works fine, filters the companies
        taskQueryable.RootOwnerFilterT(123); // compiles, throws an exception at runtime

        EntityOwnershipHelper.GetRootOwnerType(typeof(Task)); // returns typeof(Company)
        EntityOwnershipHelper.GetDirectOwnerType(typeof(Task)); // returns typeof(Project)
        EntityOwnershipHelper.GetIdType(typeof(Task)); // returns typeof(int)
        EntityOwnershipHelper.SupportsDirectOwnerFilter(typeof(Task)); // returns true

        // Filtering for some specific owner type.
        // E.g. for tasks you can filter by project and by company.
        if (EntityOwnershipHelper.SupportsSomeOwnerFilter(
                typeof(Task), typeof(Project), typeof(long)))
        {
            // Since this is to be used in generic contexts, the type arguments are explicit.
            // It doesn't really make sense in concrete contexts (you can just specify the path manually).
            taskQueryable.SomeOwnerFilterT<Task, Project, long>(42L);
        }
    }
}

#pragma warning restore CS8618
