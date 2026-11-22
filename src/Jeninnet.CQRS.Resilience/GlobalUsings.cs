//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
global using System.Collections.Concurrent;
global using System.Diagnostics.CodeAnalysis;
global using System.Threading.RateLimiting;
global using Jeninnet.CQRS.Interfaces;
global using Jeninnet.CQRS.Polyfills;
global using Jeninnet.CQRS.Tenancy;
global using Microsoft.Extensions.Configuration;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.DependencyInjection.Extensions;
global using Microsoft.Extensions.Options;