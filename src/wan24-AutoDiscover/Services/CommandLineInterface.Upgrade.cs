using Spectre.Console;
using System.ComponentModel;
using System.IO.Compression;
using System.Text;
using wan24.CLI;
using wan24.Core;

namespace wan24.AutoDiscover.Services
{
    // Upgrade
    public sealed partial class CommandLineInterface
    {
        /// <summary>
        /// wan24-AutoDiscover repository URI
        /// </summary>
        private const string REPOSITORY_URI = "https://github.com/nd1012/wan24-AutoDiscover";
        /// <summary>
        /// wan24-AutoDiscover repository URI for raw content
        /// </summary>
        private const string REPOSITORY_URI_RAW_CONTENT = "https://raw.githubusercontent.com/nd1012/wan24-AutoDiscover/main";

        /// <summary>
        /// Upgrade wan24-AutoDiscover online
        /// </summary>
        /// <param name="preCommand">Pre-command</param>
        /// <param name="postCommand">Post-command</param>
        /// <param name="noUserInteraction">No user interaction?</param>
        /// <param name="checkOnly">Check for update only</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Exit code</returns>
        [CliApi("upgrade")]
        [DisplayText("wan24-AutoDiscover upgrade")]
        [Description("Find updates of wan24-AutoDiscover online and upgrade the installation")]
        [ExitCode(code: 2, "A newer version is available online (used when the \"-checkOnly\" flag was given)")]
        public static async Task<int> UpgradeAsync(

            [CliApi(Example = "/command/to/execute")]
            [DisplayText("Pre-command")]
            [Description("Command to execute before running the upgrade setup")]
            string[]? preCommand = null,

            [CliApi(Example = "/command/to/execute")]
            [DisplayText("Post-command")]
            [Description("Command to execute after running the upgrade setup")]
            string[]? postCommand = null,

            [CliApi]
            [DisplayText("No user interaction")]
            [Description("Add this flag to disable user interaction and process automatic")]
            bool noUserInteraction = false,

            [CliApi]
            [DisplayText("Check only")]
            [Description("Exit with code #2, if there's a newer version available")]
            bool checkOnly = false,

            CancellationToken cancellationToken = default
            )
        {
            using HttpClient http = new();
            // Get latest version information
            string version;
            {
                string uri = $"{REPOSITORY_URI_RAW_CONTENT}/latest-release.txt";
                if (Logging.Trace)
                    Logging.WriteTrace($"Loading latest version information from \"{uri}\"");
                using HttpRequestMessage request = new(HttpMethod.Get, uri);
                using HttpResponseMessage response = await http.SendAsync(request, cancellationToken).DynamicContext();
                if (!response.IsSuccessStatusCode)
                {
                    Logging.WriteError($"Failed to download latest version information from \"{uri}\" - http status code is {response.StatusCode} (#{(int)response.StatusCode})");
                    return 1;
                }
                Stream versionInfo = response.Content.ReadAsStream(cancellationToken);
                await using (versionInfo.DynamicContext())
                {
                    using StreamReader reader = new(versionInfo, Encoding.UTF8, leaveOpen: true);
                    version = (await reader.ReadToEndAsync(cancellationToken).DynamicContext()).Trim();
                }
            }
            // Check if an upgrade is possible
            if (!Version.TryParse(version, out Version? latest))
            {
                Logging.WriteError("Failed to parse received online version information");
                return 1;
            }
            Logging.WriteInfo($"Current version is {VersionInfo.Current}, online version is {latest}");
            if (VersionInfo.Current >= latest)
            {
                Logging.WriteInfo("No update found - exit");
                return 0;
            }
            if (checkOnly)
            {
                Console.WriteLine(latest.ToString());
                return 2;
            }
            // Confirm upgrade
            if (!noUserInteraction)
            {
                AnsiConsole.WriteLine($"[{CliApiInfo.HighlightColor} on {CliApiInfo.BackGroundColor}]You can read the release notes online: [link]{REPOSITORY_URI}[/][/]");
                string confirmation = AnsiConsole.Prompt(
                    new TextPrompt<string>($"[{CliApiInfo.HighlightColor} on {CliApiInfo.BackGroundColor}]Perform the upgrade to version \"{version}\" now?[/] (type [{CliApiInfo.HighlightColor} on {CliApiInfo.BackGroundColor}]\"yes\"[/] or [{CliApiInfo.HighlightColor} on {CliApiInfo.BackGroundColor}]\"no\"[/] and hit enter - default is \"yes\")")
                        .AllowEmpty()
                        );
                if (confirmation.Length != 0 && !confirmation.Equals("yes", StringComparison.OrdinalIgnoreCase))
                {
                    Logging.WriteInfo("Upgrade cancelled by user");
                    return 0;
                }
            }
            // Perform the upgrade
            bool deleteTempDir = true;// Temporary folder won't be deleted, if there was a problem during upgrade, and files have been modified already
            string tempDir = Path.Combine(Settings.TempFolder, Guid.NewGuid().ToString());
            while (Directory.Exists(tempDir))
                tempDir = Path.Combine(Settings.TempFolder, Guid.NewGuid().ToString());
            try
            {
                if (Logging.Trace)
                    Logging.WriteTrace($"Using temporary folder \"{tempDir}\"");
                FsHelper.CreateFolder(tempDir);
                // Download and extract the setup
                {
                    string uri = $"{REPOSITORY_URI}/releases/download/v{version}/wan24-AutoDiscover.v{version}.zip",
                        fn = Path.Combine(tempDir, "update.zip");
                    Logging.WriteInfo($"Downloading update from \"{uri}\" to \"{fn}\"");
                    using HttpRequestMessage request = new(HttpMethod.Get, uri);
                    using HttpResponseMessage response = await http.SendAsync(request, cancellationToken).DynamicContext();
                    if (!response.IsSuccessStatusCode)
                    {
                        Logging.WriteError($"Failed to download latest version from {uri} - http status code is {response.StatusCode} (#{(int)response.StatusCode})");
                        return 1;
                    }
                    Stream? targetZip = null;
                    Stream updateZip = response.Content.ReadAsStream(cancellationToken);
                    await using (updateZip.DynamicContext())
                        try
                        {
                            targetZip = FsHelper.CreateFileStream(fn, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, overwrite: false);
                            await updateZip.CopyToAsync(targetZip, cancellationToken).DynamicContext();
                        }
                        catch
                        {
                            if (targetZip is not null) await targetZip.DisposeAsync().DynamicContext();
                            throw;
                        }
                    await using (targetZip.DynamicContext())
                    {
                        Logging.WriteInfo($"Extracting \"{fn}\"");
                        targetZip.Position = 0;
                        using ZipArchive zip = new(targetZip, ZipArchiveMode.Read, leaveOpen: true);
                        zip.ExtractToDirectory(tempDir, overwriteFiles: false);
                    }
                    if (Logging.Trace)
                        Logging.WriteTrace($"Deleting downloaded update");
                    File.Delete(fn);
                    fn = Path.Combine(tempDir, "appsettings.json");
                    if (File.Exists(fn))
                    {
                        if (Logging.Trace)
                            Logging.WriteTrace($"Deleting appsettings.json from update");
                        File.Delete(fn);
                    }
                    fn = Path.Combine(tempDir, "latest-release.txt");
                    if (File.Exists(fn))
                    {
                        string release = (await File.ReadAllTextAsync(fn, cancellationToken).DynamicContext()).Trim();
                        if (release != version)
                        {
                            Logging.WriteError($"Download release mismatch: {release.MaxLength(byte.MaxValue).ToQuotedLiteral()}/{version}");
                            return 1;
                        }
                        else if (Logging.Trace)
                        {
                            Logging.WriteTrace("Update release confirmed");
                        }
                    }
                    else
                    {
                        Logging.WriteError("Missing release information in update");
                        return 1;
                    }
                }
                // Execute pre-command
                if (preCommand is not null && preCommand.Length > 0)
                {
                    Logging.WriteInfo("Executing pre-update command");
                    int exitCode = await ProcessHelper.GetExitCodeAsync(
                        preCommand[0],
                        cancellationToken: cancellationToken,
                        args: [.. preCommand[1..]]
                        ).DynamicContext();
                    if (exitCode != 0)
                    {
                        Logging.WriteError($"Pre-update command failed to execute with exit code #{exitCode}");
                        return 1;
                    }
                }
                // Install the update
                Logging.WriteInfo("Installing the update");
                HashSet<string> backupFiles = [];
                Transaction transaction = new();
                await using (transaction.DynamicContext())
                {
                    // Rollback which restores backed up files
                    await transaction.ExecuteAsync(
                        () => Task.CompletedTask,
                        async (ta, ret, ct) =>
                        {
                            Logging.WriteWarning("Restoring backup up files during rollback of a failed transaction");
                            foreach (string file in backupFiles)
                            {
                                if (ct.IsCancellationRequested)
                                {
                                    Logging.WriteWarning("Rollback action cancellation has been requested - stop restoring backed up files");
                                    return;
                                }
                                Logging.WriteWarning($"Restoring backup \"{file}\"");
                                if (!File.Exists(file))
                                {
                                    Logging.WriteWarning("Backup file not found - possibly because there was a problem when creating the backup of the file which was going to be overwritten");
                                    continue;
                                }
                                Stream source = FsHelper.CreateFileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                                await using (source.DynamicContext())
                                {
                                    string targetFn = Path.Combine(ENV.AppFolder, file[tempDir.Length..]);
                                    Stream target = FsHelper.CreateFileStream(targetFn, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, overwrite: true);
                                    await using (target.DynamicContext())
                                        await source.CopyToAsync(target, CancellationToken.None).DynamicContext();
                                }
                            }
                            Logging.WriteInfo("All backup files have been restored during rollback of a failed transaction");
                        },
                        CancellationToken.None
                        ).DynamicContext();
                    // Copy new files
                    foreach (string file in FsHelper.FindFiles(tempDir))
                    {
                        string targetFn = Path.Combine(ENV.AppFolder, file[tempDir.Length..]),
                            targetDir = Path.GetDirectoryName(targetFn)!;
                        if (Logging.Trace)
                            Logging.WriteTrace($"Copy file \"{file}\" to \"{targetFn}\"");
                        Stream source = FsHelper.CreateFileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                        await using (source.DynamicContext())
                        {
                            // Ensure an existing target folder
                            if (!Directory.Exists(targetDir))
                                transaction.Execute(
                                    () => FsHelper.CreateFolder(targetDir),
                                    (ta, ret) =>
                                    {
                                        if (Directory.Exists(targetDir))
                                            try
                                            {
                                                Logging.WriteWarning($"Deleting previously created folder \"{targetDir}\" during rollback of a failed transaction");
                                                Directory.Delete(targetDir, recursive: true);
                                            }
                                            catch (Exception ex)
                                            {
                                                Logging.WriteWarning($"Failed to delete previously created folder \"{targetDir}\" during rollback: {ex}");
                                            }
                                    }
                                    );
                            // Open/create the target file
                            bool exists = File.Exists(targetFn);
                            Stream target = FsHelper.CreateFileStream(
                                targetFn,
                                FileMode.OpenOrCreate,
                                FileAccess.ReadWrite,
                                FileShare.None,
                                overwrite: false
                                );
                            await using (target.DynamicContext())
                            {
                                // Create a backup of the existing file, first
                                if (exists)
                                {
                                    deleteTempDir = false;
                                    string backupFn = $"{file}.backup";
                                    if (Logging.Trace)
                                        Logging.WriteTrace($"Create backup of existing file \"{targetFn}\" to \"{backupFn}\"");
                                    backupFiles.Add(targetFn);
                                    Stream backup = FsHelper.CreateFileStream(backupFn, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
                                    await using (backup.DynamicContext())
                                    {
                                        target.Position = 0;
                                        await target.CopyToAsync(backup, cancellationToken).DynamicContext();
                                    }
                                    target.SetLength(0);
                                }
                                // Copy the (new) file contents
                                await source.CopyToAsync(target, cancellationToken).DynamicContext();
                            }
                        }
                    }
                    // Signal success to the encapsulating transaction
                    transaction.Commit();
                }
                // Execute post-upgrade
                Logging.WriteInfo("Executing post-update command");
                {
                    int exitCode = await ProcessHelper.GetExitCodeAsync(
                        "dotnet",
                        cancellationToken: cancellationToken,
                        args: [
                            "wan24AutoDiscover.dll",
                            "autodiscover",
                            "post-upgrade",
                            VersionInfo.Current.ToString(),
                            version
                            ]
                        ).DynamicContext();
                    if (exitCode != 0)
                    {
                        Logging.WriteError($"Post-upgrade acion failed to execute with exit code #{exitCode}");
                        return 1;
                    }
                }
                // Execute post-command
                if (postCommand is not null && postCommand.Length > 0)
                {
                    Logging.WriteInfo("Executing post-update command");
                    int exitCode = await ProcessHelper.GetExitCodeAsync(
                        postCommand[0],
                        cancellationToken: cancellationToken,
                        args: [.. postCommand[1..]]
                        ).DynamicContext();
                    if (exitCode != 0)
                    {
                        Logging.WriteError($"Post-update command failed to execute with exit code #{exitCode}");
                        return 1;
                    }
                }
                Logging.WriteInfo("wan24-AutoDiscover upgrade done");
                deleteTempDir = true;
                return 0;
            }
            catch (Exception ex)
            {
                if (deleteTempDir)
                {
                    Logging.WriteError($"Update failed (temporary folder will be removed): {ex}");
                }
                else
                {
                    Logging.WriteError($"Update failed (won't delete temporary folder \"{tempDir}\" 'cause it may contain backup files): {ex}");
                }
                return 1;
            }
            finally
            {
                if (deleteTempDir && Directory.Exists(tempDir))
                    try
                    {
                        Directory.Delete(tempDir, recursive: true);
                    }
                    catch (Exception ex)
                    {
                        Logging.WriteError($"Failed to delete temporary folder \"{tempDir}\": {ex}");
                    }
            }
        }

        /// <summary>
        /// Post-upgrade actions
        /// </summary>
        /// <param name="previousVersion">Previous version</param>
        /// <param name="currentVersion">Current version</param>
        /// <param name="cancellationToken">Cancellation token</param>
        [CliApi("post-upgrade")]
        [DisplayText("Post-upgrade")]
        [Description("Perform post-upgrade actions (used internal)")]
        public static async Task PostUpgradeAsync(

            [CliApi(keyLessOffset: 0, Example = "1.0.0")]
            [DisplayText("Previous version")]
            [Description("Version number of the previous installation")]
            string previousVersion,

            [CliApi(keyLessOffset: 1, Example = "2.0.0")]
            [DisplayText("Current version")]
            [Description("Expected current version number after the update was installed")]
            string currentVersion,

            CancellationToken cancellationToken = default
            )
        {
            Version previous = new(previousVersion),
                current = new(currentVersion);
            // Validate previous version number
            if (previous >= current)
                throw new InvalidProgramException("Invalid previous version number");
            // Validate current version number
            if (current != VersionInfo.Current)
                throw new InvalidProgramException($"Current version is {VersionInfo.Current} - upgrade version was {currentVersion.ToQuotedLiteral()}");
            // Validate release information
            if (currentVersion != await File.ReadAllTextAsync(Path.Combine(ENV.AppFolder, "latest-release.txt"), cancellationToken).DynamicContext())
                throw new InvalidProgramException("Release information mismatch");
        }
    }
}
