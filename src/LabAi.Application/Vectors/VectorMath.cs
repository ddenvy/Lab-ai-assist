using System.Runtime.InteropServices;

namespace LabAi.Application.Vectors;

/// <summary>
/// Vector arithmetic shared by the ingest and search paths. Data-oriented design (AGENT.md 2.4):
/// spans, flat loops, no LINQ and no per-element allocations, so the JIT can auto-vectorize; the
/// search hot path never touches an interface boundary.
/// </summary>
public static class VectorMath
{
    /// <summary>
    /// Scales the vector to unit L2 length, in place. Stored vectors are normalized at write time,
    /// which is what lets search be a bare dot product with no <c>sqrt</c> in the scan. A zero
    /// vector is left untouched: dividing by its zero norm would produce NaNs, and it can never
    /// win a dot-product ranking anyway.
    /// </summary>
    public static void NormalizeInPlace(Span<float> vector)
    {
        // Double accumulator: summing 3072 float squares into a float loses more precision than
        // normalization itself tolerates. Normalization runs once per vector, not per query pair,
        // so the extra width costs nothing measurable.
        double sumOfSquares = 0;
        for (var i = 0; i < vector.Length; i++)
            sumOfSquares += (double)vector[i] * vector[i];

        var norm = Math.Sqrt(sumOfSquares);
        if (norm == 0)
            return;

        var scale = (float)(1.0 / norm);
        for (var i = 0; i < vector.Length; i++)
            vector[i] *= scale;
    }

    /// <summary>
    /// Encodes floats as raw IEEE-754 float32 little-endian bytes with no header — exactly the
    /// format the search path decodes zero-copy via <c>MemoryMarshal.Cast&lt;byte, float&gt;</c>.
    /// Dimension and model live in dedicated columns, not in the blob. Requires a little-endian
    /// host; every platform .NET runs on is one.
    /// </summary>
    public static byte[] ToFloat32Blob(ReadOnlySpan<float> vector)
    {
        var blob = new byte[vector.Length * sizeof(float)];
        MemoryMarshal.AsBytes(vector).CopyTo(blob);
        return blob;
    }
}
