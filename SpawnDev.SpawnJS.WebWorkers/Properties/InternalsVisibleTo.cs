using System.Runtime.CompilerServices;

// The demo is the test host. It exercises the library's own serialization - including the
// source-generated WebWorkersJsonContext and the internal round-trip of AppInstanceInfo - which is
// internal because it is not part of the consumer-facing API. Giving the test host access lets those
// paths be tested directly rather than only through a live worker.
[assembly: InternalsVisibleTo("SpawnDev.SpawnJS.WebWorkers.Demo")]
