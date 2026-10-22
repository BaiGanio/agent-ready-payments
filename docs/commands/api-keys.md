# API keys (lab 08)

Make a key, store its hash, revoke it.

← back to [the command index](../EVERYDAY-COMMANDS.md)

---

From lab 08 every `/payments` call needs `-H "X-Api-Key: $PAYMENTS_DEV_KEY"`. Without it the answer is `401`. `/health` needs no key. Load the keys into your shell first: `set -a; source .env; set +a`.

| I want to | Command |
|---|---|
| Make a new raw key | `openssl rand -base64 24 \| tr '+/' '-_' \| tr -d '='` — put it in `.env`, never in git |
| Store a service key for M1 (all write scopes) | `docker exec payments-postgres psql -U labs -d payments_labs -c "insert into api_key (key_hash, name, merchant_id, kind, scopes) values (encode(sha256(convert_to('$PAYMENTS_DEV_KEY','UTF8')),'hex'), 'dev-service', '11111111-1111-1111-1111-111111111111', 'service', '{payments:read,payments:write,payments:refund}');"` |
| Store a refund agent key for M1 | the same insert with `$PAYMENTS_AGENT_KEY`, name `support-bot`, kind `agent`, scopes `{payments:read,payments:refund}` |
| Store a read-only agent key for M1 | the same insert with `$PAYMENTS_READONLY_AGENT_KEY`, name `read-bot`, kind `agent`, scopes `{payments:read}` |
| See the stored keys (hashes only) | `docker exec payments-postgres psql -U labs -d payments_labs -c "select name, kind, scopes, left(key_hash, 12), revoked_at from api_key;"` |
| Revoke a key | `docker exec payments-postgres psql -U labs -d payments_labs -c "update api_key set revoked_at = now() where name = 'support-bot';"` |
| Call the API with a key | add `-H "X-Api-Key: $PAYMENTS_DEV_KEY"` to any `curl` in "The API" above |

The database stores only the SHA-256 of a key. `sha256(convert_to(…))` in Postgres and `ApiKeyHash.Of` in C# give the same lower-case hex, so a key stored by `psql` works in the API.
