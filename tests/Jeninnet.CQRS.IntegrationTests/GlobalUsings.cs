//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
global using System.Runtime.CompilerServices;
global using Jeninnet.Assertions;
global using Jeninnet.Assertions.MSTest;
global using Jeninnet.CQRS.DeferredDelivery;
global using Jeninnet.CQRS.Extensions;
global using Jeninnet.CQRS.Interfaces;
global using Jeninnet.CQRS.Notifications;
global using Jeninnet.CQRS.Recurring;
global using Jeninnet.CQRS.Serialization;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.VisualStudio.TestTools.UnitTesting;
global using static Jeninnet.Assertions.FluentActions;