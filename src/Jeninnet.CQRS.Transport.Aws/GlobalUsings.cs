//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
global using System.Security.Cryptography;
global using System.Text;
global using System.Text.Json;
global using Amazon;
global using Amazon.SimpleNotificationService;
global using Amazon.SimpleNotificationService.Model;
global using Amazon.SQS;
global using Amazon.SQS.Model;
global using Jeninnet.CQRS.ClaimCheck;
global using Jeninnet.CQRS.Extensions;
global using Jeninnet.CQRS.Observers;
global using Jeninnet.CQRS.Polyfills;
global using Jeninnet.CQRS.Serialization;
global using Jeninnet.CQRS.Transports;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.DependencyInjection.Extensions;
global using Microsoft.Extensions.Hosting;
global using Microsoft.Extensions.Options;
