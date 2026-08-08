# Continuous integration

One workflow, `.github/workflows/server-tests.yml`, running one job, `server-tests`, whose steps
cover the MCP server suite and the vendored-content guard.

**Nothing here currently executes.** Actions is disabled for this repository, so no trigger below
fires and verification is whatever is run locally. Minutes cost money on a private repository, and
while this is a personal project that cost buys nothing a local run does not. The configuration is
kept current anyway, so enabling Actions is a settings change rather than a project.

The rest of this document describes what would run once Actions is enabled.

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
pull request, for any branch that carries this workflow file — GitHub only surfaces the manual
dispatch control for workflows whose file exists on the default branch, and once triggered, the run
uses the workflow definition from the ref selected.

For `pull_request` events GitHub evaluates the workflow file from the merge commit, so a change to
this file takes effect on the pull request that introduces it.

`pull_request` defaults to the `opened`, `synchronize`, and `reopened` activity types. Retargeting
an existing pull request's base branch fires `edited`, which is none of those, so a pull request
opened against some other base and later retargeted to `main` reaches merge with no `server-tests`
run at all. Adding `edited` to `types:` is not the fix — it would also fire on every title and body
edit. Once the merge gate is active this gap closes itself: the required check is simply absent, so
the merge blocks. Before activation it is a real gap with no trigger-level guard.

## Concurrency

```yaml
concurrency:
  group: ${{ github.workflow }}-${{ github.event_name == 'pull_request' && github.ref || github.run_id }}
  cancel-in-progress: ${{ github.event_name == 'pull_request' }}
```

A burst of commits to a pull request verifies once, against the last one — the only revision that
can be merged. A canceled run reports as `cancelled` rather than `failure` and does not satisfy a
required check, which is correct for a superseded revision.

**Only pull-request runs share a group keyed on `github.ref`.** Every other run — a push to `main`
or a manual dispatch — gets `github.run_id`, which is unique per run, so those runs are never
queued behind anything and therefore never canceled. This matters because `cancel-in-progress:
false` alone would not have been enough: GitHub cancels a *pending* run in a concurrency group when
a newer one arrives in that same group, regardless of `cancel-in-progress`, so grouping pushes to
`main` together by `github.ref` would still let a later push cancel an earlier push's still-queued
run. Keying non-pull-request runs on `github.run_id` avoids sharing a group at all, so every commit
on `main` keeps an independent pass/fail record, and a manual dispatch never serializes against
pushes to `main`.

## Version pinning

Every external reference is an exact coordinate. No floating major tags, no `-latest` runner label
for the actions themselves.

```yaml
runs-on: ubuntu-latest

- uses: actions/checkout@v7.0.1
- uses: actions/setup-dotnet@v6.0.0
- uses: actions/upload-artifact@v7.0.1
```

The server suite needs SDK 10 and Git only, with no network access and no platform-specific
toolchain — unlike the language-feature example corpus this repository used to bundle, which needed
Visual Studio's `MSBuild.exe` and is now [flatrick/dotnet-code-examples](https://github.com/flatrick/dotnet-code-examples).
`ubuntu-latest` is acceptable here specifically because nothing in this repository depends on the
image beyond a current .NET 10 SDK and Git.

Exact action tags trade automatic patch adoption for one reviewable commit per version change.
`.github/dependabot.yml` supplies the adoption, weekly, scoped to `github-actions`.

### When an action cannot move

An action that cannot be upgraded is pinned to the version that works, with a comment naming what
blocks it. For example, if `setup-dotnet` v6 could not install the pinned SDK, the pin would read:

```yaml
# Pinned to v4: v5.0.0 rewrote the installer scripts and dropped older .NET
# versions, and cannot install the 10.0.302 SDK this repository requires.
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

**Nothing gates anything, at two independent levels.** Actions is disabled, so no run is produced
at all; and even with it enabled, nothing would block a merge on a result or block a direct push to
`main`. Enabling Actions therefore buys visibility, not enforcement — the gate below is a separate
step.

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

**Prerequisites.** The repository is public. The `server-tests` check has reported at least once;
any run on `main` satisfies this. The account running these commands has admin permission on the
repository.

The two values the ruleset needs, both readable from a live check run rather than assumed:

```bash
sha=$(gh api repos/flatrick/dotnet-knowledge/commits --jq '.[0].sha')
gh api "repos/flatrick/dotnet-knowledge/commits/$sha/check-runs" \
  --jq '.check_runs[] | {name: .name, app_id: .app.id}'
```

Expected: `server-tests` and the app ID for GitHub Actions checks. The check name is the job ID,
because the job declares no `name:` — renaming that job changes the required check and silently
disables the gate.

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
          { "context": "server-tests" }
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
  merging. This is what makes the result mean something; the cost is a rebase when `main` moves
  during review.
- **`deletion` and `non_fast_forward`** — they close the two ways `main`'s history can be destroyed
  without a pull request.

**Keep the `push: [main]` trigger after activating.** Once direct pushes are blocked it fires only
on merges, which invites removing it as redundant. It is not: it verifies the post-merge state, and
a concurrent merge can still produce a `main` that no run has seen.
