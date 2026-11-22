//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
global using System.Collections.Generic;
global using System.Diagnostics.CodeAnalysis;
global using Jeninnet.CQRS.DeferredDelivery;
global using Jeninnet.CQRS.Interfaces;
global using Jeninnet.CQRS.Outbox;
global using Jeninnet.CQRS.Polyfills;
global using Jeninnet.CQRS.Resilience;
global using Jeninnet.CQRS.Retention;
global using Jeninnet.CQRS.RoutingSlips;
global using Jeninnet.CQRS.Sagas;
global using Jeninnet.CQRS.Scheduling;
global using Jeninnet.CQRS.Transports;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.DependencyInjection.Extensions;
global using Microsoft.Extensions.Diagnostics.HealthChecks;
global using Microsoft.Extensions.Options;
