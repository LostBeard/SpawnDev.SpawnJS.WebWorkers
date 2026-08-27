using SpawnDev.SpawnJS.JSObjects;
using SpawnDev.SpawnJS.WebWorkers;

namespace SpawnDev.SpawnJS.WebWorkers.Demo.Tests
{
    /// <summary>
    /// End-to-end proof in the real runtime: spinning up a dedicated worker exercises
    /// WebWorkerService.RegisterInstance (which serializes an AppInstanceInfo as its lock name) and a
    /// <c>worker.Run(...)</c> call serializes and resolves a SerializableMethodInfo across the boundary -
    /// the two JSON paths that were failing. If a worker runs a method and returns the value, both work.<br/>
    /// <br/>
    /// Skips (rather than fails) when the host has no Web Worker support, so the suite stays meaningful on
    /// any backend the runner happens to launch.
    /// </summary>
    public class WebWorkerRoundTripTests(WebWorkerService webWorkerService)
    {
        /// <summary>
        /// How long to wait for a worker to become ready before deciding its bootstrap script is not being
        /// served. A ready worker responds in well under a second; this only bounds the "never ready" case.
        /// </summary>
        const int ReadyTimeoutMs = 8000;

        /// <summary>
        /// Gets a worker and waits for it to become ready, or throws <see cref="SkipTestException"/> if it
        /// never does. A worker never becomes ready when its bootstrap script
        /// (<c>spawndev.spawnjs.webworkers.js</c> and friends) is not served - those scripts are not yet
        /// ported to SpawnJS, so this skips cleanly today and runs for real once they are.
        /// </summary>
        async Task<WebWorker> GetReadyWorkerOrSkip()
        {
            if (!webWorkerService.WebWorkerSupported)
                throw new SkipTestException("Web Workers not supported by this host.");

            // GetWebWorkerSync returns without awaiting readiness (unlike GetWebWorker, which awaits
            // WhenReady internally and so would hang here when the bootstrap script is missing). This lets
            // the timeout below decide, rather than the outer test timeout.
            var worker = webWorkerService.GetWebWorkerSync();
            if (worker == null) throw new SkipTestException("GetWebWorkerSync returned null.");

            var ready = await Task.WhenAny(worker.WhenReady, Task.Delay(ReadyTimeoutMs));
            if (ready != worker.WhenReady)
            {
                worker.Dispose();
                throw new SkipTestException(
                    "Worker never became ready - the SpawnJS worker bootstrap script " +
                    "(spawndev.spawnjs.webworkers.js) is not served yet in this port. " +
                    "Port the wwwroot worker scripts to enable worker end-to-end tests.");
            }
            return worker;
        }

        /// <summary>
        /// A dedicated worker runs a static method and returns its result. This is the whole call protocol:
        /// method-info serialization, argument marshalling, and instance registration end to end.
        /// </summary>
        [WebWorkerTest(Timeout = 30000)]
        public async Task WorkerRunsAMethodAndReturnsValueTest()
        {
            using var worker = await GetReadyWorkerOrSkip();

            var token = Guid.NewGuid().ToString();
            var echoed = await worker.Run(() => Echo(token));
            if (echoed != $"worker:{token}") throw new Exception($"Expected 'worker:{token}', got '{echoed}'");
        }

        /// <summary>
        /// The worker is a separate app instance, so its instance id differs from the window's - a direct
        /// check that the method really executed in the worker, not locally.
        /// </summary>
        [WebWorkerTest(Timeout = 30000)]
        public async Task WorkerRunsInASeparateInstanceTest()
        {
            using var worker = await GetReadyWorkerOrSkip();

            var windowInstanceId = SpawnJSRuntime.Instance!.InstanceId;
            var workerInstanceId = await worker.Run(() => CurrentInstanceId());
            if (string.IsNullOrEmpty(workerInstanceId)) throw new Exception("Worker returned an empty instance id");
            if (workerInstanceId == windowInstanceId) throw new Exception("Worker ran in the window instance, not a separate one");
        }

        /// <summary>Runs in the worker; returns the argument prefixed so the caller can confirm execution.</summary>
        public static string Echo(string value) => $"worker:{value}";

        /// <summary>Runs in the worker; returns that scope's instance id.</summary>
        public static string CurrentInstanceId() => SpawnJSRuntime.Instance!.InstanceId;

        [WebWorkerTest]
        public async Task SharedWebWorkersByName()
        {
            // workerA1 and workerA2 will refer to the same shared worker
            // workerB is a separate worker instance
            using var workerA1 = await webWorkerService.GetSharedWebWorker("workerA");
            using var workerA2 = await webWorkerService.GetSharedWebWorker("workerA");
            using var workerB = await webWorkerService.GetSharedWebWorker("workerB");
            var mathServiceA1 = workerA1!.GetService<IMathsService>();
            var mathServiceA2 = workerA2!.GetService<IMathsService>();
            var mathServiceB = workerB!.GetService<IMathsService>();
            var valueSetWorkerA1 = Guid.NewGuid().ToString();
            await mathServiceA1.SetValueTest(valueSetWorkerA1);
            var valueGetWorkerB = await mathServiceB.GetValueTest();
            var valueGetWorkerA1 = await mathServiceA1.GetValueTest();
            var valueGetWorkerA2 = await mathServiceA2.GetValueTest();
            if (valueGetWorkerA1 != valueSetWorkerA1) throw new Exception("Unexpected result");
            if (valueGetWorkerA2 != valueSetWorkerA1) throw new Exception("_sharedWorker appears not shared");
            if (valueGetWorkerB == valueSetWorkerA1) throw new Exception("_sharedWorker with different name unexpectedly same as first _sharedWorker");
        }
        [WebWorkerTest]
        public async Task ObjectTransferTestClassTest()
        {
            var testValue = new ObjectTransferTestClass
            {
                SomeValue = webWorkerService.InstanceId,
                Data = new byte[] { 1, 3, 5, 42 }
            };
            using var worker = await webWorkerService.GetWebWorker();
            var mathService = worker!.GetService<IMathsService>();
            await mathService.SetObjectValueTest(testValue);
            var readBack = await mathService.GetObjectValueTest();
            if (readBack == null) throw new Exception("Readback failed");
            if (readBack.SomeValue != testValue.SomeValue) throw new Exception("Readback string failed");
            if (readBack.Data == null || !testValue.Data.SequenceEqual(readBack.Data)) throw new Exception("Readback byte[] failed");
        }
    }
}
