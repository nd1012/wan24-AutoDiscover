using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Net.Mail;
using System.Xml;
using System.Xml.XPath;
using wan24.AutoDiscover.Models;
using wan24.AutoDiscover.Services;
using wan24.Core;

namespace wan24.AutoDiscover.Controllers
{
    /// <summary>
    /// Discovery controller
    /// </summary>
    /// <remarks>
    /// Constructor
    /// </remarks>
    /// <param name="responses">Responses</param>
    [ApiController, Route("autodiscover")]
    public class DiscoveryController(XmlDocumentInstances responses) : ControllerBase()
    {
        /// <summary>
        /// Max. request length in bytes
        /// </summary>
        private const int MAX_REQUEST_LEN = 1024;
        /// <summary>
        /// XML response MIME type
        /// </summary>
        private const string XML_MIME_TYPE = "application/xml";
        /// <summary>
        /// Auto discovery XML namespace
        /// </summary>
        public const string AUTO_DISCOVER_NS = "http://schemas.microsoft.com/exchange/autodiscover/responseschema/2006";
        /// <summary>
        /// <c>Autodiscover</c> node name
        /// </summary>
        public const string AUTODISCOVER_NODE_NAME = "Autodiscover";
        /// <summary>
        /// <c>Response</c> node name
        /// </summary>
        public const string RESPONSE_NODE_NAME = "Response";
        /// <summary>
        /// <c>Account</c> node name
        /// </summary>
        public const string ACCOUNT_NODE_NAME = "Account";
        /// <summary>
        /// <c>AccountType</c> node name
        /// </summary>
        public const string ACCOUNTTYPE_NODE_NAME = "AccountType";
        /// <summary>
        /// Account type
        /// </summary>
        public const string ACCOUNTTYPE = "email";
        /// <summary>
        /// <c>Action</c> node name
        /// </summary>
        public const string ACTION_NODE_NAME = "Action";
        /// <summary>
        /// Action
        /// </summary>
        public const string ACTION = "settings";

        /// <summary>
        /// Responses
        /// </summary>
        private readonly XmlDocumentInstances Responses = responses;

        /// <summary>
        /// XPath request query
        /// </summary>
        private static readonly XPathExpression RequestQuery = XPathExpression.Compile("/*[local-name()='Autodiscover']/*[local-name()='Request']");
        /// <summary>
        /// XPath schema query
        /// </summary>
        private static readonly XPathExpression SchemaQuery = XPathExpression.Compile("./*[local-name()='AcceptableResponseSchema']");
        /// <summary>
        /// XPath email query
        /// </summary>
        private static readonly XPathExpression EmailQuery = XPathExpression.Compile("./*[local-name()='EMailAddress']");

        /// <summary>
        /// Autodiscover (POX request body required)
        /// </summary>
        /// <returns>POX response</returns>
        [HttpPost, Route("autodiscover.xml"), Consumes(XML_MIME_TYPE, IsOptional = false), RequestSizeLimit(MAX_REQUEST_LEN), Produces(XML_MIME_TYPE)]
        public async Task<ContentResult> AutoDiscoverAsync()
        {
            // Validate the request and try getting the email address
            XPathNavigator requestNavigator,
                requestNode,
                acceptableResponseSchema,
                emailNode;
            using (MemoryPoolStream ms = new())
            {
                Stream requestBody = HttpContext.Request.Body;
                await using (requestBody.DynamicContext())
                    await requestBody.CopyToAsync(ms, bufferSize: MAX_REQUEST_LEN, HttpContext.RequestAborted).DynamicContext();
                ms.Position = 0;
                try
                {
                    requestNavigator = new XPathDocument(ms).CreateNavigator();
                }
                catch (XmlException ex)
                {
                    throw new BadHttpRequestException("Invalid XML in request", ex);
                }
            }
            requestNode = requestNavigator.SelectSingleNode(RequestQuery)
                ?? throw new BadHttpRequestException("Missing request node in request");
            acceptableResponseSchema = requestNode.SelectSingleNode(SchemaQuery)
                ?? throw new BadHttpRequestException("Missing acceptable response schema node in request");
            emailNode = requestNode.SelectSingleNode(EmailQuery)
                ?? throw new BadHttpRequestException("Missing email address node in request");
            if (acceptableResponseSchema.Value.Trim() != Constants.RESPONSE_NS)
                throw new BadHttpRequestException("Unsupported acceptable response schema in request");
            string emailAddress = emailNode.Value.Trim().ToLower();
            if (Logging.Trace)
                Logging.WriteTrace($"POX request from {HttpContext.Connection.RemoteIpAddress}:{HttpContext.Connection.RemotePort} email address {emailAddress.ToQuotedLiteral()}");
            string[] emailParts = emailAddress.Split('@', 2);
            if (emailParts.Length != 2 || !MailAddress.TryCreate(emailAddress, out _))
                throw new BadHttpRequestException("Invalid email address");
            // Generate the response
            if (Logging.Trace)
                Logging.WriteTrace($"Creating POX response for \"{emailAddress}\" request from {HttpContext.Connection.RemoteIpAddress}:{HttpContext.Connection.RemotePort}");
            XmlDocument xml = await Responses.GetOneAsync(HttpContext.RequestAborted).DynamicContext();
            if (DomainConfig.GetConfig(HttpContext.Request.Host.Host, emailParts) is not DomainConfig config)
                throw new BadHttpRequestException($"Unknown request domain name {HttpContext.Request.Host.Host} / {emailParts[1]}");
            config.CreateXml(xml, xml.FirstChild?.FirstChild?.FirstChild ?? throw new InvalidProgramException("Missing response XML account node"), emailParts);
            if (Logging.Trace)
                Logging.WriteTrace($"POX response XML body to {HttpContext.Connection.RemoteIpAddress}:{HttpContext.Connection.RemotePort}: {xml.OuterXml}");
            return new()
            {
                Content = xml.OuterXml,
                ContentType = XML_MIME_TYPE,
                StatusCode = (int)HttpStatusCode.OK
            };
        }
    }
}
