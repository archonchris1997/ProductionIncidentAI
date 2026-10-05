using System.Text.RegularExpressions;
using ProductionIncident.Agents.Runtime;
using ProductionIncident.Core.Contracts;

namespace ProductionIncident.Infrastructure.Simulation;

internal abstract record ScriptAction;

internal sealed record CallTool(string Name, Dictionary<string, object?> Arguments) : ScriptAction;

internal sealed record FinalAnswer(object Payload) : ScriptAction;

/// <summary>
/// Deterministic "reasoning" of each agent for the offline scripted model. It reads tool results the same way an
/// LLM would (from summaries), decides the next tool call, hands off when a clue points to another specialist,
/// and returns the structured JSON contract. Used for local runs, CI and the regression evals — not a real model.
/// </summary>
internal static partial class ScriptedAgents
{
    [GeneratedRegex("\\bt-[0-9a-f]{6}\\b")]
    private static partial Regex TraceIdRegex();

    [GeneratedRegex("\\b([a-z][a-z0-9]*(?:-[a-z0-9]+)*-(?:api|service|svc|worker|gateway))\\b")]
    private static partial Regex ServiceRegex();

    [GeneratedRegex("\\b([a-z][a-z0-9]*(?:-[a-z0-9]+)*-db)\\b")]
    private static partial Regex DatabaseRegex();

    [GeneratedRegex("(v[0-9][0-9A-Za-z.\\-]*) at [0-9:]+ UTC \\((\\d+) minutes before the incident[^)]*\\), previous version (v[0-9][0-9A-Za-z.\\-]*)")]
    private static partial Regex RecentDeploymentRegex();

    [GeneratedRegex("Release diff (v[0-9][0-9A-Za-z.\\-]*) → (v[0-9][0-9A-Za-z.\\-]*)")]
    private static partial Regex ReleaseDiffRegex();

    [GeneratedRegex("([A-Za-z0-9]+:[A-Za-z0-9]+) changed from (\\S+) to (\\S+)")]
    private static partial Regex ConfigChangeRegex();

    [GeneratedRegex("at ([0-9]{2}:[0-9]{2} UTC) \\((\\d+) minutes before the incident\\)")]
    private static partial Regex ConfigTimingRegex();

    [GeneratedRegex("\\((\\d+(?:\\.\\d+)?x)\\)")]
    private static partial Regex MultiplierRegex();

    [GeneratedRegex("on (\\d+) replicas")]
    private static partial Regex ReplicasRegex();

    public static ScriptAction Next(ScriptedConversation c) => c.Agent switch
    {
        AgentNames.Triage => new FinalAnswer(Triage(c)),
        AgentNames.Logs => Specialist(c, LogsPlan(c), LogsHandoff(c), () => LogsEvidence(c)),
        AgentNames.Database => Specialist(c, DatabasePlan(c), null, () => DatabaseEvidence(c)),
        AgentNames.Metrics => Specialist(c, MetricsPlan(c), null, () => MetricsEvidence(c)),
        AgentNames.Deployment => Specialist(c, DeploymentPlan(c), DeploymentHandoff(c), () => DeploymentEvidence(c)),
        AgentNames.RootCause => Specialist(c, RootCausePlan(c), null, () => RootCause(c)),
        AgentNames.Supervisor => new FinalAnswer(Supervisor(c)),
        AgentNames.Remediation => Specialist(c, RemediationPlan(c), null, () => Remediation(c)),
        "Judge" => new FinalAnswer(new { groundedness = 0.9, completeness = 0.9, relevance = 0.9, rationale = "Scripted judge: evidence-backed root cause and matching remediation." }),
        _ => new FinalAnswer(new { }),
    };

    // ───────────────────────────────────────────────────────── shared

    private static ScriptAction Specialist(ScriptedConversation c, IEnumerable<CallTool> plan, (string Agent, string Reason)? handoff, Func<object> final)
    {
        foreach (var step in plan)
        {
            if (c.Tools.Contains(step.Name) && !c.Called.Contains(step.Name))
            {
                return step;
            }
        }

        if (handoff is { } h && c.Mode == AgentModes.DeepDive && c.Tools.Contains(HandoffTool.Name) && !c.Called.Contains(HandoffTool.Name))
        {
            return new CallTool(HandoffTool.Name, new Dictionary<string, object?> { ["agent"] = h.Agent, ["reason"] = h.Reason });
        }

        return new FinalAnswer(final());
    }

    private static CallTool Call(string name, params (string Key, object? Value)[] args) =>
        new(name, args.ToDictionary(a => a.Key, a => a.Value));

    private static string Service(ScriptedConversation c)
    {
        var service = c.ContextString("service");
        if (!string.IsNullOrWhiteSpace(service))
        {
            return service;
        }

        var match = ServiceRegex().Match($"{c.ContextString("title")} {c.ContextString("description")}");
        return match.Success ? match.Groups[1].Value : "unknown-service";
    }

    private static string Database(ScriptedConversation c, string fallback) =>
        DatabaseRegex().Match(c.Corpus) is { Success: true } m ? m.Groups[1].Value : fallback;

    private static bool Has(string? text, params string[] all) =>
        text is not null && all.All(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));

    private static bool Any(string? text, params string[] any) =>
        text is not null && any.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));

    private static object Evidence(string agent, List<object> evidence, List<object> hypotheses, List<string> questions) =>
        new { agentName = agent, evidence, hypotheses, openQuestions = questions };

    private static object Ev(string type, string? description, string source) => new { type, description = description ?? "", source };

    private static object Hyp(string cause, double confidence) => new { cause, confidence };

    // ───────────────────────────────────────────────────────── Triage

    private static object Triage(ScriptedConversation c)
    {
        var text = $"{c.ContextString("title")} {c.ContextString("description")}";
        var services = ServiceRegex().Matches(text.ToLowerInvariant()).Select(m => m.Groups[1].Value).ToList();
        var known = c.ContextString("service");
        if (!string.IsNullOrWhiteSpace(known) && !services.Contains(known))
        {
            services.Insert(0, known);
        }

        services = services.Distinct().ToList();
        var category = Any(text, "latency", "slow") ? "latency"
            : Any(text, "500", "502", "503", "error", "fail", "timeout", "unavailable", "down") ? "availability"
            : "other";
        var severity = Any(text, "outage", "down", "all customers") ? "SEV1" : "SEV2";

        return new
        {
            category,
            severity,
            affectedServices = services,
            agentsToRun = AgentNames.Specialists,
            strategy = "broad",
            summary = $"{category} incident on {string.Join(", ", services)}; starting a broad concurrent investigation.",
        };
    }

    // ───────────────────────────────────────────────────────── Logs

    private static IEnumerable<CallTool> LogsPlan(ScriptedConversation c)
    {
        var service = Service(c);
        yield return Call("get_recent_errors", ("service", service), ("minutes", 120));

        var traceId = TraceIdRegex().Match(c.Obs("get_recent_errors") ?? "");
        if (traceId.Success)
        {
            yield return Call("get_trace", ("traceId", traceId.Value));
        }
    }

    private static (string, string)? LogsHandoff(ScriptedConversation c) =>
        Any(c.AllObservations, "connection from the pool", "pool exhaustion")
            ? (AgentNames.Database, "Logs show SQL connection pool exhaustion: check the connection pool history at the incident timestamp.")
            : null;

    private static object LogsEvidence(ScriptedConversation c)
    {
        var evidence = new List<object>();
        var hypotheses = new List<object>();
        var questions = new List<string>();
        var obs = c.AllObservations;

        if (c.Obs("get_recent_errors") is { } errors)
        {
            evidence.Add(Ev("log", errors, "get_recent_errors"));
        }

        if (c.Obs("get_trace") is { } trace)
        {
            evidence.Add(Ev("trace", trace, "get_trace"));
        }

        if (Any(obs, "connection from the pool", "pool exhaustion"))
        {
            hypotheses.Add(Hyp("Database connection pool exhaustion (possible connection leak) causing SQL timeouts and HTTP 500", 0.75));
            questions.Add("Was the database connection pool saturated at the incident timestamp? (DatabaseAgent: connection pool history)");
        }
        else if (Any(obs, "cpu-bound", "thread pool starvation"))
        {
            hypotheses.Add(Hyp("CPU-bound request processing is starving the .NET thread pool, causing request timeouts", 0.65));
        }
        else if (Any(obs, "httpclient timeout", "client timeout"))
        {
            hypotheses.Add(Hyp("Outbound HTTP client timeout (0.5 s) to payment-gateway is too low for its normal latency", 0.7));
            questions.Add("Why was the HTTP client timeout lowered? (DeploymentAgent: configuration changes)");
        }
        else
        {
            questions.Add("No clear error signature in the logs.");
        }

        return Evidence(AgentNames.Logs, evidence, hypotheses, questions);
    }

    // ───────────────────────────────────────────────────────── Database

    private static IEnumerable<CallTool> DatabasePlan(ScriptedConversation c)
    {
        var service = Service(c);
        if (c.Mode == AgentModes.DeepDive)
        {
            yield return Call("get_connection_pool", ("service", service), ("minutes", 120));
            if (Any(c.Focus, "lock", "deadlock"))
            {
                yield return Call("get_deadlocks", ("service", service), ("minutes", 120));
            }
        }
        else
        {
            yield return Call("get_database_health", ("service", service));
            yield return Call("get_slow_queries", ("service", service), ("minutes", 120));
        }
    }

    private static object DatabaseEvidence(ScriptedConversation c)
    {
        var evidence = new List<object>();
        var hypotheses = new List<object>();
        var questions = new List<string>();
        var db = Database(c, "the database");

        if (c.Obs("get_database_health") is { } health)
        {
            evidence.Add(Ev("database", health, "get_database_health"));
        }

        if (c.Obs("get_slow_queries") is { } slow)
        {
            evidence.Add(Ev("query", slow, "get_slow_queries"));
        }

        if (c.Obs("get_deadlocks") is { } deadlocks)
        {
            evidence.Add(Ev("database", deadlocks, "get_deadlocks"));
        }

        if (c.Obs("get_connection_pool") is { } pool)
        {
            evidence.Add(Ev("connection-pool", pool, "get_connection_pool"));
            if (Any(pool, "saturated", "leak"))
            {
                hypotheses.Add(Hyp($"Connection leak in {Service(c)}: connections are not returned to the pool, exhausting the {db} connection pool (saturated 100/100 during the incident)", 0.88));
            }
            else
            {
                hypotheses.Add(Hyp($"{db} connection pool was normal during the incident: database not the cause", 0.75));
            }
        }
        else if (c.Obs("get_database_health") is { } h && Has(h, "healthy"))
        {
            hypotheses.Add(Hyp($"{db} is healthy at the moment; database not the cause", 0.7));
            questions.Add("Current snapshot only: was the connection pool saturated at the incident timestamp? (connection pool history)");
        }

        return Evidence(AgentNames.Database, evidence, hypotheses, questions);
    }

    // ───────────────────────────────────────────────────────── Metrics

    private static IEnumerable<CallTool> MetricsPlan(ScriptedConversation c)
    {
        var service = Service(c);
        foreach (var tool in new[] { "get_error_rate", "get_latency", "get_cpu", "get_memory", "get_request_rate" })
        {
            yield return Call(tool, ("service", service), ("minutes", 120));
        }
    }

    private static object MetricsEvidence(ScriptedConversation c)
    {
        var evidence = new List<object>();
        var hypotheses = new List<object>();
        var questions = new List<string>();

        foreach (var tool in new[] { "get_error_rate", "get_latency", "get_cpu", "get_memory", "get_request_rate" })
        {
            if (c.Obs(tool) is { } o)
            {
                evidence.Add(Ev("metric", o, tool));
            }
        }

        var cpu = c.Obs("get_cpu");
        var requests = c.Obs("get_request_rate");
        var latency = c.Obs("get_latency");

        if (Has(cpu, "saturated") && Has(requests, "surge"))
        {
            var multiplier = MultiplierRegex().Match(requests!) is { Success: true } m ? m.Groups[1].Value : "multiple";
            hypotheses.Add(Hyp($"CPU saturation caused by a traffic surge ({multiplier} the baseline request rate)", 0.85));
        }
        else if (Has(cpu, "saturated"))
        {
            hypotheses.Add(Hyp("CPU saturation of the service", 0.7));
        }
        else if (Has(latency, "capped"))
        {
            hypotheses.Add(Hyp("Requests fail fast at ~500 ms because of a client-side timeout", 0.65));
        }
        else if (Has(latency, "timeout"))
        {
            hypotheses.Add(Hyp("Requests are blocked waiting on a downstream dependency (p99 latency close to a timeout)", 0.6));
            questions.Add("Which dependency are requests waiting on? (LogsAgent: traces)");
        }

        return Evidence(AgentNames.Metrics, evidence, hypotheses, questions);
    }

    // ───────────────────────────────────────────────────────── Deployment

    private static IEnumerable<CallTool> DeploymentPlan(ScriptedConversation c)
    {
        var service = Service(c);
        yield return Call("get_recent_deployments", ("service", service), ("hours", 72));

        if (c.Mode == AgentModes.DeepDive)
        {
            if (RecentDeploymentRegex().Match(c.Obs("get_recent_deployments") ?? "") is { Success: true } d && int.Parse(d.Groups[2].Value) <= 60)
            {
                yield return Call("get_release_diff", ("service", service), ("fromVersion", d.Groups[3].Value), ("toVersion", d.Groups[1].Value));
            }

            if (Any(c.Focus, "config"))
            {
                yield return Call("get_config_changes", ("service", service), ("hours", 24));
            }
        }
        else
        {
            yield return Call("get_config_changes", ("service", service), ("hours", 24));
        }
    }

    private static (string, string)? DeploymentHandoff(ScriptedConversation c) =>
        Any(c.Obs("get_release_diff"), "dispose", "connection")
            ? (AgentNames.Database, "Release diff shows the SqlConnection is no longer disposed: confirm the connection leak with the connection pool history at the incident timestamp (idle sessions per client).")
            : null;

    private static object DeploymentEvidence(ScriptedConversation c)
    {
        var evidence = new List<object>();
        var hypotheses = new List<object>();
        var questions = new List<string>();
        var deployments = c.Obs("get_recent_deployments");
        var config = c.Obs("get_config_changes");
        var diff = c.Obs("get_release_diff");

        if (deployments is not null)
        {
            evidence.Add(Ev("deployment", deployments, "get_recent_deployments"));
        }

        if (config is not null)
        {
            evidence.Add(Ev("config", config, "get_config_changes"));
        }

        if (diff is not null)
        {
            evidence.Add(Ev("code-change", diff, "get_release_diff"));
        }

        var recent = RecentDeploymentRegex().Match(deployments ?? "");
        var configChange = ConfigChangeRegex().Match(config ?? "");

        if (diff is not null && Any(diff, "dispose", "disposes"))
        {
            var to = ReleaseDiffRegex().Match(diff) is { Success: true } r ? r.Groups[2].Value : "the latest release";
            hypotheses.Add(Hyp($"Release {to} removed SqlConnection disposal in OrderRepository → connection leak (connections never returned to the pool)", 0.85));
        }
        else if (recent.Success && int.Parse(recent.Groups[2].Value) <= 60)
        {
            hypotheses.Add(Hyp($"Release {recent.Groups[1].Value} (deployed {recent.Groups[2].Value} minutes before the incident) introduced a regression", 0.6));
            questions.Add($"What changed in {recent.Groups[1].Value}? Release diff {recent.Groups[3].Value} → {recent.Groups[1].Value} needed (DeploymentAgent deep dive).");
        }

        if (configChange.Success)
        {
            hypotheses.Add(Hyp($"Configuration change {configChange.Groups[1].Value} {configChange.Groups[2].Value} → {configChange.Groups[3].Value} shortly before the incident caused the failures", 0.85));
        }

        if (!recent.Success && Has(deployments, "No recent deployment"))
        {
            hypotheses.Add(configChange.Success
                ? Hyp($"No recent deployment of {Service(c)}: not a release regression", 0.75)
                : Hyp("No recent deployment or configuration change: not a release regression", 0.8));
        }

        return Evidence(AgentNames.Deployment, evidence, hypotheses, questions);
    }

    // ───────────────────────────────────────────────────────── Root cause

    private static IEnumerable<CallTool> RootCausePlan(ScriptedConversation c)
    {
        var top = c.ContextList("hypotheses").FirstOrDefault() ?? c.ContextString("title");
        var query = $"{Service(c)} {StripConfidence(top)}";
        yield return Call("search_knowledge", ("query", query));
        yield return Call("recall_similar_incidents", ("query", query));
    }

    private static string StripConfidence(string hypothesis)
    {
        var idx = hypothesis.IndexOf(" (confidence", StringComparison.Ordinal);
        return idx > 0 ? hypothesis[..idx] : hypothesis;
    }

    private static object RootCause(ScriptedConversation c)
    {
        var corpus = c.Corpus;
        var facts = c.ContextList("establishedFacts");
        var service = Service(c);
        var db = Database(c, "the database");
        var knowledge = c.Obs("search_knowledge");
        var memory = c.Obs("recall_similar_incidents");

        List<string> Support(params string[] keywords)
        {
            var list = facts.Where(f => keywords.Any(k => f.Contains(k, StringComparison.OrdinalIgnoreCase))).ToList();
            if (knowledge is not null && knowledge.StartsWith("Knowledge:", StringComparison.Ordinal))
            {
                list.Add($"Organizational knowledge: {knowledge["Knowledge:".Length..].Trim()}");
            }

            if (memory is not null && memory.StartsWith("Similar past incident", StringComparison.Ordinal))
            {
                list.Add($"Long-term memory (context, not proof): {memory}");
            }

            return list;
        }

        // A. Connection leak confirmed: code change + historical pool saturation.
        if (Has(corpus, "saturated 100/100") && Any(corpus, "no longer disposes", "disposal"))
        {
            var version = ReleaseDiffRegex().Match(corpus) is { Success: true } r ? r.Groups[2].Value : "the latest release";
            var support = Support("prior to obtaining a connection", "saturated 100/100", "no longer disposes", "minutes before the incident", "CPU normal");
            if (Has(corpus, "currently healthy"))
            {
                support.Add($"The 'currently healthy' {db} snapshot was taken after pod restarts released the leaked connections; the pool history shows saturation at incident time (contradiction resolved by evidence).");
            }

            return new
            {
                cause = $"Release {version} of {service} removed SqlConnection disposal in OrderRepository (connection leak): connections were never returned to the pool, the {db} connection pool saturated at 100/100 and requests failed with SQL connection timeouts (HTTP 500).",
                confidence = 0.92,
                supportingEvidence = support,
                contradictions = Array.Empty<string>(),
                missingEvidence = Array.Empty<string>(),
            };
        }

        // B. Connection pool exhaustion suspected, cause not yet proven.
        if (Any(corpus, "connection from the pool", "pool exhaustion"))
        {
            var missing = new List<string>();
            if (RecentDeploymentRegex().Match(corpus) is { Success: true } d)
            {
                missing.Add($"Release diff {d.Groups[3].Value} → {d.Groups[1].Value}: connection handling changes (DeploymentAgent)");
            }

            missing.Add($"Connection pool history of {db} at the incident timestamp (DatabaseAgent)");
            return new
            {
                cause = $"Probable connection leak in {service}: SQL connection pool exhaustion caused SQL timeouts and HTTP 500, possibly introduced by the latest release.",
                confidence = 0.62,
                supportingEvidence = Support("prior to obtaining a connection", "minutes before the incident", "CPU normal"),
                contradictions = c.ContextList("conflicts"),
                missingEvidence = missing,
            };
        }

        // C. CPU saturation from traffic.
        if (Has(corpus, "CPU saturated") && Has(corpus, "surge"))
        {
            var multiplier = MultiplierRegex().Match(corpus) is { Success: true } m ? m.Groups[1].Value : "several times";
            return new
            {
                cause = $"CPU saturation on {service} caused by a traffic surge ({multiplier} the baseline request rate); requests time out in CPU-bound ranking. No recent deployment or configuration change.",
                confidence = 0.88,
                supportingEvidence = Support("CPU saturated", "surge", "CPU-bound", "No recent deployment"),
                contradictions = c.ContextList("conflicts"),
                missingEvidence = Array.Empty<string>(),
            };
        }

        // D. Bad configuration change.
        if (ConfigChangeRegex().Match(corpus) is { Success: true } cfg && Any(corpus, "timeout"))
        {
            var when = ConfigTimingRegex().Match(corpus) is { Success: true } t ? $" at {t.Groups[1].Value}" : "";
            return new
            {
                cause = $"Configuration change{when}: {cfg.Groups[1].Value} lowered from {cfg.Groups[2].Value} to {cfg.Groups[3].Value} ms, so {service} times out before its dependency answers (~800 ms) and requests fail.",
                confidence = 0.91,
                supportingEvidence = Support("Configuration change", "HttpClient timeout", "canceled by the client timeout", "capped"),
                contradictions = c.ContextList("conflicts"),
                missingEvidence = Array.Empty<string>(),
            };
        }

        var best = c.ContextList("hypotheses").FirstOrDefault();
        return new
        {
            cause = best is null ? "Undetermined" : StripConfidence(best),
            confidence = 0.4,
            supportingEvidence = facts.Take(3).ToList(),
            contradictions = c.ContextList("conflicts"),
            missingEvidence = c.ContextList("openQuestions").DefaultIfEmpty("More evidence from all specialists").ToList(),
        };
    }

    // ───────────────────────────────────────────────────────── Supervisor

    private static object Supervisor(ScriptedConversation c)
    {
        var missing = c.ContextList("missingEvidence");
        var agents = new List<string>();
        foreach (var item in missing)
        {
            var agent = Any(item, "DeploymentAgent", "diff", "release", "config") ? AgentNames.Deployment
                : Any(item, "DatabaseAgent", "pool", "database", "query") ? AgentNames.Database
                : Any(item, "MetricsAgent", "cpu", "latency", "metric") ? AgentNames.Metrics
                : Any(item, "LogsAgent", "log", "trace") ? AgentNames.Logs
                : null;
            if (agent is not null && !agents.Contains(agent))
            {
                agents.Add(agent);
            }
        }

        return agents.Count == 0
            ? new { agentsToRun = Array.Empty<string>(), reason = "No specialist can obtain the missing evidence: escalate to the on-call engineer.", investigationComplete = true }
            : new { agentsToRun = agents.ToArray(), reason = $"Resolve the contradiction with evidence: {string.Join("; ", missing)}.", investigationComplete = false };
    }

    // ───────────────────────────────────────────────────────── Remediation

    private static IEnumerable<CallTool> RemediationPlan(ScriptedConversation c)
    {
        yield return Call("get_deployment_status", ("service", Service(c)));
        yield return Call("search_knowledge", ("query", $"mitigation {StripConfidence(c.ContextString("currentHypothesis"))}"), ("kind", "runbook"));
    }

    private static object Remediation(ScriptedConversation c)
    {
        var service = Service(c);
        var cause = c.ContextString("currentHypothesis");
        var corpus = c.Corpus;
        var runbook = c.Obs("search_knowledge") ?? "";

        if (Any(cause, "dispos", "leak") && RecentDeploymentRegex().Match(corpus) is { Success: true } d)
        {
            var current = d.Groups[1].Value;
            var previous = d.Groups[3].Value;
            return new
            {
                immediateMitigation = new[]
                {
                    new { tool = "rollback_deployment", arguments = new Dictionary<string, object?> { ["service"] = service, ["targetVersion"] = previous }, risk = "low", rationale = $"{previous} is the last known-good version and disposes connections; rolling back stops the leak immediately ({runbook})." },
                },
                permanentFix = new[]
                {
                    $"Restore 'await using' for the SqlConnection in OrderRepository.GetOpenOrdersAsync and release it as a fix on top of {current}.",
                    "Enable analyzer rule CA2000 (dispose objects before losing scope) as an error in Checkout.Infrastructure.",
                    "Add an integration test asserting connection pool usage stays bounded under load.",
                },
                verificationPlan = new[]
                {
                    $"get_error_rate({service}) back under 1%.",
                    $"get_connection_pool({service}): active connections stable, no idle sessions accumulating.",
                    $"get_latency({service}): p99 back near baseline.",
                },
                rollbackPlan = new[] { $"If {previous} misbehaves, roll forward to the fixed release; never redeploy {current} as is." },
            };
        }

        if (Has(cause, "CPU saturation") && Has(cause, "traffic"))
        {
            var replicas = ReplicasRegex().Match(c.Obs("get_deployment_status") ?? corpus) is { Success: true } r ? int.Parse(r.Groups[1].Value) : 4;
            var target = Math.Min(50, replicas * 3);
            return new
            {
                immediateMitigation = new[]
                {
                    new { tool = "scale_service", arguments = new Dictionary<string, object?> { ["service"] = service, ["replicas"] = target }, risk = "low", rationale = $"Scale from {replicas} to {target} replicas to absorb the traffic surge; reversible." },
                },
                permanentFix = new[] { "Raise the HPA max replicas and scale on request rate, not only CPU.", "Profile RankingService.Score to reduce CPU per request." },
                verificationPlan = new[] { $"get_cpu({service}) below 70%.", $"get_error_rate({service}) back under 1%." },
                rollbackPlan = new[] { $"Scale {service} back to {replicas} replicas once traffic returns to baseline." },
            };
        }

        if (ConfigChangeRegex().Match(corpus) is { Success: true } cfg && Has(cause, "Configuration change"))
        {
            return new
            {
                immediateMitigation = new[]
                {
                    new { tool = "change_configuration", arguments = new Dictionary<string, object?> { ["service"] = service, ["key"] = cfg.Groups[1].Value, ["value"] = cfg.Groups[2].Value }, risk = "low", rationale = $"Revert {cfg.Groups[1].Value} to its previous value {cfg.Groups[2].Value}." },
                },
                permanentFix = new[] { $"Add validation to config-sync-bot: {cfg.Groups[1].Value} must stay above the dependency p99 latency.", "Require review for timeout-related configuration keys." },
                verificationPlan = new[] { $"get_error_rate({service}) back under 1%.", $"get_latency({service}) back to baseline." },
                rollbackPlan = new[] { $"Set {cfg.Groups[1].Value} back to {cfg.Groups[3].Value} (not recommended)." },
            };
        }

        return new
        {
            immediateMitigation = Array.Empty<object>(),
            permanentFix = new[] { "No safe automated mitigation identified: hand over to the service owner." },
            verificationPlan = new[] { $"get_error_rate({service})." },
            rollbackPlan = Array.Empty<string>(),
        };
    }
}
