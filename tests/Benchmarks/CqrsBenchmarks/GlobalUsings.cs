//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
global using System;
global using System.Collections.Generic;
global using System.IO;
global using System.Linq;
global using System.Reflection;
global using System.Runtime.InteropServices;
global using System.Text.Json;
global using System.Text.Json.Serialization;
global using System.Threading;
global using System.Threading.Tasks;
global using BenchmarkDotNet.Attributes;
global using BenchmarkDotNet.Jobs;
global using BenchmarkDotNet.Reports;
global using BenchmarkDotNet.Running;
global using Jeninnet.CQRS.Caching;
global using Jeninnet.CQRS.ClaimCheck;
global using Jeninnet.CQRS.DeferredDelivery;
global using Jeninnet.CQRS.Idempotency.EntityFrameworkCore;
global using Jeninnet.CQRS.Interfaces;
global using Jeninnet.CQRS.Resilience;
global using Jeninnet.CQRS.Serialization;
global using Jeninnet.CQRS.Versioning;
global using Microsoft.EntityFrameworkCore;
global using Microsoft.Extensions.Caching.Memory;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Options;