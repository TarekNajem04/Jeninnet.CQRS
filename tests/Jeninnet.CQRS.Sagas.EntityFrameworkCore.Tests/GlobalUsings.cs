//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
global using Jeninnet.Assertions;
global using Jeninnet.Assertions.MSTest;
global using Jeninnet.CQRS;
global using Jeninnet.CQRS.Extensions;
global using Jeninnet.CQRS.Interfaces;
global using Jeninnet.CQRS.Notifications;
global using Jeninnet.CQRS.Retention;
global using Jeninnet.CQRS.RoutingSlips;
global using Jeninnet.CQRS.Sagas;
global using Jeninnet.CQRS.Sagas.Contracts;
global using Jeninnet.CQRS.Sagas.EntityFrameworkCore;
global using Jeninnet.CQRS.Sagas.EntityFrameworkCore.Extensions;
global using Jeninnet.CQRS.Sagas.Extensions;
global using Jeninnet.CQRS.Serialization;
global using Microsoft.EntityFrameworkCore;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Hosting;
global using Microsoft.Extensions.Options;
global using Microsoft.VisualStudio.TestTools.UnitTesting;
