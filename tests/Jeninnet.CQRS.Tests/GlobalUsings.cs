//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
global using System.Diagnostics;
global using System.Diagnostics.Metrics;
global using System.Runtime.CompilerServices;
global using System.Threading.RateLimiting;
global using Jeninnet.Assertions;
global using Jeninnet.Assertions.MSTest;
global using Jeninnet.CQRS.Batch;
global using Jeninnet.CQRS.Behaviors;
global using Jeninnet.CQRS.Caching;
global using Jeninnet.CQRS.Caching.Extensions;
global using Jeninnet.CQRS.Cascading;
global using Jeninnet.CQRS.ClaimCheck;
global using Jeninnet.CQRS.DeadLetters;
global using Jeninnet.CQRS.DeferredDelivery;
global using Jeninnet.CQRS.Diagnostics;
global using Jeninnet.CQRS.Diagnostics.Extensions;
global using Jeninnet.CQRS.Exceptions;
global using Jeninnet.CQRS.Extensions;
global using Jeninnet.CQRS.HandlerValidation;
global using Jeninnet.CQRS.HealthChecks;
global using Jeninnet.CQRS.HealthChecks.Extensions;
global using Jeninnet.CQRS.Idempotency;
global using Jeninnet.CQRS.Idempotency.Extensions;
global using Jeninnet.CQRS.Inbox;
global using Jeninnet.CQRS.Interfaces;
global using Jeninnet.CQRS.Jobs;
global using Jeninnet.CQRS.Notifications;
global using Jeninnet.CQRS.Observers;
global using Jeninnet.CQRS.Outbox;
global using Jeninnet.CQRS.Recurring;
global using Jeninnet.CQRS.Recurring.Extensions;
global using Jeninnet.CQRS.RequestResponse;
global using Jeninnet.CQRS.Resilience;
global using Jeninnet.CQRS.Resilience.Extensions;
global using Jeninnet.CQRS.Retention;
global using Jeninnet.CQRS.Scheduling;
global using Jeninnet.CQRS.Serialization;
global using Jeninnet.CQRS.Tenancy;
global using Jeninnet.CQRS.Tenancy.Extensions;
global using Jeninnet.CQRS.Testing;
global using Jeninnet.CQRS.Transports;
global using Jeninnet.CQRS.Versioning;
global using Microsoft.Extensions.Caching.Distributed;
global using Microsoft.Extensions.Caching.Memory;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Diagnostics.HealthChecks;
global using Microsoft.Extensions.Hosting;
global using Microsoft.Extensions.Logging;
global using Microsoft.Extensions.Logging.Abstractions;
global using Microsoft.Extensions.Options;
global using Microsoft.VisualStudio.TestTools.UnitTesting;
global using static Jeninnet.Assertions.FluentActions;
