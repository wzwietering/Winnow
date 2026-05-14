using Shouldly;
using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;

namespace Winnow.Tests;

public class WinnowerUpsertMatchByValidationTests : TestBase
{
    [Fact]
    public void MatchBy_MethodCallExpression_Throws()
    {
        var options = new UpsertOptions();

        Should.Throw<ArgumentException>(() =>
            options.WithMatchBy<Product, string>(p => p.Name.ToLower()));
    }

    [Fact]
    public void MatchBy_NestedMemberAccess_Throws()
    {
        var options = new UpsertOptions();

        // p => p.Category.Id — Category is a navigation, the lambda body is a nested MemberExpression.
        Should.Throw<ArgumentException>(() =>
            options.WithMatchBy<Product, int?>(p => p.Category!.Id));
    }

    [Fact]
    public void MatchBy_ReferencesNavigationProperty_Throws()
    {
        using var context = CreateContext();
        var saver = new Winnower<Product, int>(context);

        var options = new UpsertOptions().WithMatchBy<Product, Category?>(p => p.Category);

        Should.Throw<ArgumentException>(() =>
            saver.Upsert(new[] { NewProduct("X") }, options));
    }

    [Fact]
    public void MatchBy_UnknownProperty_Throws()
    {
        using var context = CreateContext();
        var saver = new Winnower<Product, int>(context);

        // Build an expression whose body member name doesn't exist on the entity.
        // We use a real property here but the parser receives a synthesised LambdaExpression
        // pointing at a non-existent member name to verify the validator catches it.
        var p = System.Linq.Expressions.Expression.Parameter(typeof(Product), "p");
        // Use a property that exists on the CLR type but is not mapped (e.g., DisplayId is a getter-only computed string).
        var member = System.Linq.Expressions.Expression.PropertyOrField(p, nameof(Product.DisplayId));
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<Product, string>>(member, p);

        var options = new UpsertOptions { MatchBy = lambda };

        Should.Throw<ArgumentException>(() =>
            saver.Upsert(new[] { NewProduct("X") }, options));
    }

    [Fact]
    public void MatchBy_DuplicateMatchKeysInInputBatch_Throws()
    {
        using var context = CreateContext();
        var saver = new Winnower<CustomerOrder, int>(context);
        var batch = new[]
        {
            new CustomerOrder { OrderNumber = "DUPLICATE", CustomerName = "A", TotalAmount = 1m },
            new CustomerOrder { OrderNumber = "DUPLICATE", CustomerName = "B", TotalAmount = 2m }
        };

        var options = new UpsertOptions().WithMatchBy<CustomerOrder, string>(o => o.OrderNumber);

        Should.Throw<InvalidOperationException>(() => saver.Upsert(batch, options));
    }

    [Fact]
    public void MatchBy_AmbiguousMatch_MultipleRowsInDb_Throws()
    {
        using var context = CreateContext();

        // Seed two products that share a Name — Product.Name has no unique index so duplicates are allowed in the DB.
        context.Products.Add(new Product { Name = "Same", Price = 1m, Stock = 1, LastModified = DateTimeOffset.UtcNow });
        context.Products.Add(new Product { Name = "Same", Price = 2m, Stock = 2, LastModified = DateTimeOffset.UtcNow });
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var batch = new[] { new Product { Name = "Same", Price = 99m, Stock = 99, LastModified = DateTimeOffset.UtcNow } };
        var options = new UpsertOptions().WithMatchBy<Product, string>(p => p.Name);
        var saver = new Winnower<Product, int>(context);

        Should.Throw<InvalidOperationException>(() => saver.Upsert(batch, options));
    }

    [Fact]
    public void WithMatchBy_NullOptions_Throws()
    {
        Should.Throw<ArgumentNullException>(() =>
            UpsertOptionsExtensions.WithMatchBy<Product, string>(null!, p => p.Name));
    }

    [Fact]
    public void WithMatchBy_NullExpression_Throws()
    {
        var options = new UpsertOptions();
        Should.Throw<ArgumentNullException>(() =>
            options.WithMatchBy<Product, string>(null!));
    }

    [Fact]
    public void WithMatchBy_MethodCallExpression_ThrowsAtCallSite()
    {
        var options = new UpsertOptions();
        Should.Throw<ArgumentException>(() =>
            options.WithMatchBy<Product, string>(p => p.Name.ToLower()));
    }

    [Fact]
    public void WithMatchBy_NestedMemberAccess_ThrowsAtCallSite()
    {
        var options = new UpsertOptions();
        Should.Throw<ArgumentException>(() =>
            options.WithMatchBy<Product, int?>(p => p.Category!.Id));
    }

    [Fact]
    public void WithMatchBy_AnonymousProjectionWithMethodCall_ThrowsAtCallSite()
    {
        var options = new UpsertOptions();
        Should.Throw<ArgumentException>(() =>
            options.WithMatchBy<Product, object>(p => new { Lower = p.Name.ToLower() }));
    }

    [Fact]
    public void MatchBy_ReferencesPrimaryKey_Throws()
    {
        using var context = CreateContext();
        var saver = new Winnower<Product, int>(context);

        var options = new UpsertOptions().WithMatchBy<Product, int>(p => p.Id);

        var ex = Should.Throw<ArgumentException>(() =>
            saver.Upsert(new[] { NewProduct("X") }, options));
        ex.Message.ShouldContain("primary key", Case.Insensitive);
        ex.Message.ShouldContain(nameof(Product.Id));
    }

    [Fact]
    public void MatchBy_ReferencesPrimaryKey_InCompositeProjection_Throws()
    {
        using var context = CreateContext();
        var saver = new Winnower<Product, int>(context);

        var options = new UpsertOptions().WithMatchBy<Product>(p => new { p.Id, p.Name });

        Should.Throw<ArgumentException>(() =>
            saver.Upsert(new[] { NewProduct("X") }, options));
    }

    [Fact]
    public void UpsertOptions_MatchBy_Setter_IsNotPublic()
    {
        var setter = typeof(UpsertOptions).GetProperty(nameof(UpsertOptions.MatchBy))!.SetMethod!;
        setter.IsPublic.ShouldBeFalse(
            "MatchBy direct assignment would bypass shape validation; callers must use WithMatchBy.");
    }

    [Fact]
    public void MatchBy_ReferencesStoreGeneratedColumn_Throws()
    {
        using var context = CreateContext();
        var saver = new Winnower<Product, int>(context);

        // Version is configured with IsRowVersion → ValueGenerated.OnAddOrUpdate; not safe to match on.
        var options = new UpsertOptions().WithMatchBy<Product, byte[]>(p => p.Version);

        var ex = Should.Throw<ArgumentException>(() =>
            saver.Upsert(new[] { NewProduct("X") }, options));
        ex.Message.ShouldContain("store-generated", Case.Insensitive);
        ex.Message.ShouldContain(nameof(Product.Version));
    }

    private static Product NewProduct(string name) => new()
    {
        Name = name,
        Price = 1m,
        Stock = 1,
        LastModified = DateTimeOffset.UtcNow
    };
}
