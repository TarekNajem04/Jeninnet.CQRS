//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
global using System;
global using System.Threading;
global using System.Threading.Tasks;
global using Jeninnet.Assertions;
global using Jeninnet.CQRS;
global using Jeninnet.CQRS.Caching;
global using Jeninnet.CQRS.Caching.Extensions;
global using Jeninnet.CQRS.Interfaces;
global using Microsoft.Extensions.Caching.Memory;
global using Microsoft.Extensions.DependencyInjection;