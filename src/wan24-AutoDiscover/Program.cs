using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.HttpOverrides;
using System.Diagnostics;
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
using SemaphoreSync configSync = new();
string configFile = Path.Combine(ENV.AppFolder, "appsettings.json");
async Task<IConfigurationRoot> LoadConfigAsync()
{
    using SemaphoreSyncContext ssc = await configSync.SyncContextAsync().DynamicContext();
    ConfigurationBuilder configBuilder = new();
    configBuilder.AddJsonFile(configFile, optional: false);
    IConfigurationRoot config = configBuilder.Build();
    DiscoveryConfig.Current = config.GetRequiredSection("DiscoveryConfig").Get<DiscoveryConfig>()
        ?? throw new InvalidDataException($"Failed to get a {typeof(DiscoveryConfig)} from the \"DiscoveryConfig\" section");
    DomainConfig.Registered = await DiscoveryConfig.Current.GetDiscoveryConfigAsync(config).DynamicContext();
    return config;
}
IConfigurationRoot config = await LoadConfigAsync().DynamicContext();

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
using MultiFileSystemEvents fsw = new();//TODO Throttle only the main service events
fsw.OnEvents += async (s, e) =>
{
    try
    {
        if (Logging.Debug)
            Logging.WriteDebug("Handling configuration change");
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
};
fsw.Add(new(ENV.AppFolder, "appsettings.json", NotifyFilters.LastWrite | NotifyFilters.CreationTime, throttle: 250, recursive: false));
if (DiscoveryConfig.Current.WatchEmailMappings && DiscoveryConfig.Current.EmailMappings is not null)
    fsw.Add(new(
        Path.GetDirectoryName(Path.GetFullPath(DiscoveryConfig.Current.EmailMappings))!,
        Path.GetFileName(DiscoveryConfig.Current.EmailMappings),
        NotifyFilters.LastWrite | NotifyFilters.CreationTime,
        throttle: 250,
        recursive: false,
        FileSystemEventTypes.Changes | FileSystemEventTypes.Created | FileSystemEventTypes.Deleted
        ));
if (DiscoveryConfig.Current.WatchFiles is not null)
    foreach (string file in DiscoveryConfig.Current.WatchFiles)
    {
        FileSystemEvents fse = new(
            Path.GetDirectoryName(Path.GetFullPath(file))!,
            Path.GetFileName(file),
            NotifyFilters.LastWrite | NotifyFilters.CreationTime,
            throttle: 250,
            recursive: false,
            FileSystemEventTypes.Changes | FileSystemEventTypes.Created | FileSystemEventTypes.Deleted
            );
        if (DiscoveryConfig.Current.PreReloadCommand is not null && DiscoveryConfig.Current.PreReloadCommand.Length > 0)
            fse.OnEvents += async (s, e) =>
            {
                try
                {
                    Logging.WriteInfo($"Executing pre-reload command on detected {file.ToQuotedLiteral()} change {string.Join('|', e.Arguments.Select(a => a.ChangeType.ToString()).Distinct())}");
                    using Process proc = new();
                    proc.StartInfo.FileName = DiscoveryConfig.Current.PreReloadCommand[0];
                    if (DiscoveryConfig.Current.PreReloadCommand.Length > 1)
                        proc.StartInfo.ArgumentList.AddRange(DiscoveryConfig.Current.PreReloadCommand[1..]);
                    proc.StartInfo.UseShellExecute = true;
                    proc.StartInfo.CreateNoWindow = true;
                    proc.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                    proc.Start();
                    await proc.WaitForExitAsync().DynamicContext();
                    if (proc.ExitCode != 0)
                        Logging.WriteWarning($"Pre-reload command exit code was #{proc.ExitCode}");
                }
                catch(Exception ex)
                {
                    Logging.WriteError($"Pre-reload command execution failed exceptional: {ex}");
                }
                finally
                {
                    if (Logging.Trace)
                        Logging.WriteTrace("Pre-reload command execution done");
                }
            };
        fsw.Add(fse);
    }

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
