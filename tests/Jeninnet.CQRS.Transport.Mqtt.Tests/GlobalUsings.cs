//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
global using System.Net;
global using System.Net.Sockets;
global using Jeninnet.Assertions;
global using Jeninnet.Assertions.MSTest;
global using Jeninnet.CQRS.Serialization;
global using Jeninnet.CQRS.Transport.Contracts;
global using Jeninnet.CQRS.Transport.Mqtt;
global using Jeninnet.CQRS.Transport.Mqtt.Extensions;
global using Jeninnet.CQRS.Transports;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Options;
global using Microsoft.VisualStudio.TestTools.UnitTesting;
global using MQTTnet;
global using MQTTnet.Server;
global using Testcontainers.Mosquitto;
global using static Jeninnet.Assertions.FluentActions;