//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
global using System.Text;
global using Jeninnet.CQRS.Extensions;
global using Jeninnet.CQRS.Observers;
global using Jeninnet.CQRS.Polyfills;
global using Jeninnet.CQRS.Serialization;
global using Jeninnet.CQRS.Tenancy;
global using Jeninnet.CQRS.Transports;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.DependencyInjection.Extensions;
global using Microsoft.Extensions.Hosting;
global using Microsoft.Extensions.Logging;
global using Microsoft.Extensions.Options;
global using MQTTnet;
global using MQTTnet.Client;
global using MQTTnet.Formatter;
global using MQTTnet.Protocol;
