using System.Text.Json.Serialization;

namespace SpawnDev.SpawnJS.WebWorkers
{
    /// <summary>
    /// Source-generated System.Text.Json metadata for the library's own serialized types.<br/>
    /// <br/>
    /// The library only ever serializes types it owns and closes over at compile time - an instance
    /// identity (<see cref="AppInstanceInfo"/>, used as a Web Lock name) and a method identity
    /// (<see cref="SerializableMethodInfo"/>, the call target on the wire). User method arguments and
    /// return values do NOT go through here; they are marshalled by SpawnJS over postMessage.<br/>
    /// <br/>
    /// Because these are the only shapes, source generation removes the reflection-based serializer
    /// entirely. That is what makes the library work in a trimmed / AOT app, where the reflection path
    /// is disabled and <c>JsonSerializer.Serialize(object)</c> throws "JsonSerializerIsReflectionDisabled".
    /// <br/>
    /// <see cref="JsonSourceGenerationOptionsAttribute.PropertyNameCaseInsensitive"/> preserves the
    /// tolerance the reflection path had - a value serialized here reads back regardless of property-name
    /// casing, which the deserialize side relied on.
    /// </summary>
    [JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
    [JsonSerializable(typeof(AppInstanceInfo))]
    [JsonSerializable(typeof(SerializableMethodInfo))]
    internal partial class WebWorkersJsonContext : JsonSerializerContext
    {
    }
}
