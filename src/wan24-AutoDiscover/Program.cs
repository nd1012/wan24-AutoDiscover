using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.HttpOverrides;
using wan24.AutoDiscover.Models;
using wan24.AutoDiscover.Services;
using wan24.CLI;
using wan24.Core;

// Run the CLI API
if (args.Length > 0)
{
    await Bootstrap.Async().DynamicContext();
    Translation.Current = Translation.Dummy;
    Settings.AppId = "wan24-AutoDiscover";
    Settings.ProcessId = "cli";
    Logging.Logger = new VividConsoleLogger();
    CliApi.HelpHeader = "wan24-AutoDiscover - (c) 2024 Andreas Zimmermann, wan24.de";
    return await CliApi.RunAsync(args, exportedApis: [typeof(CliHelpApi), typeof(CommandLineInterface)]).DynamicContext();
}

// Load the configuration
string configFile = Path.Combine(ENV.AppFolder, "appsettings.json");
IConfigurationRoot  LoadConfig()
{
    ConfigurationBuilder configBuilder = new();
    configBuilder.AddJsonFile(configFile, optional: false);
    IConfigurationRoot config = configBuilder.Build();
    DiscoveryConfig.Current = config.GetRequiredSection("DiscoveryConfig").Get<DiscoveryConfig>()
        ?? throw new InvalidDataException($"Failed to get a {typeof(DiscoveryConfig)} from the \"DiscoveryConfig\" section");
    DomainConfig.Registered = DiscoveryConfig.Current.GetDiscoveryConfig(config);
    return config;
}
IConfigurationRoot config = LoadConfig();

// Initialize wan24-Core
await Bootstrap.Async().DynamicContext();
Translation.Current = Translation.Dummy;
Settings.AppId = "wan24-AutoDiscover";
Settings.ProcessId = "webservice";
Settings.LogLevel = config.GetValue<LogLevel>("Logging:LogLevel:Default");
Logging.Logger = DiscoveryConfig.Current.LogFile is string logFile && !string.IsNullOrWhiteSpace(logFile)
    ? await FileLogger.CreateAsync(logFile, next: new VividConsoleLogger()).DynamicContext()
    : new VividConsoleLogger();
ErrorHandling.ErrorHandler = (e) => Logging.WriteError($"{e.Info}: {e.Exception}");
Logging.WriteInfo($"Using configuration \"{configFile}\"");

// Watch configuration changes
using ConfigChangeEventThrottle fswThrottle = new();
ConfigChangeEventThrottle.OnConfigChange += () =>
{
    try
    {
        Logging.WriteDebug("Handling configuration change");
        if (File.Exists(configFile))
        {
            Logging.WriteInfo($"Auto-reloading changed configuration from \"{configFile}\"");
            LoadConfig();
        }
        else
        {
            Logging.WriteTrace($"Configuration file \"{configFile}\" doesn't exist");
        }
    }
    catch (Exception ex)
    {
        Logging.WriteWarning($"Failed to reload configuration from \"{configFile}\": {ex}");
    }
};
void ReloadConfig(object sender, FileSystemEventArgs e)
{
    try
    {
        Logging.WriteDebug($"Detected configuration change {e.ChangeType}");
        if (File.Exists(configFile))
        {
            if (fswThrottle.IsThrottling)
            {
                Logging.WriteTrace("Skipping configuration change event due too many events");
            }
            else if (fswThrottle.Raise())
            {
                Logging.WriteTrace("Configuration change event has been raised");
            }
        }
        else
        {
            Logging.WriteTrace($"Configuration file \"{configFile}\" doesn't exist");
        }
    }
    catch (Exception ex)
    {
        Logging.WriteWarning($"Failed to handle configuration change of \"{configFile}\": {ex}");
    }
}
using FileSystemWatcher fsw = new(ENV.AppFolder, "appsettings.json")
{
    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime,
    IncludeSubdirectories = false,
    EnableRaisingEvents = true
};
fsw.Changed += ReloadConfig;
fsw.Created += ReloadConfig;

// Build and run the app
Logging.WriteInfo("Autodiscovery service app startup");
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Logging.ClearProviders()
    .AddConsole();
if (ENV.IsLinux)
    builder.Logging.AddSystemdConsole();
builder.Services.AddControllers();
builder.Services.AddSingleton(typeof(XmlDocumentInstances), services => new XmlDocumentInstances(capacity: DiscoveryConfig.Current.PreForkResponses))
    .AddHostedService(services => services.GetRequiredService<XmlDocumentInstances>())
    .AddExceptionHandler<ExceptionHandler>()
    .AddHttpLogging(options => options.LoggingFields = HttpLoggingFields.RequestPropertiesAndHeaders);
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardLimit = 2;
    options.KnownProxies.AddRange(DiscoveryConfig.Current.KnownProxies);
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});
WebApplication app = builder.Build();
try
{
    await using (app.DynamicContext())
    {
        app.MapDefaultEndpoints();
        app.UseForwardedHeaders();
        if (app.Environment.IsDevelopment())
        {
            if (Logging.Trace)
                Logging.WriteTrace("Using development environment");
            app.UseHttpLogging();
        }
        app.UseExceptionHandler(builder => { });// .NET 8 bugfix :(
        if (!app.Environment.IsDevelopment())
        {
            if (Logging.Trace)
                Logging.WriteTrace("Using production environment");
            app.UseHsts();
            app.UseHttpsRedirection();
            app.UseAuthorization();
        }
        app.MapControllers();
        Logging.WriteInfo("Autodiscovery service app starting");
        await app.RunAsync().DynamicContext();
        Logging.WriteInfo("Autodiscovery service app quitting");
    }
}
catch(Exception ex)
{
    Logging.WriteError($"Autodiscovery service app error: {ex}");
    return 1;
}
finally
{
    Logging.WriteInfo("Autodiscovery service app exit");
}
return 0;
