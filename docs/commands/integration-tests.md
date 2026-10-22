# Integration tests with containers (lab 02)

Testcontainers runs, and the containers they leave behind.

← back to [the command index](../EVERYDAY-COMMANDS.md)

---

| I want to | Command |
|---|---|
| Run only the API tests | `dotnet test tests/Payments.Api.Tests` |
| Run one group | `dotnet test tests/Payments.Api.Tests --filter "RefundTests"` |
| See the throwaway containers while tests run | `docker ps` |
| Kill a container a crashed test left behind | `docker rm -f <container id>` |
| Check what Docker socket the library will find | `docker context ls` |

The first run pulls `pgvector/pgvector:pg17` and takes about a minute. Later runs are seconds, because the image is cached. Testcontainers removes its own containers; if a fixture crashes before cleanup, its Ryuk reaper clears them within minutes, and `docker rm -f` is the impatient version.
