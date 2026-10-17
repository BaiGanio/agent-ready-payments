# Lab 08 — Interview Q&A: the C# MCP server (`Payments.Mcp`)

This is the differentiator lab. Most .NET candidates will not have built one.

---

## A. MCP basics

**Q1. What is MCP?**
A: Model Context Protocol: an open protocol (from Anthropic, now broadly adopted) for connecting AI clients to tools and data. A server exposes **tools** (actions), **resources** (readable data) and **prompts**. Messages are JSON-RPC 2.0.

**Q2. Transports?**
A: **stdio** (the client starts the server as a child process — local, simple) and **Streamable HTTP** (remote, multi-client, needs auth). I used stdio.
Trap: "With stdio, what must you never do?" → Write logs to stdout. stdout is the protocol channel. Logs go to stderr.

**Q3. How does a tool look in the C# SDK?**
A: A class with `[McpServerToolType]`, methods with `[McpServerTool]` and `[Description]` on the method and each parameter. The SDK builds the JSON Schema from the C# signature. Registered with `builder.Services.AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly()`.

**Q4. Why does the MCP server call the API over HTTP instead of the database?**
A: The API already owns validation, the state machine and idempotency. If the MCP server hit the DB, it would be a second, weaker door into the system. One door, one set of rules. The agent can do exactly what any other API client can — nothing more.

## B. Designing tools for agents

**Q5. What makes a backend API a good agent tool?**
A: Typed inputs with tight schemas (enums, ranges, formats). Small number of focused tools. Deterministic results. Stable, machine-readable error codes with a hint of what to do next. Idempotent writes. Read tools that are cheap and safe. Destructive tools that require confirmation.

**Q6. Why only three tools?**
A: Every tool costs context tokens and adds a chance the model picks the wrong one. A few well-named tools beat a mirror of 40 endpoints. Tools should match **tasks**, not tables.

**Q7. How is `refund_payment` protected?**
A: Two phases. Call 1 with `payment_id`, `amount`, `currency`, `reason` → returns a **proposal** and a short-lived **confirmation token**; nothing is refunded. Call 2 with the token → executes. The token is bound to a hash of the exact arguments; if anything changed, it is refused. The server sends its own `Idempotency-Key` (derived from the token) to the API, so a repeated confirm cannot refund twice.
Aperio link: the same design runs in production in Aperio's `db_execute` — propose, token with a 5-minute TTL, approve, re-validate, digest check, atomic claim, execute (`lib/handlers/database/databaseHandlers.js`, `lib/security/interruptService.js`).

**Q8. What do you do with a malformed amount?**
A: Refuse. Never guess or "repair" an argument to a money-moving tool. Return a clear error saying what format is expected. Aperio has this exact rule: malformed JSON for a destructive tool is refused, never regex-repaired (`lib/tools/executor.js`, `parseArgs`).

**Q9. Who confirms — the model or a human?**
A: A human, for anything that moves money. The model may propose; the client (Claude Code / UI) shows the proposal and the human approves. In MCP the client also asks the user before running tools; the token adds a server-side guarantee that doesn't trust the client.

**Q10. How should tool errors look?**
A: Return them as a tool result with `isError: true` and a stable code + hint ("refund_exceeds_captured: captured 100.00, already refunded 60.00, max refundable 40.00"). The model can recover. A protocol-level exception tells the model nothing useful.

**Q11. How do you write tool descriptions?**
A: For the model, not for humans: when to use it, when **not** to use it, units and formats ("amount in major units, decimal string, e.g. '10.50'"), and what it returns. Descriptions are part of the contract; changing them changes behaviour.

**Q12. Pagination in `list_payments`?**
A: Cursor-based with a hard max page size. Return a `next_cursor`. Never let a tool return 10,000 rows into the context window.
Aperio link: `lib/context/toolResultOffload.js` moves oversized tool results out of the context and gives the model a handle instead.
Trap: "What if the model invents a cursor, or the order shifts between calls?" → An invented cursor gets `invalid_cursor`, never page 1. The order is total (`created_at, id`), so the agent cannot find the same duplicate charge twice (Lab 02 stretch).

**Q13. Authentication?**
A: For stdio the server runs with the user's local credentials (API key in env, never in tool args). For remote HTTP MCP: OAuth 2.1 per the spec, per-user tokens, and the API enforces merchant scoping. The agent never sees secrets.

**Q14. How do you stop prompt injection via tool results?**
A: Treat tool output as data. Don't return free text from untrusted sources without marking it. Keep destructive tools behind confirmation so injected instructions can't move money. Aperio fences untrusted tool output and taints later write calls (`lib/agent/tool-safety-middleware.js`).

## C. Testing

**Q15. How did you test the MCP server?**
A: Unit tests for each tool method with a fake API client (HttpMessageHandler stub): right request, right mapping of errors. One integration test that starts the server in-process with the SDK's client and calls `tools/list` and `tools/call`. One manual run from Claude Code.

**Q16. What did you assert about `tools/list`?**
A: The schema itself: names, required fields, enum values. A schema change is a breaking change for agents — a snapshot test catches accidental ones.

**Q17. Tools vs resources — which for `get_payment`?**
A: Tool. Clients call tools reliably; resource support varies. Resources fit static or browseable content (docs, a glossary).

**Q18. If a payment company exposed its platform to agents, what would you build first?**
A: Read-only tools first (payment lookup, status, settlement status) with merchant scoping and audit logs. Then one write tool with two-phase confirm and idempotency. Measure misuse before adding more. Every agent call goes into the audit trail with agent id, user, arguments, result.

## D. Lessons from the sketches (`AGENT-READY-BACKEND.md`)

**Q19. Why is a tool's description "code"?**
A: The model decides *when* and *how* to call a tool from that sentence. "Gets a payment." makes the model pass a merchant name. "Returns one payment by its exact id (UUID). Use `list_payments` first if you only have a merchant or date. Returns `status=not_found` if missing. Read-only." makes it behave. Changing the sentence changes behaviour, so it goes under review and a snapshot test.

**Q20. Why `additionalProperties: false`?**
A: Models invent fields. Rejecting unknown fields early stops silent ignoring of what the model *thought* it was asking for.

**Q21. Is "payment not found" an error?**
A: For the tool, no. Return `status: not_found` as a normal result — a fact the model reads and acts on. Errors are for "the tool could not do its job".

**Q22. What is the worst endpoint to give an agent?**
A: A generic "run this SQL" endpoint or a fat `/process?mode=1|2|3` that returns 200 on failure. The model either owns your database or has to guess. Small, named, single-job endpoints with real status codes are the fix.

**Q23. What is the short version of "agent-ready backend"?**
A: "It is 80% ordinary API hygiene — small named endpoints, one error shape, safe retries — plus run tables and approval gates. The loop itself is about 40 lines. The AI part is the small part."

## E. Agent identity and audit

**Q24. How do you give an agent an identity?**
A: Its own credential, such as an API key or an OAuth client, separate from human users. It also has a name and scopes. The API sees "agent support-bot acting for user maria", not an anonymous call. Each agent can be turned off on its own.

**Q25. What are scopes, and how did you use them?**
A: Scopes are named permissions, such as `payments:read` and `payments:refund`. The API checks them on every endpoint. Least privilege: an agent that only answers questions gets `payments:read`. Even if a prompt tricks it into calling refund, the API refuses with `403 insufficient_scope`.

**Q26. Why is the scope check in the API and not in the MCP server?**
A: The API is the only door to the money, and every client passes it. A check in the MCP server can be skipped by calling the API directly. The MCP server may hide tools the key can't use, to reduce mistakes, but the API makes the real decision.

**Q27. Where does the agent's API key live?**
A: In the MCP server's environment or a secret store. It is never in a prompt or a tool argument, and the model never sees it. Rotate it like any service credential, and store only a hash on the server.

**Q28. What is in your audit row?**
A: Agent, the human on whose behalf it acted, tool, target payment, redacted arguments, result code, approver and trace id. A failed or forbidden attempt is audited too. Those are often the most interesting rows.


## Aperio: have you solved this before?

**Q-APERIO. "Have you solved this before?"**
A: Yes — this is my strongest area. Aperio's MCP server has typed tools, a two-phase confirm with a digest check and an atomic claim, a refuse-don't-repair rule for destructive tools, and dangerous tools off by default. The two honest gaps are agent scopes not enforced at execution time (it's in my debt register) and no per-call audit table. `Payments.Mcp` closes both, and it also goes through the API instead of the store.
Details and file references: the "Aperio comparison" table in this lab's `README.md`.
