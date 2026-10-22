# The API by hand (lab 02, lab 03)

`curl` the payment endpoints and read a problem response.

← back to [the command index](../EVERYDAY-COMMANDS.md)

---

| I want to | Command |
|---|---|
| Run the API | `dotnet run --project src/Payments.Api --urls http://localhost:5080` |
| Is it alive? | `curl -i http://localhost:5080/health` |
| Create a payment | `curl -i -X POST http://localhost:5080/payments -H 'Content-Type: application/json' -d '{"merchantId":"11111111-1111-1111-1111-111111111111","amount":100.00,"currency":"EUR"}'` |
| Read one payment | `curl -s http://localhost:5080/payments/<id> \| python3 -m json.tool` |
| Authorise / void it | `curl -i -X POST http://localhost:5080/payments/<id>/authorise` |
| Capture or refund part of it | `curl -i -X POST http://localhost:5080/payments/<id>/capture -H 'Content-Type: application/json' -d '{"amount":40.00,"currency":"EUR"}'` |
| Send a POST with an idempotency key (lab 03) | add `-H 'Idempotency-Key: <any string>'`. Same key again → same answer and `Idempotent-Replayed: true` |
| See the stored keys (lab 03) | `docker exec payments-postgres psql -U labs -d payments_labs -c "select key, status, response_code from idempotency_key;"` |
| List the key's merchant's payments (lab 08) | `curl -s 'http://localhost:5080/payments?status=Captured&limit=5' -H "X-Api-Key: $PAYMENTS_DEV_KEY" \| python3 -m json.tool` — the merchant comes from the key, at most 20 rows |
| See the headers only | add `-I`, or keep `-i` to get headers **and** body |
| Pretty-print any response | pipe it to `python3 -m json.tool` |

`-i` prints the status line and the headers with the body. That matters here: this lab's whole point is the status code, so a body alone is half the answer.

A failure body is `application/problem+json` and always carries a `code`. Read that field, not the English:

```bash
curl -s -X POST http://localhost:5080/payments/<id>/refund \
  -H 'Content-Type: application/json' -d '{"amount":9999.00,"currency":"EUR"}' \
  | python3 -c 'import sys,json; d=json.load(sys.stdin); print(d["status"], d["code"])'
```
