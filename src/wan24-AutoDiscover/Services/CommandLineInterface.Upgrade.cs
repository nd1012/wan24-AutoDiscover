using Spectre.Console;
using System.ComponentModel;
using System.IO.Compression;
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
        /// Release information file name (contains the version number of the release)
        /// </summary>
        private const string RELEASE_INFORMATION_FILE_NAME = "latest-release.txt";
        /// <summary>
        /// Configuration file name (not overwritten during an upgrade)
        /// </summary>
        private const string CONFIGURATION_FILE_NAME = "appsettings.json";
        /// <summary>
        /// App DLL file name
        /// </summary>
        private const string APP_DLL_FILE_NAME = "wan24AutoDiscover.dll";

        /// <summary>
        /// Upgrade wan24-AutoDiscover online
        /// </summary>
        /// <param name="preCommand">Pre-command</param>
        /// <param name="postCommand">Post-command</param>
        /// <param name="noUserInteraction">No user interaction?</param>
        /// <param name="checkOnly">Check for update only</param>
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
            bool checkOnly = false
            )
        {
            using HttpClient http = new();
            // Get latest version information
            string version;
            {
                string uri = $"{REPOSITORY_URI_RAW_CONTENT}/{RELEASE_INFORMATION_FILE_NAME}";
                if (Logging.Trace)
                    Logging.WriteTrace($"Loading latest version information from \"{uri}\"");
                using HttpResponseMessage response = await http.GetAsync(uri, CancellationToken.None).DynamicContext();
                if (!response.IsSuccessStatusCode)
                {
                    Logging.WriteError($"Failed to download latest version information from \"{uri}\" - http status code is {response.StatusCode} (#{(int)response.StatusCode})");
                    return 1;
                }
                version = (await response.Content.ReadAsStringAsync(CancellationToken.None).DynamicContext()).Trim();
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
                AnsiConsole.MarkupLine($"[{CliApiInfo.HighlightColor} on {CliApiInfo.BackGroundColor}]You can read the release notes online: [link]{REPOSITORY_URI}[/][/]");
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
            bool deleteTempDir = true;// Temporary folder won't be deleted after the installation started, unless the upgrade succeeded (it contains the backup files)
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
                    string uri = GetReleaseUri(version),
                        fn = Path.Combine(tempDir, "update.zip");
                    Logging.WriteInfo($"Downloading update from \"{uri}\" to \"{fn}\"");
                    using HttpResponseMessage response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None).DynamicContext();
                    if (!response.IsSuccessStatusCode)
                    {
                        Logging.WriteError($"Failed to download latest version from {uri} - http status code is {response.StatusCode} (#{(int)response.StatusCode})");
                        return 1;
                    }
                    FileStream targetZip = FsHelper.CreateFileStream(fn, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, overwrite: false);
                    await using (targetZip.DynamicContext())
                    {
                        Stream updateZip = await response.Content.ReadAsStreamAsync(CancellationToken.None).DynamicContext();
                        await using (updateZip.DynamicContext())
                            await updateZip.CopyToAsync(targetZip, CancellationToken.None).DynamicContext();
                        Logging.WriteInfo($"Extracting \"{fn}\"");
                        targetZip.Position = 0;
                        using ZipArchive zip = new(targetZip, ZipArchiveMode.Read, leaveOpen: true);
                        await zip.ExtractToDirectoryAsync(tempDir, overwriteFiles: false, CancellationToken.None).DynamicContext();
                    }
                    if (Logging.Trace)
                        Logging.WriteTrace($"Deleting downloaded update");
                    File.Delete(fn);
                    fn = Path.Combine(tempDir, CONFIGURATION_FILE_NAME);
                    if (File.Exists(fn))
                    {
                        if (Logging.Trace)
                            Logging.WriteTrace($"Deleting {CONFIGURATION_FILE_NAME} from update");
                        File.Delete(fn);
                    }
                    fn = Path.Combine(tempDir, RELEASE_INFORMATION_FILE_NAME);
                    if (File.Exists(fn))
                    {
                        string release = await File.ReadAllTextAsync(fn, CancellationToken.None).DynamicContext();
                        if (!IsReleaseInformation(release, version))
                        {
                            Logging.WriteError($"Download release mismatch: {release.Trim().MaxLength(byte.MaxValue).ToQuotedLiteral()}/{version}");
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
                        cancellationToken: CancellationToken.None,
                        args: [.. preCommand.AsSpan(1)]
                        ).DynamicContext();
                    if (exitCode != 0)
                    {
                        Logging.WriteError($"Pre-update command failed to execute with exit code #{exitCode}");
                        return 1;
                    }
                }
                // Install the update (from here on the temporary folder is kept until the upgrade succeeded, because it contains the backups of
                // the replaced files)
                Logging.WriteInfo($"Installing the update to \"{ENV.AppFolder}\"");
                deleteTempDir = false;
                InstallFiles(tempDir, ENV.AppFolder);
                // Execute post-upgrade
                Logging.WriteInfo("Executing post-upgrade action");
                {
                    int exitCode = await ProcessHelper.GetExitCodeAsync(
                        "dotnet",
                        cancellationToken: CancellationToken.None,
                        args: [
                            Path.Combine(ENV.AppFolder, APP_DLL_FILE_NAME),
                            "autodiscover",
                            "post-upgrade",
                            VersionInfo.Current.ToString(),
                            version
                            ]
                        ).DynamicContext();
                    if (exitCode != 0)
                    {
                        Logging.WriteError($"Post-upgrade action failed to execute with exit code #{exitCode} (the backups of the replaced files are in \"{tempDir}\")");
                        return 1;
                    }
                }
                // Execute post-command
                if (postCommand is not null && postCommand.Length > 0)
                {
                    Logging.WriteInfo("Executing post-update command");
                    int exitCode = await ProcessHelper.GetExitCodeAsync(
                        postCommand[0],
                        cancellationToken: CancellationToken.None,
                        args: [.. postCommand.AsSpan(1)]
                        ).DynamicContext();
                    if (exitCode != 0)
                    {
                        Logging.WriteError($"Post-update command failed to execute with exit code #{exitCode} (the backups of the replaced files are in \"{tempDir}\")");
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
                    Logging.WriteError($"Update failed (won't delete temporary folder \"{tempDir}\" 'cause it contains the backup files): {ex}");
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
        /// <returns>Task (faults with an <see cref="InvalidProgramException"/>, if a version or the installed release information doesn't match)</returns>
        [CliApi("post-upgrade")]
        [DisplayText("Post-upgrade")]
        [Description("Perform post-upgrade actions (used internal)")]
        public static async Task PostUpgradeAsync(

            [CliApi(keyLessOffset: 0, Example = "1.0.0")]
            [DisplayText("Previous version")]
            [Description("Version number of the previous installation")]
            string previousVersion,

            // wan24-CLI counts the keyless offset from the keyless arguments which follow the previously parsed one, so 0 is the next argument
            [CliApi(keyLessOffset: 0, Example = "2.0.0")]
            [DisplayText("Current version")]
            [Description("Expected current version number after the update was installed")]
            string currentVersion
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
            if (!IsReleaseInformation(
                await File.ReadAllTextAsync(Path.Combine(ENV.AppFolder, RELEASE_INFORMATION_FILE_NAME), CancellationToken.None).DynamicContext(),
                currentVersion
                ))
                throw new InvalidProgramException("Release information mismatch");
        }

        /// <summary>
        /// Get the download URI of a release
        /// </summary>
        /// <param name="version">Version number (used as URI segment; the returned URI is passed to <see cref="HttpClient"/>, which takes a
        /// <see cref="string"/>)</param>
        /// <returns>URI of the release ZIP file</returns>
        internal static string GetReleaseUri(string version) => $"{REPOSITORY_URI}/releases/download/v{version}/wan24-AutoDiscover-{version}.zip";

        /// <summary>
        /// Determine if the content of a release information file names a version
        /// </summary>
        /// <param name="content">Release information file content (borrowed for the call; surrounding white space is ignored)</param>
        /// <param name="version">Version number (borrowed for the call)</param>
        /// <returns>If the release information names the version</returns>
        internal static bool IsReleaseInformation(ReadOnlySpan<char> content, ReadOnlySpan<char> version)
            => content.Trim().Equals(version, StringComparison.Ordinal);
    }
}
