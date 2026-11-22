//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
global using System.Diagnostics;
global using System.Diagnostics.Metrics;
global using Jeninnet.Assertions;
global using Jeninnet.Assertions.MSTest;
global using Jeninnet.CQRS;
global using Jeninnet.CQRS.DeadLetters;
global using Jeninnet.CQRS.DeferredDelivery;
global using Jeninnet.CQRS.Extensions;
global using Jeninnet.CQRS.HealthChecks;
global using Jeninnet.CQRS.HealthChecks.Extensions;
global using Jeninnet.CQRS.Interfaces;
global using Jeninnet.CQRS.Notifications;
global using Jeninnet.CQRS.RoutingSlips;
global using Jeninnet.CQRS.Sagas;
global using Jeninnet.CQRS.Sagas.Contracts;
global using Jeninnet.CQRS.Sagas.Extensions;
global using Jeninnet.CQRS.Serialization;
global using Jeninnet.CQRS.Testing;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Diagnostics.HealthChecks;
global using Microsoft.Extensions.Hosting;
global using Microsoft.Extensions.Logging;
global using Microsoft.Extensions.Options;
global using Microsoft.VisualStudio.TestTools.UnitTesting;
