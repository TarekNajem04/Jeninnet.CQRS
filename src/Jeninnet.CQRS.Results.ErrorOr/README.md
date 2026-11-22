# Jeninnet.CQRS.Results.ErrorOr

ErrorOr result adapter for the Jeninnet CQRS validation pipeline: validation
failures become `ErrorOr<T>` values instead of `ValidationException`s, and the
handler never runs.

```csharp
services.AddCqrsValidationBehavior(options => options.UseErrorOrResultAdapter<string>());
```

One `Error.Validation` per failure; the code, message, and member/severity/code
metadata follow `ValidationErrorFormatter` in `Jeninnet.CQRS.Validation`.