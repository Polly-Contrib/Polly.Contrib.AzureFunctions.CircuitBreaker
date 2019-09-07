# Polly.Contrib.AzureFunctions.CircuitBreaker

This repo provides a **durable, distributed circuit-breaker**, implemented in Azure Functions.

It is provided as a proof-of-concept, starter pattern for users to trial and adapt into Azure functions apps.

The durable, distributed circuit-breaker can be consumed in two ways:

+ from within an Azure functions app, by another function; 
+ from outside Azure functions, from anywhere, over an http/s api.

Feedback on usage and suggestions/PRs welcome.

## Conceptual overview

The implementation uses [Durable Entity functions](https://docs.microsoft.com/en-us/azure/azure-functions/durable/durable-functions-preview#entity-functions) (in preview at time of writing, June 2019) to persist circuit state durably across invocations and across scaled-out function apps.

The breaker behaves as a consecutive-count circuit-breaker, as described for the [original Polly circuit-breaker](https://github.com/App-vNext/Polly/wiki/Circuit-Breaker):

+ In **Closed** state, the circuit-breaker permits executions and counts consecutive failures.
+ If a configuration threshold `MaxConsecutiveFailures` is met, the circuit transitions to **Open** for a duration of `BreakDuration`.
+ In **Open** state executions are blocked, and fail fast.
+ After `BreakDuration`, the circuit transitions to **half-open** state, and test-executions (in Consistency mode only a single test execution) is/are permitted
  - if a test execution succeeds, the circuit _closes_ again
  - if a test execution fails, the circuit _opens_ again.

For more detail, see the [original Polly circuit-breaker wiki](https://github.com/App-vNext/Polly/wiki/Circuit-Breaker).

The Azure functions implementation manages multiple circuit-breaker instances, each identified by a unique `circuitBreakerId`.  

## Executing code through a circuit-breaker from within an Azure function

The function examples demonstrate two different modes in which we can consume the circuit-breaker from within Azure Functions: **consistency priority** and **performance priority**.

### Consistency priority

**Consistency priority** checks with the circuit-breaker, to determine whether the execution is permitted, for each execution through the breaker.  It uses the strongly-consistent APIs on the entity function to do so.

This provides behaviour completely faithful to the classic circuit-breaker pattern described in **Conceptual overview** above.

#### Executing code through the breaker: Consistency priority

To execute code through a circuit breaker within an Azure function, adopt the pattern shown in `FooFragileFunctionConsumingBreaker_ConsistencyPriority`:

    if (!await orchestrationClient.IsExecutionPermittedByBreaker_ConsistencyPriority(CircuitBreakerId, log))
    {
        // We throw an exception here to indicate the circuit is not permitting calls. Other logic could be adopted if preferred.
        throw new BrokenCircuitException();
    }

    try
    {
        var result = await OriginalFunctionMethod(req, log);

        await orchestrationClient.RecordSuccessForBreaker(CircuitBreakerId, log);

        return result;
    }
    catch
    {
        await orchestrationClient.RecordFailureForBreaker(CircuitBreakerId, log);

        throw;
    }

Of course, you can extend this pattern to filter on specific exceptions; or treat certain returned result values also as failures.

### Performance priority

**Performance priority** is designed for scenarios with high numbers of executions per second.  It uses the APIs on entity functions which prioritise performance over consistency. 

The behaviour changes in the following ways, for performance gain:

+ The state of the circuit-breaker is cached for periods controlled by configuration, to avoid repeated reads of state that is unlikely to have changed.  
+ There is a weaker guarantee in half-open state. The half-open state does not control that only one trial operation is permitted in half-open; any number are permitted.  However, the first success or failure in half-open state will cause the circuit to transition from half-open to closed or open again.


#### Executing code through the breaker: Performance priority

The pattern is identical, except for the following call to determine whether an execution is permitted:

    if (!await orchestrationClient.IsExecutionPermittedByBreaker_PerformancePriority(CircuitBreakerId, log))

### Demo: Executing code through a circuit-breaker within an Azure function

+ Start the functions app locally [using the Azure Functions Core Tools local development experience](https://docs.microsoft.com/en-us/azure/azure-functions/functions-develop-local), or deploy to your Azure subscription and set environment variables parallel to the choices in `local.settings.json`.
+ Hit the endpoint configured in your function app for `FooFragileFunctionConsumingBreaker_ConsistencyPriority` (or `FooFragileFunctionConsumingBreaker_PerformancePriority`), repeatedly.
+ The endpoint simulates an operation failing 50% of the time.
+ The sample is configured to break the circuit for 10 seconds when 2 consecutive failures have occurred.

_Hint:_ Watch the logging to see the operation of the circuit-breaker.


## Consuming as a durable, distributed circuit-breaker from any location, over http

The durable circuit-breaker exposes an Http/s api for consuming the circuit-breaker over http.  By default, the operations are exposed on the below endpoints:

| Endpoint | Http verb | Operation | Example return value |
| --- | --- | --- | --- | 
| `/DurableCircuitBreaker/{circuitBreakerId:alpha}/IsExecutionPermitted` | POST<sup>1</sup> | Returns whether an execution is permitted | 200 OK `true` |
| `/DurableCircuitBreaker/{circuitBreakerId:alpha}/RecordSuccess` | POST<sup>1</sup> | Records a success outcome in the breaker's internal statistics | 200 OK |
| `/DurableCircuitBreaker/{circuitBreakerId:alpha}/RecordFailure` | POST<sup>1</sup> | Records a failure outcome in the breaker's internal statistics | 200 OK  |
| `/DurableCircuitBreaker/{circuitBreakerId:alpha}/GetCircuitState` | GET | Returns the state of the circuit-breaker | 200 OK `Closed` |

<sup>1</sup> Operations described as POST (including IsExecutionPermitted) are POST because they modify the circuit's internal statistics or state.  For the sake of easy demoing, the sample application exposes these endpoints also as GET.

To consume this circuit-breaker from an external application, you make calls to `IsExecutionPermitted`, `RecordSuccess` and `RecordFailure` from your client code. You would typically implement a pattern similar to the below.  

_(example intentionally in pseudo-code, not C#, as you may consume the distributed circuit-breaker from any language)_

    string circuitBreakerId = ... // circuit breaker to use

    if (calling /{circuitBreakerId}/IsExecutionPermitted returns false)
    {
        throw Exception("Circuit is broken.") // or choose some other way to indicate a broken circuit to your code
    }

    try
    {
        var result = /* execute the code you want guarded by the breaker */

        call /{circuitBreakerId}/RecordSuccess // can be fire-and-forget
    }
    catch exception
    {
        call /{circuitBreakerId}/RecordFailure // can be fire-and-forget

        rethrow exception
    }

In .NET Core, the most obvious way to implement this would be to use HttpClientFactory to define an `HttpClient` with BaseUri and authorization pre-configured for placing the calls to the distributed circuit-breaker functions.

The `GetCircuitState` endpoint is provided for information. It is not required to use it, to operate the circuit-breaker.

### Demo: Consuming as a distributed circuit-breaker over http

+ Start the functions app locally [using the Azure Functions Core Tools local development experience](https://docs.microsoft.com/en-us/azure/azure-functions/functions-develop-local) or deploy to your Azure subscription with appropriate environment variables set.
+ The circuit-breaker with `circuitBreakerId: MyCircuitBreaker` is configured to break for five seconds if 3 consecutive failures occur.
+ Prepare the following endpoints as calls you can repeatedly make - eg with Postman, Curl, or simply open each as a separate tab in a local browser:  (as mentioned above, strictly RESTful semantics would have three of these POST-only, but the sample exposes all as GET for quick exploration/demo via a browser)
  - `https://<yourfunctionhost>/DurableCircuitBreaker/MyCircuitBreaker/GetCircuitState`
  - `https://<yourfunctionhost>/DurableCircuitBreaker/MyCircuitBreaker/IsExecutionPermitted`
  - `https://<yourfunctionhost>/DurableCircuitBreaker/MyCircuitBreaker/RecordSuccess`
  - `https://<yourfunctionhost>/DurableCircuitBreaker/MyCircuitBreaker/RecordFailure`

+ You can then call the endpoints in turn to exercise the circuit-breaker. For example:

  - `GetCircuitState` should initially return `Closed`
  - `IsExecutionPermitted` should initially return `true`
  - Interleave calling `RecordSuccess` and `RecordFailure` without yet three failures in a row - between these calls, `GetCircuitState` and `IsExecutionPermitted` should still return `Closed` and `true`
  - Call `RecordFailure` three times in a row. 
    - `GetCircuitState` should then return `open`
    - Within the following 5 seconds, `IsExecutionPermitted` should return `false`

+ and so on ... see the [original Polly circuit-breaker wiki](https://github.com/App-vNext/Polly/wiki/Circuit-Breaker) for full info on how the circuit-breaker cycles through states.

## Configuring a circuit-breaker instance

Each circuit-breaker instance consumed is configured in the function app's app settings.

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
| `ConsistencyPriorityCheckCircuitTimeout` | ISO 8601 duration | In consistency mode, the maximum duration to wait to determine whether an execution should be permitted. (If the circuit state cannot be determined within this timespan, the execution is permitted.) |`PT2S`|
| `ConsistencyPriorityCheckCircuitRetryInterval` | ISO 8601 duration | In consistency mode, an internal setting determining how often to retry obtaining state (within the above timeout), if it is delayed. |`PT0.25S`|
| `PerformancePriorityCheckCircuitInterval` | ISO 8601 duration | In priority mode, the state will be memory-cached and not requeried for this period, to prioritise performance |`PT5S`|

## Scoping named circuit-breaker instances

Operations consuming `DurableCircuitBreaker` specify a `circuitBreakerId` which identifies a unique circuit-breaker instance maintaining its own state.

Multiple code locations in an azure functions app can execute code through the same-named `circuitBreakerId`:

+ Use the same-named circuit-breaker instance across call sites or functions when you want those call sites to share circuit state and break in common - for example they share a common downstream dependency. 
+ Use independent-named circuit-breakers when you want call sites to have independent circuit state and break independently.

## Logging

For the purposes of visibility while demoing, the demo code intentionally centralises circuit-breaker logging through `LoggerExtensions.LogCircuitBreakerMessage(...)`. The `LogLevel` visibility of these messages can be set using the config setting `CircuitBreakerSettings:<circuitBreakerId>:LogLevel`. 

In a production app you might choose to organise your logging differently.

## Load

The demonstration implementation is expected to be put through load-testing in early July - we hope to publish some load-testing statistics when available.

The implementation makes lightweight use of the new Entity Functions persistence features.

## Implementation details

### Components

| File | Kind | Role |
| --- | --- | --- |
|`DurableCircuitBreakerEntity`|durable entity function|contains the core circuit-breaker logic. Defines and implements four operations:<br/>+ `IsExecutionPermitted`<br/>+ `RecordSuccess`<br/>+ `RecordFailure`<br/> + `GetCircuitState`|
|`I/DurableCircuitBreakerClient`|methods for consumption by functions|Defines methods for querying and invoking actions on the circuit entity|
|`DurableCircuitBreakerExternalApi`|http-triggered functions|an external API for consuming the durable circuit-breaker from anywhere, over http|
|`local.settings.json`|configuration environment variables|demonstrates configuration for circuit-breaker instances|
|`FooFragileFunction_*`|Standard http-triggered function|demonstrates a standard Azure function executing its work through the circuit-breaker|

### Characteristics

The implementation intentionally makes lightweight use of Entity Functions features using a single Entity Function to maintain state and a low number of calls and signals to or between entities.  