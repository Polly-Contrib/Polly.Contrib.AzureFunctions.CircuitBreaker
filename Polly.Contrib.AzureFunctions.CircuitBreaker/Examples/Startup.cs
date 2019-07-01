﻿using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Polly.Caching;
using Polly.Caching.Memory;
using Polly.Registry;

[assembly: FunctionsStartup(typeof(Polly.Contrib.AzureFunctions.CircuitBreaker.Examples.Startup))]

namespace Polly.Contrib.AzureFunctions.CircuitBreaker.Examples
{
    public class Startup : FunctionsStartup
    {
        public override void Configure(IFunctionsHostBuilder builder)
        {
            builder.Services.AddMemoryCache();
            builder.Services.AddSingleton<IAsyncCacheProvider, MemoryCacheProvider>();
            builder.Services.AddSingleton<IPolicyRegistry<string>>(new PolicyRegistry());

            builder.Services.AddSingleton<IDurableCircuitBreakerOrchestrator, DurableCircuitBreakerOrchestrator>();
        }
    }
}