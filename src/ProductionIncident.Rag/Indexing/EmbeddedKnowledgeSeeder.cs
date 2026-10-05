using ProductionIncident.Core.Abstractions;

namespace ProductionIncident.Rag.Indexing;

/// <summary>
/// Ingests the seed corpus (docs/knowledge/**.md, embedded at build time): runbooks, architecture docs,
/// postmortems and the known-error catalog. Front matter: id, title, kind, tags.
/// </summary>
public static class EmbeddedKnowledgeSeeder
{
    public static IReadOnlyList<KnowledgeDocument> LoadDocuments()
    {
        var assembly = typeof(EmbeddedKnowledgeSeeder).Assembly;
        var documents = new List<KnowledgeDocument>();
        foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.Replace('\\', '/').StartsWith("knowledge/", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(resource);
            if (stream is null)
            {
                continue;
            }

            using var reader = new StreamReader(stream);
            documents.Add(Parse(reader.ReadToEnd(), Path.GetFileNameWithoutExtension(resource.Replace('\\', '/'))));
        }

        return documents;
    }

    public static async Task SeedAsync(IKnowledgeBase knowledgeBase, CancellationToken cancellationToken = default)
    {
        foreach (var document in LoadDocuments())
        {
            await knowledgeBase.IndexAsync(document, cancellationToken).ConfigureAwait(false);
        }
    }

    public static KnowledgeDocument Parse(string text, string fallbackId)
    {
        var body = text.Replace("\r\n", "\n");
        string id = fallbackId, title = fallbackId, kind = "doc";
        var tags = new List<string>();

        if (body.StartsWith("---\n", StringComparison.Ordinal))
        {
            var end = body.IndexOf("\n---", 4, StringComparison.Ordinal);
            if (end > 0)
            {
                foreach (var line in body[4..end].Split('\n'))
                {
                    var idx = line.IndexOf(':');
                    if (idx <= 0)
                    {
                        continue;
                    }

                    var key = line[..idx].Trim();
                    var value = line[(idx + 1)..].Trim().Trim('"');
                    switch (key)
                    {
                        case "id": id = value; break;
                        case "title": title = value; break;
                        case "kind": kind = value; break;
                        case "tags":
                            tags.AddRange(value.Trim('[', ']').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                            break;
                    }
                }

                body = body[(end + 4)..].TrimStart('\n');
            }
        }

        return new KnowledgeDocument(id, title, kind, body, tags);
    }
}
