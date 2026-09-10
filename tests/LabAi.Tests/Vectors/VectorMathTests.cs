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
}
