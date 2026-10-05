namespace ProductionIncident.Agents.Prompts;

/// <summary>JSON shapes the agents must return (mirror of ProductionIncident.Core.Contracts).</summary>
public static class OutputContracts
{
    public const string AgentEvidence = """
        {
          "agentName": "<your agent name>",
          "evidence": [ { "type": "log|trace|database|connection-pool|query|metric|deployment|code-change|config", "description": "<fact with numbers and UTC timestamps>", "source": "<tool name>" } ],
          "hypotheses": [ { "cause": "<possible cause>", "confidence": 0.0 } ],
          "openQuestions": [ "<what is still unknown and who could check it>" ]
        }
        """;

    public const string Triage = """
        {
          "category": "availability|latency|data|security|capacity|other",
          "severity": "SEV1|SEV2|SEV3|SEV4",
          "affectedServices": [ "<service>" ],
          "agentsToRun": [ "LogsAgent", "DatabaseAgent", "MetricsAgent", "DeploymentAgent" ],
          "strategy": "broad|focused",
          "summary": "<one sentence>"
        }
        """;

    public const string RootCause = """
        {
          "cause": "<trigger → mechanism → symptom, one or two sentences>",
          "confidence": 0.0,
          "supportingEvidence": [ "<evidence item, with source>" ],
          "contradictions": [ "<unresolved contradiction between agents/evidence>" ],
          "missingEvidence": [ "<specific fact that would confirm or refute the cause>" ]
        }
        """;

    public const string Supervisor = """
        {
          "agentsToRun": [ "<first = best starting specialist>", "..." ],
          "reason": "<one sentence>",
          "investigationComplete": false
        }
        """;

    public const string Remediation = """
        {
          "immediateMitigation": [ { "tool": "<exact tool name, e.g. rollback_deployment>", "arguments": { "service": "..." }, "risk": "low|medium|high", "rationale": "..." } ],
          "permanentFix": [ "..." ],
          "verificationPlan": [ "<read-only check that proves recovery>" ],
          "rollbackPlan": [ "<how to undo the mitigation>" ]
        }
        """;
}
