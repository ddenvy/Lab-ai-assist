using System.Security.Cryptography;
using System.Text;

namespace LabAi.Application.Rag;

/// <summary>
/// Computes a SHA256 hash over the fully rendered system + user prompts. Each field is
/// length-prefixed to prevent field-boundary injection attacks (the lesson from Mini-CDS
/// HashChain.AppendField: a bare separator like "|" is vulnerable because ("ab","c") and
/// ("a","bc") would produce the same concatenated string).
/// </summary>
public static class PromptHasher
{
    /// <summary>
    /// Returns a hex-encoded SHA256 of the system prompt, user prompt, and model ID, each
    /// length-prefixed with a 4-byte big-endian integer. Together with Temperature = 0 this
    /// triple forms reproducible evidence: the same hash against the same model implies the
    /// same answer.
    /// </summary>
    public static string Compute(string systemPrompt, String userPrompt, string modelId)
    {
        using var sha = SHA256.Create();

        // Length-prefix each field so that ("ab","c") ≠ ("a","bc").
        AppendField(sha, Encoding.UTF8.GetBytes(systemPrompt));
        AppendField(sha, Encoding.UTF8.GetBytes(userPrompt));
        AppendField(sha, Encoding.UTF8.GetBytes(modelId));

        // Finalize the hash computation.
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

        var hash = sha.Hash!;
        return Convert.ToHexStringLower(hash);
    }

    private static void AppendField(HashAlgorithm sha, byte[] field)
    {
        // 4-byte big-endian length prefix.
        var lengthBytes = new byte[4];
        var len = field.Length;
        lengthBytes[0] = (byte)(len >> 24);
        lengthBytes[1] = (byte)(len >> 16);
        lengthBytes[2] = (byte)(len >> 8);
        lengthBytes[3] = (byte)(len & 0xFF);

        sha.TransformBlock(lengthBytes, 0, 4, null, 0);
        sha.TransformBlock(field, 0, field.Length, null, 0);
    }
}
