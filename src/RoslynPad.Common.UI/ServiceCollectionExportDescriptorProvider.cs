﻿using System.Composition.Hosting.Core;
using Microsoft.Extensions.DependencyInjection;

namespace RoslynPad;

public class ServiceCollectionExportDescriptorProvider(ServiceCollection services) : ExportDescriptorProvider
{
    private readonly Dictionary<Type, ServiceDescriptor> _services = services.GroupBy(s => s.ServiceType).Select(s => s.Last()).ToDictionary(s => s.ServiceType);
    private readonly ServiceProvider _serviceProvider = services.BuildServiceProvider();

    public override IEnumerable<ExportDescriptorPromise> GetExportDescriptors(CompositionContract contract, DependencyAccessor descriptorAccessor)
    {
        if (!_services.TryGetValue(contract.ContractType, out var service) &&
            !(contract.ContractType.IsGenericType && contract.ContractType.GetGenericTypeDefinition() is var genericType &&
            _services.TryGetValue(genericType, out service)))
        {
            yield break;
        }

        yield return new ExportDescriptorPromise(contract, nameof(ServiceCollectionExportDescriptorProvider),
            service.Lifetime != ServiceLifetime.Transient, Array.Empty<CompositionDependency>,
            _ => ExportDescriptor.Create((_, _) => _serviceProvider.GetService(contract.ContractType),
                new Dictionary<string, object>()));
    }
}
