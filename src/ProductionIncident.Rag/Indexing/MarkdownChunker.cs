using ProductionIncident.Core.Abstractions;

namespace ProductionIncident.Rag.Indexing;

public sealed record KnowledgeChunk(string ChunkId, string SourceId, string Title, string Kind, string Section, string Content, IReadOnlyList<string> Tags);

/// <summary>Splits markdown documents by "##" sections so retrieval returns focused chunks with their source id.</summary>
public static class MarkdownChunker
{
    public static IReadOnlyList<KnowledgeChunk> Chunk(KnowledgeDocument document, int maxChars = 1_500)
    {
        var chunks = new List<KnowledgeChunk>();
        var lines = document.Content.Replace("\r\n", "\n").Split('\n');
        var section = "Overview";
        var buffer = new List<string>();

        void Flush()
        {
            var text = string.Join('\n', buffer).Trim();
            buffer.Clear();
            if (text.Length == 0)
            {
                return;
            }

            for (var offset = 0; offset < text.Length; offset += maxChars)
            {
                var part = text.Substring(offset, Math.Min(maxChars, text.Length - offset));
                chunks.Add(new KnowledgeChunk($"{document.Id}#{chunks.Count}", document.Id, document.Title, document.Kind, section, part, document.Tags));
            }
        }

        foreach (var line in lines)
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                Flush();
                section = line[3..].Trim();
                continue;
            }

            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                continue; // document title, already in metadata
            }

            buffer.Add(line);
        }

        Flush();
        return chunks;
    }
}
