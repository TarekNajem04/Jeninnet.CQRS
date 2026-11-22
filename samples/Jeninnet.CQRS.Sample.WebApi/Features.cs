//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//

namespace Jeninnet.CQRS.Sample.WebApi;

// Define a simple command and handler for the sample
public record CreateSampleCommand(string Content) : ICommand<string>;
public class CreateSampleHandler : ICommandHandler<CreateSampleCommand, string> {
    public Task<string> HandleAsync(CreateSampleCommand request, CancellationToken cancellationToken = default) =>
        Task.FromResult($"Sample command processed: {request.Content}");
}

// Define a simple query and handler for the sample
public record GetSampleQuery : IQuery<string>;
public class GetSampleHandler : IQueryHandler<GetSampleQuery, string> {
    public Task<string> HandleAsync(GetSampleQuery request, CancellationToken cancellationToken = default) =>
        Task.FromResult("Sample query result: Hello from Jeninnet.CQRS!");
}
