# Jeninnet.CQRS.Behaviors Template Project

This project serves as a template for creating new CQRS pipeline behavior extensions.

## Structure
- `Behaviors/`: Contains the behavior implementation classes.
- `Extensions/`: Contains the `IServiceCollection` extension methods for registration.

## Getting Started
1. Copy this project structure to a new folder.
2. Rename the project and assembly according to your behavior's purpose.
3. Implement `IOrderedPipelineBehavior<TRequest, TResponse>`.
4. Add a registration method in `Extensions/ServiceCollectionExtensions.cs`.
