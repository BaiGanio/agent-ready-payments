# Debug with breakpoints (VS Code)

Stop the program on a line and look at the values.

← back to [the command index](../EVERYDAY-COMMANDS.md)

---

A **breakpoint** is a red dot on a line. The program stops there so you can look at the values.

| I want to | How |
|---|---|
| Open the project in VS Code | `code /Users/lk/Projects/BaiGanio/agent-ready-payments` |
| Set a breakpoint | Click left of the line number, or put the caret on the line and press `F9` |
| Start debugging | `F5`, then pick **Debug Payments.Db** the first time |
| Pass arguments (for example `migrate --engine postgres`) | Edit `"args"` in `.vscode/launch.json` |
| Step over one line | `F10` |
| Step into a method | `F11` |
| Continue to the next breakpoint | `F5` |
| Stop debugging | `Shift-F6` |
| See a value | Hover over it, or read the **Variables** panel |
| Test an expression while stopped | Type it in the **Debug Console** |
| Stop only when a condition is true | Right-click the breakpoint → **Edit Breakpoint** |
| Debug one test | Click **Debug Test** above the test method (needs the C# Dev Kit) |
| Attach to an already running process | `F5` → **Attach to a running .NET process** |

The debug run loads `.env`, so `MSSQL_SA_PASSWORD` is there. Start the databases first if the code needs them.
