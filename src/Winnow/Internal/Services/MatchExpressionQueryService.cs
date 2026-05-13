using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Winnow.Internal.Services;

/// <summary>
/// Executes a batched SELECT against an entity table that resolves user-supplied
/// MatchBy values to existing rows. Returns full entity instances so upsert can
/// copy primary-key and concurrency-token values onto the caller-supplied entities
/// before flipping them to <see cref="Microsoft.EntityFrameworkCore.EntityState.Modified"/>.
/// </summary>
internal class MatchExpressionQueryService
{
    private const int MaxChunkSize = 500;
    private const int SqlParameterBudget = 1800;

    private static readonly MethodInfo EfPropertyMethod =
        typeof(EF).GetMethod(nameof(EF.Property), BindingFlags.Public | BindingFlags.Static)
        ?? throw new InvalidOperationException("EF.Property<T>(object, string) method not found");

    private readonly DbContext _context;

    internal MatchExpressionQueryService(DbContext context) => _context = context;

    internal Dictionary<MatchKey, TEntity> QueryExisting<TEntity>(
        IReadOnlyList<IProperty> matchProperties,
        IReadOnlyList<object?[]> matchValueTuples) where TEntity : class
    {
        var nonNull = FilterNonNullTuples(matchValueTuples);
        if (nonNull.Count == 0) return new Dictionary<MatchKey, TEntity>();

        var dict = new Dictionary<MatchKey, TEntity>();
        foreach (var chunk in EnumerateChunks(nonNull, matchProperties.Count))
        {
            var rows = BuildQuery<TEntity>(matchProperties, chunk).ToList();
            Accumulate(rows, matchProperties, dict);
        }
        return dict;
    }

    internal async Task<Dictionary<MatchKey, TEntity>> QueryExistingAsync<TEntity>(
        IReadOnlyList<IProperty> matchProperties,
        IReadOnlyList<object?[]> matchValueTuples,
        CancellationToken cancellationToken) where TEntity : class
    {
        var nonNull = FilterNonNullTuples(matchValueTuples);
        if (nonNull.Count == 0) return new Dictionary<MatchKey, TEntity>();

        var dict = new Dictionary<MatchKey, TEntity>();
        foreach (var chunk in EnumerateChunks(nonNull, matchProperties.Count))
        {
            var rows = await BuildQuery<TEntity>(matchProperties, chunk).ToListAsync(cancellationToken);
            Accumulate(rows, matchProperties, dict);
        }
        return dict;
    }

    private static IEnumerable<List<object?[]>> EnumerateChunks(List<object?[]> source, int columnsPerTuple)
    {
        var chunkSize = ChunkSizeFor(columnsPerTuple);
        for (var start = 0; start < source.Count; start += chunkSize)
        {
            var len = Math.Min(chunkSize, source.Count - start);
            yield return source.GetRange(start, len);
        }
    }

    private static int ChunkSizeFor(int columnsPerTuple)
    {
        var budgetBased = SqlParameterBudget / Math.Max(1, columnsPerTuple);
        return Math.Max(1, Math.Min(MaxChunkSize, budgetBased));
    }

    private IQueryable<TEntity> BuildQuery<TEntity>(
        IReadOnlyList<IProperty> matchProperties,
        List<object?[]> chunk) where TEntity : class
    {
        var entityParam = Expression.Parameter(typeof(TEntity), "e");
        var predicate = BuildOrPredicate(entityParam, matchProperties, chunk);
        var whereLambda = Expression.Lambda<Func<TEntity, bool>>(predicate, entityParam);
        return _context.Set<TEntity>().AsNoTracking().Where(whereLambda);
    }

    private static Expression BuildOrPredicate(
        ParameterExpression entityParam,
        IReadOnlyList<IProperty> matchProperties,
        List<object?[]> tuples)
    {
        Expression? combined = null;
        foreach (var tuple in tuples)
        {
            var conjunction = BuildAndPredicate(entityParam, matchProperties, tuple);
            combined = combined is null ? conjunction : Expression.OrElse(combined, conjunction);
        }
        return combined!;
    }

    private static Expression BuildAndPredicate(
        ParameterExpression entityParam,
        IReadOnlyList<IProperty> matchProperties,
        object?[] tuple)
    {
        Expression? combined = null;
        for (var i = 0; i < matchProperties.Count; i++)
        {
            var prop = matchProperties[i];
            var efCall = BuildEfPropertyAccess(entityParam, prop);
            var valueExpr = BuildParameterizedValue(tuple[i], prop.ClrType);
            var eq = Expression.Equal(efCall, valueExpr);
            combined = combined is null ? eq : Expression.AndAlso(combined, eq);
        }
        return combined!;
    }

    private static Expression BuildEfPropertyAccess(ParameterExpression entityParam, IProperty prop)
    {
        var generic = EfPropertyMethod.MakeGenericMethod(prop.ClrType);
        return Expression.Call(generic, entityParam, Expression.Constant(prop.Name));
    }

    private static Expression BuildParameterizedValue(object? value, Type targetType)
    {
        // Wrap the value in a holder so EF Core treats the access as a captured local and
        // parameterizes the SQL, rather than inlining constants and bloating the query plan.
        var holderType = typeof(ValueHolder<>).MakeGenericType(targetType);
        var holder = Activator.CreateInstance(holderType, value)!;
        var holderConst = Expression.Constant(holder, holderType);
        return Expression.Property(holderConst, nameof(ValueHolder<object>.Value));
    }

    private static List<object?[]> FilterNonNullTuples(IReadOnlyList<object?[]> tuples)
    {
        var result = new List<object?[]>(tuples.Count);
        foreach (var tuple in tuples)
        {
            if (HasAnyNull(tuple)) continue;
            result.Add(tuple);
        }
        return result;
    }

    private static bool HasAnyNull(object?[] tuple)
    {
        foreach (var v in tuple)
        {
            if (v is null) return true;
        }
        return false;
    }

    private static void Accumulate<TEntity>(
        List<TEntity> rows,
        IReadOnlyList<IProperty> matchProperties,
        Dictionary<MatchKey, TEntity> destination) where TEntity : class
    {
        foreach (var entity in rows)
        {
            var matchKey = ExtractMatchKey(entity, matchProperties);
            if (!destination.TryAdd(matchKey, entity))
            {
                throw new InvalidOperationException(
                    "MatchBy resolved multiple existing rows for the same match values. " +
                    "The MatchBy expression must select a unique row. " +
                    "Add a unique constraint on the matched columns or refine MatchBy.");
            }
        }
    }

    private static MatchKey ExtractMatchKey<TEntity>(
        TEntity entity, IReadOnlyList<IProperty> matchProperties) where TEntity : class
    {
        var values = new object?[matchProperties.Count];
        for (var i = 0; i < matchProperties.Count; i++)
        {
            var propertyInfo = matchProperties[i].PropertyInfo
                ?? throw new InvalidOperationException(
                    $"MatchBy property '{matchProperties[i].Name}' is a shadow property; MatchBy requires CLR properties.");
            values[i] = propertyInfo.GetValue(entity);
        }
        return new MatchKey(values);
    }

    private sealed class ValueHolder<T>
    {
        public T Value { get; }
        public ValueHolder(T value) => Value = value;
    }
}
