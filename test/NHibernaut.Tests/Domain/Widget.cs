using NHibernate.Mapping.ByCode;
using NHibernate.Mapping.ByCode.Conformist;

namespace NHibernaut.Tests.Domain;

// Minimal entity used by the environment + walking-skeleton tests.
public class Widget
{
    public virtual int Id { get; set; }
    public virtual string? Name { get; set; }
}

public class WidgetMap : ClassMapping<Widget>
{
    public WidgetMap()
    {
        Table("widgets");
        Id(x => x.Id, m => m.Generator(Generators.Native));
        Property(x => x.Name);
    }
}
