# Contributing to Jeninnet.CQRS

Thank you for your interest in Jeninnet.CQRS.

The project aims to provide a lightweight, modular CQRS framework for .NET with a strong focus on
correctness, deterministic behavior, explicit contracts, source-generated registrations, and
long-term API stability.

## Current Project Status

Jeninnet.CQRS is in a focused stabilization phase for `1.0.0-preview.2` and the upcoming stable
release. The validation engine lives in the standalone
[`Jeninnet.Validation`](https://github.com/TarekNajem04/Jeninnet.Validation) repository and is
consumed here as a NuGet package. The public surface may still change before `1.0.0`.

## Contribution Status

**We are not accepting external code contributions at this time.** This is a deliberate, temporary
decision, not a judgment about the value of your work:

- the maintainer's available time is committed to stabilizing the API, the documentation, and the
  release gates;
- we cannot currently provide the code review, testing, and follow-up support that a contribution
  deserves;
- accepting a change we cannot review properly would be worse for the project than declining it
  with a clear explanation.

We will reopen contributions once the API and the release process are stable. Until then, please do
not open pull requests, including documentation pull requests. This page will be updated with the
contributor guide, review process, and required checks when the window opens.

## How You Can Help Today

Bug reports, feature requests, design and API feedback, real-world usage reports, and security
observations are genuinely valuable and remain welcome. Please use GitHub Issues or Discussions,
and report security issues through [`SECURITY.md`](.github/SECURITY.md) rather than a public issue.
That feedback helps us stabilize the project without adding unreviewed code.

## Pull Requests

At the current stage:

- We are not accepting pull requests.
- Unsolicited pull requests will be closed with a link to this policy.
- If the maintainer asks for a specific fix, the invitation and its scope will be stated in the
  relevant issue. That is the only exception.

## Future Contributor Program

When the project reaches a stable baseline, the contribution window will reopen with:

- a documented pull-request process and review expectations;
- the required checks (build, tests, format, documentation, and the quality gate);
- API-change and compatibility rules;
- documentation and test expectations for every change;
- the code of conduct and security process.

Until then, thank you for your interest, your patience, and the reports that help make the project
better.

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

These are the gates a future contribution will need to satisfy. They are documented now so the bar
is clear when contributions reopen; they are not an invitation to open a pull request during the
stabilization phase.

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