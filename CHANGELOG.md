## 1.0.0-rc3 (December 27, 2019)

- Use v2.0 GA features

## 1.0.0-rc2 (November 18, 2019)

- Update to Microsoft.Azure.WebJobs.Extensions.DurableTask v2.0 GA

## 1.0.0-rc1 (September 10, 2019)

- Use a single demonstration `FooFragileFunctionWithCircuitBreaker`

## 1.0.0-preview7

- Simplify code naming and presentation
- Add more methods on IDurableCircuitBreakerClient, for use within orchestration functions
- Improve parameter order on IDurableCircuitBreakerClient methods
- Add GetBreakerState as a function available via the external API
- Allow circuit-breaker logging to be optional
- Adjust default circuit state cache duration, in performance priority mode

## 1.0.0-preview6

- Rename modes to **performance priority** and **consistency priority**
- Add local settings (enables immediate trialling)

## 1.0.0-preview5

- Rearrange solution to place functions in the root folder, circuit-breaker classes a subfolder
- Separate configuration code from durable entity code

## 1.0.0-preview4

- Switch breaker accessible by external API to throughput priority mode

## 1.0.0-preview3

- Fix persistence bug

## 1.0.0-preview2

- Differentiate **throughput priority** and **fidelity priority** patterns, when consuming from within Azure Functions

## 1.0.0-preview1

- Initial version