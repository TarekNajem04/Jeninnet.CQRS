# Jeninnet.CQRS.Sample.Console

Basic CQRS console demo: one command (`AddProductCommand`) and one query
(`GetProductQuery`) through the source-generated `AddCqrs()` registration with
error-handling and metrics behaviors.

## Run

```powershell
dotnet run --project samples/Jeninnet.CQRS.Sample.Console -f net11.0
```

Expected output (metrics lines omitted):

```text
=== Jeninnet CQRS Sample Console ===

Executing Command: Adding a new product...
Result: Product created with ID: 1

Executing Query: Fetching product 1...
Result: Found Sample Product ($ 99,99)

=== Demonstration Complete ===
```