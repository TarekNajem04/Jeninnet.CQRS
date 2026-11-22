//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
global using System.Text;
global using Amazon;
global using Amazon.Runtime;
global using Amazon.S3;
global using Amazon.S3.Model;
global using Azure.Storage.Blobs;
global using DotNet.Testcontainers.Builders;
global using DotNet.Testcontainers.Containers;
global using Jeninnet.Assertions;
global using Jeninnet.Assertions.MSTest;
global using Jeninnet.CQRS.ClaimCheck;
global using Jeninnet.CQRS.ClaimCheck.AmazonS3;
global using Jeninnet.CQRS.ClaimCheck.AzureBlob;
global using Jeninnet.CQRS.ClaimCheck.Contracts;
global using Jeninnet.CQRS.ClaimCheck.FileSystem;
global using Microsoft.VisualStudio.TestTools.UnitTesting;
global using Testcontainers.LocalStack;