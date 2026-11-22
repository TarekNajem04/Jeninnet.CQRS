//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.HandlerValidation;

/// <summary>
/// The registration snapshot taken when <c>AddCqrsHandlerValidation</c> runs. Call it after all
/// handlers are registered (typically last in the configuration chain).
/// </summary>
internal sealed class HandlerRegistrationSnapshot(ServiceDescriptor[] descriptors) {
    internal IReadOnlyList<ServiceDescriptor> Descriptors { get; } = descriptors;
}