using LabAi.Application.Vectors;

namespace LabAi.Tests.Vectors;

public sealed class SearchIndexTests
{
    [Fact]
    public void RowsAreLaidOutBackToBackInOneFlatBuffer()
    {
        var index = new SearchIndex(
            [10, 20],
            [1, 2],
            [1f, 0f, 0f, 1f],
            dimension: 2);

        index.Count.Should().Be(2);
        index.Vectors.Should().HaveCount(4);
        index.Vectors.AsSpan(1 * 2, 2).ToArray().Should().Equal(0f, 1f); // row 1 starts at i * dimension
    }

    [Fact]
    public void MismatchedIdArrayLengthsAreRejected()
    {
        var act = () => new SearchIndex([1, 2, 3], [1], [], 0);

        act.Should().Throw<ArgumentException>().WithParameterName("documentIds");
    }

    [Fact]
    public void VectorBufferNotFillingAllRowsIsRejected()
    {
        // Two rows of dimension 3 need 6 floats; 5 would mean row 1 is truncated mid-vector.
        var act = () => new SearchIndex([1, 2], [1, 2], [0f, 0f, 0f, 0f, 0f], 3);

        act.Should().Throw<ArgumentException>().WithParameterName("vectors");
    }

    [Fact]
    public void EmptyIndexHasZeroCountAndDimension()
    {
        SearchIndex.Empty.Count.Should().Be(0);
        SearchIndex.Empty.Dimension.Should().Be(0);
    }
}
