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
A: Two phases. Call 1 with `payment_id`, `amount`, `currency` → returns a **proposal** and a short-lived **confirmation token** (5 minutes, kept in the MCP server's memory); nothing is refunded. Call 2 with the same arguments plus the token → executes. The token is bound to the exact parsed arguments; if anything changed, it is `token_mismatch` and nothing is posted. A repeated confirm replays the stored outcome. The server sends its own `Idempotency-Key`, `mcp-<token>`, so a retry after a lost answer cannot refund twice either.
Aperio link: the same design runs in production in Aperio's `db_execute` — propose, token with a 5-minute TTL, approve, re-validate, digest check, atomic claim, execute (`lib/handlers/database/databaseHandlers.js`, `lib/security/interruptService.js`).

**Q8. What do you do with a malformed amount?**
A: Refuse. Never guess or "repair" an argument to a money-moving tool. Return a clear error saying what format is expected. Aperio has this exact rule: malformed JSON for a destructive tool is refused, never regex-repaired (`lib/tools/executor.js`, `parseArgs`).

**Q9. Who confirms — the model or a human?**
A: A human, for anything that moves money. The model may propose; the client (the MCP Inspector, our lab 09 loop, an approval UI) shows the proposal and the human approves. In MCP the client also asks the user before running tools; the token adds a server-side guarantee that doesn't trust the client.

**Q10. How should tool errors look?**
A: Return them as a tool result with `isError: true` and a stable code + hint ("amount_exceeds_captured: captured 100.00, already refunded 60.00, max refundable 40.00"). The model can recover. A protocol-level exception tells the model nothing useful.

**Q11. How do you write tool descriptions?**
A: For the model, not for humans: when to use it, when **not** to use it, units and formats ("amount in major units, decimal string, e.g. '10.50'"), and what it returns. Descriptions are part of the contract; changing them changes behaviour.

**Q12. Pagination in `list_payments`?**
A: In this lab: a hard cap, no paging. At most 20 rows, a total order, and `truncated: true` when more exist, with a hint to narrow by `status`. Never let a tool return 10,000 rows into the context window. Cursor paging plugs in later (`CF-022`): one new field, `nextCursor`, and one new argument, `cursor`.
Aperio link: `lib/context/toolResultOffload.js` moves oversized tool results out of the context and gives the model a handle instead.
Trap: "What if the model invents a cursor, or the order shifts between calls?" → Today `cursor` is an unknown argument, so it is refused by name, never treated as page 1. With paging, an invented cursor gets `invalid_cursor`. The order is total (`created_at desc, id desc`), so the agent cannot find the same duplicate charge twice.

**Q13. Authentication?**
A: For stdio the server runs with the user's local credentials (API key in env, never in tool args). For remote HTTP MCP: OAuth 2.1 per the spec, per-user tokens, and the API enforces merchant scoping. The agent never sees secrets.

**Q14. How do you stop prompt injection via tool results?**
A: Treat tool output as data. Don't return free text from untrusted sources without marking it. Keep destructive tools behind confirmation so injected instructions can't move money. Aperio fences untrusted tool output and taints later write calls (`lib/agent/tool-safety-middleware.js`).

## C. Testing

**Q15. How did you test the MCP server?**
A: Unit tests for each tool method with a fake API client (HttpMessageHandler stub): right request, right mapping of errors. One integration test that starts the server in-process with the SDK's client and calls `tools/list` and `tools/call`. One manual run in the MCP Inspector, and in lab 09 a run with a local model.

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

**Q28. What is in your audit row?** *(The table is built in lab 14, moved 2026-10-21. Say the design.)*
A: Agent, the human on whose behalf it acted, tool, target payment, redacted arguments, result code, approver and trace id. A failed or forbidden attempt is audited too. Those are often the most interesting rows.


## F. From the build (added 2026-10-21)

**Q29. With stdio, where do your logs go, and how did you prove it?**
A: To stderr. stdout is the protocol channel, and the .NET host's default console logger writes there — "Application started" would be the first thing the client tries to parse. One line fixes it: `AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace)`. The proof is `dotnet run … < /dev/null 2>/dev/null | wc -c` printing `0`. The same trap hides in `dotnet run`: with a `launchSettings.json` it prints "Using launch settings from …" on stdout, so the client starts the built `.dll`.

**Q30. Your tool schema says `limit` has a maximum of 20. Is that enough?**
A: No. The schema is a promise to the model, not a check. I verified on the C# SDK 2.3.0 that `[Range]` and `[AllowedValues]` show up in the schema as `minimum`, `maximum` and `enum`, and that `limit: 500` still reaches the method. So every tool parses and checks every value itself, and the tests send the bad values on purpose.

**Q31. Why an endpoint filter for API keys, and not ASP.NET Core authentication handlers?**
A: The handlers are the textbook answer and right for a bigger system: they plug into `[Authorize]`, policies and claims. But their default `401` and `403` have empty bodies, and getting our ProblemDetails with a stable `code` and `required_scope` out of them needs a custom `IAuthorizationMiddlewareResultHandler` — more code than the filter. Two filters do the job: one on the group ("who are you?"), one per route ("may you?"). I would move to handlers the day we add OAuth or a second kind of credential.

**Q32. An agent asks for another merchant's payment. `403` or `404`?**
A: `404 payment_not_found`, the same as an id that does not exist. A `403` says "this exists, it is just not yours", which is already a leak. Create is different: the caller sent the other merchant's id in the body, so `403 merchant_mismatch` tells it nothing new.

**Q33. What does the SDK do with an argument the tool does not declare?**
A: It drops it silently — verified on 2.3.0. That is dangerous: the model sends `merchant: "M1 Hotel"`, the tool ignores it, and the model believes its list is filtered. So one call filter compares the sent names with the tool's own schema and refuses `unknown_argument`, naming the field. It is the C# version of `additionalProperties: false`, and a new tool is fenced for free.

**Q34. Are `readOnlyHint` and `destructiveHint` security?**
A: No. They are hints a client may use to decide when to ask the human. A client can ignore them. The security is the confirmation token in the MCP server and the scope check in the API.

**Q35. The confirmation token lives in memory. What breaks when the MCP server restarts?**
A: Open proposals are lost, so the human proposes again. Nothing is refunded twice, because a refund needs a token and a lost token is simply unknown. I accepted that for one stdio client. With a remote server, several replicas or a human approval screen (lab 19), proposals move into the API's database, with the approver and an expiry.

**Q36. The refund call times out. Did the money move?**
A: You cannot know, so the tool does not guess: it answers `refund_in_doubt` and puts the token back to `Pending`. The retry sends the **same** `Idempotency-Key` (`mcp-<token>`), so the API replays the first answer if the refund happened, and does it once if it did not. That is lab 03 paying off. A test drops the answer on purpose after the API did the work, and counts exactly one `Refund` row.

**Q37. How does the list know it is truncated without a `count(*)`?**
A: It asks for `limit + 1` rows. If the extra row comes back, there is more: `truncated` is true and the extra row is dropped. One query, no race between a count and a page. With exactly 20 rows, `truncated` is false — the off-by-one test.

**Q38. Why does every demo run on a local model?**
A: Payment data — and what an agent reads while it works — must not leave the company for an external AI provider. A payments company will run models inside its own network. So the labs do too: `llama-server` with a local gemma in lab 09, and in lab 08 no model at all, just the MCP Inspector driven by hand. The MCP server does not care: any MCP client on stdio gets the same three tools.


## Aperio: have you solved this before?

**Q-APERIO. "Have you solved this before?"**
A: Yes — this is my strongest area. Aperio's MCP server has typed tools, a two-phase confirm with a digest check and an atomic claim, a refuse-don't-repair rule for destructive tools, and dangerous tools off by default. The two honest gaps are agent scopes not enforced at execution time (it's in my debt register) and no per-call audit table. `Payments.Mcp` closes the first — keys with scopes and one merchant, enforced by the API — and goes through the API instead of the store. The audit table comes in lab 14, with the trace id.
Details and file references: the "Aperio comparison" table in this lab's `README.md`.
