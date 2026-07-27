# CI merge gate and workflow pinning

`.github/workflows/corpus-tests.yml` should run when a pull request is about to be merged, and its
result should be what allows the merge. Two things stand between the workflow and that goal: it
currently runs on every push to every branch, and nothing enforces its result.

This design fixes the first completely, closes [issue #2](https://github.com/flatrick/dotnet-knowledge/issues/2)
along the way, and specifies the enforcement half as a runbook to execute when the repository
becomes public.

## The constraint

The repository is private and the owner is on the Free plan. Both endpoints that could block a
merge refuse:

```
gh api repos/flatrick/dotnet-knowledge/branches/main/protection   -> 403
gh api repos/flatrick/dotnet-knowledge/rulesets                   -> 403
"Upgrade to GitHub Pro or make this repository public to enable this feature."
```

Required status checks are the only mechanism GitHub offers for blocking a merge on a workflow
result. There is no workflow-level substitute: a job can fail, but nothing consumes that failure as
a merge precondition. So the enforcement half is not implementable today, and no amount of YAML
changes that.

The repository is intended to become public. Making it public restores both endpoints at no cost,
which makes this a sequencing problem rather than a blocked one. Everything below is written so that
opening the repository is the only remaining action — see [Activating the gate](#activating-the-gate-when-the-repository-goes-public).

## Trigger model

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

Feature-branch pushes stop triggering runs. Verification happens when a pull request targets `main`,
which is when the result is actually needed.

`push` on `main` stays. Nothing blocks a direct push to `main` today, so this is the only signal that
`main` is green. It remains useful after the gate is active, because it verifies the post-merge state
of `main` rather than the pre-merge prediction.

`workflow_dispatch` allows re-running verification against any branch from the Actions tab without
opening a pull request.

Concurrency cancels superseded pull-request runs: three commits pushed in quick succession verify
once, against the last one, which is the only revision that can be merged. Canceled runs report as
`cancelled`, not `failure`, and do not satisfy a required check — which is the correct behavior for a
superseded revision.

Only pull-request runs share a group keyed on `github.ref`; every other run gets `github.run_id`,
which is unique per run. `cancel-in-progress: false` alone would not be enough to keep runs on
`main` uncanceled — GitHub cancels a *pending* run in a concurrency group when a newer one arrives
in that group regardless of that setting, so grouping pushes to `main` by `github.ref` would still
let a later push cancel an earlier push's still-queued run. Keying non-pull-request runs on
`github.run_id` instead means they never share a group with anything, so every commit on `main`
keeps an independent pass/fail record and a manual dispatch never serializes against pushes to
`main`.

For `pull_request` events GitHub evaluates the workflow file from the merge commit, so the new
trigger configuration governs the pull request that introduces it. There is no bootstrap gap.

## Version pinning

Issue #2 reports that all three actions are two to three majors behind. Verified against the release
API:

| Action | Current | Target | Released |
|---|---|---|---|
| `actions/checkout` | v4 | v7.0.1 | 2026-07-20 |
| `actions/setup-dotnet` | v4 | v6.0.0 | 2026-07-16 |
| `actions/upload-artifact` | v4 | v7.0.1 | 2026-04-10 |

Every breaking change across those majors is a runtime or packaging change — Node 20 to Node 24, then
CommonJS to ESM — not an input-schema change. The workflow passes `dotnet-version`, `name`, and
`path`, and none of those inputs moved. GitHub-hosted runners satisfy the Node 24 minimum runner
version (2.327.1).

Two release notes deserve attention rather than assumption:

- **`setup-dotnet` v5.0.0 removed support for older .NET versions and rewrote the installer
  scripts.** The workflow pins `10.0.302`, which is current, so this should not apply. It is the one
  step that can plausibly fail on the first run, and it must be checked rather than assumed. **If it
  does fail, staying on `setup-dotnet@v4` is the answer, not a workaround** — see
  [When an action cannot move](#when-an-action-cannot-move).
- **`checkout` v7.0.0 blocks checking out a fork pull request under `pull_request_target` and
  `workflow_run`.** This workflow uses `pull_request`, so it is unaffected. It becomes relevant if a
  future workflow needs secrets on fork pull requests, which the public repository will make possible.

Versions are pinned to exact tags rather than floating majors, matching how the rest of the
repository treats versions as named coordinates:

```yaml
- uses: actions/checkout@v7.0.1
- uses: actions/setup-dotnet@v6.0.0
- uses: actions/upload-artifact@v7.0.1
```

Exact tags trade automatic patch adoption for a reviewable commit per version change. Dependabot
supplies the adoption.

### When an action cannot move

An action that cannot be upgraded is pinned to the version that works, with a comment stating what
blocks it. That is a resolved state, not a deferred one.

Issue #2 is about **drift**, not about being on the newest tag. The three actions reached v4 because
nothing recorded a decision and nothing watched for new releases — the version in the file carried no
information about whether it was chosen or merely inherited. A pin annotated with the reason is the
opposite of that: it says the version was evaluated, and it tells the next reader what to re-test
before moving it.

So if `setup-dotnet` v6 cannot install the pinned SDK, the workflow keeps `setup-dotnet@v4`:

```yaml
# Pinned to v4: v5.0.0 rewrote the installer scripts and dropped older .NET
# versions, and cannot install the 10.0.302 SDK this corpus requires.
# Re-test against a newer major before changing this.
- uses: actions/setup-dotnet@v4.3.1
```

Issue #2 is closed either way, and the rest of the change — the other two actions, the runner image,
Dependabot, the trigger model — lands unchanged.

Dependabot will keep opening pull requests for a pinned-back action. Close them with the reason, or
add an `ignore` entry for that dependency's major version once the block is confirmed durable rather
than provisional. Do not silence it before the block is confirmed; an unexplained `ignore` recreates
exactly the invisible staleness issue #2 reports.

## Runner image

```yaml
runs-on: windows-2025-vs2026
```

`windows-latest` is a moving label. It resolves to Windows Server 2025 with Visual Studio 2026 today
— `windows-2025` and `windows-2025-vs2026` are aliases for that same image — and GitHub's runner-images
README states that `-latest` migrations happen gradually over one to two months, during which a
workflow "may see changes in the OS version."

That matters more here than in most repositories. The net48 half of the corpus depends on Visual
Studio's `MSBuild.exe`, and `scripts/verify-feature-floors.cs` locates compilers through `vswhere`
and through the in-box `%WINDIR%\Microsoft.NET\Framework64` compilers. An image migration can change
what those find. Under `windows-latest`, the resulting corpus failure would arrive with no
corresponding commit and would read as a corpus regression.

`windows-2025-vs2026` names both the OS and the Visual Studio version, which is what the corpus
actually depends on. The next image bump becomes a deliberate commit.

## Dependabot

New file, `.github/dependabot.yml`:

```yaml
version: 2
updates:
  - package-ecosystem: github-actions
    directory: "/"
    schedule:
      interval: weekly
```

This is what closes issue #2 rather than resetting its clock. The drift from v4 to v7 accumulated
over months because nothing watched for it; bumping the tags without adding a watcher reproduces the
starting conditions exactly.

Dependabot raises each update as a pull request, which the corpus workflow then verifies — the
version bump and its verification are the same event.

Scope is `github-actions` only. The repository's NuGet dependencies are deliberately pinned to
specific historical versions as part of the corpus contract (`Microsoft.Net.Compilers` 1.3.2 is
pinned precisely because it is old), so a `nuget` ecosystem entry would generate pull requests that
must always be rejected.

These are Dependabot **version** updates, which are available on private repositories on the Free
plan. They are independent of Dependabot security alerts, which are currently disabled on this
repository.

## Documentation

The gap between "the workflow runs" and "the workflow gates" is invisible in the YAML and easy to
misread as enforcement. It is recorded in three places, each serving a different reader.

**1. Header comment in `.github/workflows/corpus-tests.yml`** — for whoever is editing the workflow.
States that merge blocking requires a required-status-check rule unavailable on the current plan,
names the check context (`corpus-tests`), and points at `docs/design/ci.md`.

**2. A line in `AGENTS.md`**, under the conventions section — for an agent reading the corpus rules.
Without it, an agent can reasonably infer that a commit on `main` was gated by CI. It was not, and
that inference would make `main` look more trustworthy than it is.

**3. `docs/design/ci.md`** — for whoever needs the reasoning or the activation procedure. Carries
what the YAML cannot express: why `main` runs are not canceled, why tags are exact rather than
floating, why the runner image is pinned to a VS-qualified label, and the runbook below.

`docs/design/ci.md` is a runbook, not a history. It describes the current configuration and the
procedure to change it. It does not narrate how the configuration got here, per the repository's
"state current truth only" convention.

## Activating the gate when the repository goes public

Executed once, after the repository visibility changes. No workflow changes are needed at that point
— the check already reports under the right name.

**Prerequisites.** The repository is public. The `corpus-tests` check has reported at least once
(any run on `main` satisfies this). Verified facts about this repository, read from a live check run
rather than assumed:

- check context: `corpus-tests` — the job ID, since the job declares no `name:`
- GitHub Actions app id: `15368`
- the repository owner has admin permission

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

**Step 2 — verify enforcement.**

```bash
gh api repos/flatrick/dotnet-knowledge/rulesets --jq '.[] | {id, name, enforcement}'
gh api repos/flatrick/dotnet-knowledge/rules/branches/main --jq '.[].type'
```

The second command lists the rules that actually apply to `main`. It must include
`required_status_checks` and `pull_request`.

**Step 3 — confirm the gate is real.** Attempt a direct push to `main` on a throwaway commit and
confirm it is rejected. A ruleset that lists correctly but does not block is the failure mode worth
one minute to rule out.

Decisions embedded in that payload, each of which can be reconsidered at activation time:

- **`required_approving_review_count: 0`.** A pull request is required; an approving review is not.
  On a single-maintainer repository, requiring an approval means either self-approval is disallowed
  and nothing can ever merge, or the requirement is bypassed and means nothing. Raise this when there
  is a second maintainer.
- **No `bypass_actors`.** Omitting the field means the rule applies to admins too, including the
  owner. With a bypass entry the gate is advisory again, which is the state this design exists to
  leave. The owner can still edit or delete the ruleset, which is the appropriate escape hatch — it
  is deliberate and leaves an audit trail, unlike a silent bypass.
- **`strict_required_status_checks_policy: true`.** A branch must be up to date with `main` before
  merging. This is what makes the check result mean something: a stale branch's green run says
  nothing about the merged state. The cost is a rebase when `main` moves during review. Given the
  corpus's cross-project invariants — namespace-to-`RootNamespace` agreement, MANIFEST completeness,
  zero warnings across every project — a green run against a stale base is exactly the result that
  would mislead.
- **`deletion` and `non_fast_forward`.** Not requested, but they cost nothing and close the two ways
  a protected branch's history can be destroyed without a pull request.

**No workflow change follows activation.** Once direct pushes to `main` are blocked, the
`push: [main]` trigger fires only on merges, which invites removing it as redundant. Keep it: it
verifies the post-merge state of `main` rather than the pre-merge prediction, and
`strict_required_status_checks_policy` makes those nearly but not exactly equivalent — a concurrent
merge can still produce a `main` that no run has seen.

## Verification

The change is verified by the first pull request that carries it, since `pull_request` events use the
workflow from the merge commit.

1. Open a pull request from the working branch to `main`. Confirm exactly one run starts, named
   `corpus-tests`, on `windows-2025-vs2026`.
2. Confirm the **Install private corpus test SDKs** and **Restore corpus tests** steps pass. These
   exercise `setup-dotnet@v6.0.0` against the pinned `10.0.302` SDK and are where the v5 installer
   rewrite would surface.
3. Confirm the **Upload corpus test results** step publishes `corpus-test-results` under
   `upload-artifact@v7.0.1`, and that the artifact contains `corpus-tests.trx`.
4. Push a second commit to the pull request while the first run is in flight. Confirm the first run
   reports `cancelled` and only the second completes.
5. Push a commit to any branch with no open pull request. Confirm no run starts.
6. Confirm Dependabot picked up the config: `.github/dependabot.yml` appears under Insights →
   Dependency graph → Dependabot with a recorded last-checked time. The `dependabot/alerts` endpoint
   is not the probe — it reports security alerts, which are a separate feature and are disabled here.

Failure at step 2 is the anticipated outcome-two, not a defect. It would mean `setup-dotnet` v6
cannot install `10.0.302`; the workflow then pins `setup-dotnet@v4.3.1` with the blocking reason in a
comment, per [When an action cannot move](#when-an-action-cannot-move), and every other part of the
change lands unchanged. Both outcomes close issue #2.

## Out of scope

- **The `10.0.302` SDK pin.** It matches the SDK that `scripts/install-corpus-test-sdks.cs` places in
  `.artifacts/dotnet`, and changing one without the other splits the toolchain. A bump is its own
  task.
- **NuGet dependency updates.** Corpus package versions are pinned as part of the corpus contract.
- **Adding jobs.** `verify-feature-floors.cs` and `verify-project-namespaces.cs` are not in the
  workflow. Whether they should be is a separate question about what CI is responsible for proving.
- **Making the repository public.** This design specifies what to do at that point; the decision and
  its timing belong to the owner.

## Files touched

| Path | Change |
|---|---|
| `.github/workflows/corpus-tests.yml` | Triggers, concurrency, runner image, three action tags, header comment |
| `.github/dependabot.yml` | New — weekly `github-actions` updates |
| `docs/design/ci.md` | New — trigger model, pinning policy, activation runbook |
| `AGENTS.md` | One line: CI is advisory until the gate is activated |
