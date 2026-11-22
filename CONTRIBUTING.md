# Contributing to Jeninnet.CQRS

Thank you for your interest in Jeninnet.CQRS.

The project aims to provide a lightweight, modular CQRS framework for .NET with a strong focus on
correctness, deterministic behavior, explicit contracts, source-generated registrations, and
long-term API stability.

## Current Project Status

Jeninnet.CQRS is in active preview. The validation engine was extracted to the standalone
[`Jeninnet.Validation`](https://github.com/TarekNajem04/Jeninnet.Validation) repository, and
this solution now consumes it as a NuGet package. `1.0.0-preview.1` is the first public preview;
the public surface can still change before the stable `1.0.0` release.

To maintain consistency and avoid unnecessary breaking changes, **source-code contributions are
temporarily limited to project maintainers**.

## How You Can Contribute Today

Community participation is highly appreciated.

The most valuable contributions at this stage are:

- Bug reports
- Feature requests
- Design discussions
- API design feedback
- Documentation improvements
- Source-generator behavior observations
- Security observations
- Real-world usage feedback

Please use GitHub Issues or Discussions to share ideas and suggestions.

## Pull Requests

At the current stage of the project:

- Pull requests containing source-code changes may not be accepted.
- Pull requests that improve documentation may be considered on a case-by-case basis.
- Large design proposals should begin as an Issue or Discussion before implementation.

Once the project reaches a stable baseline, external code contributions will be opened publicly.

## Project Structure

```text
src/
    Jeninnet.CQRS/                   Core abstractions, dispatcher, broker, behaviors
    Jeninnet.CQRS.Generators/       Roslyn source generator (handler registration)
    Jeninnet.CQRS.Behaviors/        Shared pipeline behaviors
    Jeninnet.CQRS.Caching/          Caching pipeline behavior
    Jeninnet.CQRS.FluentValidation/ FluentValidation bridge
    Jeninnet.CQRS.Idempotency/      Idempotency pipeline behavior
    Jeninnet.CQRS.Resilience/       Retry/circuit-breaker pipeline behavior
    Jeninnet.CQRS.Validation/       Jeninnet.Validation pipeline behavior (NuGet-backed)

tests/
    Jeninnet.CQRS.Tests/            Dispatcher, caching, retry, processor tests
    Jeninnet.CQRS.IntegrationTests/ Source-generator registration tests
    Jeninnet.CQRS.Validation.Tests/ Validation behavior tests

samples/
    Jeninnet.CQRS.Sample.Console/   Console sample
    Jeninnet.CQRS.Sample.WebApi/    Minimal API sample
scripts/                             Build, format, coverage, Sonar, badge, and security checks
```

## Design Philosophy

The architecture emphasizes:

- Explicit contracts: requests, handlers, and pipeline behaviors compose through small interfaces
- Compile-time safety: handler registrations are source-generated
- Deterministic behavior: pipeline ordering is explicit and observable
- Minimal dependencies: BCL and approved `Microsoft.Extensions.*` packages first
- Validation as an external concern: `Jeninnet.CQRS.Validation` integrates the standalone
  `Jeninnet.Validation` engine instead of owning validation rules

When evaluating proposals, maintainability, determinism, and long-term API stability are generally
preferred over short-term convenience.

## Development Standards

Run the following before sending a change:

```powershell
dotnet build Jeninnet.CQRS.slnx
dotnet test Jeninnet.CQRS.slnx
dotnet format Jeninnet.CQRS.slnx --verify-no-changes --severity info
pwsh scripts/security/check-vulnerable-packages.ps1
```

Tests use [`Jeninnet.Assertions`](https://github.com/TarekNajem04/Jeninnet.Assertions) with the
MSTest adapter: wrap expectations in `MSTestAssertions.That(() => x.Should().*)`. See the
`GlobalUsings.cs` of any test project for the standard imports.

The build must produce 0 warnings and 0 errors. The Sonar gate requires coverage above 80% and zero
duplicated lines; see `scripts/sonar/README.md`.

## License

The project is licensed under the MIT License. Any contribution accepted by the maintainers is
licensed under the same terms.