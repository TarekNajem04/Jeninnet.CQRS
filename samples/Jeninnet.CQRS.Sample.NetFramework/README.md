# Jeninnet.CQRS net462 smoke

A .NET Framework 4.6.2 console that dispatches one command and one query through the
`net462` core asset. It is the characterization test for the legacy dispatch surface.

```powershell
dotnet run --project samples/Jeninnet.CQRS.Sample.NetFramework --framework net462
```

Expected output:

```text
net462 dispatch: command=42; query=net462
```

The exit code is `0` only when both results match. Hosting, EF Core durability, validation,
and ASP.NET Core remain outside the `net462` surface; see the [ADR registry](../../docs/adr/README.md).
