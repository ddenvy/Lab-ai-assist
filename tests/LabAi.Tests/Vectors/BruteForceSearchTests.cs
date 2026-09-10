using LabAi.Application.Vectors;
using LabAi.Domain.ValueObjects;

namespace LabAi.Tests.Vectors;

public sealed class BruteForceSearchTests
{
    [Fact]
    public void TopKReturnsHitsInDescendingScoreOrder()
    {
        // Unit rows; the query (0.9, 0.4, 0.1) dots to 0.96 / 0.86 / 0.1 against rows 103/102/101.
        var index = new SearchIndex(
            [101, 102, 103],
            [1, 1, 2],
            [0f, 0f, 1f, 0.6f, 0.8f, 0f, 0.8f, 0.6f, 0f],
            dimension: 3);

        var hits = Search(index, [0.9f, 0.4f, 0.1f], k: 3, minScore: -1f);

        hits.Select(h => h.ChunkId).Should().Equal(103, 102, 101);
        hits[0].Score.Should().BeGreaterThan(hits[1].Score);
        hits[1].Score.Should().BeGreaterThan(hits[2].Score);
        // The document id rides along for the UI to resolve title and version.
        hits[0].DocumentId.Should().Be(2);
    }

    [Fact]
    public void HitsBelowMinScoreAreExcludedEntirely()
    {
        var index = new SearchIndex(
            [1, 2],
            [1, 1],
            [1f, 0f, 0.5f, 0.8660254f], // row 2 is 60° away from row 1
            dimension: 2);

        var hits = Search(index, [1f, 0f], k: 5, minScore: 0.7f);

        hits.Select(h => h.ChunkId).Should().Equal(1); // cos = 0.5 < 0.7 is dropped, not merely ranked lower
    }

    [Fact]
    public void KGreaterThanRowCountReturnsEveryQualifyingHit()
    {
        var index = new SearchIndex([5, 3], [1, 1], [1f, 0f, 0f, 1f], dimension: 2);

        var hits = Search(index, [1f, 0f], k: 10, minScore: 0f);

        hits.Should().HaveCount(2);
        hits.Select(h => h.ChunkId).Should().Equal(5, 3);
    }

    [Fact]
    public void EqualScoresBreakByAscendingChunkIdDeterministically()
    {
        // Rows 7 and 4 are the same unit vector → identical scores; only the tie-break may order them.
        var index = new SearchIndex([7, 4], [1, 1], [1f, 0f, 1f, 0f], dimension: 2);

        var first = Search(index, [1f, 0f], k: 2, minScore: 0f);
        var second = Search(index, [1f, 0f], k: 2, minScore: 0f);

        first.Select(h => h.ChunkId).Should().Equal(4, 7);
        second.Select(h => h.ChunkId).Should().Equal(4, 7); // two scans, one answer — no scan-order dependence
    }

    [Fact]
    public void LateRowReplacingTheCurrentWorstKeepsTheBufferSorted()
    {
        // Row 3 arrives last and must displace row 1 (the then-worst) inside a full k=2 buffer.
        var index = new SearchIndex(
            [1, 2, 3],
            [1, 1, 1],
            [0.5f, 0.8660254f, 1f, 0f, 0.9659258f, 0.2588191f], // 60°, 0°, 15° from the query axis
            dimension: 2);

        var hits = Search(index, [1f, 0f], k: 2, minScore: 0f);

        hits.Select(h => h.ChunkId).Should().Equal(2, 3);
    }

    [Fact]
    public void EmptyIndexYieldsZeroHits()
    {
        var hits = Search(SearchIndex.Empty, [], k: 5, minScore: 0f);

        hits.Should().BeEmpty();
    }

    [Fact]
    public void ZeroKWritesNothing()
    {
        var index = new SearchIndex([1], [1], [1f, 0f], dimension: 2);

        var hits = Search(index, [1f, 0f], k: 0, minScore: 0f);

        hits.Should().BeEmpty();
    }

    [Fact]
    public void QueryOfWrongDimensionIsRejected()
    {
        var index = new SearchIndex([1], [1], [1f, 0f], dimension: 2);

        var act = () => Search(index, [1f, 0f, 0f], k: 1, minScore: 0f);

        act.Should().Throw<ArgumentException>().WithParameterName("query");
    }

    [Fact]
    public void DestinationSmallerThanKIsRejected()
    {
        var index = new SearchIndex([1, 2], [1, 1], [1f, 0f, 0f, 1f], dimension: 2);

        var act = () => Search(index, [1f, 0f], k: 2, minScore: 0f, destinationLength: 1);

        act.Should().Throw<ArgumentException>().WithParameterName("destination");
    }

    private static List<SearchHit> Search(
        SearchIndex index,
        float[] query,
        int k,
        float minScore,
        int destinationLength = 16)
    {
        var destination = new SearchHit[destinationLength];
        var taken = BruteForceSearch.TopK(index, query, k, minScore, destination);
        return destination.Take(taken).ToList();
    }
}
