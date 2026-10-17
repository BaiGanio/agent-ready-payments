# Lab 10 — Interview Q&A: .NET Framework → .NET (Core) migration

The ad: *"C# .NET Framework, while contributing to the transition towards .NET Core."* You can't run Framework on a Mac, so this lab is reading and planning. Answer from understanding, and say so honestly.

---

**Q1. What are the main differences?**
A: .NET Framework 4.8.x: Windows only, in maintenance, ships with Windows. .NET (Core → 5 → … → 10): cross-platform, open source, much faster, side-by-side versions, yearly releases with LTS every two years (8, 10).

**Q2. What does not exist in modern .NET?**
A: ASP.NET Web Forms, WCF **server** (CoreWCF is a community port), .NET Remoting, AppDomains (only one per process), Code Access Security, `System.Web` (`HttpContext.Current`), WF (Workflow Foundation).

**Q3. How do you plan a migration of a big payment platform?**
A: Incrementally, never big-bang. (1) Inventory: projects, dependencies, NuGet packages, Windows-only APIs. (2) Move class libraries to **.NET Standard 2.0** or multi-target `net48;net8.0` so both worlds can use them. (3) Convert to SDK-style project files. (4) Move services one by one, behind the same contracts. (5) Use the **strangler fig** pattern: route endpoint by endpoint from the old app to the new. (6) Tests first: characterisation tests on the old behaviour before moving.

**Q4. What tools help?**
A: .NET Upgrade Assistant (also in Visual Studio / GitHub Copilot modernization flows), the API Portability analyzers, `try-convert` for project files, and `Microsoft.Windows.Compatibility` package as a temporary bridge for Windows APIs.

**Q5. What is .NET Standard and is it still relevant?**
A: A spec of APIs available on both Framework and Core. .NET Standard 2.0 is still the bridge target when a library must serve Framework *and* modern .NET during a migration. For new code only on modern .NET, target `net10.0` directly.

**Q6. `web.config` / `app.config` → ?**
A: `appsettings.json` + environment variables + user secrets + `IConfiguration` / `IOptions<T>`. `ConfigurationManager.AppSettings` calls must be replaced (or bridged via the compatibility package for a while).

**Q7. `HttpContext.Current` → ?**
A: Inject `IHttpContextAccessor` or, better, pass what you need explicitly. Static ambient access hides dependencies and breaks tests.

**Q8. DI differences?**
A: Framework apps often use Autofac/Unity/Ninject or none. Modern .NET has built-in `Microsoft.Extensions.DependencyInjection`. Autofac can stay as the container if the codebase depends on its features.

**Q9. WCF services in a payment platform — what do you do?**
A: Clients: `System.ServiceModel.*` client packages work on modern .NET. Servers: CoreWCF for a like-for-like port, or move to gRPC / REST over time. Keep the contract stable for existing partners.

**Q10. Data access: `System.Data.SqlClient` → ?**
A: `Microsoft.Data.SqlClient`. Watch `Encrypt=true` default (v4+) and some connection string differences. Dapper and stored procedures move as they are — that is a big reason SQL-heavy systems migrate relatively smoothly.

**Q11. EF6 → EF Core?**
A: Not a drop-in. EF6 6.4+ runs on modern .NET, so you can move the runtime first and the ORM later.

**Q12. Async differences that bite?**
A: Classic ASP.NET has a `SynchronizationContext`; `.Result` on async code can deadlock. ASP.NET Core has none, so some bugs disappear and some hidden timing assumptions change. Code with `.Result` should be fixed, not just moved.

**Q13. Threading APIs that changed?**
A: `Thread.Abort` is not supported (throws). Use cooperative `CancellationToken` everywhere.

**Q14. Logging?**
A: `log4net`/`NLog` still work, but move to `ILogger<T>` abstraction with structured logging; keep the old sink behind it during migration.

**Q15. Hosting: IIS / Windows Services → ?**
A: Kestrel, behind IIS (in-process or out-of-process) or a reverse proxy. Windows Services become `BackgroundService` hosted with `UseWindowsService()` — or Linux containers later.

**Q16. Performance — what do you gain?**
A: Big wins from runtime/JIT/GC improvements, `Span<T>`, pooled buffers, faster JSON (`System.Text.Json`), Kestrel. Measure before and after with the same load test; payments care about p99 latency, not averages.

**Q17. Biggest risk?**
A: Behaviour drift in edge cases (culture/globalization — ICU vs NLS on Windows, string comparison, decimal formatting, time zones). In payments, formatting and rounding differences are real money. Characterisation tests on amounts and dates catch them.

**Q18. Honest answer about your experience?**
A: (Fill in truthfully.) Say which Framework versions you used professionally, what you migrated or saw migrated, and that this lab was where you organised the knowledge.


## Aperio: have you solved this before?

**Q-APERIO. "Have you solved this before?"**
A: Not the .NET migration itself. But I've done the same kind of work: extracting load-bearing code from a large module step by step, guided by a written coupling map and fragile-zone list in `AGENTS.md`. The method — characterise, extract one piece, verify, repeat — is the method I'd use for Framework → Core.
Details and file references: the "Aperio comparison" table in this lab's `README.md`.
