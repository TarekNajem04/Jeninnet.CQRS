# Parity matrix checker

`extract-matrix.ps1` audits a 78-row parity matrix and its task ledger. A row marked `●` is
shipped. Any non-`●` row must be referenced by at least one task's matrix-row column; an uncovered
row fails the script.

The authoritative matrix and ledger are maintainer-only files and are not part of the public
snapshot. Supply their paths explicitly when running the audit.

## Usage

```powershell
pwsh ./scripts/parity/extract-matrix.ps1 -MatrixPath <matrix.md> -IndexPath <ledger.md>
pwsh ./scripts/parity/extract-matrix.ps1 -SelfTest
```

The self-test runs three in-memory fixtures: all rows shipped, an uncovered row, and a non-`●`
row backed by a task. It does not touch the repository.

## Options

| Option | Default | Purpose |
|---|---|---|
| `-MatrixPath` | none (required for an audit) | Matrix to audit |
| `-IndexPath` | none (required for an audit) | Task ledger |
| `-ExpectedRows` | `78` | Fail on a missing or extra matrix row |
| `-SelfTest` | off | Run the fixture tests instead of the repository audit |

Exit code `0` means every row is shipped or tracked; `1` means at least one non-`●` row has no
task (or the matrix is structurally invalid). Run the self-test in CI; the repository audit needs
the local-only files.