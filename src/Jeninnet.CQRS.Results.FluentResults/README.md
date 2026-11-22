# Jeninnet.CQRS.Results.FluentResults

FluentResults result adapter for the Jeninnet CQRS validation pipeline:
validation failures become failed `Result<T>` values instead of
`ValidationException`s, and the handler never runs.

```csharp
services.AddCqrsValidationBehavior(options => options.UseFluentResultsAdapter<string>());
```

One `Error` per failure; the message plus member/severity/code metadata follow
`ValidationErrorFormatter` in `Jeninnet.CQRS.Validation`.