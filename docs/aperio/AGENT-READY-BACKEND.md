# Agent-Ready Backend — the job in code and in pictures

Reused from an earlier conversation and adapted to payments (invoices → payments). Small bugs in the original sketches are fixed and marked **Fix:**.

These are **sketches for the interview**, not lab answers. In the labs you write the real code test-first.

Labs that use each section: §1–3 → Lab 09 · §4, §6 → Lab 02 · §5 → Lab 03 · §7 → Lab 08 · §8 → the pitch in `TALKING-POINTS.md`.

---

## 0. One picture for the whole thing: the restaurant

The model is a very clever chef. The chef is blindfolded and locked in a room. The chef cannot walk, open the fridge or pick up a pan. The chef can only shout one instruction through the door.

You are the waiter. You listen at the door, you do the thing, you shout back what happened, you wait for the next instruction.

That is the agent loop. The chef is smart and helpless. All the hands belong to you.

| Concept | Everyday picture |
|---|---|
| Agent loop | The waiter at the door. One shout = one step. The chef decides the next step only after hearing the result. |
| State | The chef has amnesia. Before every shout you read the whole story back from your notepad. Lose the notepad, lose the dinner. Your shift ends? The next waiter picks up the notepad: "resume after a crash". |
| Tool boundary | A short list on the door: *fetch · chop · fry · plate · read the order*. Chef shouts "call the bank" → "That is not on my list." Calmly. No is an answer, not a crash. |
| Deterministic tool | A vending machine: B4 = Snickers, every time. The bad one has one "GO" button, a hidden dial 1/2/3, says "THANK YOU" when it fails, and setting 3 phones your mother. That is the old `/process` endpoint. |
| Idempotency | Double-tap on the card reader. A good shop sees the same receipt number and shows the same receipt. Two tills checking the list at the same instant both see "not there yet" — so you need a till that physically refuses a duplicate number: the **unique index**. |
| One error contract | Hotel reception. A shrug, a long apology in French, or a printed card "ROOM NOT READY — code 409 — ticket #88213". Only the card is useful. ProblemDetails = the card, every time, same layout. |
| Tool description | Two drawers: "stuff" vs "sharp knives — handles to the left — do not reach in blind". Same code behind both; the label decided if it works. |
| Approval gate | "Bin the whole fish delivery." A gated tool stops, writes a note, waits for the manager. The loop sleeps and wakes on Monday when someone says yes. |

**The whole job in one sentence:** they have a restaurant with one big GO button, a reception that shrugs, a till that double-charges and no list on the kitchen door. The job is to fit labelled buttons, print the cards, number the receipts and nail the list to the door — so a blindfolded chef can run the kitchen. **The chef is the easy part. You buy the chef. The kitchen is the work.**

---

## 1. The agent loop — it is a `while`

```csharp
public async Task<AgentRun> RunAsync(AgentRun run, CancellationToken ct)
{
    const int maxSteps = 6;   // Aperio's DEFAULT_TURN_STEP_LIMIT is 6 too

    while (run.Status == RunStatus.Running)
    {
        if (run.StepCount >= maxSteps) { run.Fail("step_limit_reached"); break; }

        // 1. Ask the model what to do next. Give it the whole history.
        var reply = await _model.NextAsync(run.Messages, _tools.Schemas, ct);

        // 2. The model either answers, or asks for one tool.
        if (reply.ToolCall is null) { run.Finish(reply.Text); break; }

        // 3. Money-moving tool? Stop and wait for a person.
        if (_tools.RequiresApproval(reply.ToolCall.Name))
        {
            run.WaitForHuman(reply.ToolCall);
            break;
        }

        // 4. YOU run the tool. The model never runs anything.
        var result = await _tools.InvokeAsync(reply.ToolCall, run.Id, ct);

        // 5. Hand the result back, save, go round again.
        run.Append(reply, result);
        await _runs.SaveAsync(run, ct);   // save EVERY step
    }

    await _runs.SaveAsync(run, ct);       // and the final status
    return run;
}
```

- The model never touches the database. It returns a name and some JSON.
- `maxSteps` is the stop rule. Without it a confused model loops forever and burns money.
- Save after every step (§2).
- **Fix:** the original checked the limit only in the `while` condition and never saved the final `Done`/`Failed` status. Now both are explicit.
- **Fix:** added the approval gate inside the loop (`WaitingForHuman`). Lab 08 adds a second gate on the server side (confirmation token), so a client that skips the first gate still can't refund.

---

## 2. State — the agent has no memory. Your table is the memory.

```sql
CREATE TABLE dbo.AgentRun (
    Id          UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    Status      VARCHAR(20)      NOT NULL
                CHECK (Status IN ('Running','Done','Failed','WaitingForHuman')),
    StepCount   INT              NOT NULL,
    Messages    NVARCHAR(MAX)    NOT NULL,   -- conversation so far, JSON
    PendingCall NVARCHAR(MAX)    NULL,       -- the tool call waiting for approval
    CreatedUtc  DATETIME2        NOT NULL,
    UpdatedUtc  DATETIME2        NOT NULL,
    RowVer      ROWVERSION                    -- two workers must not resume the same run
);

CREATE TABLE dbo.AgentStep (
    Id          UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    RunId       UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.AgentRun(Id),
    Ordinal     INT              NOT NULL,
    ToolName    VARCHAR(100)     NULL,
    InputJson   NVARCHAR(MAX)    NULL,
    OutputJson  NVARCHAR(MAX)    NULL,
    Outcome     VARCHAR(20)      NOT NULL CHECK (Outcome IN ('Ok','ToolError','Rejected')),
    DurationMs  INT              NOT NULL,
    CONSTRAINT UX_AgentStep_Run_Ordinal UNIQUE (RunId, Ordinal)
);
```

- The process restarts, the Pi reboots, the pod is evicted. With these tables you load the run and carry on from step 7. Without them the work is gone and the customer may pay twice.
- `WaitingForHuman` is the valuable status: the loop stops, a person approves, it resumes days later.
- **Fix:** added the `CHECK` constraints, `PendingCall`, a `ROWVERSION` (optimistic concurrency so two workers can't both resume one run) and a unique `(RunId, Ordinal)`.
- Aperio equivalent: `agent_runs`, `agent_interrupts` with a `CHECK`-constrained status (`db/migrations/002_agent_jobs.sql`, `008_agent_interrupts.sql`).
- **EF Core fits here.** The app owns this schema, so EF Core with a JSON column for `Messages` and `[Timestamp]` on `RowVer` is a good choice. See PLAN.md "Dapper vs EF Core".

---

## 3. The tool-calling boundary — the fence

The wrong way (people really do this):

```csharp
// NEVER. The model now owns your database.
[HttpPost("query")]
public async Task<IActionResult> RunSql([FromBody] string sql)
    => Ok(await _db.QueryAsync(sql));
```

The right way: a registry. The agent can only reach what you put in it.

```csharp
public interface IAgentTool
{
    string Name { get; }
    string Description { get; }          // the model reads this (§7)
    JsonNode InputSchema { get; }        // and this
    bool IsReadOnly { get; }
    bool RequiresApproval { get; }       // refund, void: true
    Task<ToolResult> InvokeAsync(JsonNode input, ToolContext ctx, CancellationToken ct);
}

public sealed class ToolRegistry(IEnumerable<IAgentTool> tools, ILogger<ToolRegistry> log)
{
    private readonly Dictionary<string, IAgentTool> _byName =
        tools.ToDictionary(t => t.Name, StringComparer.Ordinal);

    public bool RequiresApproval(string name)
        => _byName.TryGetValue(name, out var t) && t.RequiresApproval;

    public async Task<ToolResult> InvokeAsync(ToolCall call, Guid runId, CancellationToken ct)
    {
        // The fence. An unknown name is not an exception, it is a message back.
        if (!_byName.TryGetValue(call.Name, out var tool))
            return ToolResult.Rejected($"No tool named '{call.Name}'.");

        // Validate the shape BEFORE touching the domain. Never "repair" it.
        if (!SchemaValidator.Matches(call.Input, tool.InputSchema, out var why))
            return ToolResult.Rejected(why);

        try
        {
            return await tool.InvokeAsync(call.Input, new ToolContext(runId), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Tool {Tool} failed in run {Run}", call.Name, runId);
            // The model gets a clean sentence. Not your stack trace.
            return ToolResult.Failed("The tool failed. Do not retry this input.");
        }
    }
}
```

- A tool failure must not crash the loop. It is a fact you tell the model, so it can choose something else. That is the difference between an agent and a script.
- **Fix:** `catch` now lets `OperationCanceledException` through — a user pressing Stop is not a tool failure.
- **Fix:** `call.Input.Matches(...)` was pseudo-code. Use a real JSON Schema validator (e.g. the `JsonSchema.Net` package) behind `SchemaValidator`.
- Aperio equivalent: refuse-to-repair for destructive tools (`lib/tools/executor.js` 705), safety middleware (`lib/agent/tool-safety-middleware.js`).

One real tool:

```csharp
public sealed class GetPaymentTool(IPaymentQueries payments) : IAgentTool
{
    public string Name => "get_payment";
    public string Description =>
        "Returns one payment by its exact id (a UUID). " +
        "Use list_payments first if you only have a merchant, an order number or a date. " +
        "Returns status=not_found when no payment has that id. Read-only.";
    public bool IsReadOnly => true;
    public bool RequiresApproval => false;

    public JsonNode InputSchema => JsonNode.Parse("""
    {
      "type": "object",
      "properties": { "paymentId": { "type": "string", "format": "uuid" } },
      "required": ["paymentId"],
      "additionalProperties": false
    }
    """)!;

    public async Task<ToolResult> InvokeAsync(JsonNode input, ToolContext ctx, CancellationToken ct)
    {
        var id = input["paymentId"]!.GetValue<Guid>();
        var dto = await payments.GetAsync(id, ct);
        return dto is null
            ? ToolResult.Ok(new { status = "not_found", paymentId = id })
            : ToolResult.Ok(dto);
    }
}
```

- `additionalProperties: false` — the model will invent fields. Reject them early.
- `not_found` is a **success with a fact in it**, not an exception. The model reads it and moves on.

---

## 4. "Deterministic tool" — bad endpoint vs good endpoint

The kind of thing in a 2014 payments codebase:

```csharp
[HttpPost("process")]
public async Task<IActionResult> Process([FromBody] ProcessRequest req)
{
    try
    {
        if (req.Mode == 1)      { /* authorise */ }
        else if (req.Mode == 2) { /* capture, sometimes */ }
        else if (req.Mode == 3) { /* refund, and also email the merchant */ }
        return Ok(new { success = true, data = result, msg = "" });
    }
    catch (Exception ex)
    {
        return Ok(new { success = false, data = (object?)null, msg = ex.Message });
    }
}
```

Why a model (or a new developer) can't use it:
- `Mode = 1|2|3` means nothing. The caller must guess.
- A failure returns **HTTP 200**. The caller thinks it worked.
- `msg` is a raw exception string, different every time. Nothing to branch on.
- One endpoint, three behaviours, one sends email. No way to say "read, but do not act".

The same capability as small, named endpoints:

```csharp
app.MapGet("/payments/{id:guid}", async (Guid id, IPaymentQueries q, CancellationToken ct)
    => await q.GetAsync(id, ct) is { } dto ? Results.Ok(dto) : Results.NotFound());

app.MapPost("/payments", CreatePayment);                       // authorise
app.MapPost("/payments/{id:guid}/capture", CapturePayment);
app.MapPost("/payments/{id:guid}/refunds", RefundPayment);     // email = separate event, not a side effect here
```

One job each. A real status code each. Thin endpoint. This is Lab 02.

---

## 5. Idempotency — "same input, one result" (Lab 03)

An agent will retry: the network blips, the loop re-runs a step, the model repeats itself. If create runs twice, the customer is charged twice.

```csharp
app.MapPost("/payments", async (
    [FromHeader(Name = "Idempotency-Key")] string? key,
    CreatePayment cmd, PaymentService svc, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(key))
        return Results.Problem(statusCode: 400, extensions: new Dictionary<string, object?> { ["code"] = "idempotency_key_required" });
    return Results.Ok(await svc.CreateAsync(cmd with { IdempotencyKey = key }, ct));
});
```

```sql
CREATE UNIQUE INDEX UX_Payment_Merchant_IdempotencyKey
    ON dbo.Payment (MerchantId, IdempotencyKey) WHERE IdempotencyKey IS NOT NULL;
```

**Version A — EF Core (check, then let the index catch the race):**

```csharp
public async Task<Guid> CreateAsync(CreatePayment cmd, CancellationToken ct)
{
    var existing = await _db.Payments.AsNoTracking()
        .SingleOrDefaultAsync(p => p.MerchantId == cmd.MerchantId && p.IdempotencyKey == cmd.IdempotencyKey, ct);
    if (existing is not null) return existing.Id;            // second call: same answer, no new row

    var payment = Payment.Authorise(cmd.MerchantId, cmd.Amount, cmd.IdempotencyKey);
    _db.Payments.Add(payment);
    try
    {
        await _db.SaveChangesAsync(ct);
        return payment.Id;
    }
    catch (DbUpdateException ex) when (IsUniqueViolation(ex))
    {
        // Two calls raced. The index caught it. Return the winner.
        _db.ChangeTracker.Clear();
        return (await _db.Payments.AsNoTracking()
            .SingleAsync(p => p.MerchantId == cmd.MerchantId && p.IdempotencyKey == cmd.IdempotencyKey, ct)).Id;
    }
}

static bool IsUniqueViolation(DbUpdateException ex) => ex.InnerException switch
{
    SqlException { Number: 2627 or 2601 } => true,                 // SQL Server
    PostgresException { SqlState: "23505" } => true,               // Postgres
    _ => false
};
```

**Version B — Dapper insert-first (no check, the index decides):**

```csharp
// Postgres: ON CONFLICT tells us who won without an exception.
const string sql = """
    INSERT INTO payment (id, merchant_id, idempotency_key, amount, currency, status)
    VALUES (@Id, @MerchantId, @Key, @Amount, @Currency, 'Authorised')
    ON CONFLICT (merchant_id, idempotency_key) DO NOTHING
    RETURNING id;
    """;
var inserted = await conn.QuerySingleOrDefaultAsync<Guid?>(sql, args);
return inserted ?? await conn.QuerySingleAsync<Guid>(
    "SELECT id FROM payment WHERE merchant_id = @MerchantId AND idempotency_key = @Key", args);
```

- **Why the catch and not just the `if`:** two threads can both pass the `if`. Only the unique index is actually safe. Say this in the interview.
- **Fix:** the original wrote `catch (DbUpdateException) when (IsUniqueViolation(ex))` — `ex` was never declared, so it would not compile.
- **Fix:** after a failed `SaveChanges` the `DbContext` still tracks the failed entity. `ChangeTracker.Clear()` stops a later save from retrying the bad insert.
- **Fix:** the key is scoped by merchant. Keys are client-made; two merchants can send the same string.
- Real Lab 03 also stores a request hash (same key + different body → 422) and the stored response.

---

## 6. One error shape (Lab 02)

```json
{
  "type": "https://api.agent-ready-payments.local/problems/refund-exceeds-captured",
  "title": "Refund exceeds captured amount",
  "status": 409,
  "detail": "Captured 100.00 EUR, already refunded 60.00 EUR. Max refundable is 40.00 EUR.",
  "traceId": "00-4bf92f...-01",
  "code": "refund_exceeds_captured"
}
```

`code` is what the agent (or any client) branches on. `detail` is for humans. `code` is stable, lowercase, safe to branch on:

```csharp
var result = await _api.CapturePaymentAsync(id, amount, ct);
if (result.Problem?.Code == "payment_already_captured")
    return ToolResult.Ok(new { status = "already_captured", paymentId = id });
```

Compare branching on `ex.Message`: it breaks the first time somebody fixes a typo. An agent is just a client with no patience and no colleague to ask.

---

## 7. The thing nobody tells you: the description is code (Lab 08)

```csharp
// Weak. The model will call it with a merchant name and fail.
public string Description => "Gets a payment.";

// Strong. Says what it needs, and what happens when it is not there.
public string Description =>
    "Returns one payment by its exact id (a UUID). " +
    "Use list_payments first if you only have a merchant, an order number or a date. " +
    "Returns status=not_found when no payment has that id. Read-only.";
```

The model's behaviour changes when that sentence changes. Treat descriptions like public API docs: under code review, snapshot-tested.

---

## 8. What the job actually needs built — in order

1. **Clean tools.** Split fat `/process` endpoints into small, named, single-job endpoints. ~70% of the work, plain .NET.
2. **One error contract.** ProblemDetails with a stable `code`. One middleware.
3. **Idempotency keys** on every write, backed by a unique index.
4. **Run tables.** `AgentRun` and `AgentStep`.
5. **The registry.** `IAgentTool`, schema validation, the allow-list, errors-as-results.
6. **The loop.** ~40 lines (§1).
7. **Approval gates.** `WaitingForHuman` before anything that moves money or sends email.

Points 1–4 are the job. Points 5–6 are a week. **The AI part is the small part.**
