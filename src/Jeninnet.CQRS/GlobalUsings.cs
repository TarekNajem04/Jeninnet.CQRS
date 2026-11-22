//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
global using System.Collections.Concurrent;
global using System.Collections.Generic;
global using System.Diagnostics;
global using System.Diagnostics.CodeAnalysis;
global using System.Diagnostics.Metrics;
global using System.Globalization;
global using System.Linq.Expressions;
global using System.Reflection;
global using System.Runtime.CompilerServices;
global using System.Runtime.ExceptionServices;
global using System.Security.Cryptography;
global using System.Text;
global using System.Text.Json;
global using Jeninnet.CQRS.Batch;
global using Jeninnet.CQRS.Behaviors;
global using Jeninnet.CQRS.Cascading;
global using Jeninnet.CQRS.ClaimCheck;
global using Jeninnet.CQRS.Configurations;
global using Jeninnet.CQRS.Conventions;
global using Jeninnet.CQRS.DeadLetters;
global using Jeninnet.CQRS.DeferredDelivery;
global using Jeninnet.CQRS.Exceptions;
global using Jeninnet.CQRS.Extensions;
global using Jeninnet.CQRS.HandlerValidation;
global using Jeninnet.CQRS.Inbox;
global using Jeninnet.CQRS.Interfaces;
global using Jeninnet.CQRS.Jobs;
global using Jeninnet.CQRS.Notifications;
global using Jeninnet.CQRS.Observers;
global using Jeninnet.CQRS.Outbox;
global using Jeninnet.CQRS.Polyfills;
global using Jeninnet.CQRS.Recurring;
global using Jeninnet.CQRS.RequestResponse;
global using Jeninnet.CQRS.Retention;
global using Jeninnet.CQRS.Serialization;
global using Jeninnet.CQRS.Tenancy;
global using Jeninnet.CQRS.Transports;
global using Jeninnet.CQRS.Versioning;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.DependencyInjection.Extensions;
global using Microsoft.Extensions.Hosting;
global using Microsoft.Extensions.Logging;
global using Microsoft.Extensions.Options;
