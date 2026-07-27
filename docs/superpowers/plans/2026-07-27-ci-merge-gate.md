# CI Merge Gate Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `.github/workflows/corpus-tests.yml` run only when a pull request targeting `main` needs verifying, pin every moving reference it depends on, and document that its result does not yet block a merge.

**Architecture:** Four file changes, no code. The workflow's trigger block is narrowed and given a concurrency group; its runner image and three action references are replaced with exact coordinates; a new `.github/dependabot.yml` watches for action updates; and the un-enforced merge gate is recorded in three places, of which `docs/design/ci.md` carries the runbook to activate it once the repository is public.

**Tech Stack:** GitHub Actions workflow YAML, Dependabot v2 config, Markdown. Verification uses `npx js-yaml` to parse and Node to assert structure — a scratchpad-only harness, never committed.

## Global Constraints

Every task's requirements implicitly include this section.

- **Spec:** `docs/superpowers/specs/2026-07-27-ci-merge-gate-design.md`. Where this plan and the spec disagree, the spec wins — stop and report the discrepancy.
- **Exact action versions:** `actions/checkout@v7.0.1`, `actions/setup-dotnet@v6.0.0`, `actions/upload-artifact@v7.0.1`. Exact `vX.Y.Z` tags, never bare majors.
- **Exact runner image:** `windows-2025-vs2026`. Never `windows-latest`.
- **Do not change** the `dotnet-version: 10.0.302` pin, any `run:` step body, any step `name:`, the job ID `corpus-tests`, or the workflow `name: Corpus verification`. The job ID is the required-check context named in the activation runbook; renaming it silently breaks the future gate.
- **Do not commit the verification harness.** The `check-*.js` files live only in the scratchpad directory. The repository's convention is that committed tooling is single-file C#, never a shell or script file. Verify with `git status --short` before each commit that no `.js` file is staged.
- **LF line endings, UTF-8** for every file, enforced by `.gitattributes`.
- **American English** in all prose and comments.
- **State current truth only.** No document narrates its own history, carries a "previously said X" footer, or a dated verification stamp. `git log` is the changelog.
- **Scratchpad path.** Every command below assumes `$SP` is set. Run this once per shell session,
  from the repository root, before Task 1 — the shell's working directory persists between calls but
  its variables do not:

  ```bash
  SP="C:/Users/patri/AppData/Local/Temp/claude/C--src-github-flatrick-dotnet-knowledge/7077ff07-7550-4df0-9dca-7363e5062f46/scratchpad"
  mkdir -p "$SP" && echo "$SP"
  ```

  If a check command reports `Cannot find module` for a `check-*.js` file, `$SP` was lost — re-run
  the assignment above and re-create that task's script.

## Why the tests look like this

There is no unit-test framework for CI configuration, and the authoritative validator — GitHub itself — only runs after a push. So each task gets a real red/green cycle from a two-part local check:

1. `npx --yes js-yaml <file>` proves the file parses as YAML at all. Exit 0 means well-formed.
2. A Node script reads that parsed JSON from stdin and asserts the exact structure the spec requires, printing every failure and exiting 1.

Both parts have been confirmed to fail against the current files and pass against the target. This catches structural errors — the cheap, common ones. It does not catch a name GitHub rejects, which is what Task 5 is for.

## File Structure

| Path | Responsibility | Task |
|---|---|---|
| `.github/workflows/corpus-tests.yml` | When verification runs, on what image, using which action versions | 1, 2, 4 |
| `.github/dependabot.yml` | Watching for action updates so pins stay deliberate rather than stale | 3 |
| `docs/design/ci.md` | The reasoning behind the CI configuration, and the runbook to activate the merge gate | 4 |
| `AGENTS.md` | One-line warning to agents that CI green is advisory, plus a directory-map row | 4 |

Tasks 1, 2 and 4 all touch the workflow file, in that order, and each is independently reviewable: a reviewer could accept the new trigger model while rejecting the runner-image pin, or accept both while wanting different comment wording.

---

### Task 1: Narrow the triggers and add concurrency

**Files:**
- Modify: `.github/workflows/corpus-tests.yml:3-5` (the `on:` block)
- Test: `$SP/check-triggers.js` (scratchpad only — never committed)

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: the `on:` and `concurrency:` blocks that Task 4's header comment describes, and the run behavior Task 5 verifies live.

- [ ] **Step 1: Write the failing test**

Create `$SP/check-triggers.js`:

```javascript
let s = '';
process.stdin.on('data', d => s += d).on('end', () => {
  const w = JSON.parse(s), fail = [];
  const on = w.on;
  const eq = (a, b) => JSON.stringify(a) === JSON.stringify(b);
  if (!on) fail.push('no `on:` block');
  else {
    if (!eq(on.pull_request && on.pull_request.branches, ['main']))
      fail.push('on.pull_request.branches must be ["main"], got ' + JSON.stringify(on.pull_request && on.pull_request.branches));
    if (!eq(on.push && on.push.branches, ['main']))
      fail.push('on.push.branches must be ["main"], got ' + JSON.stringify(on.push && on.push.branches));
    if (!('workflow_dispatch' in on)) fail.push('on.workflow_dispatch missing');
  }
  const c = w.concurrency;
  if (!c) fail.push('no `concurrency:` block');
  else {
    if (c.group !== '${{ github.workflow }}-${{ github.event_name == \'pull_request\' && github.ref || github.run_id }}') fail.push('concurrency.group wrong: ' + c.group);
    if (c['cancel-in-progress'] !== "${{ github.event_name == 'pull_request' }}")
      fail.push('cancel-in-progress wrong: ' + c['cancel-in-progress']);
  }
  if (fail.length) { console.error('FAIL'); fail.forEach(f => console.error('  - ' + f)); process.exit(1); }
  console.log('PASS: triggers and concurrency');
});
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
npx --yes js-yaml .github/workflows/corpus-tests.yml | node "$SP/check-triggers.js"
```

Expected: exit 1, and exactly these four lines under `FAIL`:

```
  - on.pull_request.branches must be ["main"], got null
  - on.push.branches must be ["main"], got null
  - on.workflow_dispatch missing
  - no `concurrency:` block
```

- [ ] **Step 3: Make the change**

Replace lines 3–5 of `.github/workflows/corpus-tests.yml`, which currently read:

```yaml
on:
  pull_request:
  push:
```

with:

```yaml
on:
  pull_request:
    branches: [main]
  push:
    branches: [main]
  workflow_dispatch:

concurrency:
  group: ${{ github.workflow }}-${{ github.event_name == 'pull_request' && github.ref || github.run_id }}
  cancel-in-progress: ${{ github.event_name == 'pull_request' }}
```

Leave the blank line and `jobs:` that follow exactly as they are.

- [ ] **Step 4: Run the test to verify it passes**

```bash
npx --yes js-yaml .github/workflows/corpus-tests.yml | node "$SP/check-triggers.js"
```

Expected: exit 0, `PASS: triggers and concurrency`.

- [ ] **Step 5: Confirm nothing else moved**

```bash
git diff --stat
git diff .github/workflows/corpus-tests.yml
```

Expected: one file changed, additions only in the `on:`/`concurrency:` region. If the diff shows a change to `runs-on`, any `uses:`, any `run:` body, or `dotnet-version`, revert it — those belong to Task 2 or to no task at all.

- [ ] **Step 6: Commit**

```bash
git add .github/workflows/corpus-tests.yml
git status --short
git commit -m "Run corpus verification only for pull requests to main

Feature-branch pushes no longer trigger a run. Verification happens when
a pull request targets main, on pushes to main, and on manual dispatch.

Superseded pull-request runs are cancelled, so a burst of commits
verifies once against the revision that can actually be merged. Runs on
main are never cancelled, because nothing currently blocks a direct push
there and each commit needs its own pass/fail record."
```

`git status --short` must show no `.js` file. If it does, the scratchpad path was wrong — remove the file from the repository and re-create it under `$SP`.

---

### Task 2: Pin the runner image and the three action versions

**Files:**
- Modify: `.github/workflows/corpus-tests.yml` — the `runs-on:` value and three `uses:` values
- Test: `$SP/check-pins.js` (scratchpad only — never committed)

**Interfaces:**
- Consumes: the workflow file as left by Task 1.
- Produces: the pinned coordinates that Task 3's Dependabot config watches and that Task 4's `docs/design/ci.md` documents.

**Context an implementer needs:** this closes [issue #2](https://github.com/flatrick/dotnet-knowledge/issues/2). Every major version crossed here is a Node 20 → Node 24 runtime bump and a CommonJS → ESM packaging change, not an input-schema change. The workflow passes `dotnet-version`, `name` and `path`; none of those inputs moved. `windows-2025-vs2026` is the alias naming both the OS and the Visual Studio version of the image that `windows-latest` resolves to today.

- [ ] **Step 1: Write the failing test**

Create `$SP/check-pins.js`:

```javascript
const EXPECTED_IMAGE = 'windows-2025-vs2026';
const ALLOWED = {
  'actions/checkout': 'v7.0.1',
  'actions/setup-dotnet': 'v6.0.0',
  'actions/upload-artifact': 'v7.0.1',
};
let s = '';
process.stdin.on('data', d => s += d).on('end', () => {
  const w = JSON.parse(s), fail = [];
  for (const [jobId, job] of Object.entries(w.jobs || {})) {
    if (job['runs-on'] !== EXPECTED_IMAGE)
      fail.push(`job ${jobId}: runs-on must be ${EXPECTED_IMAGE}, got ${job['runs-on']}`);
    for (const step of job.steps || []) {
      if (!step.uses) continue;
      const [action, ref] = step.uses.split('@');
      if (!ref) { fail.push(`step "${step.uses}" has no @ref`); continue; }
      if (!/^v\d+\.\d+\.\d+$/.test(ref))
        fail.push(`step "${step.uses}" is not pinned to an exact vX.Y.Z tag`);
      if (ALLOWED[action] && ref !== ALLOWED[action])
        fail.push(`${action} must be ${ALLOWED[action]}, got ${ref}`);
    }
  }
  if (fail.length) { console.error('FAIL'); fail.forEach(f => console.error('  - ' + f)); process.exit(1); }
  console.log('PASS: runner image and action pins');
});
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
npx --yes js-yaml .github/workflows/corpus-tests.yml | node "$SP/check-pins.js"
```

Expected: exit 1, with seven lines under `FAIL` — one for `runs-on: windows-latest`, then two per action (not an exact tag; wrong version).

- [ ] **Step 3: Make the change**

Four single-line edits in `.github/workflows/corpus-tests.yml`:

| Current | Replace with |
|---|---|
| `    runs-on: windows-latest` | `    runs-on: windows-2025-vs2026` |
| `      - uses: actions/checkout@v4` | `      - uses: actions/checkout@v7.0.1` |
| `        uses: actions/setup-dotnet@v4` | `        uses: actions/setup-dotnet@v6.0.0` |
| `        uses: actions/upload-artifact@v4` | `        uses: actions/upload-artifact@v7.0.1` |

Indentation differs between the first `uses:` and the other two — the first is a bare step, the others sit under a `name:`. Preserve each line's existing leading whitespace.

- [ ] **Step 4: Run the test to verify it passes**

```bash
npx --yes js-yaml .github/workflows/corpus-tests.yml | node "$SP/check-pins.js"
```

Expected: exit 0, `PASS: runner image and action pins`.

Then, **only if `$SP/check-triggers.js` still exists** — it will not in a fresh session, and that is
fine — re-run it to confirm this task did not disturb Task 1's work:

```bash
[ -f "$SP/check-triggers.js" ] && npx --yes js-yaml .github/workflows/corpus-tests.yml | node "$SP/check-triggers.js" || echo "skipped: check-triggers.js not present in this session"
```

- [ ] **Step 5: Confirm the diff is exactly four lines**

```bash
git diff --numstat .github/workflows/corpus-tests.yml
```

Expected: `4	4	.github/workflows/corpus-tests.yml`. Any other number means a `run:` body or the `dotnet-version` pin was touched — revert and redo.

- [ ] **Step 6: Commit**

```bash
git add .github/workflows/corpus-tests.yml
git status --short
git commit -m "Pin the runner image and every action to an exact version

Closes #2.

checkout v4 to v7.0.1, setup-dotnet v4 to v6.0.0, upload-artifact v4 to
v7.0.1. Every major crossed is a Node 24 and ESM change rather than an
input-schema change, and this workflow passes only dotnet-version, name
and path.

windows-latest becomes windows-2025-vs2026, the alias naming both the OS
and the Visual Studio version. The label resolves to that image today,
but it migrates on GitHub's schedule, and the net48 corpus depends on
Visual Studio's MSBuild and on vswhere. Under a moving label an image
migration surfaces as a corpus regression with no corresponding commit."
```

**If the run later fails at the SDK-install step:** that is the anticipated second outcome, not a defect. Per the spec's "When an action cannot move", pin `setup-dotnet` back with the reason recorded in place, and update `ALLOWED['actions/setup-dotnet']` in the check script to match:

```yaml
      # Pinned to v4: v5.0.0 rewrote the installer scripts and dropped older
      # .NET versions, and cannot install the 10.0.302 SDK this corpus needs.
      # Re-test against a newer major before changing this.
        uses: actions/setup-dotnet@v4.3.1
```

Issue #2 closes either way — it reports drift, not being off the newest tag.

---

### Task 3: Add the Dependabot configuration

**Files:**
- Create: `.github/dependabot.yml`
- Test: `$SP/check-dependabot.js` (scratchpad only — never committed)

**Interfaces:**
- Consumes: the exact pins from Task 2 — Dependabot has nothing to update without them.
- Produces: the update stream that `docs/design/ci.md` (Task 4) tells the reader how to handle when a pin is blocked.

**Context an implementer needs:** the ecosystem is `github-actions` **only**. A `nuget` entry would open pull requests that must always be rejected, because corpus package versions are pinned deliberately to historical releases — `Microsoft.Net.Compilers` 1.3.2 is pinned precisely because it is old. These are Dependabot *version* updates, which work on private repositories on the Free plan, and are a different feature from Dependabot security alerts.

- [ ] **Step 1: Write the failing test**

Create `$SP/check-dependabot.js`:

```javascript
let s = '';
process.stdin.on('data', d => s += d).on('end', () => {
  const c = JSON.parse(s), fail = [];
  if (c.version !== 2) fail.push('version must be 2, got ' + c.version);
  const ups = c.updates || [];
  const ga = ups.filter(u => u['package-ecosystem'] === 'github-actions');
  if (ga.length !== 1) fail.push('expected exactly 1 github-actions entry, got ' + ga.length);
  else {
    if (ga[0].directory !== '/') fail.push('directory must be "/", got ' + ga[0].directory);
    if (!ga[0].schedule || ga[0].schedule.interval !== 'weekly')
      fail.push('schedule.interval must be weekly, got ' + JSON.stringify(ga[0].schedule));
  }
  const nuget = ups.filter(u => u['package-ecosystem'] === 'nuget');
  if (nuget.length) fail.push('nuget ecosystem is out of scope per spec; found ' + nuget.length + ' entry');
  if (fail.length) { console.error('FAIL'); fail.forEach(f => console.error('  - ' + f)); process.exit(1); }
  console.log('PASS: dependabot config');
});
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
npx --yes js-yaml .github/dependabot.yml | node "$SP/check-dependabot.js"
```

Expected: exit 2, with `File not found: .github/dependabot.yml` printed before Node ever runs. That is the correct red state.

- [ ] **Step 3: Create the file**

`.github/dependabot.yml`:

```yaml
# Watches the action versions pinned in workflows/corpus-tests.yml. Those are
# exact vX.Y.Z tags, so every update arrives as a reviewable pull request that
# the corpus workflow then verifies.
#
# github-actions only. NuGet versions in this repository are pinned to specific
# historical releases as part of the corpus contract — Microsoft.Net.Compilers
# 1.3.2 is pinned because it is old — so a nuget entry would open pull requests
# that must always be rejected.
version: 2
updates:
  - package-ecosystem: github-actions
    directory: "/"
    schedule:
      interval: weekly
```

- [ ] **Step 4: Run the test to verify it passes**

```bash
npx --yes js-yaml .github/dependabot.yml | node "$SP/check-dependabot.js"
```

Expected: exit 0, `PASS: dependabot config`.

- [ ] **Step 5: Commit**

```bash
git add .github/dependabot.yml
git status --short
git commit -m "Watch GitHub Actions versions with Dependabot

Exact pins go stale silently without something watching; the drift that
issue #2 reports accumulated over months for exactly that reason, so
bumping the tags alone would reproduce the starting conditions.

Scoped to github-actions. NuGet versions here are pinned to historical
releases on purpose, so that ecosystem would only produce pull requests
that must be rejected."
```

---

### Task 4: Document the gap and the activation runbook

**Files:**
- Create: `docs/design/ci.md`
- Modify: `.github/workflows/corpus-tests.yml` — insert a header comment above `name:`
- Modify: `AGENTS.md` — one directory-map row, and a new closing section

**Interfaces:**
- Consumes: the trigger model (Task 1), the pins (Task 2), the Dependabot scope (Task 3). All three are described in `docs/design/ci.md`.
- Produces: nothing later tasks depend on. Task 5 verifies the workflow still parses afterward.

**Context an implementer needs:** the gap being documented is that this workflow **runs** but does not **gate**. Required status checks need a ruleset, and `gh api repos/flatrick/dotnet-knowledge/rulesets` returns 403 — *"Upgrade to GitHub Pro or make this repository public"* — because the repository is private on the Free plan. The repository is intended to become public, so the runbook below is the activation procedure, not speculation. Write `docs/design/ci.md` as a runbook describing current configuration and how to change it; it must not narrate how the configuration got here.

- [ ] **Step 1: Create `docs/design/ci.md`**

````markdown
# Continuous integration

One workflow, `.github/workflows/corpus-tests.yml`, running one job, `corpus-tests`.

## What runs, and when

```yaml
on:
  pull_request:
    branches: [main]
  push:
    branches: [main]
  workflow_dispatch:
```

Pushes to a feature branch do not trigger a run. Verification happens when a pull request targets
`main`, which is when the result is needed.

`push` on `main` is not redundant with the pull-request run. It verifies the post-merge state of
`main` rather than the pre-merge prediction of it, and while the merge gate is inactive it is the
only signal that `main` is green at all, because nothing prevents a direct push.

`workflow_dispatch` re-runs verification against any branch from the Actions tab without opening a
pull request.

For `pull_request` events GitHub evaluates the workflow file from the merge commit, so a change to
this file takes effect on the pull request that introduces it.

## Concurrency

```yaml
concurrency:
  group: ${{ github.workflow }}-${{ github.ref }}
  cancel-in-progress: ${{ github.event_name == 'pull_request' }}
```

A burst of commits to a pull request verifies once, against the last one — the only revision that
can be merged. A cancelled run reports as `cancelled` rather than `failure` and does not satisfy a
required check, which is correct for a superseded revision.

**Runs on `main` are never cancelled.** The condition restricts cancellation to `pull_request`
events specifically so that every commit on `main` keeps an independent pass/fail record.

## Version pinning

Every external reference is an exact coordinate. No floating major tags, no `-latest` runner label.

```yaml
runs-on: windows-2025-vs2026

- uses: actions/checkout@v7.0.1
- uses: actions/setup-dotnet@v6.0.0
- uses: actions/upload-artifact@v7.0.1
```

`windows-2025-vs2026` names both the operating system and the Visual Studio version. That second
half is the part this repository actually depends on: the net48 corpus builds through Visual
Studio's `MSBuild.exe`, and `scripts/verify-feature-floors.cs` locates compilers through `vswhere`
and through the in-box `%WINDIR%\Microsoft.NET\Framework64` compilers. GitHub migrates `-latest`
labels to a new image gradually, so under `windows-latest` an image migration would surface as a
corpus failure with no corresponding commit — indistinguishable, at first reading, from a corpus
regression.

Exact action tags trade automatic patch adoption for one reviewable commit per version change.
`.github/dependabot.yml` supplies the adoption, weekly, scoped to `github-actions`. NuGet is
deliberately excluded: corpus package versions are pinned to historical releases as part of the
corpus contract, so that ecosystem would only produce pull requests that must be rejected.

### When an action cannot move

An action that cannot be upgraded is pinned to the version that works, with a comment naming what
blocks it:

```yaml
# Pinned to v4: v5.0.0 rewrote the installer scripts and dropped older .NET
# versions, and cannot install the 10.0.302 SDK this corpus requires.
# Re-test against a newer major before changing this.
- uses: actions/setup-dotnet@v4.3.1
```

That is a resolved state, not a deferred one. The failure mode this repository guards against is a
version nobody chose — one that carries no information about whether it was evaluated or merely
inherited. An annotated pin carries that information and tells the next reader what to re-test.

Dependabot will keep opening pull requests against a pinned-back action. Close them with the
reason, or add an `ignore` entry for that major once the block is confirmed durable rather than
provisional. Do not add the `ignore` first: an unexplained one recreates exactly the invisible
staleness the pin exists to prevent.

## The merge gate is not active

**A green run does not gate anything.** The workflow runs on every pull request to `main`, but
nothing blocks a merge on its result, and nothing blocks a direct push to `main` either.

Required status checks are the only mechanism GitHub offers for blocking a merge on a workflow
result, and they need a ruleset or branch protection. Both endpoints refuse while this repository
is private on the Free plan:

```
gh api repos/flatrick/dotnet-knowledge/branches/main/protection   -> 403
gh api repos/flatrick/dotnet-knowledge/rulesets                   -> 403
"Upgrade to GitHub Pro or make this repository public to enable this feature."
```

There is no workflow-level substitute. A job can fail; nothing consumes that failure as a merge
precondition.

## Activating the gate

Run once, after the repository becomes public. No workflow change is needed — the check already
reports under the name the ruleset asks for.

**Prerequisites.** The repository is public. The `corpus-tests` check has reported at least once;
any run on `main` satisfies this. The account running these commands has admin permission on the
repository.

The two values the ruleset needs, both readable from a live check run rather than assumed:

```bash
sha=$(gh api repos/flatrick/dotnet-knowledge/commits --jq '.[0].sha')
gh api "repos/flatrick/dotnet-knowledge/commits/$sha/check-runs" \
  --jq '.check_runs[] | {name: .name, app_id: .app.id}'
```

Expected: `corpus-tests` and `15368`. The check name is the job ID, because the job declares no
`name:` — renaming that job changes the required check and silently disables the gate.

**Step 1 — create the ruleset.**

```bash
gh api repos/flatrick/dotnet-knowledge/rulesets \
  --method POST \
  --input - <<'JSON'
{
  "name": "main",
  "target": "branch",
  "enforcement": "active",
  "conditions": {
    "ref_name": { "include": ["~DEFAULT_BRANCH"], "exclude": [] }
  },
  "rules": [
    { "type": "deletion" },
    { "type": "non_fast_forward" },
    {
      "type": "pull_request",
      "parameters": {
        "required_approving_review_count": 0,
        "dismiss_stale_reviews_on_push": false,
        "require_code_owner_review": false,
        "require_last_push_approval": false,
        "required_review_thread_resolution": false
      }
    },
    {
      "type": "required_status_checks",
      "parameters": {
        "strict_required_status_checks_policy": true,
        "required_status_checks": [
          { "context": "corpus-tests", "integration_id": 15368 }
        ]
      }
    }
  ]
}
JSON
```

**Step 2 — verify the rules apply to `main`.**

```bash
gh api repos/flatrick/dotnet-knowledge/rulesets --jq '.[] | {id, name, enforcement}'
gh api repos/flatrick/dotnet-knowledge/rules/branches/main --jq '.[].type'
```

The second command lists the rules actually in force on `main`. It must include
`required_status_checks` and `pull_request`.

**Step 3 — confirm the gate blocks.** Attempt a direct push to `main` with a throwaway commit and
confirm it is rejected. A ruleset that lists correctly but does not block is the failure mode worth
one minute to rule out.

### Why the ruleset is shaped this way

- **`required_approving_review_count: 0`** — a pull request is required, an approving review is
  not. With a single maintainer, requiring an approval either blocks every merge or is bypassed and
  means nothing. Raise it when there is a second maintainer.
- **No `bypass_actors`** — the rules apply to admins, including the owner. A bypass entry makes the
  gate advisory again, which is the state it exists to leave. Editing or deleting the ruleset
  remains possible and is the appropriate escape hatch: deliberate, and it leaves a trail.
- **`strict_required_status_checks_policy: true`** — a branch must be up to date with `main` before
  merging. This is what makes the result mean something. Given the corpus's cross-project
  invariants — namespace-to-`RootNamespace` agreement, MANIFEST completeness, zero warnings across
  every project — a green run against a stale base is precisely the result that misleads. The cost
  is a rebase when `main` moves during review.
- **`deletion` and `non_fast_forward`** — they close the two ways `main`'s history can be destroyed
  without a pull request.

**Keep the `push: [main]` trigger after activating.** Once direct pushes are blocked it fires only
on merges, which invites removing it as redundant. It is not: it verifies the post-merge state, and
a concurrent merge can still produce a `main` that no run has seen.
````

- [ ] **Step 2: Verify the new doc's fenced blocks are balanced**

```bash
node -e "const t=require('fs').readFileSync('docs/design/ci.md','utf8');const n=(t.match(/^\`\`\`/gm)||[]).length;console.log('fence count',n, n%2===0?'BALANCED':'UNBALANCED');process.exit(n%2===0?0:1)"
```

Expected: an even count and `BALANCED`.

- [ ] **Step 3: Add the workflow header comment**

Insert above line 1 of `.github/workflows/corpus-tests.yml`, so the file begins with these six lines
followed by the existing `name: Corpus verification`:

```yaml
# This workflow runs on every pull request to main, but its result does not
# block a merge. Required status checks need a ruleset, and the API refuses to
# create one while this repository is private on the Free plan. The check
# context to require, once the repository is public, is `corpus-tests` — the
# job ID below, so renaming that job disables the future gate.
# Activation runbook and the reasoning for every pin: docs/design/ci.md
```

- [ ] **Step 4: Verify the workflow still parses and its structure is untouched**

This check is self-contained — it does not depend on scripts from earlier tasks:

```bash
npx --yes js-yaml .github/workflows/corpus-tests.yml | node -e "
let s='';process.stdin.on('data',d=>s+=d).on('end',()=>{
  const w=JSON.parse(s), f=[];
  if(w.name!=='Corpus verification') f.push('workflow name changed: '+w.name);
  if(!w.jobs||!w.jobs['corpus-tests']) f.push('job id corpus-tests missing');
  if(!w.on||!w.on.pull_request) f.push('on.pull_request missing');
  if(!w.concurrency) f.push('concurrency block missing');
  if(f.length){console.error('FAIL');f.forEach(x=>console.error('  - '+x));process.exit(1)}
  console.log('PASS: comment did not disturb the workflow structure');
});"
```

Expected: exit 0. A comment cannot change the parse, so a failure here means the insert landed
inside a block rather than above `name:`.

Then, **only if the earlier tasks' scripts still exist in this session**:

```bash
[ -f "$SP/check-triggers.js" ] && npx --yes js-yaml .github/workflows/corpus-tests.yml | node "$SP/check-triggers.js" || echo "skipped: check-triggers.js not present"
[ -f "$SP/check-pins.js" ] && npx --yes js-yaml .github/workflows/corpus-tests.yml | node "$SP/check-pins.js" || echo "skipped: check-pins.js not present"
```

- [ ] **Step 5: Add the `AGENTS.md` directory-map row**

In the table under `## Directory map`, insert after the
`docs/design/language-feature-showcase-design.md` row (line 29):

```markdown
| `docs/design/ci.md` | What CI runs and when, why every version is pinned, and the runbook to activate the merge gate. |
```

- [ ] **Step 6: Add the `AGENTS.md` closing section**

Append to the end of `AGENTS.md`, after the feature-floors section:

```markdown

## Continuous integration

**A green CI run does not mean a commit was gated.** `.github/workflows/corpus-tests.yml` runs on
every pull request to `main`, but nothing blocks a merge on its result and nothing blocks a direct
push to `main` — required status checks need a ruleset, which GitHub withholds while this
repository is private on the Free plan. Do not infer from a commit's presence on `main` that
verification passed for it; read the run. [`docs/design/ci.md`](docs/design/ci.md) has the
reasoning and the runbook to activate the gate once the repository is public.
```

- [ ] **Step 7: Verify the AGENTS.md links resolve**

```bash
node -e "
const fs=require('fs');
const t=fs.readFileSync('AGENTS.md','utf8');
const bad=[...t.matchAll(/\]\((?!https?:)([^)#]+)\)/g)].map(m=>m[1]).filter(p=>!fs.existsSync(p));
if(bad.length){console.error('MISSING TARGETS:');bad.forEach(b=>console.error('  - '+b));process.exit(1)}
console.log('PASS: every relative link in AGENTS.md resolves');
"
```

Expected: exit 0. This proves the new `docs/design/ci.md` reference points at a file that exists.

- [ ] **Step 8: Commit**

```bash
git add .github/workflows/corpus-tests.yml docs/design/ci.md AGENTS.md
git status --short
git commit -m "Record that CI runs but does not gate merges

The gap between a workflow running and a workflow gating is invisible in
the YAML and reads as enforcement. It is now stated where each reader
will meet it: a header comment for whoever edits the workflow, a section
in AGENTS.md for an agent that would otherwise infer main was verified,
and docs/design/ci.md for the reasoning and the activation runbook.

The runbook is executable as written once the repository is public. It
pins the required check to corpus-tests and records why the ruleset
grants no bypass to admins."
```

---

### Task 5: Verify the live run on a pull request

**Files:** none — this task changes nothing. It confirms GitHub accepts the configuration and that
it behaves as designed.

**Interfaces:**
- Consumes: everything from Tasks 1–4, pushed to `origin`.
- Produces: the evidence that closes issue #2 and the go/no-go on `setup-dotnet@v6.0.0`.

**This task performs outward-facing actions — pushing the branch and opening a pull request. Get
the user's explicit go-ahead before Step 1.** The local checks prove structure; only GitHub proves
the configuration is valid, because `windows-2025-vs2026` and `actions/checkout@v7.0.1` are names
this repository cannot resolve offline.

- [ ] **Step 1: Push the branch**

```bash
git push -u origin project-namespace-naming
```

Expected: success. No workflow run starts — the branch is not `main` and has no pull request. That
absence is the first confirmation Task 1 worked.

- [ ] **Step 2: Confirm no run started**

```bash
gh run list --branch project-namespace-naming --limit 5
```

Expected: no run newer than the push. A new run here means the `on:` block is still matching
feature-branch pushes.

- [ ] **Step 3: Open the pull request**

```bash
gh pr create --base main --title "Gate corpus verification on pull requests and pin every CI version" --body "Restricts \`corpus-tests.yml\` to pull requests targeting \`main\`, pushes to \`main\`, and manual dispatch, cancelling superseded pull-request runs only.

Pins the runner image to \`windows-2025-vs2026\` and the three actions to exact tags, and adds Dependabot so those pins stay deliberate rather than stale. Closes #2.

Merge gating itself is not enforceable while the repository is private on the Free plan; \`docs/design/ci.md\` carries the runbook to activate it once the repository is public.

Design: \`docs/superpowers/specs/2026-07-27-ci-merge-gate-design.md\`"
```

- [ ] **Step 4: Confirm exactly one run starts, and capture its ID**

`gh run view` requires an explicit run ID when it is not attached to a terminal — it exits with
`run or job ID required when not running interactively`. Capture the ID once and reuse it:

```bash
gh run list --branch project-namespace-naming --limit 5
RUN_ID=$(gh run list --branch project-namespace-naming --limit 1 --json databaseId --jq '.[0].databaseId')
echo "RUN_ID=$RUN_ID"
gh run view "$RUN_ID" --json event,status,jobs --jq '{event, status, jobs: [.jobs[].name]}'
```

Expected: exactly one run, `event` of `pull_request`, and a single job named `corpus-tests`. Two
runs means the `push` and `pull_request` triggers are both matching — re-check Task 1.

- [ ] **Step 5: Confirm the job actually picks up a runner**

```bash
gh run view "$RUN_ID" --json jobs --jq '.jobs[] | {name, status, startedAt}'
```

Expected: `status` leaves `queued` and reaches `in_progress` within about a minute.

**This is the check for a bad runner label.** An invalid or unavailable `runs-on` value does not
produce an error — no runner matches it, so the job sits in `queued` indefinitely until it times
out hours later. A job stuck in `queued` means `windows-2025-vs2026` is wrong, not that GitHub is
slow.

- [ ] **Step 6: Watch to completion, then verify the image and the SDK steps**

```bash
gh run watch "$RUN_ID"
gh run view "$RUN_ID" --log > "$SP/run.log"
grep -iE "image: |image version" "$SP/run.log" | head -5
grep -E "Set up .NET 10 SDK|Install private corpus test SDKs|Restore corpus tests" "$SP/run.log" | head -20
```

Expected: the "Set up job" section reports a Windows Server 2025 image, and all three named steps
succeed.

**This is where `setup-dotnet@v6.0.0` is proven or disproven** — v5.0.0 rewrote the installer
scripts and dropped older .NET versions, and `10.0.302` is what this step installs. If it fails
here, apply the pinned-back `setup-dotnet@v4.3.1` from Task 2's closing note, push, and repeat from
Step 4. That outcome still closes issue #2.

- [ ] **Step 7: Confirm the run concluded and the artifact uploaded**

```bash
gh run view "$RUN_ID" --json conclusion --jq '.conclusion'
gh api "repos/flatrick/dotnet-knowledge/actions/runs/$RUN_ID/artifacts" \
  --jq '.artifacts[] | {name, size_in_bytes}'
```

Expected: `success`, and an artifact named `corpus-test-results` with a nonzero
`size_in_bytes` — proving `upload-artifact@v7.0.1` works with the existing `name:`/`path:` inputs.

- [ ] **Step 8: Verify cancellation of superseded runs**

```bash
git commit --allow-empty -m "Trigger a second run to verify concurrency cancellation"
git push
gh run list --branch project-namespace-naming --limit 3 --json databaseId,status,conclusion,event
```

Re-run the `gh run list` command until both runs have a `conclusion`. Do not use `sleep` to wait —
foreground sleeps are blocked in this harness.

Push this commit while the previous run is still in flight, not after it finishes, or there is
nothing to cancel. If the previous run already completed, push a second empty commit immediately
after this one instead.

Expected: the earlier of the two `pull_request` runs shows `conclusion: cancelled`, and the newer
one is `in_progress` or `success`. If the earlier run completes instead, the `cancel-in-progress`
expression is not evaluating to `true` — confirm it reads `${{ github.event_name == 'pull_request' }}`
exactly, with single quotes around `pull_request` inside the double-braced expression.

- [ ] **Step 9: Confirm Dependabot picked up the config**

Open Insights → Dependency graph → Dependabot in the repository and confirm
`.github/dependabot.yml` is listed with a recorded last-checked time.

The `dependabot/alerts` API is **not** the probe — it reports security alerts, a separate feature
that is disabled on this repository.

- [ ] **Step 10: Report results**

Report to the user: whether `setup-dotnet@v6.0.0` held or was pinned back, the run duration
compared to the ~5 minutes before this change, and confirmation that Steps 2 and 7 behaved as
designed. Do not merge the pull request — that is the user's call.

---

## Notes for whoever executes this

**Do not run `dotnet scripts/generate-net48-examples.cs`.** It targets a deleted project layout.
Nothing in this plan needs it, but it is adjacent to the CI topic and easy to reach for.

**The corpus test suite is not run locally as part of this plan.** It needs SDKs from a
repository-private host installed via `dotnet scripts/install-corpus-test-sdks.cs`. Task 5's CI run
exercises it; there is no reason to reproduce that locally.

**If a task's red step does not fail as described,** stop and report rather than proceeding. A
check that passes before the change was made is not testing what it claims to test.
