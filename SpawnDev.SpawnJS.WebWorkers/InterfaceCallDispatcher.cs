using System.Diagnostics.CodeAnalysis;
using SpawnDev.SpawnJS.Marshaller;
using System.Reflection;

namespace SpawnDev.SpawnJS.WebWorkers
{
    /// <summary>
    /// Shared text for the interface-proxy APIs' [RequiresDynamicCode].
    /// </summary>
    public static class InterfaceCallDispatcherInfo
    {
        /// <summary>
        /// Why the interface proxy needs dynamic code. Browser .NET (Mono: interpreter, or AOT with its interpreter
        /// fallback) runs it; NativeAOT cannot. Run/Invoke/New calls never use a proxy and have no such limit.
        /// </summary>
        public const string DynamicCodeMessage = "GetService/GetKeyedService build a System.Reflection.DispatchProxy, which emits a proxy type at runtime. Supported on browser .NET (Mono interpreter, and Mono AOT via its interpreter fallback); not supported under NativeAOT. The expression based Run/Invoke/New APIs need no proxy.";
    }
    /// <summary>
    /// This Proxy presents itself as the interface it is created with but calls are converted to MethodInfos with arguments and passed onto the ICallDispatcher given at creation
    /// </summary>
    /// <typeparam name="TServiceInterface"></typeparam>
    public class InterfaceCallDispatcher<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TServiceInterface> : DispatchProxy where TServiceInterface : class
    {
        private Func<Type, MethodInfo, object?[]?, object?>? Resolver { get; set; }
        private Func<Type, MethodInfo, object?[]?, Task<object?>>? AsyncResolver { get; set; }
        private Func<Type, object, MethodInfo, object?[]?, object?>? KeyedResolver { get; set; }
        private Func<Type, object, MethodInfo, object?[]?, Task<object?>>? AsyncKeyedResolver { get; set; }
        /// <summary>
        /// Service Key
        /// </summary>
        public object? Key { get; private set; }
        /// <summary>
        /// True if the the service is a keyed service
        /// </summary>
        public bool Keyed { get; private set; }
        /// <summary>
        /// The service Type
        /// </summary>
        public Type ServiceType { get; private set; } = default!;
        private Task<object?> CallAsync(MethodInfo methodInfo, object?[]? args)
        {
            if (AsyncKeyedResolver != null) return AsyncKeyedResolver(ServiceType, Key!, methodInfo, args);
            if (AsyncResolver != null) return AsyncResolver(ServiceType, methodInfo, args);
            throw new NullReferenceException("InterfaceCallDispatcher: No asynchronous call resolver set");
        }
        private object? Call(MethodInfo methodInfo, object?[]? args)
        {
            if (KeyedResolver != null) return KeyedResolver(ServiceType, Key, methodInfo, args);
            if (Resolver != null) return Resolver(ServiceType, methodInfo, args);
            throw new NullReferenceException("InterfaceCallDispatcher: No synchronous call resolver set");
        }
        /// <summary>
        /// Handles all requests on interface TServiceInterface
        /// </summary>
        /// <param name="targetMethod"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod == null) return null;
            var returnType = targetMethod.ReturnType;
            var isTask = returnType.IsTask();
            var isValueTask = !isTask && returnType.IsValueTask();
            Type finalReturnType = isTask || isValueTask ? returnType.GetGenericArguments().FirstOrDefault() ?? typeof(void) : returnType;
            if (isTask) return CallAsync(targetMethod, args).RecastTask(finalReturnType);
            if (isValueTask) return CallAsync(targetMethod, args).RecastValueTask(finalReturnType);
            return Call(targetMethod, args);
        }
        /// <summary>
        /// Creates a new AsyncInterfaceCallDispatcher returned as type TServiceInterface
        /// </summary>
        /// <param name="key"></param>
        /// <param name="keyedResolver"></param>
        /// <param name="asyncKeyedResolver"></param>
        /// <returns></returns>
        [RequiresDynamicCode(InterfaceCallDispatcherInfo.DynamicCodeMessage)]
        public static TServiceInterface CreateInterfaceDispatcher(object key, Func<Type, object?, MethodInfo, object?[]?, Task<object?>> keyedResolver, Func<Type, object?, MethodInfo, object?[]?, Task<object?>>? asyncKeyedResolver = null)
        {
            if (!typeof(TServiceInterface).IsInterface) throw new Exception("InterfaceCallDispatcher can only be created for interface types");
            var ret = Create<TServiceInterface, InterfaceCallDispatcher<TServiceInterface>>();
            var proxy = (ret as InterfaceCallDispatcher<TServiceInterface>)!;
            proxy.KeyedResolver = keyedResolver;
            proxy.AsyncKeyedResolver = asyncKeyedResolver;
            proxy.Key = key;
            proxy.Keyed = true;
            proxy.ServiceType = typeof(TServiceInterface);
            return ret;
        }
        /// <summary>
        /// Creates a new AsyncInterfaceCallDispatcher returned as type TServiceInterface
        /// </summary>
        /// <param name="key"></param>
        /// <param name="asyncKeyedResolver"></param>
        /// <returns></returns>
        [RequiresDynamicCode(InterfaceCallDispatcherInfo.DynamicCodeMessage)]
        public static TServiceInterface CreateInterfaceDispatcher(object key, Func<Type, object, MethodInfo, object?[]?, Task<object?>> asyncKeyedResolver)
        {
            if (!typeof(TServiceInterface).IsInterface) throw new Exception("InterfaceCallDispatcher can only be created for interface types");
            var ret = Create<TServiceInterface, InterfaceCallDispatcher<TServiceInterface>>();
            var proxy = (ret as InterfaceCallDispatcher<TServiceInterface>)!;
            proxy.AsyncKeyedResolver = asyncKeyedResolver;
            proxy.Key = key;
            proxy.Keyed = true;
            proxy.ServiceType = typeof(TServiceInterface);
            return ret;
        }
        /// <summary>
        /// Creates a new AsyncInterfaceCallDispatcher returned as type TServiceInterface
        /// </summary>
        /// <param name="asyncResolver"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        [RequiresDynamicCode(InterfaceCallDispatcherInfo.DynamicCodeMessage)]
        public static TServiceInterface CreateInterfaceDispatcher(Func<Type, MethodInfo, object?[]?, Task<object?>> asyncResolver)
        {
            if (!typeof(TServiceInterface).IsInterface) throw new Exception("InterfaceCallDispatcher can only be created for interface types");
            var ret = Create<TServiceInterface, InterfaceCallDispatcher<TServiceInterface>>();
            var proxy = ret as InterfaceCallDispatcher<TServiceInterface>;
            proxy!.AsyncResolver = asyncResolver;
            proxy.ServiceType = typeof(TServiceInterface);
            return ret;
        }
        /// <summary>
        /// Creates a new AsyncInterfaceCallDispatcher returned as type TServiceInterface
        /// </summary>
        /// <param name="resolver"></param>
        /// <param name="asyncResolver"></param>
        /// <returns></returns>
        [RequiresDynamicCode(InterfaceCallDispatcherInfo.DynamicCodeMessage)]
        public static TServiceInterface CreateInterfaceDispatcher(Func<Type, MethodInfo, object?[]?, object?> resolver, Func<Type, MethodInfo, object?[]?, Task<object?>>? asyncResolver = null)
        {
            if (!typeof(TServiceInterface).IsInterface) throw new Exception("InterfaceCallDispatcher can only be created for interface types");
            var ret = Create<TServiceInterface, InterfaceCallDispatcher<TServiceInterface>>();
            var proxy = (ret as InterfaceCallDispatcher<TServiceInterface>)!;
            proxy.AsyncResolver = asyncResolver;
            proxy.Resolver = resolver;
            proxy.ServiceType = typeof(TServiceInterface);
            return ret;
        }
    }
}
