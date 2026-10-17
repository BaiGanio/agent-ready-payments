# Lab 10 — .NET Framework → .NET Core (paper lab)

## What we solve
Know what breaks when an old Windows-only payment service moves to modern .NET, and in what order to move it.

## Simple example
An old service reads config with `ConfigurationManager.AppSettings["AcquirerUrl"]`, gets the user from `HttpContext.Current`, and exposes a WCF endpoint.
- On .NET 10: `HttpContext.Current` doesn't exist. WCF server doesn't exist. `web.config` is not read.
- So: config → `IOptions<T>`, `HttpContext.Current` → injected service, WCF → CoreWCF or REST/gRPC — one piece at a time, with tests around the old behaviour first.

## Why it matters for a payment platform
The ad: *"C# .NET Framework, while contributing to the transition towards .NET Core."* They are in the middle of this move. Showing a calm, step-by-step plan is worth a lot.

## Everyday picture
**Moving to a new country.** Your furniture mostly fits (your C# and SQL). The plugs are different (config, `HttpContext`), some appliances don't work at all (WCF server, Web Forms), and you don't move everything in one night — room by room, while still living there (strangler fig).

## EF Core angle
EF6 → EF Core is not a drop-in. EF6 6.4+ runs on modern .NET, so move the runtime first and the ORM later.

## Aperio comparison (our backbone)
| Case | Aperio | Where (verified 2026-10-17) | What this lab adds |
|---|---|---|---|
| .NET Framework → .NET | ❌ No | Aperio is Node.js | The whole topic |
| Incremental extraction from a big module | ✅ Has | `turnLock.js` was extracted from `wsHandler.js` in steps ("Phase 5b, issue #307", file header) | Strangler-fig at service level |
| Rules for module size and blast radius | ✅ Has | `AGENTS.md`: "Module size", "Fragile / No-Touch Zones", "Module Coupling Map" | A migration order based on coupling |

## Done when
A one-page migration note for a small Framework-style sample. (No running code: .NET Framework can't run on macOS.)

Sample + spec: written when the lab starts.
