//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Sagas.Tests;

/// <summary>Runs the shared repository contract against the in-memory saga store.</summary>
[TestClass]
public sealed class InMemorySagaRepositoryContractTests : SagaRepositoryContractTests {
    /// <inheritdoc />
    protected override ISagaRepository<SagaRepositoryTestState> CreateRepository() {
        var serializer = new JsonMessageSerializer(Options.Create(new JsonMessageSerializerOptions()));

        return new SagaRepository<SagaRepositoryTestState>(new InMemorySagaStore(serializer));
    }
}
