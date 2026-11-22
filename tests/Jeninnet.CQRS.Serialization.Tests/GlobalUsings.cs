//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
global using System.Text;
global using System.Text.Json;
global using Jeninnet.Assertions;
global using Jeninnet.Assertions.MSTest;
global using Jeninnet.CQRS.DeadLetters;
global using Jeninnet.CQRS.Extensions;
global using Jeninnet.CQRS.Serialization;
global using Jeninnet.CQRS.Serialization.MessagePack;
global using Jeninnet.CQRS.Serialization.MessagePack.Extensions;
global using Jeninnet.CQRS.Serialization.NewtonsoftJson;
global using Jeninnet.CQRS.Serialization.NewtonsoftJson.Extensions;
global using Jeninnet.CQRS.Transports;
global using MessagePack;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Options;
global using Microsoft.VisualStudio.TestTools.UnitTesting;
global using Newtonsoft.Json.Serialization;
