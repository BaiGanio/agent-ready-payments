# The MCP server (lab 08)

The Inspector, the raw shell client, and the timeout checklist.

← back to [the command index](../EVERYDAY-COMMANDS.md)

---

| I want to | Command |
|---|---|
| Build it | `dotnet build src/Payments.Mcp` |
| Prove nothing goes to stdout | `dotnet run --project src/Payments.Mcp < /dev/null 2>/dev/null \| wc -c` — must print `0` |
| Run only the MCP tests | `dotnet test tests/Payments.Mcp.Tests` |
| Drive the tools by hand (no model) | `npx @modelcontextprotocol/inspector -e PAYMENTS_API_URL=http://localhost:5080 -e PAYMENTS_API_KEY="$PAYMENTS_AGENT_KEY" dotnet "$PWD/src/Payments.Mcp/bin/Debug/net10.0/Payments.Mcp.dll"` |

The Inspector starts the built `.dll`, never `dotnet run`: `dotnet run` can print `Using launch settings from …` on stdout, and stdout is the protocol. The API must be running first.

## Before you start the Inspector

| I want to | Command |
|---|---|
| Load the keys into this shell | `set -a; source .env; set +a` |
| Check the API answers with the agent key | `curl -s -o /dev/null -w '%{http_code}\n' 'http://localhost:5080/payments?status=Captured' -H "X-Api-Key: $PAYMENTS_AGENT_KEY"` — must print `200`, never `401` |
| Make a captured 80.00 EUR payment to refund | the three `curl` calls in "Make a captured payment" below |
| See every MCP server still running | `ps aux \| grep '[P]ayments.Mcp.dll'` |
| Kill a server the Inspector left behind | `kill <pid>` from the line above |

A `401` here means the key is in `.env` but not in the database. Store it with the inserts in "API keys (from lab 08)" above. The MCP server turns every API failure into the same `tool_failed` message, so the Inspector cannot tell you which one it was.

## Make a captured payment

Every money-moving `POST` needs an `Idempotency-Key`, so all three calls carry one. A fresh `uuidgen` per call: these are three different requests, and one key reused would make the second call replay the first. Change both amounts to the amount you need.

```bash
set -a; source .env; set +a
id=$(curl -s -X POST http://localhost:5080/payments \
  -H "X-Api-Key: $PAYMENTS_DEV_KEY" -H "Idempotency-Key: $(uuidgen)" -H 'Content-Type: application/json' \
  -d '{"merchantId":"11111111-1111-1111-1111-111111111111","amount":80.00,"currency":"EUR"}' \
  | python3 -c 'import sys,json; print(json.load(sys.stdin)["id"])')
curl -s -o /dev/null -X POST "http://localhost:5080/payments/$id/authorise" \
  -H "X-Api-Key: $PAYMENTS_DEV_KEY" -H "Idempotency-Key: $(uuidgen)"
curl -s -o /dev/null -X POST "http://localhost:5080/payments/$id/capture" \
  -H "X-Api-Key: $PAYMENTS_DEV_KEY" -H "Idempotency-Key: $(uuidgen)" -H 'Content-Type: application/json' \
  -d '{"amount":80.00,"currency":"EUR"}'
echo "captured: $id"
```

A `python3` traceback with an empty `json.load` means the create call answered an error, not a payment. Run the first `curl` on its own to read it: a missing `Idempotency-Key` is a `400 idempotency_key_required`, and a `401` means the key is not in the database.

## Drive the tools without the Inspector

The Inspector is only a client. The protocol is JSON on stdin and stdout, one message per line, so your shell can be the client. Use this when the Inspector hangs, when you want to see the raw bytes, or in a script.

```bash
set -a; source .env; set +a
{ printf '%s\n' \
  '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"shell","version":"1"}}}' \
  '{"jsonrpc":"2.0","method":"notifications/initialized"}' \
  '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"list_payments","arguments":{"status":"Captured"}}}'
  sleep 5; } \
| PAYMENTS_API_URL=http://localhost:5080 PAYMENTS_API_KEY="$PAYMENTS_AGENT_KEY" \
  dotnet "$PWD/src/Payments.Mcp/bin/Debug/net10.0/Payments.Mcp.dll" 2>/dev/null
```

| Part | Why it is there |
|---|---|
| `initialize`, then `notifications/initialized` | the handshake. A `tools/call` before it is refused |
| `sleep 5` | stdin must stay open until the answer comes back. Without it the server sees end-of-input and quits first, and you get nothing |
| `2>/dev/null` | the logs go to stderr. Drop `2>/dev/null` to read them |
| `tools/list` in place of `tools/call` | lists the three tools and their schemas |

Change the last line of the message block to call another tool:

```
{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}
{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"get_payment","arguments":{"payment_id":"<id>"}}}
{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"refund_payment","arguments":{"payment_id":"<id>","amount":"80.00","currency":"EUR"}}}
```

For the second refund call, add `,"confirmation_token":"rf_…"` from the first answer, inside the same `arguments`.

## When the Inspector says "Request timed out after 1m00s"

The server is almost never the problem. Check in this order:

| Check | Command |
|---|---|
| Does the server answer at all? | the "Drive the tools without the Inspector" block above |
| Is the API up? | `curl -s -o /dev/null -w '%{http_code}\n' http://localhost:5080/health` |
| Is a stale server still running? | `ps aux \| grep '[P]ayments.Mcp.dll'` |

If the shell block answers and the Inspector does not, the client is stuck: stop it with `Ctrl+C` and start it again. A server that answers once and then freezes for ever is the stderr trap in the lab 08 Traps table: the client stopped reading stderr, the pipe filled, and the next log line blocks the server. The cure is in the server, not the client — log at `Warning`, not at `Information`.
