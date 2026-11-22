//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
global using System.Diagnostics;
global using System.Diagnostics.CodeAnalysis;
global using System.Diagnostics.Metrics;
global using Jeninnet.CQRS;
global using Jeninnet.CQRS.DeadLetters;
global using Jeninnet.CQRS.DeferredDelivery;
global using Jeninnet.CQRS.Interfaces;
global using Jeninnet.CQRS.Outbox;
global using Jeninnet.CQRS.Polyfills;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.DependencyInjection.Extensions;
global using Microsoft.Extensions.Hosting;
global using Microsoft.Extensions.Options;
