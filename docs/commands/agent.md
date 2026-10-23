# The agent and the local model (lab 09)

Start the model, talk to the agents, read a run's evidence, score models.

← back to [the command index](../EVERYDAY-COMMANDS.md)

---

Load the keys first, in every terminal: `set -a; source .env; set +a`. The API must run on `:5080` (`docs/commands/api.md`), and `Payments.Mcp` must be built: the agent starts the built `.dll` as a child process.

## The model

| I want to | Command |
|---|---|
| Start the small model (rung 1) | `llama-server -hf unsloth/gemma-4-E4B-it-qat-GGUF:UD-Q4_K_XL --jinja -c 16384 --port 8080` |
| Start the bigger model (rung 3) | `llama-server -hf unsloth/gemma-4-26B-A4B-it-GGUF:UD-Q4_K_XL --jinja -c 16384 --port 8080` |
| Is it up, and which model? | `curl -s localhost:8080/v1/models` |
| Which models are on disk? | `ls ~/.cache/huggingface/hub \| grep -i gguf` |

Never drop `--jinja`: without it the model prints tool calls as text, and every call is a "leak".

## The agent

| I want to | Command |
|---|---|
| Build the MCP server it starts | `dotnet build src/Payments.Mcp` |
| Make the demo payments | `dotnet run --project src/Payments.Agent -- seed-demo` |
| Chat with the refund agent | `dotnet run --project src/Payments.Agent -- chat --agent support-bot` |
| Chat with the read-only agent | `dotnet run --project src/Payments.Agent -- chat --agent read-bot` |
| One request, before the profiles exist (step 16) | `dotnet run --project src/Payments.Agent -- run "<request>"` |
| One request, no chat | `dotnet run --project src/Payments.Agent -- run --agent support-bot "<request>"` |
| Crash on purpose after 3 steps | add `--crash-after 3` to `run` — it exits with `99` |
| Resume a run | `dotnet run --project src/Payments.Agent -- resume <runId>` |
| Show a run and every step | `dotnet run --project src/Payments.Agent -- show <runId>` |
| Score a model on the evaluation set | `dotnet run --project src/Payments.Agent -- eval --label <model name>` |
| Run only the agent tests | `dotnet test --project tests/Payments.Agent.Tests` |
| Run only the agent tests CI runs | `dotnet test --project tests/Payments.Agent.Tests --filter-not-trait runs=local` |

## The runs in SQL Server

| I want to | Command |
|---|---|
| The last 10 runs | `docker exec payments-mssql /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -d PaymentsLabs -Q "SELECT TOP 10 Id, AgentName, Status, Reason, ModelId FROM AgentRun ORDER BY CreatedUtc DESC;"` |
| One run's steps | `docker exec payments-mssql /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -d PaymentsLabs -Q "SELECT Ordinal, Kind, ToolName, Outcome, Approver FROM AgentStep WHERE RunId = '<runId>' ORDER BY Ordinal;"` |
| Test databases left behind | `docker exec payments-mssql /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -Q "SELECT name FROM sys.databases WHERE name LIKE 'PaymentsLabs_Test_%';"` |

The whole live demo, scene by scene: [`labs/09-agent-loop/DEMO.md`](../../labs/09-agent-loop/DEMO.md).
