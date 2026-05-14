using Shouldly;
using Winnow.Internal.Services;

namespace Winnow.Tests.Internal;

/// <summary>
/// Boundary tests for <see cref="MatchExpressionQueryService.ChunkSizeFor"/>. The chunking
/// integration tests cover the happy path; these pin the edge cases (parameter-budget
/// saturation, very wide keys, degenerate zero-column input) without a DB roundtrip.
/// </summary>
public class MatchExpressionQueryServiceChunkSizeTests
{
    [Theory]
    [InlineData(1, 500)]    // budget gives 1800/1=1800, capped at MaxChunkSize=500
    [InlineData(2, 500)]    // 1800/2=900, capped at 500
    [InlineData(3, 500)]    // 1800/3=600, capped at 500
    [InlineData(4, 450)]    // 1800/4=450 < 500, budget binds
    [InlineData(9, 200)]    // 1800/9=200
    [InlineData(1800, 1)]   // 1800/1800=1, floor binds
    [InlineData(1801, 1)]   // 1800/1801=0 → Math.Max(1, ...)=1
    [InlineData(10_000, 1)] // pathological wide key
    public void ChunkSizeFor_KnownInputs_ReturnsExpected(int columns, int expected)
    {
        MatchExpressionQueryService.ChunkSizeFor(columns).ShouldBe(expected);
    }

    [Fact]
    public void ChunkSizeFor_ZeroColumns_FlooredToOne()
    {
        // Math.Max(1, 0) protects the division; Math.Max(1, ...) protects the return.
        MatchExpressionQueryService.ChunkSizeFor(0).ShouldBeGreaterThanOrEqualTo(1);
    }
}
