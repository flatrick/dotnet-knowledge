# Private corpus test SDKs

The corpus test matrix requires these exact SDK versions in a repository-private root:

- `5.0.408`
- `7.0.410`
- `10.0.302`

Install or verify them with:

```powershell
dotnet scripts/install-corpus-test-sdks.cs
```

The command installs only versions that are absent from `.artifacts/dotnet`. It never changes the
machine-wide .NET installation or `PATH`. The private root supplies the exact compiler and runtime
bands that the matrix needs while allowing the machine's normal .NET setup to remain untouched.

`TargetFramework=net5.0` selects the target runtime and reference APIs; it does not select the
SDK 5 compiler. The `dotnet` host selects the SDK, so tests that exercise compiler boundaries must
run through the private host containing the exact SDK versions above.

Check a root without downloading or modifying anything:

```powershell
dotnet scripts/install-corpus-test-sdks.cs -- --check
```

Use another absolute or relative private directory when needed:

```powershell
dotnet scripts/install-corpus-test-sdks.cs -- --install-dir C:\build\corpus-dotnet
```

To invoke the full suite through the private host, set `DOTNET_HOST_PATH` and use that host:

```powershell
$env:DOTNET_HOST_PATH = (Resolve-Path .artifacts\dotnet\dotnet.exe)
& $env:DOTNET_HOST_PATH test tests/DotNetKnowledge.Corpus.Tests/DotNetKnowledge.Corpus.Tests.csproj --nologo
```

Remove the private installation by deleting its directory, for example
`.artifacts/dotnet`; the next normal install command recreates it. SDK updates are deliberate,
reviewed changes: update both the exact versions in `install-corpus-test-sdks.cs` and the matrix
expectations together.
