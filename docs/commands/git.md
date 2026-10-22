# Git (Gitflow)

Branch, commit, merge, tag, back-merge, and escaping vim.

← back to [the command index](../EVERYDAY-COMMANDS.md)

---

## The commands

You run every git command. The agent never commits, merges, pushes or tags.

| I want to | Command |
|---|---|
| Where am I? | `git status --short && git branch --show-current` |
| Is `develop` behind `main`? | `git log --oneline develop..main` (empty = level) |
| Bring `develop` level with `main` | `git checkout develop && git merge main` |
| Start a lab | `git checkout develop && git checkout -b feature/lab-NN-short-name` |
| Stage one file | `git add path/to/file` |
| See what I staged, line by line | `git diff --staged` |
| Commit | `git commit -m "type(scope): summary"` |
| Fix the last commit message | `git commit --amend` |
| Unstage a file, keep the edits | `git restore --staged path/to/file` |
| Throw away edits in a file | `git restore path/to/file` |
| See the history as a picture | `git log --oneline --graph --all \| head -20` |
| List the tags | `git tag` |
| List the branches | `git branch` |
| Delete a merged feature branch | `git branch -d feature/lab-NN-short-name` |
| Go to the end of a finished lab | `git checkout lab-NN` (then `git checkout develop` to come back) |
| Finish a lab | see the "Close the lab" step in that lab's `WORKBENCH.md` |
| Get out of vim when git opens it | `:wq` then **Enter** to save, or `:q!` then **Enter** to cancel |
| Stop git opening an editor at all | add `--no-edit` to the merge, or set a kinder editor once (below) |

Closing a lab, in full:

```bash
git checkout develop
git merge --no-ff --no-edit feature/lab-NN-short-name
git checkout main
git merge --no-ff --no-edit develop
git tag lab-NN
git checkout develop
git merge main                 # the back-merge
git branch -d feature/lab-NN-short-name
```

### The editor that opens on a merge

`git merge --no-ff` wants a commit message, so it opens your editor. Out of the box that is **vim**, which is a dark screen full of `~` and no way out that it tells you about. Two ways to never meet it:

- **`--no-edit`** on the merge, as above. Git keeps its own default message — `Merge branch 'feature/…' into develop` — which is exactly what you want for a lab merge. Nothing opens.
- **A kinder editor**, set once, for every git command that needs one:

```bash
git config --global core.editor "nano"
```

Nano lists its keys at the bottom of the screen: `Ctrl+O` then **Enter** saves, `Ctrl+X` exits.

If vim is already on your screen right now, the default message on line 1 is already correct:

| You want | Type | Then |
|---|---|---|
| Keep the message and finish the merge | `:wq` | **Enter** |
| Cancel — an empty message aborts the commit | `:q!` | **Enter** |

`:` opens vim's command line at the bottom, `w` writes, `q` quits, `!` means "even though I changed nothing".

**The back-merge is not optional.** Merging `develop` into `main` makes a merge commit that lives only on `main`, so `develop` is behind from that second on. The next feature branch would then start from a base missing `main`'s history, and a hotfix made on `main` would never reach `develop` at all. Check with `git log --oneline develop..main` — empty means level.

Never commit lab work straight to `main`. If it happened anyway and `main` is only ahead, fast-forward `develop` onto it:

```bash
git checkout develop
git merge --ff-only main
```

`--no-ff` forces a merge commit even when git could fast-forward. That commit is the marker that one lab finished.

`git branch -d` deletes the branch name only when its work is merged. A branch name is just a pointer at one commit, so this drops the pointer and keeps every commit. The tag `lab-NN` is the bookmark after that: `git checkout lab-NN` puts you at the exact end of that lab.

Commit message shape: `<type>(<scope>): <summary in imperative mood>`. Types: `feat`, `fix`, `test`, `docs`, `refactor`, `chore`, `build`, `ci`. Scope is the lab (`lab-03`) or an area (`db`, `api`, `mcp`, `agent`, `plan`, `repo`).

The suggested message for each step is in that step's **Commit** part in `WORKBENCH.md`. You do not have to invent one.

---

## Safety check before any commit

```bash
git status --short          # .env must NOT appear
git diff --staged           # read every line you are about to commit
```

No real card data. Not in code, tests, seeds, logs or prompts. Test card numbers only.
