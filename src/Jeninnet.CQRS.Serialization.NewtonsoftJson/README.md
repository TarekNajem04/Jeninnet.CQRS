# Jeninnet.CQRS.Serialization.NewtonsoftJson

Newtonsoft.Json wire format for `Jeninnet.CQRS`, negotiated by the envelope content type
`application/x-newtonsoft-json`.

```csharp
builder.Services.AddCqrsNewtonsoftJsonSerialization(options => {
    options.Serializer.NullValueHandling = NullValueHandling.Ignore;
});
```

The package registers `NewtonsoftJsonMessageSerializer` as an additional
`MessageSerializerFormat`; it does **not** replace the default `System.Text.Json` serializer used
by durable stores and outgoing messages. Consumers resolve `IMessageSerializerRegistry` or
`TransportMessageReader` and the transport envelope's `content-type` selects the reader. Payloads
without a content type keep using the default JSON format.

Defaults mirror the web JSON contract (`camelCase` properties, case-insensitive reads, UTC dates)
so a message can move between the default JSON format and Newtonsoft without translation.
Serialization uses `JsonConvert.SerializeObject(message, messageType, settings)` with
`Encoding.UTF8`, so non-ASCII data round-trips losslessly.

## AOT

Newtonsoft.Json is reflection-based. This package is intended for interop with peers and legacy
services; native AOT and aggressive trimming should use the default `System.Text.Json` serializer
or `Jeninnet.CQRS.Serialization.MessagePack` with source-generated formatters.