using System.Reflection;
using Shouldly;

namespace Winnow.Tests.Internal;

/// <summary>
/// Guards against re-introducing the duplicated <c>HasAnyNull</c> helper that previously
/// lived in both <c>UpsertOperation</c> and <c>MatchExpressionQueryService</c>.
/// </summary>
public class MatchHelpersTests
{
    private static readonly string[] NullCheckHelperNames = { "HasAnyNull", "ContainsNull" };

    [Fact]
    public void NullCheckHelper_HasExactlyOneDefinition_InWinnowAssembly()
    {
        var winnowAssembly = typeof(UpsertOptions).Assembly;
        var definitions = winnowAssembly
            .GetTypes()
            .SelectMany(t => t.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
            .Where(m => NullCheckHelperNames.Contains(m.Name)
                && m.GetParameters().Length == 1
                && m.GetParameters()[0].ParameterType == typeof(object?[]))
            .Select(m => $"{m.DeclaringType!.FullName}.{m.Name}")
            .ToList();

        definitions.Count.ShouldBe(1,
            $"Expected a single match-tuple null-check helper; found: {string.Join(", ", definitions)}.");
    }
}
