using System.Collections.Concurrent;

namespace SpawnDev.SpawnJS.WebWorkers.Demo.Tests
{
    /// <summary>
    /// One test per reflection path in the worker call protocol: the paths the trimmer cannot see on its own and
    /// that WebWorkers' trim annotations / suppressions exist to cover. Run this suite against a TRIMMED publish
    /// (PublishTrimmed=true) as well as a plain build - a path that only breaks under trimming breaks here.<br/>
    /// - callback (Action&lt;T&gt;) arguments: the worker builds a typed Action via CreateTypedActionTn<br/>
    /// - Task&lt;T&gt; / ValueTask&lt;T&gt; / ValueTask returns: MethodInfoExtension.InvokeAsync<br/>
    /// - generic and overloaded methods: SerializableMethodInfo resolves by name + signature, MakeGenericMethod<br/>
    /// - exceptions: ExceptionSerializer rebuilds the thrown type on the caller side<br/>
    /// - runtime services: AddService&lt;I, T&gt; and New(() =&gt; new T(...)) construct by Type on the worker<br/>
    /// </summary>
    public class WebWorkerReflectionPathTests(WebWorkerService webWorkerService)
    {
        async Task<WebWorker> GetWorker()
        {
            if (!webWorkerService.WebWorkerSupported) throw new SkipTestException("Web Workers not supported by this host.");
            var worker = await webWorkerService.GetWebWorker();
            return worker ?? throw new Exception("GetWebWorker returned null");
        }

        // ---- callback arguments

        /// <summary>Runs in the worker: calls back once per value, then returns how many it sent.</summary>
        public static async Task<int> CallBackEach(int count, Action<int> callback)
        {
            for (var i = 0; i < count; i++)
            {
                callback(i * 10);
                await Task.Delay(1);
            }
            return count;
        }

        /// <summary>
        /// An Action&lt;int&gt; argument crosses to the worker and its calls come back with their values. The
        /// worker side builds the typed Action through CreateTypedActionT1, which a trimmed build used to drop
        /// (it was found by an interpolated name).
        /// </summary>
        [WebWorkerTest(Timeout = 30000)]
        public async Task CallbackActionArgumentRoundTripsTest()
        {
            using var worker = await GetWorker();
            var seen = new ConcurrentQueue<int>();
            var sent = await worker.Run(() => CallBackEach(3, new Action<int>(v => seen.Enqueue(v))));
            if (sent != 3) throw new Exception($"Expected 3 callbacks sent, worker reported {sent}");
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (seen.Count < 3 && DateTime.UtcNow < deadline) await Task.Delay(20);
            var got = string.Join(",", seen);
            if (got != "0,10,20") throw new Exception($"Expected callbacks 0,10,20, got '{got}'");
        }

        // ---- return shapes

        /// <summary>Runs in the worker.</summary>
        public static ValueTask<int> ValueTaskDoubled(int value) => ValueTask.FromResult(value * 2);
        /// <summary>Runs in the worker; completes asynchronously.</summary>
        public static async ValueTask<string> ValueTaskDelayed(string value)
        {
            await Task.Delay(5);
            return $"vt:{value}";
        }
        /// <summary>Runs in the worker; completes asynchronously.</summary>
        public static async Task<string> TaskDelayed(string value)
        {
            await Task.Delay(5);
            return $"t:{value}";
        }
        static int _valueTaskSideEffect;
        /// <summary>Runs in the worker; a non-generic ValueTask, observed through a second call.</summary>
        public static async ValueTask SetSideEffect(int value)
        {
            await Task.Delay(5);
            _valueTaskSideEffect = value;
        }
        /// <summary>Runs in the worker.</summary>
        public static int GetSideEffect() => _valueTaskSideEffect;

        /// <summary>
        /// Every awaitable return shape the worker unwraps in InvokeAsync: ValueTask&lt;T&gt; completed and
        /// asynchronous, Task&lt;T&gt; asynchronous, and non-generic ValueTask.
        /// </summary>
        [WebWorkerTest(Timeout = 30000)]
        public async Task AwaitableReturnShapesUnwrapTest()
        {
            using var worker = await GetWorker();
            var doubled = await worker.Run(() => ValueTaskDoubled(21));
            if (doubled != 42) throw new Exception($"ValueTask<int>: expected 42, got {doubled}");
            var vt = await worker.Run(() => ValueTaskDelayed("a"));
            if (vt != "vt:a") throw new Exception($"async ValueTask<string>: expected 'vt:a', got '{vt}'");
            var t = await worker.Run(() => TaskDelayed("b"));
            if (t != "t:b") throw new Exception($"async Task<string>: expected 't:b', got '{t}'");
            await worker.Run(() => SetSideEffect(7));
            var side = await worker.Run(() => GetSideEffect());
            if (side != 7) throw new Exception($"ValueTask: expected the worker to have run it (7), got {side}");
        }

        // ---- generic and overloaded methods

        /// <summary>
        /// A generic interface method (closed with MakeGenericMethod on the worker) and the overloads of one
        /// name (picked by parameter signature) resolve to the right method.
        /// </summary>
        [WebWorkerTest(Timeout = 30000)]
        public async Task GenericAndOverloadedMethodsResolveTest()
        {
            using var worker = await GetWorker();
            var maths = worker.GetService<IMathsService>();
            var generic = await maths.TestGenerics<int, string>(5, "five");
            if (generic != "five") throw new Exception($"TestGenerics<int,string>: expected 'five', got '{generic}'");
            var fromInt = await maths.TestMultiSigMethod(3);
            if (fromInt != 3d) throw new Exception($"TestMultiSigMethod(int): expected 3, got {fromInt}");
            var fromString = await maths.TestMultiSigMethod("s");
            if (fromString != "s") throw new Exception($"TestMultiSigMethod(string): expected 's', got '{fromString}'");
            var fromPair = await maths.TestMultiSigMethod("p", 1.5);
            if (fromPair != "p 1.5") throw new Exception($"TestMultiSigMethod(string,double): expected 'p 1.5', got '{fromPair}'");
        }

        // ---- exceptions

        /// <summary>Runs in the worker.</summary>
        public static int ThrowArgumentOutOfRange(string marker) => throw new ArgumentOutOfRangeException(nameof(marker), marker);
        /// <summary>Runs in the worker.</summary>
        public static int ThrowCanceled(string marker) => throw new OperationCanceledException(marker);

        /// <summary>
        /// An exception thrown in the worker arrives as the same type, still carrying its message, so callers
        /// can catch it by type (OperationCanceledException above all).
        /// </summary>
        [WebWorkerTest(Timeout = 30000)]
        public async Task WorkerExceptionsKeepTheirTypeTest()
        {
            using var worker = await GetWorker();
            var marker = Guid.NewGuid().ToString("N");
            try
            {
                await worker.Run(() => ThrowArgumentOutOfRange(marker));
                throw new Exception("ArgumentOutOfRangeException was not thrown");
            }
            catch (ArgumentOutOfRangeException ex)
            {
                if (!ex.Message.Contains(marker)) throw new Exception($"ArgumentOutOfRangeException lost its message: '{ex.Message}'");
            }
            try
            {
                await worker.Run(() => ThrowCanceled(marker));
                throw new Exception("OperationCanceledException was not thrown");
            }
            catch (OperationCanceledException ex)
            {
                if (!ex.Message.Contains(marker)) throw new Exception($"OperationCanceledException lost its message: '{ex.Message}'");
            }
        }

        // ---- runtime services

        /// <summary>
        /// A service added to the worker at runtime (no DI registration) is constructed there from its Type and
        /// answers calls.
        /// </summary>
        [WebWorkerTest(Timeout = 30000)]
        public async Task RuntimeAddedServiceAnswersTest()
        {
            using var worker = await GetWorker();
            var added = await worker.AddService<IRuntimeAddedService, RuntimeAddedService>();
            if (!added) throw new Exception("AddService returned false");
            var token = Guid.NewGuid().ToString("N");
            var reply = await worker.Run<IRuntimeAddedService, string>(s => s.Hello(token));
            var expected = $"hello {token}";
            if (!reply.StartsWith(expected)) throw new Exception($"Expected '{expected}...', got '{reply}'");
            if (reply == $"{expected} from {SpawnJSRuntime.Instance!.InstanceId}") throw new Exception("Runtime service ran in the window, not the worker");
        }

        /// <summary>
        /// New(() =&gt; new T(args)) constructs the instance in the worker with those arguments, and later calls
        /// reach that instance.
        /// </summary>
        [WebWorkerTest(Timeout = 30000)]
        public async Task NewConstructsInTheWorkerTest()
        {
            using var worker = await GetWorker();
            var token = Guid.NewGuid().ToString("N");
            await worker.New(() => new RuntimeCreatedService(token));
            var init = await worker.Run<RuntimeCreatedService, string>(s => s.GetInit());
            if (init != token) throw new Exception($"Expected the constructor argument '{token}', got '{init}'");
        }
    }

    /// <summary>Added to a worker at runtime by <see cref="WebWorkerReflectionPathTests"/>; not registered in DI.</summary>
    public interface IRuntimeAddedService
    {
        /// <summary>Echoes the name with the answering instance's id.</summary>
        Task<string> Hello(string name);
    }

    /// <summary>Implementation of <see cref="IRuntimeAddedService"/>.</summary>
    public class RuntimeAddedService : IRuntimeAddedService
    {
        /// <inheritdoc/>
        public Task<string> Hello(string name) => Task.FromResult($"hello {name} from {SpawnJSRuntime.Instance!.InstanceId}");
    }

    /// <summary>Constructed in a worker by New(() =&gt; new RuntimeCreatedService(...)).</summary>
    public class RuntimeCreatedService
    {
        readonly string _init;
        /// <summary>Keeps the constructor argument.</summary>
        public RuntimeCreatedService(string init) => _init = init;
        /// <summary>Returns the constructor argument.</summary>
        public string GetInit() => _init;
    }
}
