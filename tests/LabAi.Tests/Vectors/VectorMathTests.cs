using System.Runtime.InteropServices;
using LabAi.Application.Vectors;

namespace LabAi.Tests.Vectors;

public sealed class VectorMathTests
{
    [Fact]
    public void NormalizeScalesToUnitLength()
    {
        var vector = new[] { 3f, 4f };

        VectorMath.NormalizeInPlace(vector);

        vector[0].Should().BeApproximately(0.6f, 1e-6f);
        vector[1].Should().BeApproximately(0.8f, 1e-6f);
        var norm = MathF.Sqrt(vector[0] * vector[0] + vector[1] * vector[1]);
        norm.Should().BeApproximately(1f, 1e-5f);
    }

    [Fact]
    public void NormalizeLeavesZeroVectorUntouched()
    {
        var vector = new[] { 0f, 0f };

        VectorMath.NormalizeInPlace(vector);

        // Dividing by the zero norm would produce NaNs that poison every dot product; the zero
        // vector cannot win a ranking, so leaving it alone is both safe and cheap.
        vector.Should().Equal(0f, 0f);
    }

    [Fact]
    public void BlobDecodesToExactlyTheSameFloats()
    {
        float[] vector = [1.5f, -2.25f, 3.75f];

        var blob = VectorMath.ToFloat32Blob(vector);

        blob.Length.Should().Be(12);
        var decoded = MemoryMarshal.Cast<byte, float>(blob).ToArray();
        decoded.Should().Equal(vector);
    }

    [Fact]
    public void BlobIsLittleEndianFloat32WithoutHeader()
    {
        float[] vector = [1f];

        var blob = VectorMath.ToFloat32Blob(vector);

        // IEEE-754 1.0f little-endian, no header bytes: 00 00 80 3F.
        blob.Should().Equal(0x00, 0x00, 0x80, 0x3F);
    }

    [Fact]
    public void BlobDecodesBackThroughTheVectorMathHelper()
    {
        var vector = DeterministicVector(3072, seed: 7);

        var decoded = VectorMath.FromFloat32Blob(VectorMath.ToFloat32Blob(vector));

        decoded.Should().Equal(vector);
    }

    [Fact]
    public void BlobOfAliasedLengthIsRejected()
    {
        var blob = new byte[7];

        var act = () => VectorMath.FromFloat32Blob(blob);

        act.Should().Throw<ArgumentException>().WithParameterName("blob");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(256)]
    [InlineData(3072)]
    public void DotProductMatchesTheNaiveReferenceAtEveryDimension(int dimension)
    {
        var left = DeterministicVector(dimension, seed: 11);
        var right = DeterministicVector(dimension, seed: 13);

        var actual = VectorMath.DotProduct(left, right);
        var expected = NaiveDotProduct(left, right);

        // Parity is asserted against the same-scale float accumulation, so the tolerance stays
        // tight; 1e-3 relative would also pass a vectorized (reassociated) implementation, which
        // is exactly what must be allowed to change under our feet.
        actual.Should().BeApproximately(expected, 1e-4f * MathF.Max(1f, MathF.Abs(expected)));
    }

    [Fact]
    public void DotProductOfUnitVectorsEqualsCosineSimilarity()
    {
        // The semantic contract the retrieval layer leans on: with both sides normalized at write
        // time, a bare dot product IS the cosine similarity — no sqrt, no division in the scan.
        var left = DeterministicVector(64, seed: 3);
        var right = DeterministicVector(64, seed: 4);
        VectorMath.NormalizeInPlace(left);
        VectorMath.NormalizeInPlace(right);

        var cosine = NaiveDotProduct(left, right) /
                     (MathF.Sqrt(NaiveDotProduct(left, left)) * MathF.Sqrt(NaiveDotProduct(right, right)));

        VectorMath.DotProduct(left, right).Should().BeApproximately(cosine, 1e-5f);
    }

    [Fact]
    public void DotProductOfOrthogonalUnitVectorsIsZero()
    {
        VectorMath.DotProduct([1f, 0f, 0f], [0f, 1f, 0f]).Should().Be(0f);
    }

    [Fact]
    public void DotProductRejectsMismatchedLengths()
    {
        var act = () => VectorMath.DotProduct(new float[3], new float[4]);

        act.Should().Throw<ArgumentException>();
    }

    private static float[] DeterministicVector(int dimension, int seed)
    {
        var random = new Random(seed);
        var vector = new float[dimension];
        for (var i = 0; i < dimension; i++)
            vector[i] = random.NextSingle() * 2f - 1f;
        return vector;
    }

    private static float NaiveDotProduct(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        var sum = 0f;
        for (var i = 0; i < left.Length; i++)
            sum += left[i] * right[i];
        return sum;
    }
}
