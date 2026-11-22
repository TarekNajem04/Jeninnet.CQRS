# Jeninnet.CQRS.Serialization.MessagePack

MessagePack wire format for `Jeninnet.CQRS`, negotiated by the envelope content type
`application/x-msgpack`.

```csharp
builder.Services.AddCqrsMessagePackSerialization();

[MessagePackObject]
public sealed record OrderCreated([property: Key(0)] Guid OrderId, [property: Key(1)] int Version);
```

The package registers `MessagePackMessageSerializer` as an additional `MessageSerializerFormat`;
it does **not** replace the default `System.Text.Json` serializer. Consumers resolve
`IMessageSerializerRegistry` or `TransportMessageReader`, and the envelope content type selects
the reader; payloads without a content type keep using the default JSON format.

## Resolver and AOT

The default options are `MessagePackSerializerOptions.Standard`. MessagePack's standard resolver
discovers source-generated formatters for `[MessagePackObject]` types, so the annotated contract
is native-AOT safe. Configure a custom resolver through
`MessagePackMessageSerializerOptions.Serializer` when the application owns one:

```csharp
builder.Services.AddCqrsMessagePackSerialization(options => {
    options.Serializer = MessagePackSerializerOptions.Standard.WithResolver(new MyGeneratedResolver());
});
```

For contractless contracts, `ContractlessStandardResolver` works but relies on reflection; supply
a source-generated resolver (or annotate the contracts) before publishing with trimming or AOT.
MessagePack is a binary format: consumers must use `application/x-msgpack`, not the JSON reader.