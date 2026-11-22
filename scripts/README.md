# Repository scripts

PowerShell tooling for repository maintenance. Each script is preview-first unless its README says otherwise, and each folder documents its own parameters, configuration, and outputs.

| Script | Purpose | Documentation |
|----|----|----|
| `scripts/common` | Shared banner, repository-root, configuration, and summary helpers | [README](common/README.md) |
| `scripts/clean` | Removes generated files and directories, including docs/API output and IDE caches | [README](clean/README.md) |
| `scripts/coverage` | Runs tests with XPlat Code Coverage and generates an HTML report | [README](coverage/README.md) |
| `scripts/format` | Applies and verifies .NET formatting | [README](format/README.md) |
| `scripts/parity` | Verifies the 78-row parity matrix has no untracked non-`●` gap | [README](parity/README.md) |
| `scripts/sonar` | Runs the SonarScanner preview used by CI | [README](sonar/README.md) |
| `scripts/badges` | Generates the repository README badges | `generate.ps1` |
| `scripts/security` | Fails the build when unreviewed NuGet vulnerabilities are reported | `check-vulnerable-packages.ps1` |

The coverage and formatting scripts are documented in their folders. They are not required to build the library unless you are reproducing the CI checks locally.

## Related documentation

- [Repository README](../README.md)
- [Coverage tool](coverage/README.md)
- [Format tool](format/README.md)
- [Sonar preview](sonar/README.md)

## License

MIT — see [`LICENSE`](../LICENSE).