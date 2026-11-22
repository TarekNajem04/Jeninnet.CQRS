//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
global using System.Net;
global using System.Net.Http.Json;
global using System.Runtime.CompilerServices;
global using System.Text;
global using System.Text.Json;
global using Jeninnet.Assertions;
global using Jeninnet.Assertions.MSTest;
global using Jeninnet.CQRS;
global using Jeninnet.CQRS.AspNetCore;
global using Jeninnet.CQRS.AspNetCore.Extensions;
global using Jeninnet.CQRS.Interfaces;
global using Jeninnet.CQRS.Validation;
global using Jeninnet.CQRS.Validation.Extensions;
global using Jeninnet.Validation;
global using Jeninnet.Validation.Abstractions;
global using Microsoft.AspNetCore.Builder;
global using Microsoft.AspNetCore.Hosting;
global using Microsoft.AspNetCore.Http;
global using Microsoft.AspNetCore.TestHost;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Hosting;
global using Microsoft.Extensions.Logging;
global using Microsoft.VisualStudio.TestTools.UnitTesting;
global using static Jeninnet.Assertions.FluentActions;