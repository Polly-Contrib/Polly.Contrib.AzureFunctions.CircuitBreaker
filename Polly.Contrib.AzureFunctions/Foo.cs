using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Polly.Contrib.AzureFunctions
{
    public static class Foo
    {        
        // Used by this demonstration code to generate random failures of the simulated work.
        private static readonly Random Rand = new Random();

        public static async Task<IActionResult> DoFragileWork(HttpRequestMessage req, ILogger log, string context)
        {
            // Do something fragile!
            if (Rand.Next(2) == 0)
            {
                throw new Exception("Something fragile went wrong.");
            }

            // Do some work and return some result.
            await Task.CompletedTask;

            var helloWorld = $"Hello world: from inside the function ({context})";
            log.LogInformation(helloWorld);

            return new OkObjectResult(helloWorld);
        }
    }
}
