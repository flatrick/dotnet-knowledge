# The floor cache's scope key is unverified

`scripts/verify-feature-floors.cs` caches a row's floor verdict so a row appearing in several
projects is probed once. The cache key includes a `Scope` string whose job is to keep verdicts
from being shared across projects with different reference sets.

`Scope` is a declared claim. Nothing checks it against the reference set actually resolved.

## Why it matters

A verdict computed under one toolchain would be reported for a project compiled under another.
The verdict would look ordinary — same outcome vocabulary, same evidence tier — while resting
on a reference set the project does not have.

## Evidence

For VB the claim is accurate: `Scope` is `familyName|kind`, and reference sets are supplied by
each family's `Directory.Build.props`, so they are identical within a family and kind.

For C# it is already false. Every `CSharp_v*` project declares `Scope: ""`, but they do not
share a reference set:

- the eleven non-SDK projects (`CSharp_v1.0` … `CSharp_v7.3`) carry `System`, `System.Core`,
  `System.Data`, `System.Xml`, `System.Xml.Linq`, `System.Net.Http`, `Microsoft.CSharp`, plus
  `System.Memory` and `System.Threading.Tasks.Extensions`;
- `CSharp_v8.0` is SDK-style with `Microsoft.NETFramework.ReferenceAssemblies`,
  `System.Memory` and `Microsoft.Bcl.AsyncInterfaces`, and no
  `System.Threading.Tasks.Extensions`;
- `CSharp_v1.0-Unsafe` and `CSharp_v8.0-Unsafe` are SDK-style and declare no explicit
  references at all.

It is safe today only because the projects that author cache entries happen to sit inside the
eleven-project identical group. Nothing preserves that.

## Suggested fix

Derive `Scope` from the resolved reference set rather than declaring it — a hash of the
resolved paths would do — so a project whose references differ cannot silently reuse another's
verdict.

## Related

- `scripts/verify-feature-floors.md`, Limitations
