//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
global using System.Collections.ObjectModel;
global using ErrorOr;
global using FluentResults;
global using Jeninnet.Assertions;
global using Jeninnet.Assertions.MSTest;
global using Jeninnet.CQRS.Interfaces;
global using Jeninnet.CQRS.Results.CSharpFunctionalExtensions;
global using Jeninnet.CQRS.Results.ErrorOr;
global using Jeninnet.CQRS.Results.FluentResults;
global using Jeninnet.CQRS.Validation;
global using Jeninnet.Validation;
global using Jeninnet.Validation.Abstractions;
global using Jeninnet.Validation.Configuration;
global using Jeninnet.Validation.Core;
global using Jeninnet.Validation.Fluent;
global using Jeninnet.Validation.Rules;
global using Microsoft.Extensions.Logging;
global using Microsoft.Extensions.Logging.Abstractions;
global using Microsoft.Extensions.Options;
global using Microsoft.VisualStudio.TestTools.UnitTesting;
global using static Jeninnet.Assertions.FluentActions;
