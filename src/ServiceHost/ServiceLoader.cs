using CloudWorker.ServiceInterface;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Reflection;

namespace CloudWorker.ServiceHost;

interface IServiceLoader
{
    IUserService CreateServiceInstance();
}

class ServiceLoaderOptions
{
    [Required]
    public string? AssemblyPath { get; set; }
}

class ServiceLoader : IServiceLoader
{
    //NOTE: These types are shared between the host and the plugin (the user service) and thus
    //require special handling. See the following documents for what and why:
    //https://github.com/natemcmaster/DotNetCorePlugins
    //https://learn.microsoft.com/en-us/dotnet/api/system.runtime.loader.assemblyloadcontext?view=net-8.0

    private static Type[] SharedTypes = [typeof(ILogger), typeof(IConfiguration)];

    private readonly ILogger _logger;
    private readonly ILogger _userLogger;
    private readonly IConfiguration _configuration;
    private readonly ServiceLoaderOptions _options;

    public ServiceLoader(ILogger<ServiceLoader> logger, ILogger<IUserService> userLogger,
        IConfiguration configuration, IOptions<ServiceLoaderOptions> options)
    {
        _logger = logger;
        _userLogger = userLogger;
        _configuration = configuration;
        _options = options.Value;
    }

    public IUserService CreateServiceInstance()
    {
        try
        {
            var assembly = LoadAssembly(_options.AssemblyPath!, _logger);
            var type = GetUserServiceType(assembly);
            var instance = (Activator.CreateInstance(type, _userLogger, _configuration) as IUserService)!;
            return instance;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error when creating user service instance");
            throw;
        }
    }

    static Assembly LoadAssembly(string path, ILogger? logger = null)
    {
        var loadContext = new ServiceAssemblyLoadContext(path, SharedTypes, logger);
        return loadContext.LoadFromAssemblyName(new AssemblyName(Path.GetFileNameWithoutExtension(path)));
    }

    static Type GetUserServiceType(Assembly assembly)
    {
        foreach (Type type in assembly.GetTypes())
        {
            if (typeof(IUserService).IsAssignableFrom(type))
            {
                return type;
            }
        }

        throw new ApplicationException($"Can't find a type that implements IUserService in {assembly} from {assembly.Location}.");
    }
}

static class ServiceCollectionUserServiceExtensions
{
    public static IServiceCollection AddUserService(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddTransient<IServiceLoader, ServiceLoader>();

        services.AddOptionsWithValidateOnStart<ServiceLoaderOptions>()
            .Bind(configuration.GetSection("Service"))
            .ValidateDataAnnotations();

        services.AddTransient<IUserService>(provider => provider.GetService<IServiceLoader>()!.CreateServiceInstance());
        return services;
    }
}
