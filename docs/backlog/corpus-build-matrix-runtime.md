# The corpus build matrix dominates suite runtime

`CorpusProjectBuildTests` builds every discovered SDK-style corpus project with `-t:Rebuild`,
under `[DoNotParallelize]`. It is the largest single cost in the corpus test suite: each VB family
holds a project per language-version pin, and the matrix scales with that project count.

## Why it matters

A suite people avoid running protects nothing. This is the gate that makes "0 errors and
0 warnings" mechanical rather than a matter of discipline, so it has to stay runnable.

## Evidence

Roughly four and a half minutes for the build matrix alone on a developer machine. The rest of
the suite is fast; the unit category completes in seconds.

## Suggested fix

Do **not** trim the pin ladder. Adjacent pins with identical row sets are the intended
evidence — a green build at a pin whose row set equals its predecessor's is what documents that
the version in between gates nothing.

Two levers, cheaper first:

1. **Drop `-t:Rebuild`** in favor of an ordinary build for projects whose inputs have not
   changed. The rebuild exists to guarantee a clean compile rather than a cached one; measure
   whether an incremental build with a cleaned output directory gives the same guarantee.
2. **Parallelize the matrix.** Currently blocked: the nine net48 VB `library` projects all
   reference `CSharp_v8.0`, so concurrent builds contend on one output. Building that project
   once up front and referencing its assembly would unblock it.
