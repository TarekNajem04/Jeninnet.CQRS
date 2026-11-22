//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
global using System.Diagnostics;
global using System.Diagnostics.CodeAnalysis;
global using System.Text;
global using System.Text.Json;
global using Jeninnet.Assertions;
global using Jeninnet.Assertions.MSTest;
global using Jeninnet.CQRS;
global using Jeninnet.CQRS.DeadLetters;
global using Jeninnet.CQRS.DeferredDelivery;
global using Jeninnet.CQRS.Idempotency;
global using Jeninnet.CQRS.Idempotency.EntityFrameworkCore;
global using Jeninnet.CQRS.Idempotency.EntityFrameworkCore.Extensions;
global using Jeninnet.CQRS.Inbox;
global using Jeninnet.CQRS.Interfaces;
global using Jeninnet.CQRS.Jobs;
global using Jeninnet.CQRS.Notifications;
global using Jeninnet.CQRS.Outbox;
global using Jeninnet.CQRS.Recurring;
global using Jeninnet.CQRS.Recurring.Extensions;
global using Jeninnet.CQRS.RequestResponse;
global using Jeninnet.CQRS.Retention;
global using Jeninnet.CQRS.Scheduling;
global using Jeninnet.CQRS.Serialization;
global using Jeninnet.CQRS.Tenancy;
global using Jeninnet.CQRS.Tenancy.Extensions;
global using Jeninnet.CQRS.Transports;
global using Jeninnet.CQRS.Versioning;
global using Microsoft.EntityFrameworkCore;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Hosting;
global using Microsoft.Extensions.Options;
global using Microsoft.VisualStudio.TestTools.UnitTesting;
global using static Jeninnet.Assertions.FluentActions;