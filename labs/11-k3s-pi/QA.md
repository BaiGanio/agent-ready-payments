# Lab 11 (optional) — Interview Q&A: containers, k3s, running the API

Aperio already has k3s manifests (`k8s/postgres.yaml`, `k8s/aperio.yaml`, `k8s/k3s-instructions.md`) and a deploy workflow (`.github/workflows/cd.k3s-deploy.yml`). You can point to them.

---

**Q1. Docker image for a .NET API — key points?**
A: Multi-stage build: `sdk` image to build/publish, `aspnet` (or chiseled/distroless) runtime image to run. Run as non-root. `dotnet publish` with the right RID (`linux-arm64` for the Pi). Small images, fewer CVEs.

**Q2. Deployment vs StatefulSet?**
A: Deployment for stateless pods (the API) — interchangeable replicas. StatefulSet for stateful ones (Postgres) — stable name, stable storage per pod, ordered start/stop.

**Q3. How does the API find Postgres?**
A: A Kubernetes **Service** gives a stable DNS name (`postgres.payments.svc.cluster.local`). Connection string from a **Secret**, other settings from a **ConfigMap**.

**Q4. Liveness vs readiness probes?**
A: Liveness: "is the process stuck? restart it". Readiness: "can it take traffic now?" (DB reachable, warm). In .NET: `MapHealthChecks("/health/live")` and `/health/ready` with a DB check. Don't put the DB in liveness — a DB blip would restart every pod.

**Q5. Graceful shutdown in Kubernetes?**
A: SIGTERM → the host's `stoppingToken` fires → stop taking new work, finish in-flight requests and pipeline items (Lab 06) → exit before `terminationGracePeriodSeconds`.

**Q6. Why not SQL Server on the Pi?**
A: There is no ARM64 Linux SQL Server image; Azure SQL Edge (the ARM option) is retired. Postgres runs natively.

**Q7. Config and secrets in .NET in a container?**
A: Environment variables override `appsettings.json` (`ConnectionStrings__Default`). Secrets from Kubernetes Secrets or a vault, never in the image.

**Q8. Scaling the payment API horizontally — what must be true?**
A: No in-memory state that matters: idempotency in the DB (Lab 03), no sticky sessions, locks in the DB not in `lock` statements. That is why the earlier labs keep state in the database.

**Q9. Migrations in Kubernetes?**
A: Run them as a separate Job or an init step before the new version rolls out, not on every pod start (race between replicas).

**Q10. k3s vs full Kubernetes?**
A: k3s is a lightweight certified distribution: single binary, SQLite/etcd, good for edge and small clusters like a Pi. Same API, same manifests.


## Aperio: have you solved this before?

**Q-APERIO. "Have you solved this before?"**
A: Yes. Aperio already deploys to k3s with a Postgres StatefulSet, an app Deployment, probes and a CD workflow. This lab repeats it for a .NET API on the Pi, so it is mostly about the .NET-specific parts.
Details and file references: the "Aperio comparison" table in this lab's `README.md`.
