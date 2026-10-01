using System.Diagnostics.CodeAnalysis;

namespace SpawnDev.SpawnJS.WebWorkers
{
    /// <summary>
    /// A simple serializer for exceptions that can be used to pass exceptions between window, service worker, dedicated worker, and shared worker contexts.<br/>
    /// </summary>
    public static class ExceptionSerializer
    {
        /// <summary>
        /// Exception type cache
        /// </summary>
        static Dictionary<string, Type?> ExceptionTypes = new Dictionary<string, Type?>();
        /// <summary>
        /// Exceptions callers catch by type, rebuilt without reflection so their identity survives any trimming.
        /// Anything else goes through the by-name path in Deserialize.
        /// </summary>
        static readonly Dictionary<string, Func<string, Exception>> KnownExceptions = new Dictionary<string, Func<string, Exception>>
        {
            [typeof(Exception).FullName!] = m => new Exception(m),
            [typeof(OperationCanceledException).FullName!] = m => new OperationCanceledException(m),
            [typeof(TaskCanceledException).FullName!] = m => new TaskCanceledException(m),
            [typeof(TimeoutException).FullName!] = m => new TimeoutException(m),
            [typeof(ArgumentException).FullName!] = m => new ArgumentException(m),
            [typeof(ArgumentNullException).FullName!] = m => new ArgumentNullException(null, m),
            [typeof(ArgumentOutOfRangeException).FullName!] = m => new ArgumentOutOfRangeException(null, m),
            [typeof(InvalidOperationException).FullName!] = m => new InvalidOperationException(m),
            [typeof(NotSupportedException).FullName!] = m => new NotSupportedException(m),
            [typeof(NotImplementedException).FullName!] = m => new NotImplementedException(m),
            [typeof(ObjectDisposedException).FullName!] = m => new ObjectDisposedException(null, m),
            [typeof(KeyNotFoundException).FullName!] = m => new KeyNotFoundException(m),
            [typeof(IndexOutOfRangeException).FullName!] = m => new IndexOutOfRangeException(m),
            [typeof(NullReferenceException).FullName!] = m => new NullReferenceException(m),
            [typeof(InvalidCastException).FullName!] = m => new InvalidCastException(m),
            [typeof(FormatException).FullName!] = m => new FormatException(m),
            [typeof(OverflowException).FullName!] = m => new OverflowException(m),
            [typeof(DivideByZeroException).FullName!] = m => new DivideByZeroException(m),
            [typeof(UnauthorizedAccessException).FullName!] = m => new UnauthorizedAccessException(m),
            [typeof(IOException).FullName!] = m => new IOException(m),
            [typeof(FileNotFoundException).FullName!] = m => new FileNotFoundException(m),
            [typeof(OutOfMemoryException).FullName!] = m => new OutOfMemoryException(m),
        };
        /// <summary>
        /// Serializes an exception to a string.
        /// </summary>
        /// <param name="exception"></param>
        /// <returns></returns>
        public static string? Serialize(Exception? exception)
        {
            if (exception == null) return null;
            // most of the time, Exceptions prefix the Exception type's FullName to the ToString() return value but it is not required
            // if it is not already there, add it
            var exceptionString = exception.ToString();
            var typeNamePart = $"{exception.GetType().FullName}: ";
            return exceptionString.StartsWith(typeNamePart) ? exceptionString : $"{typeNamePart}{exceptionString}";
        }
        /// <summary>
        /// Deserializes an exception from a serialized string.
        /// </summary>
        /// <param name="serializedException"></param>
        /// <returns></returns>
        [UnconditionalSuppressMessage("Trimming", "IL2057", Justification = "Best-effort rebuild of an exception type the other side threw from the same build. If trimming removed the type or its constructors, the result is a plain Exception carrying the full serialized text, never a failure.")]
        [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "See IL2057: Activator failures are caught and fall back to a plain Exception.")]
        public static Exception? Deserialize(string? serializedException)
        {
            if (string.IsNullOrEmpty(serializedException)) return null;
            var parts = serializedException.Split(new[] { ": " }, 2, StringSplitOptions.None);
            if (parts.Length < 2) return null;
            var typeName = parts[0];
            var message = parts[1];
            if (KnownExceptions.TryGetValue(typeName, out var knownFactory)) return knownFactory(message);
            if (!ExceptionTypes.TryGetValue(typeName, out var exTypeCached))
            {
                exTypeCached = Type.GetType(typeName);
                ExceptionTypes[typeName] = exTypeCached;
            }
            if (exTypeCached == null)
            {
                return new Exception(serializedException);
            }
            try
            {
                return (Exception)Activator.CreateInstance(exTypeCached, new object?[] { message })!;
            }
            catch { }
            try
            {
                return (Exception)Activator.CreateInstance(exTypeCached)!;
            }
            catch
            {
                ExceptionTypes[typeName] = null;
                return new Exception(serializedException);
            }
        }
    }
}
