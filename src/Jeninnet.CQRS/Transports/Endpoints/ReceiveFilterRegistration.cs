//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transports;

/// <summary>One receive-filter registration captured during service configuration.</summary>
/// <param name="MessageType">The message type the filter applies to.</param>
/// <param name="EndpointName">The endpoint name, or <see langword="null"/> for a global filter.</param>
/// <param name="FilterType">The concrete filter type resolved from DI.</param>
internal sealed record ReceiveFilterRegistration(Type MessageType, string? EndpointName, Type FilterType);
