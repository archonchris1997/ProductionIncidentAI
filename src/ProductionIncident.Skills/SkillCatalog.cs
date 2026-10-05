using System.Reflection;

namespace ProductionIncident.Skills;

/// <summary>A Skill = procedure: how to perform a specialized task (blueprint §3.3). Tools execute; Skills guide.</summary>
public sealed record SkillDefinition(
    string Name,
    string Version,
    string Description,
    string Body,
    IReadOnlyDictionary<string, string> References)
{
    public string VersionTag => $"{Name}@{Version}";
}

public interface ISkillCatalog
{
    SkillDefinition? Get(string name);

    IReadOnlyList<SkillDefinition> All { get; }
}

/// <summary>Loads SKILL.md files (YAML-like front matter + markdown body) embedded in this assembly.</summary>
public sealed class EmbeddedSkillCatalog : ISkillCatalog
{
    private readonly Dictionary<string, SkillDefinition> _skills;

    public EmbeddedSkillCatalog()
    {
        var assembly = typeof(EmbeddedSkillCatalog).Assembly;
        var resources = assembly.GetManifestResourceNames()
            .Select(n => (Resource: n, Path: n.Replace('\\', '/')))
            .Where(x => x.Path.StartsWith("skills/", StringComparison.Ordinal))
            .ToList();

        _skills = new Dictionary<string, SkillDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var skillFile in resources.Where(x => x.Path.EndsWith("/SKILL.md", StringComparison.Ordinal)))
        {
            var folder = skillFile.Path[..skillFile.Path.LastIndexOf('/')];
            var references = resources
                .Where(x => x.Path.StartsWith(folder + "/references/", StringComparison.Ordinal))
                .ToDictionary(x => Path.GetFileName(x.Path), x => Read(assembly, x.Resource));

            var skill = Parse(Read(assembly, skillFile.Resource), fallbackName: folder["skills/".Length..], references);
            _skills[skill.Name] = skill;
        }
    }

    public IReadOnlyList<SkillDefinition> All => _skills.Values.OrderBy(s => s.Name).ToList();

    public SkillDefinition? Get(string name) => _skills.GetValueOrDefault(name);

    public static SkillDefinition Parse(string text, string fallbackName, IReadOnlyDictionary<string, string>? references = null)
    {
        var name = fallbackName;
        var version = "0.0.0";
        var description = "";
        var body = text.Replace("\r\n", "\n");

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
                        case "name": name = value; break;
                        case "version": version = value; break;
                        case "description": description = value; break;
                    }
                }

                body = body[(end + 4)..].TrimStart('\n');
            }
        }

        return new SkillDefinition(name, version, description, body.Trim(), references ?? new Dictionary<string, string>());
    }

    private static string Read(Assembly assembly, string resource)
    {
        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded skill resource '{resource}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
