//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
global using System.Diagnostics;
global using System.Text.Json;
global using Jeninnet.Assertions;
global using Jeninnet.Assertions.MSTest;
global using Jeninnet.CQRS.DeadLetters;
global using Jeninnet.CQRS.Extensions;
global using Jeninnet.CQRS.Serialization;
global using Jeninnet.CQRS.Transport.AzureServiceBus;
global using Jeninnet.CQRS.Transport.AzureServiceBus.Extensions;
global using Jeninnet.CQRS.Transport.Contracts;
global using Jeninnet.CQRS.Transports;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Options;
global using Microsoft.VisualStudio.TestTools.UnitTesting;
global using Testcontainers.ServiceBus;
global using static Jeninnet.Assertions.FluentActions;