using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using wan24.AutoDiscover.Controllers;
using wan24.AutoDiscover.Models;
using wan24.Core;

namespace wan24.AutoDiscover.Tests
{
    /// <summary>
    /// <see cref="DiscoveryController"/> tests (through the MVC pipeline of a test server)
    /// </summary>
    [TestClass]
    public sealed class DiscoveryController_Test
    {
        /// <summary>
        /// Request URI
        /// </summary>
        private const string REQUEST_URI = "/autodiscover/autodiscover.xml";
        /// <summary>
        /// Request XML start (with a namespace)
        /// </summary>
        private const string REQUEST_XML_START = "<Autodiscover xmlns=\"http://schemas.microsoft.com/exchange/autodiscover/outlook/requestschema/2006\">"
            + "<Request><EMailAddress>";
        /// <summary>
        /// Request XML end
        /// </summary>
        private const string REQUEST_XML_END = "</EMailAddress>"
            + "<AcceptableResponseSchema>http://schemas.microsoft.com/exchange/autodiscover/outlook/responseschema/2006a</AcceptableResponseSchema>"
            + "</Request></Autodiscover>";

        /// <summary>
        /// Test application
        /// </summary>
        private static WebApplication? App;
        /// <summary>
        /// Test client
        /// </summary>
        private static HttpClient? Client;

        /// <summary>
        /// Registered domain configurations before the test
        /// </summary>
        private IReadOnlyDictionary<string, DomainConfig>? PreviousRegistered;

        /// <summary>
        /// Start the test application
        /// </summary>
        /// <param name="context">Test context</param>
        /// <returns>Task</returns>
        [ClassInitialize]
        public static async Task ClassInitializeAsync(TestContext context)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddControllers().AddApplicationPart(typeof(DiscoveryController).Assembly);
            builder.Services.AddSingleton(new InstancePool<XmlResponse>(capacity: 2));
            builder.Services.AddHostedService(services => services.GetRequiredService<InstancePool<XmlResponse>>());
            WebApplication app = builder.Build();
            app.MapControllers();
            await app.StartAsync();
            App = app;
            Client = app.GetTestClient();
        }

        /// <summary>
        /// Stop the test application
        /// </summary>
        /// <returns>Task</returns>
        [ClassCleanup]
        public static async Task ClassCleanupAsync()
        {
            Client?.Dispose();
            if (App is not null)
                await App.DisposeAsync();
        }

        /// <summary>
        /// Register the test domain
        /// </summary>
        [TestInitialize]
        public void Initialize()
        {
            PreviousRegistered = DomainConfig.Registered;
            DomainConfig.Registered = new Dictionary<string, DomainConfig>()
            {
                {
                    "example.org",
                    new DomainConfig()
                    {
                        Protocols = [new Protocol() { Type = "IMAP", Server = "imap.example.org", Port = 993 }]
                    }
                }
            };
        }

        /// <summary>
        /// Restore the registered domain configurations
        /// </summary>
        [TestCleanup]
        public void Cleanup() => DomainConfig.Registered = PreviousRegistered!;

        /// <summary>
        /// The namespace constants hold the Microsoft schema URIs
        /// </summary>
        [TestMethod, Timeout(5000)]
        public void Constants_Namespaces()
        {
            Assert.AreEqual("http://schemas.microsoft.com/exchange/autodiscover/responseschema/2006", Constants.AUTO_DISCOVER_NS);
            Assert.AreEqual("http://schemas.microsoft.com/exchange/autodiscover/outlook/responseschema/2006a", Constants.RESPONSE_NS);
        }

        /// <summary>
        /// A request is processed for every content type (also without a content type)
        /// </summary>
        /// <param name="contentType">Request content type (empty for none)</param>
        /// <returns>Task</returns>
        [TestMethod, Timeout(15000)]
        [DataRow("")]
        [DataRow("application/xml")]
        [DataRow("text/xml")]
        [DataRow("text/xml; charset=utf-8")]
        [DataRow("text/plain")]
        [DataRow("application/octet-stream")]
        public async Task AutoDiscover_AnyContentType(string contentType)
        {
            using HttpResponseMessage response = await PostAsync(CreateRequestXml("alias@example.org"), contentType);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual("application/xml", response.Content.Headers.ContentType?.MediaType);
            XDocument xml = await ReadXmlAsync(response);
            XNamespace autoDiscoverNs = Constants.AUTO_DISCOVER_NS,
                responseNs = Constants.RESPONSE_NS;
            Assert.IsNotNull(xml.Root);
            Assert.AreEqual(autoDiscoverNs + "Autodiscover", xml.Root.Name);
            XElement responseElement = xml.Root.Element(responseNs + "Response")!;
            Assert.IsNotNull(responseElement);
            XElement account = responseElement.Element(responseNs + "Account")!;
            Assert.IsNotNull(account);
            Assert.AreEqual("email", account.Element(responseNs + "AccountType")?.Value);
            Assert.AreEqual("settings", account.Element(responseNs + "Action")?.Value);
            XElement protocol = account.Element(responseNs + "Protocol")!;
            Assert.IsNotNull(protocol);
            Assert.AreEqual("IMAP", protocol.Element(responseNs + "Type")?.Value);
            Assert.AreEqual("imap.example.org", protocol.Element(responseNs + "Server")?.Value);
            Assert.AreEqual("993", protocol.Element(responseNs + "Port")?.Value);
            Assert.AreEqual("alias", protocol.Element(responseNs + "LoginName")?.Value);
            Assert.AreEqual("off", protocol.Element(responseNs + "SPA")?.Value);
            Assert.AreEqual("on", protocol.Element(responseNs + "SSL")?.Value);
            Assert.AreEqual("on", protocol.Element(responseNs + "AuthRequired")?.Value);
        }

        /// <summary>
        /// The node name of the email address is matched case insensitive and without a namespace
        /// </summary>
        /// <returns>Task</returns>
        [TestMethod, Timeout(15000)]
        public async Task AutoDiscover_EmailNodeNameIgnoresCase()
        {
            using HttpResponseMessage response = await PostAsync(
                "<Autodiscover><Request><emailaddress>alias@example.org</emailaddress></Request></Autodiscover>",
                "text/xml"
                );
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            XNamespace responseNs = Constants.RESPONSE_NS;
            XDocument xml = await ReadXmlAsync(response);
            Assert.AreEqual("alias", xml.Descendants(responseNs + "LoginName").Single().Value);
        }

        /// <summary>
        /// Invalid request XML is rejected
        /// </summary>
        /// <returns>Task</returns>
        [TestMethod, Timeout(15000)]
        public async Task AutoDiscover_InvalidXml()
        {
            using HttpResponseMessage response = await PostAsync("<Autodiscover><Request></Autodiscover>", "text/xml");
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.AreEqual("Invalid Request XML", await response.Content.ReadAsStringAsync());
        }

        /// <summary>
        /// A request without an email address is rejected
        /// </summary>
        /// <returns>Task</returns>
        [TestMethod, Timeout(15000)]
        public async Task AutoDiscover_MissingEmailAddress()
        {
            using HttpResponseMessage response = await PostAsync("<Autodiscover><Request></Request></Autodiscover>", "text/xml");
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.AreEqual("Missing Email Address", await response.Content.ReadAsStringAsync());
        }

        /// <summary>
        /// An invalid email address is rejected
        /// </summary>
        /// <param name="emailAddress">Email address</param>
        /// <returns>Task</returns>
        [TestMethod, Timeout(15000)]
        [DataRow("no-email-address")]
        [DataRow("alias@")]
        public async Task AutoDiscover_InvalidEmailAddress(string emailAddress)
        {
            using HttpResponseMessage response = await PostAsync(CreateRequestXml(emailAddress), "text/xml");
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.AreEqual("Invalid Email Address", await response.Content.ReadAsStringAsync());
        }

        /// <summary>
        /// An email address of an unknown domain is rejected
        /// </summary>
        /// <returns>Task</returns>
        [TestMethod, Timeout(15000)]
        public async Task AutoDiscover_UnknownDomain()
        {
            using HttpResponseMessage response = await PostAsync(CreateRequestXml("alias@unknown.example.com"), "text/xml");
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.AreEqual("Unknown Domain Name", await response.Content.ReadAsStringAsync());
        }

        /// <summary>
        /// A formatted request of about 2 KiB is accepted (an email address may have up to 254 characters)
        /// </summary>
        /// <returns>Task</returns>
        [TestMethod, Timeout(15000)]
        public async Task AutoDiscover_LargeRequest()
        {
            string body = $"<?xml version=\"1.0\" encoding=\"utf-8\"?>{new string(' ', 2000)}{CreateRequestXml("alias@example.org")}";
            using HttpResponseMessage response = await PostAsync(body, "text/xml");
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        }

        /// <summary>
        /// A failure while writing the response XML ends the request (an invalid character in the configuration makes the XML writer fail)
        /// </summary>
        /// <returns>Task</returns>
        [TestMethod, Timeout(15000)]
        public async Task AutoDiscover_ResponseXmlFailureEndsRequest()
        {
            DomainConfig.Registered = new Dictionary<string, DomainConfig>()
            {
                {
                    "example.org",
                    new DomainConfig()
                    {
                        Protocols = [new Protocol() { Type = "IMAP", Server = "imap\u0001.example.org", Port = 993 }]
                    }
                }
            };
            Task<HttpResponseMessage> request = PostAsync(CreateRequestXml("alias@example.org"), "text/xml");
            Task completed = await Task.WhenAny(request, Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.AreSame(request, completed, "The request didn't end");
            try
            {
                using HttpResponseMessage response = await request;
                await response.Content.ReadAsByteArrayAsync();
            }
            catch (Exception)
            {
                // The test server aborts the started response or forwards the exception of the application
            }
        }

        /// <summary>
        /// A GET request isn't allowed
        /// </summary>
        /// <returns>Task</returns>
        [TestMethod, Timeout(15000)]
        public async Task AutoDiscover_GetNotAllowed()
        {
            using HttpResponseMessage response = await Client!.GetAsync(REQUEST_URI);
            Assert.AreEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        }

        /// <summary>
        /// Create a request XML
        /// </summary>
        /// <param name="emailAddress">Email address</param>
        /// <returns>Request XML</returns>
        private static string CreateRequestXml(string emailAddress) => $"{REQUEST_XML_START}{emailAddress}{REQUEST_XML_END}";

        /// <summary>
        /// Post a request
        /// </summary>
        /// <param name="body">Request body</param>
        /// <param name="contentType">Request content type (empty for none)</param>
        /// <returns>Response</returns>
        private static async Task<HttpResponseMessage> PostAsync(string body, string contentType)
        {
            using ByteArrayContent content = new(Encoding.UTF8.GetBytes(body));
            content.Headers.ContentType = contentType.Length == 0
                ? null
                : MediaTypeHeaderValue.Parse(contentType);
            return await Client!.PostAsync(REQUEST_URI, content);
        }

        /// <summary>
        /// Read the response XML
        /// </summary>
        /// <param name="response">Response</param>
        /// <returns>XML</returns>
        private static async Task<XDocument> ReadXmlAsync(HttpResponseMessage response)
        {
            await using Stream body = await response.Content.ReadAsStreamAsync();
            return await XDocument.LoadAsync(body, LoadOptions.None, CancellationToken.None);
        }
    }
}
