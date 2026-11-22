# Jeninnet.CQRS.Results.CSharpFunctionalExtensions

CSharpFunctionalExtensions result adapter for the Jeninnet CQRS validation
pipeline: validation failures become failed `Result<T>` values instead of
`ValidationException`s, and the handler never runs.

```csharp
services.AddCqrsValidationBehavior(
    options => options.UseCSharpFunctionalExtensionsResultAdapter<string>());
```

`Result<T>` carries a single string error, so every validation error is joined
with `ValidationErrorFormatter` (member, severity, message, and code preserved
textually). Custom state and message parameters are not representable; choose
the ErrorOr or FluentResults adapter when structured metadata matters.
