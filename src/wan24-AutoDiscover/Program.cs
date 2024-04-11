using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.HttpOverrides;
using wan24.AutoDiscover;
using wan24.AutoDiscover.Models;
using wan24.AutoDiscover.Services;
using wan24.CLI;
using wan24.Core;

// Bootstrapping
using CancellationTokenSource cts = new();
CliConfig.Apply(new(args));
await Bootstrap.Async(cancellationToken: cts.Token).DynamicContext();
Translation.Current = Translation.Dummy;
ErrorHandling.ErrorHandler = (e) => Logging.WriteError($"{e.Info}: {e.Exception}");
Settings.AppId = "wan24-AutoDiscover";

// Run the CLI API
if (args.Length > 0 && !args[0].StartsWith('-'))
{
    Settings.ProcessId = "cli";
    Logging.Logger ??= new VividConsoleLogger();
    CliApi.HelpHeader = $"wan24-AutoDiscover {VersionInfo.Current} - (c) 2024 Andreas Zimmermann, wan24.de";
    AboutApi.Version = VersionInfo.Current;
    AboutApi.Info = "(c) 2024 Andreas Zimmermann, wan24.de";
    return await CliApi.RunAsync(args, cts.Token, [typeof(CliHelpApi), typeof(CommandLineInterface), typeof(AboutApi)]).DynamicContext();
}

// Load the configuration
string configFile = Path.Combine(ENV.AppFolder, "appsettings.json");
async Task<IConfigurationRoot> LoadConfigAsync()
{
    ConfigurationBuilder configBuilder = new();
    configBuilder.AddJsonFile(configFile, optional: false);
    IConfigurationRoot config = configBuilder.Build();
    DiscoveryConfig.Current = config.GetRequiredSection("DiscoveryConfig").Get<DiscoveryConfig>()
        ?? throw new InvalidDataException($"Failed to get a {typeof(DiscoveryConfig)} from the \"DiscoveryConfig\" section");
    DomainConfig.Registered = await DiscoveryConfig.Current.GetDiscoveryConfigAsync(config, cts.Token).DynamicContext();
    return config;
}
IConfigurationRoot config = await LoadConfigAsync().DynamicContext();

// Initialize wan24-Core
Settings.ProcessId = "service";
Settings.LogLevel = config.GetValue<LogLevel>("Logging:LogLevel:Default");
Logging.Logger ??= !string.IsNullOrWhiteSpace(DiscoveryConfig.Current.LogFile)
    ? await FileLogger.CreateAsync(DiscoveryConfig.Current.LogFile, next: new VividConsoleLogger(), cancellationToken: cts.Token).DynamicContext()
    : new VividConsoleLogger();
Logging.WriteInfo($"wan24-AutoDiscover {VersionInfo.Current} Using configuration \"{configFile}\"");

// Watch configuration changes
using SemaphoreSync configSync = new();
using MultiFileSystemEvents fsw = new(throttle: 250);
fsw.OnEvents += async (s, e) =>
{
    try
    {
        if (Logging.Debug)
            Logging.WriteDebug("Handling configuration change");
        if (configSync.IsSynchronized)
        {
            Logging.WriteWarning("Can't handle configuration change, because another handler is still processing (configuration reload takes too long!)");
            return;
        }
        using SemaphoreSyncContext ssc = await configSync.SyncContextAsync(cts.Token).DynamicContext();
        // Pre-reload command
        if (DiscoveryConfig.Current.PreReloadCommand is not null && DiscoveryConfig.Current.PreReloadCommand.Length > 0)
            try
            {
                Logging.WriteInfo("Executing pre-reload command on detected configuration change");
                int exitCode = await ProcessHelper.GetExitCodeAsync(
                    DiscoveryConfig.Current.PreReloadCommand[0],
                    cancellationToken: cts.Token,
                    args: [.. DiscoveryConfig.Current.PreReloadCommand[1..]]
                    ).DynamicContext();
                if (exitCode != 0)
                    Logging.WriteWarning($"Pre-reload command exit code was #{exitCode}");
                if (Logging.Trace)
                    Logging.WriteTrace("Pre-reload command execution done");
            }
            catch (Exception ex)
            {
                Logging.WriteError($"Pre-reload command execution failed exceptional: {ex}");
            }
        // Reload configuration
        if (File.Exists(configFile))
        {
            Logging.WriteInfo($"Auto-reloading changed configuration from \"{configFile}\"");
            await LoadConfigAsync().DynamicContext();
            if (Logging.Trace)
                Logging.WriteTrace($"Auto-reloading changed configuration from \"{configFile}\" done");
        }
        else if(Logging.Trace)
        {
            Logging.WriteTrace($"Configuration file \"{configFile}\" doesn't exist");
        }
    }
    catch (Exception ex)
    {
        Logging.WriteError($"Failed to reload configuration from \"{configFile}\": {ex}");
    }
};
fsw.Add(new(ENV.AppFolder, "appsettings.json", NotifyFilters.LastWrite | NotifyFilters.CreationTime, throttle: 250, recursive: false));
if (DiscoveryConfig.Current.WatchEmailMappings && !string.IsNullOrWhiteSpace(DiscoveryConfig.Current.EmailMappings))
    fsw.Add(new(
        Path.GetDirectoryName(Path.GetFullPath(DiscoveryConfig.Current.EmailMappings))!,
        Path.GetFileName(DiscoveryConfig.Current.EmailMappings),
        NotifyFilters.LastWrite | NotifyFilters.CreationTime,
        recursive: false,
        events: FileSystemEventTypes.Changes | FileSystemEventTypes.Created | FileSystemEventTypes.Deleted
        ));
if (DiscoveryConfig.Current.WatchFiles is not null)
    foreach (string file in DiscoveryConfig.Current.WatchFiles)
        if (!string.IsNullOrWhiteSpace(file))
            fsw.Add(new(
                Path.GetDirectoryName(Path.GetFullPath(file))!,
                Path.GetFileName(file),
                NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                recursive: false,
                events: FileSystemEventTypes.Changes | FileSystemEventTypes.Created | FileSystemEventTypes.Deleted
                ));

// Build and run the app
Logging.WriteInfo("Autodiscovery service app startup");
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Logging.ClearProviders()
    .AddConsole();
if (ENV.IsLinux)
    builder.Logging.AddSystemdConsole();
builder.Services.AddControllers();
builder.Services.AddSingleton(typeof(XmlResponseInstances), services => new XmlResponseInstances(capacity: DiscoveryConfig.Current.PreForkResponses))
    .AddSingleton(cts)
    .AddHostedService(services => services.GetRequiredService<XmlResponseInstances>())
    .AddHostedService(services => fsw)
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
        app.Lifetime.ApplicationStopping.Register(() =>
        {
            Logging.WriteInfo("Autodiscovery service app shutdown");
            cts.Cancel();
        });
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
        await app.RunAsync(cts.Token).DynamicContext();
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
    cts.Cancel();
    Logging.WriteInfo("Autodiscovery service app exit");
}
return 0;
