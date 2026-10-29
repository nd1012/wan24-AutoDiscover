namespace wan24.AutoDiscover.Tests
{
    /// <summary>
    /// <see cref="VersionInfo"/> tests
    /// </summary>
    [TestClass]
    public sealed class VersionInfo_Test
    {
        /// <summary>
        /// Name of the release information file which is copied to the output folder
        /// </summary>
        private const string RELEASE_FILE_NAME = "latest-release.txt";

        /// <summary>
        /// The current version has the three components of a release tag (major.minor.patch)
        /// </summary>
        [TestMethod, Timeout(5000)]
        public void Current_HasReleaseFormat()
        {
            Assert.IsTrue(VersionInfo.Current.Build >= 0, "Missing patch component");
            Assert.AreEqual(-1, VersionInfo.Current.Revision);
        }

        /// <summary>
        /// The current version equals the trimmed content of the release information file
        /// </summary>
        /// <returns>Task</returns>
        [TestMethod, Timeout(5000)]
        public async Task Current_Equals_ReleaseFile()
        {
            string content = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, RELEASE_FILE_NAME));
            Assert.AreEqual(VersionInfo.Current.ToString(), content.Trim());
        }
    }
}
