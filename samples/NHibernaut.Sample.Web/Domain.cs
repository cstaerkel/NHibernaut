using System.Collections.Generic;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using NHibernate.Driver;
using NHibernate.Mapping.ByCode;
using NHibernate.Mapping.ByCode.Conformist;

namespace NHibernaut.Sample.Web;

public class Blog
{
    public virtual int Id { get; set; }
    public virtual string? Name { get; set; }
    public virtual IList<Post> Posts { get; set; } = new List<Post>();
}

public class Post
{
    public virtual int Id { get; set; }
    public virtual string? Title { get; set; }
    public virtual Blog? Blog { get; set; }
}

public class BlogMap : ClassMapping<Blog>
{
    public BlogMap()
    {
        Table("blogs");
        Id(x => x.Id, m => m.Generator(Generators.Native));
        Property(x => x.Name);
        Bag(x => x.Posts, m =>
        {
            m.Key(k => k.Column("blog_id"));
            m.Cascade(Cascade.All);
            m.Inverse(true);
        }, r => r.OneToMany());
    }
}

public class PostMap : ClassMapping<Post>
{
    public PostMap()
    {
        Table("posts");
        Id(x => x.Id, m => m.Generator(Generators.Native));
        Property(x => x.Title);
        ManyToOne(x => x.Blog, m => m.Column("blog_id"));
    }
}

/// <summary>Consumer-side NHibernate driver for Microsoft.Data.Sqlite (none ships with NHibernate 5.x).</summary>
public class MicrosoftDataSqliteDriver : DriverBase
{
    public override DbConnection CreateConnection() => new SqliteConnection();
    public override DbCommand CreateCommand() => new SqliteCommand();
    public override bool UseNamedPrefixInSql => true;
    public override bool UseNamedPrefixInParameter => true;
    public override string NamedPrefix => "@";
}
