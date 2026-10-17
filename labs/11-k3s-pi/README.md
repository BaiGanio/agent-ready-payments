# Lab 11 (optional) — k3s on the Raspberry Pi 5

## What we solve
Run the payment API and its database in a real small cluster, not just on the laptop.

## Simple example
You unplug the Pi's API pod (delete it). Kubernetes starts a new one within seconds. Postgres keeps its data because it runs as a **StatefulSet** with its own disk. Callers don't notice, because the API keeps no state in memory (idempotency and locks live in the DB from Labs 03–04).

## Why it matters for a payment platform
"Enterprise-scale, multi-server" is in the nice-to-have list. This shows you understand what makes a service safe to run as many copies. Postgres only — SQL Server has no ARM image.

## Everyday picture
A **restaurant chain** with interchangeable waiters. If one waiter goes home sick, another takes the same notepad and carries on. It only works because nobody keeps the orders in their head — they are all on the shared notepad (the database).

## Aperio comparison (our backbone)
| Case | Aperio | Where (verified 2026-10-17) | What this lab adds |
|---|---|---|---|
| k3s deployment | ✅ Has | `k8s/`: Postgres `StatefulSet` + `Service` (`postgres.yaml`), app `Deployment` + `PersistentVolumeClaim` (`aperio.yaml`), `k3s-instructions.md`, `.github/workflows/cd.k3s-deploy.yml` | The same for a .NET API on arm64 |
| Health probes | ✅ Has | `readinessProbe` and `livenessProbe` in `k8s/aperio.yaml` | Separate live/ready checks in .NET |
| Multi-stage image | ✅ Has | `docker/Dockerfile` (`node:24-trixie-slim`) | .NET SDK → runtime image, `linux-arm64` |

**Related:** Lab 17 adds a `startupProbe` and a readiness check that waits for warm-up (live ≠ ready).

## Done when
API + Postgres run on the Pi; health checks pass; a pod restart loses nothing.

Spec + manifests: written when the lab starts.
