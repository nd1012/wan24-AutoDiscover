using wan24.AutoDiscover.Services;
using wan24.CLI;
using wan24.Core;

namespace wan24.AutoDiscover.Tests
{
    /// <summary>
    /// <see cref="CommandLineInterface"/> tests
    /// </summary>
    [TestClass]
    public sealed class CommandLineInterface_Test
    {
        /// <summary>
        /// Arguments of the <c>systemd</c> CLI command
        /// </summary>
        private static readonly string[] SystemdArguments = ["autodiscover", "systemd"];
        /// <summary>
        /// Arguments of the <c>version</c> CLI command of the about API
        /// </summary>
        private static readonly string[] VersionArguments = ["about", "version"];

        /// <summary>
        /// Temporary root folder of the test
        /// </summary>
        private string Root = string.Empty;
        /// <summary>
        /// Source folder
        /// </summary>
        private string Source = string.Empty;
        /// <summary>
        /// Target folder
        /// </summary>
        private string Target = string.Empty;

        /// <summary>
        /// Initialize wan24-Core like the app does before it runs a CLI command
        /// </summary>
        /// <param name="context">Test context</param>
        /// <returns>Task</returns>
        [ClassInitialize]
        public static async Task ClassInitializeAsync(TestContext context)
        {
            await Bootstrap.Async();
            Translation.Current = Translation.Dummy;
        }

        /// <summary>
        /// Create the temporary folders
        /// </summary>
        [TestInitialize]
        public void Initialize()
        {
            Root = Path.Combine(Path.GetTempPath(), $"wan24AutoDiscoverTests-{Guid.NewGuid():N}");
            Source = Path.Combine(Root, "source");
            Target = Path.Combine(Root, "target");
            Directory.CreateDirectory(Source);
            Directory.CreateDirectory(Target);
        }

        /// <summary>
        /// Delete the temporary folders
        /// </summary>
        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }

        /// <summary>
        /// The release download URI contains the version
        /// </summary>
        /// <param name="version">Version</param>
        /// <param name="expected">Expected URI</param>
        [TestMethod, Timeout(5000)]
        [DataRow("2.0.0", "https://github.com/nd1012/wan24-AutoDiscover/releases/download/v2.0.0/wan24-AutoDiscover-2.0.0.zip")]
        [DataRow("2.1.3", "https://github.com/nd1012/wan24-AutoDiscover/releases/download/v2.1.3/wan24-AutoDiscover-2.1.3.zip")]
        public void GetReleaseUri_ContainsVersion(string version, string expected)
            => Assert.AreEqual(expected, CommandLineInterface.GetReleaseUri(version));

        /// <summary>
        /// Release information matches only the exact (white space trimmed) version
        /// </summary>
        /// <param name="content">Release information content</param>
        /// <param name="version">Version</param>
        /// <param name="expected">Expected result</param>
        [TestMethod, Timeout(5000)]
        [DataRow("2.0.0", "2.0.0", true)]
        [DataRow("2.0.0\n", "2.0.0", true)]
        [DataRow(" 2.0.0 ", "2.0.0", true)]
        [DataRow("\r\n2.0.0\r\n", "2.0.0", true)]
        [DataRow("2.0.1", "2.0.0", false)]
        [DataRow("", "2.0.0", false)]
        [DataRow("2.0.0.1", "2.0.0", false)]
        [DataRow("2.0", "2.0.0", false)]
        [DataRow("2.0.0 2.0.1", "2.0.0", false)]
        public void IsReleaseInformation_TrimmedExactMatch(string content, string version, bool expected)
            => Assert.AreEqual(expected, CommandLineInterface.IsReleaseInformation(content, version));

        /// <summary>
        /// All files are copied to an empty target folder and missing folders are created
        /// </summary>
        [TestMethod, Timeout(5000)]
        public void InstallFiles_EmptyTarget()
        {
            WriteFile(Source, "a.dll", "A");
            WriteFile(Source, Path.Combine("sub", "b.dll"), "B");
            CommandLineInterface.InstallFiles(Source, Target);
            Assert.AreEqual("A", File.ReadAllText(Path.Combine(Target, "a.dll")));
            Assert.IsTrue(Directory.Exists(Path.Combine(Target, "sub")));
            Assert.AreEqual("B", File.ReadAllText(Path.Combine(Target, "sub", "b.dll")));
            // Nothing is written outside of the target folder
            Assert.AreEqual(2, Directory.GetFileSystemEntries(Root).Length);
            Assert.AreEqual(2, Directory.GetFileSystemEntries(Target).Length);
            Assert.AreEqual(1, Directory.GetFileSystemEntries(Path.Combine(Target, "sub")).Length);
            // Without an overwritten file there is no backup
            Assert.IsFalse(File.Exists(Path.Combine(Source, $"a.dll{CommandLineInterface.BACKUP_EXTENSION}")));
            Assert.IsFalse(File.Exists(Path.Combine(Source, "sub", $"b.dll{CommandLineInterface.BACKUP_EXTENSION}")));
            Assert.AreEqual(2, Directory.GetFileSystemEntries(Source).Length);
        }

        /// <summary>
        /// An existing target file is replaced and its old content is backed up next to the source file
        /// </summary>
        [TestMethod, Timeout(5000)]
        public void InstallFiles_ReplaceWithBackup()
        {
            WriteFile(Source, "a.dll", "new");
            WriteFile(Target, "a.dll", "old");
            CommandLineInterface.InstallFiles(Source, Target);
            Assert.AreEqual("new", File.ReadAllText(Path.Combine(Target, "a.dll")));
            Assert.AreEqual("old", File.ReadAllText(Path.Combine(Source, $"a.dll{CommandLineInterface.BACKUP_EXTENSION}")));
        }

        /// <summary>
        /// A failure rolls back all changes and rethrows the failure
        /// </summary>
        [TestMethod, Timeout(5000)]
        public void InstallFiles_FailureRollsBack()
        {
            WriteFile(Source, "a.dll", "new-a");
            WriteFile(Source, Path.Combine("sub", "b.dll"), "new-b");
            WriteFile(Source, "z.dll", "new-z");
            WriteFile(Target, "a.dll", "old-a");
            string blocker = Path.Combine(Target, "z.dll");
            Directory.CreateDirectory(blocker);// A directory can't be replaced by a file
            Exception ex = Assert.Throws<Exception>(() => CommandLineInterface.InstallFiles(Source, Target));
            Assert.IsTrue(ex is IOException or UnauthorizedAccessException, $"Unexpected exception type {ex.GetType()}");
            Assert.AreEqual("old-a", File.ReadAllText(Path.Combine(Target, "a.dll")));
            Assert.IsFalse(File.Exists(Path.Combine(Target, "sub", "b.dll")));
            Assert.IsFalse(Directory.Exists(Path.Combine(Target, "sub")));
            Assert.IsTrue(Directory.Exists(blocker));
            Assert.AreEqual(2, Directory.GetFileSystemEntries(Target).Length);
        }

        /// <summary>
        /// The post-upgrade action accepts an upgrade from an older version to the installed version
        /// </summary>
        /// <returns>Task</returns>
        [TestMethod, Timeout(5000)]
        public async Task PostUpgradeAsync_InstalledVersion()
            => await CommandLineInterface.PostUpgradeAsync("0.1.0", VersionInfo.Current.ToString());

        /// <summary>
        /// The post-upgrade action rejects a previous version which isn't older than the installed version
        /// </summary>
        /// <returns>Task</returns>
        [TestMethod, Timeout(5000)]
        public async Task PostUpgradeAsync_InvalidPreviousVersion()
        {
            string current = VersionInfo.Current.ToString();
            await Assert.ThrowsAsync<InvalidProgramException>(() => CommandLineInterface.PostUpgradeAsync(current, current));
            await Assert.ThrowsAsync<InvalidProgramException>(
                () => CommandLineInterface.PostUpgradeAsync(new Version(VersionInfo.Current.Major + 1, 0, 0).ToString(), current)
                );
        }

        /// <summary>
        /// The post-upgrade action rejects an upgrade version which isn't the installed version
        /// </summary>
        /// <returns>Task</returns>
        [TestMethod, Timeout(5000)]
        public async Task PostUpgradeAsync_OtherVersion()
            => await Assert.ThrowsAsync<InvalidProgramException>(
                () => CommandLineInterface.PostUpgradeAsync("0.1.0", new Version(VersionInfo.Current.Major, VersionInfo.Current.Minor + 1, 0).ToString())
                );

        /// <summary>
        /// The <c>post-upgrade</c> CLI command runs through the CLI dispatcher and fails for an invalid previous version
        /// </summary>
        /// <returns>Task</returns>
        [TestMethod, Timeout(30000)]
        public async Task Cli_PostUpgrade()
        {
            string current = VersionInfo.Current.ToString();
            Assert.AreEqual(0, await RunCliAsync(["autodiscover", "post-upgrade", "0.1.0", current]));
            Assert.AreNotEqual(0, await RunCliAsync(["autodiscover", "post-upgrade", current, current]));
        }

        /// <summary>
        /// The <c>systemd</c> CLI command runs through the CLI dispatcher
        /// </summary>
        /// <returns>Task</returns>
        [TestMethod, Timeout(30000)]
        public async Task Cli_Systemd()
            => Assert.AreEqual(0, await RunCliAsync(SystemdArguments));

        /// <summary>
        /// The <c>about version</c> CLI command runs through the CLI dispatcher
        /// </summary>
        /// <returns>Task</returns>
        [TestMethod, Timeout(30000)]
        public async Task Cli_AboutVersion()
            => Assert.AreEqual(0, await RunCliAsync(VersionArguments));

        /// <summary>
        /// Run a CLI command with the APIs which the app exports
        /// </summary>
        /// <param name="args">Arguments</param>
        /// <returns>Exit code</returns>
        private static Task<int> RunCliAsync(string[] args)
            => CliApi.RunAsync(args, CancellationToken.None, typeof(CliHelpApi), typeof(CommandLineInterface), typeof(AboutApi));

        /// <summary>
        /// Write a file
        /// </summary>
        /// <param name="folder">Base folder</param>
        /// <param name="relativePath">Relative file path</param>
        /// <param name="content">Content</param>
        private static void WriteFile(string folder, string relativePath, string content)
        {
            string path = Path.Combine(folder, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }
    }
}
