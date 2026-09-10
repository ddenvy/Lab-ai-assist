using LabAi.Domain.ValueObjects;

namespace LabAi.Application.Rag;

/// <summary>
/// Builds the system and user prompts for grounded generation by concatenating retrieved chunks
/// into a single string with positional citations [S1]..[Sn]. No SK templates — plain string
/// composition so that PromptHash is provably the hash of exactly what the model saw.
/// </summary>
public static class PromptComposer
{
    public const string SystemPrompt = """
        You are a laboratory compliance assistant. Answer questions strictly based on the provided context fragments from SOPs and instrument data.

        Rules:
        1. Every factual claim must be supported by a citation in the format [S1], [S2], etc., referring to the numbered context fragments below.
        2. If the answer cannot be found in the provided context, respond with exactly: "Not found in sources."
        3. Do not invent numbers, tolerances, identifiers, or procedures not present in the context.
        4. After your answer, add a section "--- Rationale:" explaining which fragments support each claim.
        5. Respond in the same language as the question.
        """;

    /// <summary>
    /// Composes the user prompt from the question and retrieved chunks. Each chunk gets a
    /// positional citation tag [S1]..[Sn] prepended, so the model can reference them and the
    /// UI can map tags back to actual chunk IDs without trusting the model's numbering.
    /// </summary>
    public static (string UserPrompt, IReadOnlyList<CitedChunk> Citations) Compose(
        string question,
        IReadOnlyList<RetrievedChunk> chunks)
    {
        if (chunks.Count == 0)
        {
            return ($"Question: {question}", Array.Empty<CitedChunk>());
        }

        var citedChunks = new List<CitedChunk>(chunks.Count);
        var parts = new List<string>(chunks.Count + 1);
        parts.Add($"Question: {question}\n\nContext fragments:");

        for (var i = 0; i < chunks.Count; i++)
        {
            var index = i + 1;
            var chunk = chunks[i];
            var header = $"[S{index}] {chunk.Title} (v{chunk.Version}) — {chunk.SectionPath}";
            var text = $"{header}\n{chunk.Text}";
            parts.Add(text);

            citedChunks.Add(new CitedChunk(
                Index: index,
                ChunkId: chunk.ChunkId,
                DocumentId: chunk.DocumentId,
                Score: chunk.Score));
        }

        parts.Add("\nAnswer the question using only the context fragments above. Cite each claim with [S1], [S2], etc.");

        return (string.Join("\n\n", parts), citedChunks);
    }
}

/// <summary>A chunk retrieved from the vector store, ready to be cited in a prompt.</summary>
public sealed record RetrievedChunk(
    long ChunkId,
    long DocumentId,
    string Title,
    int Version,
    string SectionPath,
    string Text,
    double Score);
