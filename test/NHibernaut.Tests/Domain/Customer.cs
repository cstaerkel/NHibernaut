using System.Collections.Generic;
using NHibernate.Mapping.ByCode;
using NHibernate.Mapping.ByCode.Conformist;

namespace NHibernaut.Tests.Domain;

// Parent/child with a lazy collection — used to trigger genuine N+1 lazy loading in a loop.
// (Named Customer/Order rather than Server/* to avoid clashing with the NHibernaut.Server namespace.)
public class Customer
{
    public virtual int Id { get; set; }
    public virtual string? Name { get; set; }
    public virtual IList<Order> Orders { get; set; } = new List<Order>();
}

public class Order
{
    public virtual int Id { get; set; }
    public virtual string? Description { get; set; }
    public virtual Customer? Customer { get; set; }
}

public class CustomerMap : ClassMapping<Customer>
{
    public CustomerMap()
    {
        Table("customers");
        Id(x => x.Id, m => m.Generator(Generators.Native));
        Property(x => x.Name);
        Bag(x => x.Orders, m =>
        {
            m.Key(k => k.Column("customer_id"));
            m.Cascade(Cascade.All);
            m.Inverse(true);
            // lazy by default — iterating Orders per parent forces a SELECT per parent (N+1).
        }, r => r.OneToMany());
    }
}

public class OrderMap : ClassMapping<Order>
{
    public OrderMap()
    {
        Table("orders");
        Id(x => x.Id, m => m.Generator(Generators.Native));
        Property(x => x.Description);
        ManyToOne(x => x.Customer, m => m.Column("customer_id"));
    }
}
