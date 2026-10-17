# Lab 14 — Interview Q&A: observability and audit for agents

---

## A. Tracing basics

**Q1. Logs, metrics, traces: what is each for?**
A: **Logs** are single events with detail ("refund rejected: exceeds captured"). **Metrics** are numbers over time ("tool error rate 3%"), cheap and good for alerts. **Traces** show one request's path across services, with timings for each hop. You need all three.

**Q2. What is OpenTelemetry?**
A: The vendor-neutral standard and SDKs for traces, metrics and logs. You instrument once and export to any backend (Jaeger, Grafana Tempo, Azure Monitor, Datadog…). In .NET it builds on `System.Diagnostics.Activity` and `Meter`.

**Q3. Span, trace, context propagation?**
A: A **span** is one timed operation. A **trace** is the tree of spans for one request. **Context propagation** carries the trace id and parent span id across process borders (W3C `traceparent` header), so the spans join into one tree.

**Q4. How did you carry the trace across the MCP stdio hop?**
A: Stdio has no HTTP headers. The agent puts the `traceparent` value into the tool call's metadata. `Payments.Mcp` reads it, starts its span as a child, and sends it on as an HTTP header to the API. ASP.NET Core picks it up automatically.

**Q5. In .NET, what is an `ActivitySource`?**
A: The .NET API for creating spans: `using var activity = source.StartActivity("refund_payment");`. OpenTelemetry listens to it. A `Meter` with `Counter<T>` and `Histogram<T>` does the same for metrics.

## B. Agent-specific observability

**Q6. What do you trace for a model call?**
A: Model name, token counts in and out, duration, finish reason, tool calls requested. These follow the OpenTelemetry GenAI conventions (`gen_ai.*`). The prompt and the reply are *not* recorded by default.

**Q7. Why not log prompts?**
A: Prompts can contain customer names, emails, payment details and dispute text. That brings in GDPR, and possibly PCI. Record them only when needed, in a protected store with retention rules and redaction. Aperio redacts before data reaches a model (`lib/privacy/redact.js`, `lib/agent/redaction-relay.js`).

**Q8. Which agent metrics would you alert on?**
A: Spikes in tool error rate, step-cap hits (an agent looping), approval reject rate (agents proposing wrong actions), model latency p95, tokens per task (cost), and calls to refund tools per hour (abuse).

**Q9. How do you find a runaway agent quickly?**
A: Look at the step-cap and repeat-breaker metrics, then open a trace with many identical tool spans. Aperio emits events for exactly these breakers (`tool_repeat_break`, `tool_step_limit` in `lib/agent/providers/llamacpp.js`).

## C. Audit

**Q10. Audit log vs application log?**
A: Application logs are for engineers: noisy, sampled, kept for a short time. An audit log is a business record: complete, append-only, kept for years, and readable by compliance. It records who did what, to which object, when, with which approval. Never sample it.

**Q11. What goes in an agent audit row?**
A: Time, agent identity (key id, not the secret), the human on whose behalf it acted, scope, tool name, target id (payment id), redacted arguments, result code, approval id and approver, and the trace id to link to the trace.

**Q12. How do you make an audit log tamper-evident?**
A: Append-only permissions (insert only, no update or delete for the app user). Hash-chain the rows (each row stores the hash of the previous one), or use a ledger feature (SQL Server ledger tables). Ship copies to separate storage.

**Q13. Why is the audit written by the API and not by the MCP server?**
A: The API is the one door to the money, and every caller passes it. If audit lived in the MCP server, a caller that skips the MCP server would leave no record. The MCP server never touches the database, by design.

## D. Practical

**Q14. Sampling: do you trace every request?**
A: Not at high volume. Use head sampling (a percentage) for normal traffic. Use tail sampling to keep every trace with errors or slow spans. Always keep traces for agent actions that move money, because the volume is low and the value is high.

**Q15. What is the Aspire dashboard?**
A: A small local OpenTelemetry viewer from the .NET Aspire project. It can run on its own as a container and accepts OTLP. It is ideal for development. In production you send to your real backend.

**Q16. How did you test observability?**
A: Use an in-memory exporter in tests: run the refund flow, then check that the spans exist, have the right parent and child links, share one trace id, and contain no card number or prompt text.

**Q17. Correlation id vs trace id?**
A: A trace id is generated and carried by tracing. A correlation id is often a business id that you choose (a chargeback id) and put on logs. Use both: the trace id for the technical path, the business id to find all traces about one case.

**Q18. Honest gap?**
A: Aperio has structured logs, lifecycle traces and event emitters (`lib/agent/lifecycle-trace.js`, `lib/agent/turn-diagnostics.js`), but not OpenTelemetry. This lab is where I wired OpenTelemetry end to end in .NET.


## Aperio: have you solved this before?

**Q-APERIO. "Have you solved this before?"**
A: Partly. Aperio has lifecycle traces, safety events, run records and redaction, but no OpenTelemetry and no per-call audit table. This lab adds standard tracing across process borders and an audit trail you could show a compliance team.
Details and file references: the "Aperio comparison" table in this lab's `README.md`.
