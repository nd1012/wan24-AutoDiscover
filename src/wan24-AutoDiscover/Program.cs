using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.HttpOverrides;
using System.Diagnostics;
using wan24.AutoDiscover;
using wan24.AutoDiscover.Models;
using wan24.AutoDiscover.Services;
using wan24.CLI;
using wan24.Core;

// Global cancellation token source
using CancellationTokenSource cts = new();

// Run the CLI API
if (args.Length > 0 && !args[0].StartsWith('-'))
{
    CliConfig.Apply(new(args));
    await Bootstrap.Async(cancellationToken: cts.Token).DynamicContext();
    Translation.Current = Translation.Dummy;
    Settings.AppId = "wan24-AutoDiscover";
    Settings.ProcessId = "cli";
    Logging.Logger ??= new VividConsoleLogger();
    CliApi.HelpHeader = $"wan24-AutoDiscover {VersionInfo.Current} - (c) 2024 Andreas Zimmermann, wan24.de";
    return await CliApi.RunAsync(args, cts.Token, [typeof(CliHelpApi), typeof(CommandLineInterface)]).DynamicContext();
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
CliConfig.Apply(new(args));
await Bootstrap.Async().DynamicContext();
Translation.Current = Translation.Dummy;
Settings.AppId = "wan24-AutoDiscover";
Settings.ProcessId = "service";
Settings.LogLevel = config.GetValue<LogLevel>("Logging:LogLevel:Default");
Logging.Logger ??= DiscoveryConfig.Current.LogFile is string logFile && !string.IsNullOrWhiteSpace(logFile)
    ? await FileLogger.CreateAsync(logFile, next: new VividConsoleLogger()).DynamicContext()
    : new VividConsoleLogger();
ErrorHandling.ErrorHandler = (e) => Logging.WriteError($"{e.Info}: {e.Exception}");
Logging.WriteInfo($"wan24-AutoDiscover {VersionInfo.Current} Using configuration \"{configFile}\"");

// Watch configuration changes
using SemaphoreSync configSync = new();
using MultiFileSystemEvents fsw = new();//TODO Throttle only the main service events
fsw.OnEvents += async (s, e) =>
{
    try
    {
        if (Logging.Debug)
            Logging.WriteDebug("Handling configuration change");
        if (configSync.IsSynchronized)
        {
            Logging.WriteWarning("Can't handle configuration change, because another handler is still processing (configuration reload takes too long)");
            return;
        }
        using SemaphoreSyncContext ssc = await configSync.SyncContextAsync(cts.Token).DynamicContext();
        // Pre-reload command
        if (DiscoveryConfig.Current.PreReloadCommand is not null && DiscoveryConfig.Current.PreReloadCommand.Length > 0)
            try
            {
                Logging.WriteInfo("Executing pre-reload command on detected configuration change");
                using Process proc = new();
                proc.StartInfo.FileName = DiscoveryConfig.Current.PreReloadCommand[0];
                if (DiscoveryConfig.Current.PreReloadCommand.Length > 1)
                    proc.StartInfo.ArgumentList.AddRange(DiscoveryConfig.Current.PreReloadCommand[1..]);
                proc.StartInfo.UseShellExecute = true;
                proc.StartInfo.CreateNoWindow = true;
                proc.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                proc.Start();
                await proc.WaitForExitAsync(cts.Token).DynamicContext();
                if (proc.ExitCode != 0)
                    Logging.WriteWarning($"Pre-reload command exit code was #{proc.ExitCode}");
            }
            catch (Exception ex)
            {
                Logging.WriteError($"Pre-reload command execution failed exceptional: {ex}");
            }
            finally
            {
                if (Logging.Trace)
                    Logging.WriteTrace("Pre-reload command execution done");
            }
        // Reload configuration
        if (File.Exists(configFile))
        {
            Logging.WriteInfo($"Auto-reloading changed configuration from \"{configFile}\"");
            await LoadConfigAsync().DynamicContext();
        }
        else if(Logging.Trace)
        {
            Logging.WriteTrace($"Configuration file \"{configFile}\" doesn't exist");
        }
    }
    catch (Exception ex)
    {
        Logging.WriteWarning($"Failed to reload configuration from \"{configFile}\": {ex}");
    }
    finally
    {
        if (Logging.Trace)
            Logging.WriteTrace($"Auto-reloading changed configuration from \"{configFile}\" done");
    }
};
fsw.Add(new(ENV.AppFolder, "appsettings.json", NotifyFilters.LastWrite | NotifyFilters.CreationTime, throttle: 250, recursive: false));
if (DiscoveryConfig.Current.WatchEmailMappings && DiscoveryConfig.Current.EmailMappings is not null)
    fsw.Add(new(
        Path.GetDirectoryName(Path.GetFullPath(DiscoveryConfig.Current.EmailMappings))!,
        Path.GetFileName(DiscoveryConfig.Current.EmailMappings),
        NotifyFilters.LastWrite | NotifyFilters.CreationTime,
        throttle: 250,//TODO Throttle only the main service events
        recursive: false,
        FileSystemEventTypes.Changes | FileSystemEventTypes.Created | FileSystemEventTypes.Deleted
        ));
if (DiscoveryConfig.Current.WatchFiles is not null)
    foreach (string file in DiscoveryConfig.Current.WatchFiles)
        fsw.Add(new(
            Path.GetDirectoryName(Path.GetFullPath(file))!,
            Path.GetFileName(file),
            NotifyFilters.LastWrite | NotifyFilters.CreationTime,
            throttle: 250,//TODO Throttle only the main service events
            recursive: false,
            FileSystemEventTypes.Changes | FileSystemEventTypes.Created | FileSystemEventTypes.Deleted
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
builder.Services.AddSingleton(typeof(XmlDocumentInstances), services => new XmlDocumentInstances(capacity: DiscoveryConfig.Current.PreForkResponses))
    .AddHostedService(services => services.GetRequiredService<XmlDocumentInstances>())
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
