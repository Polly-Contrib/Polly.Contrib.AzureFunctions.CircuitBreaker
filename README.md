# Polly.Contrib.AzureFunctions.CircuitBreaker

This repo provides a **durable, distributed circuit-breaker**, implemented in Azure Functions.

It is provided as a proof-of-concept, starter pattern for users to trial and potentially adapt into Azure functions apps.

Feedback on usage and suggestions/PRs welcome.

## Intended usage

The circuit-breaker can be consumed in two ways:

+ from within an Azure functions app; 
+ from anywhere, over an http api.

## Conceptual overview

The implementation uses [Durable Entity functions](https://docs.microsoft.com/en-us/azure/azure-functions/durable/durable-functions-preview#entity-functions) (in preview at time of writing, May 2019) to persist circuit state durably across invocations and across scaled-out function apps.

The breaker behaves as a consecutive-count circuit-breaker, as described for the [original Polly circuit-breaker](https://github.com/App-vNext/Polly/wiki/Circuit-Breaker):

+ In **Closed** state, the circuit-breaker permits executions and counts consecutive failures.
+ If a configuration threshold `MaxConsecutiveFailures` is met, the circuit transitions to **Open** for a duration of `BreakDuration`.
+ In **Open** state executions are blocked, and fail fast.
+ After `BreakDuration`, the circuit transitions to **half-open** state, and a single test-execution is permitted: if it succeeds, the circuit _closes_ again; if it fails, the circuit _opens_ again.

For more detail, see the [original Polly circuit-breaker wiki](https://github.com/App-vNext/Polly/wiki/Circuit-Breaker).

The Azure functions implementation manages multiple circuit-breaker instances, each identified by a unique `circuitBreakerId`.  

## Executing code through a circuit-breaker from within an Azure function

To execute code through a circuit breaker within an Azure function, call the extension method:

    IDurableOrchestrationClient.ExecuteThroughCircuitBreakerAsync<T>(
        Func<Task<T>> action,
        string circuitBreakerId,
        ILogger log)

For example, in the sample code:

    [FunctionName("FooFragileFunction")]
    public static Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequestMessage req,
        ILogger log,
        [OrchestrationClient]IDurableOrchestrationClient orchestrationClient // Used to drive the circuit-breaker.
        )
    {
        return orchestrationClient.ExecuteThroughCircuitBreakerAsync(
            () => OriginalFunctionMethod(req, log), 
            circuitBreakerId: "FooFragileFunction", 
            log);
    }

This short function executes a single method through the circuit-breaker. It is just a simple example, to demonstrate how you might take the original method of a function and execute it through a circuit-breaker. However, a function could make any number of outbound calls guarded by circuit-breakers. 

### Demo: Executing code through a circuit-breaker within an Azure function

+ Start the functions app locally [using the Azure Functions Core Tools local development experience](https://docs.microsoft.com/en-us/azure/azure-functions/functions-develop-local), or deploy to your Azure subscription setting environment variables parallel to the choices in `local.settings.json`.
+ Hit the endpoint configured in your function app for `FooFragileFunction`, repeatedly.
+ The `FooFragileFunction` endpoint simulates an operation failing 50% of the time.
+ The sample is configured to break the circuit for 10 seconds when 2 consecutive failures have occurred.

_Hint:_ Watch the logging to see the operation of the circuit-breaker.


## Consuming as a durable, distributed circuit-breaker from any location, over http

The durable circuit-breaker exposes an Http api for consuming the circuit-breaker over http.  By default, the operations are exposed on the below endpoints (you can of course vary this).

| Endpoint | Http verb | Operation | Example return value |
| --- | --- | --- | --- | 
| `/DurableCircuitBreaker/IsExecutionPermitted/{circuitBreakerId:alpha}` | POST<sup>1</sup> | Returns the state of the circuit-breaker | 200 OK `true` |
| `/DurableCircuitBreaker/RecordSuccess/{circuitBreakerId:alpha}` | POST<sup>1</sup> | Returns the state of the circuit-breaker | 200 OK |
| `/DurableCircuitBreaker/RecordFailure/{circuitBreakerId:alpha}` | POST<sup>1</sup> | Returns the state of the circuit-breaker | 200 OK  |
| `/DurableCircuitBreaker/GetCircuitState/{circuitBreakerId:alpha}` | GET | Returns the state of the circuit-breaker | 200 OK `Closed` |

<sup>1</sup> Operations described as POST (including IsExecutionPermitted) are POST because they modify the circuit's internal statistics or state.  For the sake of easy demoing, the sample application exposes these endpoints also as GET.

To consume this circuit-breaker from an external application, you must make calls to `IsExecutionPermitted`, `RecordSuccess` and `RecordFailure` yourself. You would typically implement a pattern similar to the below (as also seen in `DurableCircuitBreakerFunctionsInternalApi.ExecuteThroughCircuitBreakerAsync<T>()`).  

_(example intentionally in pseudo-code, not C#, as you may consume the distributed circuit-breaker from any language)_

    string circuitBreakerId = ... // circuit breaker to use

    if (calling /IsExecutionPermitted/{circuitBreakerId} returns false)
    {
        throw Exception("Circuit is broken.") // or choose some other way to indicate a broken circuit to your code
    }

    try
    {
        var result = /* execute the code you want guarded by the breaker */

        call /RecordSuccess/{circuitBreakerId}
    }
    catch exception
    {
        call /RecordFailure/{circuitBreakerId}

        rethrow exception
    }

In .NET Core, the most obvious way to implement this would be to use HttpClientFactory to define an `HttpClient` with BaseUri and authorization ready-configured to place the calls to the distributed circuit-breaker functions.

The `GetCircuitState` endpoint is provided for information. It is not required to use it, to operate the circuit-breaker.

### Demo: Consuming as a distributed circuit-breaker over http

+ Start the functions app locally [using the Azure Functions Core Tools local development experience](https://docs.microsoft.com/en-us/azure/azure-functions/functions-develop-local) or deploy to your Azure subscription with appropriate environment variables set.
+ The circuit-breaker with `circuitBreakerId: MyCircuitBreaker` is configured to break for five seconds if 3 consecutive failures occur.
+ Prepare the following endpoints as calls you can repeatedly make - eg with Postman, Curl, or simply open each as a separate tab in a local browser:  (as mentioned above, strictly RESTful semantics would have three of these POST-only, but the sample exposes all as GET for quick exploration/demo via a browser)
  - `https://<yourfunctionhost>/DurableCircuitBreaker/GetCircuitState/MyCircuitBreaker`
  - `https://<yourfunctionhost>/DurableCircuitBreaker/IsExecutionPermitted/MyCircuitBreaker`
  - `https://<yourfunctionhost>/DurableCircuitBreaker/RecordSuccess/MyCircuitBreaker`
  - `https://<yourfunctionhost>/DurableCircuitBreaker/RecordFailure/MyCircuitBreaker`

+ You can then call the endpoints in turn to exercise the circuit-breaker. For example:

  - `GetCircuitState` should initially return `Closed`
  - `IsExecutionPermitted` should initially return `true`
  - Interleave calling `RecordSuccess` and `RecordFailure` without yet three failures in a row - between these calls, `GetCircuitState` and `IsExecutionPermitted` should still return `Closed` and `true`
  - Call `RecordFailure` three times in a row. 
    - `GetCircuitState` should then return `open`
    - Within the following 5 seconds, `IsExecutionPermitted` should return `false`

+ and so on ... see the [original Polly circuit-breaker wiki](https://github.com/App-vNext/Polly/wiki/Circuit-Breaker) for full info on how the circuit-breaker cycles through states.

## Configuring a circuit-breaker instance

Each circuit-breaker instance consumed must be configured in the function app's app settings.

All configuration values are of the form: 

    "CircuitBreakerSettings:<circuitBreakerId>:<settingName>": <value>

### Mandatory settings per `circuitBreakerId`

| Setting name | Type | Meaning | Example |
| --- | --- | --- | --- | --- |
| `MaxConsecutiveFailures` | int |  The number of consecutive failures at which the circuit will break. | 2 |
| `BreakDuration` | ISO 8601 duration |  The duration for which to break the circuit. |`PT30S` (30&nbsp;seconds)|

### Optional settings, per `circuitBreakerId`

| Setting name | Type | Meaning | Default <br/>(if not specified) |
| --- | --- | --- | --- | --- |
| `LogLevel` | `Microsoft .Extensions .Logging .LogLevel` | The level at which to log circuit events. | `LogLevel .Information` |
| `CheckCircuitTimeout` | ISO 8601 duration | The maximum duration to wait to determine whether an execution should be permitted. (If the circuit state cannot be determined within this timespan, the execution is permitted.) |`PT2S`|
| `CheckCircuitRetryInterval` | ISO 8601 duration | The interval to wait before retrying obtaining circuit state |`PT0.25S`|

## Scoping named circuit-breaker instances

Operations consuming `DurableCircuitBreaker` specify a `circuitBreakerId` which identifies a unique circuit-breaker instance maintaining its own state.

Multiple code locations in an azure functions app can execute code through the same-named `circuitBreakerId`:

+ Use the same-named circuit-breaker instance across call sites or functions when you want those call sites to share circuit state and break in common - for example they share a common downstream dependency. 
+ Use independent-named circuit-breakers when you want call sites to have independent circuit state and break independently.

## Logging

For the purposes of visibility while demoing, the demo code intentionally centralises circuit-breaker logging through `LoggerExtensions.LogCircuitBreakerMessage(...)`. The `LogLevel` visibility of these messages can be set using the config setting `CircuitBreakerSettings:<circuitBreakerId>:LogLevel`. 

In a production app you might choose to organise your logging differently.

## Implementation details

### Components

| File | Kind | Role |
| --- | --- | --- |
|`DurableCircuitBreaker`|durable entity function|contains the core circuit-breaker logic. Defines and implements four operations:<br/>+ `IsExecutionPermitted`<br/>+ `RecordSuccess`<br/>+ `RecordFailure`<br/> + `GetCircuitState`|
|`DurableCircuitBreakerOrchestrator`|orchestrator function and static helper methods|Internal helper methods for orchestrating calls through the circuit-breaker |
|`DurableCircuitBreakerFunctionsInternalApi`|static extension mehods|Api for executing calls through a circuit-breaker from within an Azure Functions app|
|`DurableCircuitBreakerExternalApi`|http-triggered functions|an external API for consuming the durable circuit-breaker from anywhere, over http|
|`local.settings.json`|configuration environment variables|demonstrates configuration for circuit-breaker instances|
|`FooFragileFunction`|Standard http-triggered function|demonstrates a standard Azure function executing its work through the circuit-breaker|

The implementation intentionally makes lightweight use of Entity Functions features using a single Entity Function to maintain state and a low number of calls and signals to or between entities.  