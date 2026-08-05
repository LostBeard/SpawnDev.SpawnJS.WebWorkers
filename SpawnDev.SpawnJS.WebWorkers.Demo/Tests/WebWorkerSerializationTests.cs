using System.Reflection;
using System.Text.Json;
using SpawnDev.SpawnJS.WebWorkers;

namespace SpawnDev.SpawnJS.WebWorkers.Demo.Tests
{
    /// <summary>
    /// Deterministic, browser-independent proof that the library's System.Text.Json usage no longer needs
    /// the reflection-based serializer.<br/>
    /// <br/>
    /// These exercise the exact operations that threw "JsonSerializerIsReflectionDisabled" in a trimmed
    /// app: serializing an <see cref="AppInstanceInfo"/> as a Web Lock name (WebWorkerService.RegisterInstance)
    /// and round-tripping a <see cref="SerializableMethodInfo"/> as the call target on the wire. Both now go
    /// through the source-generated <c>WebWorkersJsonContext</c>. A source-gen context cannot fall back to
    /// reflection by construction, so if these round-trip correctly the library works reflection-free.
    /// </summary>
    public class WebWorkerSerializationTests
    {
        /// <summary>
        /// Serializes and deserializes an AppInstanceInfo through the source-gen context - the exact shape
        /// WebWorkerService uses to build and later parse a Web Lock name. Every field must survive.
        /// </summary>
        [WebWorkerTest]
        public async Task AppInstanceInfoRoundTripsReflectionFreeTest()
        {
            var info = new AppInstanceInfo
            {
                InstanceId = Guid.NewGuid().ToString(),
                //OwnerId = Guid.NewGuid().ToString(),
                //ChildId = "child-7",
                Url = "https://example.test/app/",
                BaseUrl = "https://example.test/",
                Scope = GlobalScope.DedicatedWorker,
                Name = "worker-name",
                ParentInstanceId = Guid.NewGuid().ToString(),
                ClientId = Guid.NewGuid().ToString(),
                LockName = null,
                //TaskPoolWorker = true,
                //IFrameWorker = false,
            };

            // this is the operation at WebWorkerService.RegisterInstance that threw under trimming
            var json = JsonSerializer.Serialize(info, WebWorkersJsonContext.Default.AppInstanceInfo);
            if (string.IsNullOrEmpty(json)) throw new Exception("Serialize produced no output");

            var back = JsonSerializer.Deserialize(json, WebWorkersJsonContext.Default.AppInstanceInfo);
            if (back == null) throw new Exception("Deserialize returned null");

            if (back.InstanceId != info.InstanceId) throw new Exception($"InstanceId mismatch: '{back.InstanceId}' != '{info.InstanceId}'");
            //if (back.OwnerId != info.OwnerId) throw new Exception("OwnerId mismatch");
            //if (back.ChildId != info.ChildId) throw new Exception("ChildId mismatch");
            if (back.Url != info.Url) throw new Exception("Url mismatch");
            if (back.BaseUrl != info.BaseUrl) throw new Exception("BaseUrl mismatch");
            if (back.Scope != info.Scope) throw new Exception($"Scope mismatch: {back.Scope} != {info.Scope}");
            if (back.Name != info.Name) throw new Exception("Name mismatch");
            if (back.ParentInstanceId != info.ParentInstanceId) throw new Exception("ParentInstanceId mismatch");
            if (back.ClientId != info.ClientId) throw new Exception("ClientId mismatch");
            //if (back.TaskPoolWorker != info.TaskPoolWorker) throw new Exception("TaskPoolWorker mismatch");
            //if (back.IFrameWorker != info.IFrameWorker) throw new Exception("IFrameWorker mismatch");
        }

        /// <summary>
        /// A null-valued nullable property is omitted (WhenWritingNull) and reads back as null - the same
        /// tolerance the reflection path had, now provided by the source-gen context.
        /// </summary>
        [WebWorkerTest]
        public async Task AppInstanceInfoOmitsNullPropertiesTest()
        {
            var info = new AppInstanceInfo
            {
                InstanceId = "id",
                Url = "u",
                BaseUrl = "b",
                Scope = GlobalScope.Window,
                // OwnerId, ChildId, Name, ParentInstanceId, ClientId, LockName all null
            };
            var json = JsonSerializer.Serialize(info, WebWorkersJsonContext.Default.AppInstanceInfo);
            if (json.Contains("\"Name\"")) throw new Exception("Null Name should have been omitted");
            if (json.Contains("\"LockName\"")) throw new Exception("Null LockName should have been omitted");

            var back = JsonSerializer.Deserialize(json, WebWorkersJsonContext.Default.AppInstanceInfo)!;
            if (back.Name != null) throw new Exception("Name should read back as null");
            //if (back.OwnerId != null) throw new Exception("OwnerId should read back as null");
        }

        /// <summary>
        /// Round-trips a MethodInfo through the public SerializableMethodInfo API (the on-the-wire call
        /// target). This is the production serialize/deserialize path, now backed by the source-gen context,
        /// and it must resolve back to the very same method.
        /// </summary>
        [WebWorkerTest]
        public async Task SerializableMethodInfoRoundTripsReflectionFreeTest()
        {
            var original = typeof(WebWorkerSerializationTests).GetMethod(nameof(SampleMethodForSerialization))!;

            var json = SerializableMethodInfo.SerializeMethodInfo(original);
            if (string.IsNullOrEmpty(json) || !json.StartsWith("{")) throw new Exception($"Serialize produced no JSON object: '{json}'");

            var resolved = SerializableMethodInfo.DeserializeMethodInfo(json);
            if (resolved == null) throw new Exception("DeserializeMethodInfo returned null");
            if (resolved != original) throw new Exception($"Resolved a different method: {resolved.DeclaringType?.Name}.{resolved.Name} != {original.DeclaringType?.Name}.{original.Name}");
        }

        /// <summary>
        /// A method with parameters and a return value must round-trip its full signature, so parameter
        /// types are part of the serialized identity and the correct overload resolves back.
        /// </summary>
        [WebWorkerTest]
        public async Task SerializableMethodInfoWithParametersRoundTripsTest()
        {
            var original = typeof(WebWorkerSerializationTests).GetMethod(nameof(SampleMethodWithParameters))!;
            var json = SerializableMethodInfo.SerializeMethodInfo(original);
            var resolved = SerializableMethodInfo.DeserializeMethodInfo(json);
            if (resolved != original) throw new Exception("Parameterized method did not round-trip to the same MethodInfo");
        }

        /// <summary>A stable target for method-info serialization.</summary>
        public static void SampleMethodForSerialization() { }

        /// <summary>A stable parameterized target for method-info serialization.</summary>
        public static string SampleMethodWithParameters(string a, int b) => $"{a}:{b}";
    }
}
