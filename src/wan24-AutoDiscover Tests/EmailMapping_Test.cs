using wan24.AutoDiscover.Models;

namespace wan24.AutoDiscover.Tests
{
    /// <summary>
    /// <see cref="EmailMapping"/> tests
    /// </summary>
    [TestClass]
    public sealed class EmailMapping_Test
    {
        /// <summary>
        /// A local target is the login user
        /// </summary>
        [TestMethod, Timeout(5000)]
        public void GetLoginUser_LocalTarget()
            => Assert.AreEqual("user1", EmailMapping.GetLoginUser([Map("a@x", "user1")], "a@x"));

        /// <summary>
        /// The first local target wins over an earlier email address target
        /// </summary>
        [TestMethod, Timeout(5000)]
        public void GetLoginUser_LocalTargetPreferred()
            => Assert.AreEqual("user1", EmailMapping.GetLoginUser([Map("a@x", "b@x", "user1"), Map("b@x", "other")], "a@x"));

        /// <summary>
        /// The email address lookup ignores the case
        /// </summary>
        [TestMethod, Timeout(5000)]
        public void GetLoginUser_LookupIgnoresCase()
        {
            Assert.AreEqual("user1", EmailMapping.GetLoginUser([Map("a@x", "user1")], "A@X"));
            Assert.AreEqual("user1", EmailMapping.GetLoginUser([Map("A@X", "user1")], "a@x"));
        }

        /// <summary>
        /// An unknown email address has no login user
        /// </summary>
        [TestMethod, Timeout(5000)]
        public void GetLoginUser_UnknownEmail()
        {
            Assert.IsNull(EmailMapping.GetLoginUser([Map("a@x", "user1")], "other@x"));
            Assert.IsNull(EmailMapping.GetLoginUser([], "a@x"));
        }

        /// <summary>
        /// A chain of mappings is followed to the local login user
        /// </summary>
        [TestMethod, Timeout(5000)]
        public void GetLoginUser_Chain()
            => Assert.AreEqual(
                "user2",
                EmailMapping.GetLoginUser([Map("a@x", "b@x"), Map("b@x", "c@x"), Map("c@x", "user2")], "a@x")
                );

        /// <summary>
        /// The nearest local login user is found first (breadth-first)
        /// </summary>
        [TestMethod, Timeout(5000)]
        public void GetLoginUser_BreadthFirst()
            => Assert.AreEqual(
                "shallow",
                EmailMapping.GetLoginUser(
                    [Map("a@x", "b@x", "c@x"), Map("b@x", "d@x"), Map("d@x", "deep"), Map("c@x", "shallow")],
                    "a@x"
                    )
                );

        /// <summary>
        /// A mapping cycle ends without a login user
        /// </summary>
        [TestMethod, Timeout(5000)]
        public void GetLoginUser_Cycle()
        {
            Assert.IsNull(EmailMapping.GetLoginUser([Map("a@x", "b@x"), Map("b@x", "a@x")], "a@x"));
            Assert.IsNull(EmailMapping.GetLoginUser([Map("a@x", "a@x")], "a@x"));
        }

        /// <summary>
        /// A cycle doesn't hide a local login user which is reachable otherwise
        /// </summary>
        [TestMethod, Timeout(5000)]
        public void GetLoginUser_CycleWithExit()
            => Assert.AreEqual(
                "user3",
                EmailMapping.GetLoginUser([Map("a@x", "b@x"), Map("b@x", "a@x", "c@x"), Map("c@x", "user3")], "a@x")
                );

        /// <summary>
        /// An external target without a mapping has no login user
        /// </summary>
        [TestMethod, Timeout(5000)]
        public void GetLoginUser_ExternalTarget()
            => Assert.IsNull(EmailMapping.GetLoginUser([Map("a@x", "ext@other.org")], "a@x"));

        /// <summary>
        /// Create an email mapping
        /// </summary>
        /// <param name="email">Email address</param>
        /// <param name="targets">Targets</param>
        /// <returns>Mapping</returns>
        private static EmailMapping Map(string email, params ReadOnlySpan<string> targets) => new() { Email = email, Targets = [.. targets] };
    }
}
