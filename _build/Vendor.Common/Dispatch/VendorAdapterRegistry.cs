using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Vendor.Common.Abstractions;
using Vendor.Common.Configuration;

namespace Vendor.Common.Dispatch
{
    /// <summary>
    /// Loads vendor adapters from the &lt;vendorAdapters&gt; config section and holds
    /// them in a vendor-name-indexed dictionary for the dispatcher's lookup.
    ///
    /// INSTANTIATION POLICY:
    /// - Each adapter type is instantiated ONCE (adapters are stateful — they hold
    ///   rate limiter counters, HTTP clients, etc., and are meant to be singletons).
    /// - Eager loading at startup. Misconfiguration fails LOUDLY at startup, not
    ///   silently at 2 AM when the first event arrives.
    /// - Adapters must expose either:
    ///     (a) a parameterless constructor, OR
    ///     (b) a constructor taking (ClientProfileRepository, Action&lt;Exception&gt; errorHandler)
    ///   Type (b) is preferred when an adapter needs config lookup at dispatch time.
    ///
    /// LOOKUP IS BY VENDOR NAME (string), case-insensitive. The config section's
    /// vendorName attribute is the canonical key.
    /// </summary>
    public class VendorAdapterRegistry
    {
        private readonly Dictionary<string, IVendorAdapter> _adapters
            = new Dictionary<string, IVendorAdapter>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, IInboundEventProcessor> _inboundProcessors
            = new Dictionary<string, IInboundEventProcessor>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, IWebhookSignatureValidator> _validators
            = new Dictionary<string, IWebhookSignatureValidator>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Eagerly loads all adapters declared in the &lt;vendorAdapters&gt; config section.
        /// Throws <see cref="VendorAdapterRegistryException"/> if any adapter type is
        /// missing, lacks a usable constructor, or fails to instantiate.
        ///
        /// Pass a <paramref name="profileRepository"/> and <paramref name="errorHandler"/>
        /// for adapters whose constructors take them. Adapters with parameterless
        /// constructors ignore these.
        /// </summary>
        public VendorAdapterRegistry(
            VendorAdaptersSection section,
            ClientProfileRepository profileRepository = null,
            Action<Exception> errorHandler = null)
        {
            if (section == null)
                throw new ArgumentNullException(nameof(section),
                    "vendorAdapters config section is required. Did you forget to declare it in Web.config / App.config?");

            if (section.Adapters.Count == 0)
                throw new VendorAdapterRegistryException(
                    "vendorAdapters section is empty. At least one <add vendorName=... /> row is required.");

            for (int i = 0; i < section.Adapters.Count; i++)
            {
                var element = section.Adapters[i];
                LoadOne(element, profileRepository, errorHandler);
            }
        }

        /// <summary>
        /// Test-friendly overload: caller hands us already-instantiated adapters.
        /// Useful in unit tests and smoke tests where you want to bypass config and
        /// reflection. Not used in production paths.
        /// </summary>
        public VendorAdapterRegistry(IEnumerable<IVendorAdapter> adapters)
        {
            if (adapters == null) throw new ArgumentNullException(nameof(adapters));

            foreach (var a in adapters)
            {
                if (a == null) continue;
                if (string.IsNullOrWhiteSpace(a.VendorName))
                    throw new VendorAdapterRegistryException(
                        $"Adapter {a.GetType().FullName} returned a null/empty VendorName.");
                _adapters[a.VendorName] = a;
            }
        }

        /// <summary>
        /// Returns the adapter for the given vendor name, or null if no adapter is
        /// registered for that vendor. Case-insensitive match.
        /// </summary>
        public IVendorAdapter GetAdapter(string vendorName)
        {
            if (string.IsNullOrWhiteSpace(vendorName)) return null;
            return _adapters.TryGetValue(vendorName, out var adapter) ? adapter : null;
        }

        /// <summary>Returns the inbound processor for the given vendor name, or null.</summary>
        public IInboundEventProcessor GetInboundProcessor(string vendorName)
        {
            if (string.IsNullOrWhiteSpace(vendorName)) return null;
            return _inboundProcessors.TryGetValue(vendorName, out var p) ? p : null;
        }

        /// <summary>Returns the webhook validator for the given vendor name, or null.</summary>
        public IWebhookSignatureValidator GetValidator(string vendorName)
        {
            if (string.IsNullOrWhiteSpace(vendorName)) return null;
            return _validators.TryGetValue(vendorName, out var v) ? v : null;
        }

        /// <summary>Returns all registered vendor names. Useful for diagnostics.</summary>
        public IReadOnlyCollection<string> RegisteredVendorNames => _adapters.Keys;

        // ─── Internals ────────────────────────────────────────────────────────

        private void LoadOne(
            VendorAdapterElement element,
            ClientProfileRepository profileRepository,
            Action<Exception> errorHandler)
        {
            // 1. Adapter (required)
            var adapter = Instantiate<IVendorAdapter>(
                element.AdapterType, "adapterType", element.VendorName,
                profileRepository, errorHandler);

            if (!string.Equals(adapter.VendorName, element.VendorName, StringComparison.OrdinalIgnoreCase))
                throw new VendorAdapterRegistryException(
                    $"Adapter type {element.AdapterType} reports VendorName '{adapter.VendorName}' " +
                    $"but config declared vendorName='{element.VendorName}'. These must match.");

            _adapters[element.VendorName] = adapter;

            // 2. Inbound processor (optional)
            if (!string.IsNullOrWhiteSpace(element.InboundProcessorType))
            {
                var processor = Instantiate<IInboundEventProcessor>(
                    element.InboundProcessorType, "inboundProcessorType", element.VendorName,
                    profileRepository, errorHandler);

                if (!string.Equals(processor.VendorName, element.VendorName, StringComparison.OrdinalIgnoreCase))
                    throw new VendorAdapterRegistryException(
                        $"Inbound processor {element.InboundProcessorType} reports VendorName " +
                        $"'{processor.VendorName}' but config declared vendorName='{element.VendorName}'.");

                _inboundProcessors[element.VendorName] = processor;
            }

            // 3. Webhook validator (optional)
            if (!string.IsNullOrWhiteSpace(element.WebhookValidatorType))
            {
                var validator = Instantiate<IWebhookSignatureValidator>(
                    element.WebhookValidatorType, "webhookValidatorType", element.VendorName,
                    profileRepository, errorHandler);

                if (!string.Equals(validator.VendorName, element.VendorName, StringComparison.OrdinalIgnoreCase))
                    throw new VendorAdapterRegistryException(
                        $"Webhook validator {element.WebhookValidatorType} reports VendorName " +
                        $"'{validator.VendorName}' but config declared vendorName='{element.VendorName}'.");

                _validators[element.VendorName] = validator;
            }
        }

        private static T Instantiate<T>(
            string assemblyQualifiedTypeName,
            string attributeNameForErrors,
            string vendorName,
            ClientProfileRepository profileRepository,
            Action<Exception> errorHandler) where T : class
        {
            if (string.IsNullOrWhiteSpace(assemblyQualifiedTypeName))
                throw new VendorAdapterRegistryException(
                    $"Vendor '{vendorName}': {attributeNameForErrors} is required.");

            Type type;
            try
            {
                type = Type.GetType(assemblyQualifiedTypeName, throwOnError: true);
            }
            catch (Exception ex)
            {
                throw new VendorAdapterRegistryException(
                    $"Vendor '{vendorName}': could not load type '{assemblyQualifiedTypeName}'. " +
                    "Check the assembly-qualified name and that the assembly is deployed.",
                    ex);
            }

            if (!typeof(T).IsAssignableFrom(type))
                throw new VendorAdapterRegistryException(
                    $"Vendor '{vendorName}': type '{type.FullName}' does not implement {typeof(T).Name}.");

            // Try ctor (ClientProfileRepository, Action<Exception>) first
            var ctorWithDeps = type.GetConstructor(
                BindingFlags.Public | BindingFlags.Instance, binder: null,
                types: new[] { typeof(ClientProfileRepository), typeof(Action<Exception>) },
                modifiers: null);
            if (ctorWithDeps != null)
            {
                try
                {
                    return (T)ctorWithDeps.Invoke(new object[] { profileRepository, errorHandler });
                }
                catch (TargetInvocationException tie)
                {
                    throw new VendorAdapterRegistryException(
                        $"Vendor '{vendorName}': constructor of '{type.FullName}' threw on invocation. " +
                        "See inner exception.",
                        tie.InnerException ?? tie);
                }
            }

            // Fall back to parameterless ctor
            var ctorEmpty = type.GetConstructor(Type.EmptyTypes);
            if (ctorEmpty != null)
            {
                try
                {
                    return (T)ctorEmpty.Invoke(null);
                }
                catch (TargetInvocationException tie)
                {
                    throw new VendorAdapterRegistryException(
                        $"Vendor '{vendorName}': parameterless constructor of '{type.FullName}' threw on invocation. " +
                        "See inner exception.",
                        tie.InnerException ?? tie);
                }
            }

            throw new VendorAdapterRegistryException(
                $"Vendor '{vendorName}': type '{type.FullName}' has no usable constructor. " +
                "Provide either a parameterless constructor OR a constructor taking " +
                "(ClientProfileRepository, Action<Exception>).");
        }
    }

    /// <summary>Thrown by VendorAdapterRegistry when config or reflection fails.</summary>
    public class VendorAdapterRegistryException : Exception
    {
        public VendorAdapterRegistryException(string message) : base(message) { }
        public VendorAdapterRegistryException(string message, Exception inner) : base(message, inner) { }
    }
}
