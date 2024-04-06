using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Xml;
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
        /// Request XML email address node XPath selector
        /// </summary>
        private const string EMAIL_NODE_XPATH = "//*[local-name()='EMailAddress']";
        /// <summary>
        /// Request XML acceptable response node XPath selector
        /// </summary>
        private const string ACCEPTABLE_RESPONSE_SCHEMA_NODE_XPATH = "//*[local-name()='AcceptableResponseSchema']";
        /// <summary>
        /// Response XML account node XPath selector
        /// </summary>
        private const string ACCOUNT_NODE_XPATH = $"//*[local-name()='{ACCOUNT_NODE_NAME}']";
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
        /// Auto discovery (POX)
        /// </summary>
        /// <returns>XML response</returns>
        [HttpPost, Route("autodiscover.xml")]
        public async Task<ContentResult> AutoDiscoverAsync()
        {
            // Try getting the requested email address from the request
            XmlDocument requestXml = new();
            Stream requestBody = HttpContext.Request.Body;
            await using (requestBody.DynamicContext())
            using (StreamReader reader = new(requestBody, Encoding.UTF8, leaveOpen: true))
            {
                string requestXmlString = await reader.ReadToEndAsync(HttpContext.RequestAborted).DynamicContext();
                if (Logging.Debug)
                    Logging.WriteDebug($"POX request XML body from {HttpContext.Connection.RemoteIpAddress}:{HttpContext.Connection.RemotePort}: {requestXmlString.ToQuotedLiteral()}");
                requestXml.LoadXml(requestXmlString);
            }
            if (
                requestXml.SelectSingleNode(ACCEPTABLE_RESPONSE_SCHEMA_NODE_XPATH) is XmlNode acceptableResponseSchema &&
                acceptableResponseSchema.InnerText.Trim() != Constants.RESPONSE_NS
                )
                throw new BadHttpRequestException($"Unsupported response schema {acceptableResponseSchema.InnerText.ToQuotedLiteral()}");
            if (requestXml.SelectSingleNode(EMAIL_NODE_XPATH) is not XmlNode emailNode)
                throw new BadHttpRequestException("Missing email address in request");
            string emailAddress = emailNode.InnerText.Trim().ToLower();
            if (Logging.Debug)
                Logging.WriteDebug($"POX request from {HttpContext.Connection.RemoteIpAddress}:{HttpContext.Connection.RemotePort} email address {emailAddress.ToQuotedLiteral()}");
            string[]  emailParts = emailAddress.Split('@', 2);
            if (emailParts.Length != 2 || !MailAddress.TryCreate(emailAddress, out _))
                throw new BadHttpRequestException("Invalid email address");
            // Generate discovery response
            if (Logging.Debug)
                Logging.WriteDebug($"Creating POX response for {emailAddress.ToQuotedLiteral()} request from {HttpContext.Connection.RemoteIpAddress}:{HttpContext.Connection.RemotePort}");
            XmlDocument xml = await Responses.GetOneAsync(HttpContext.RequestAborted).DynamicContext();
            if (
                !DomainConfig.Registered.TryGetValue(emailParts[1], out DomainConfig? config) &&
                !DomainConfig.Registered.TryGetValue(HttpContext.Request.Host.Host, out config) &&
                !DomainConfig.Registered.TryGetValue(
                    DomainConfig.Registered.Where(kvp => kvp.Value.AcceptedDomains?.Contains(emailParts[1], StringComparer.OrdinalIgnoreCase) ?? false)
                        .Select(kvp => kvp.Key)
                        .FirstOrDefault() ?? string.Empty,
                    out config
                    )
                )
                throw new BadHttpRequestException($"Unknown request domain name \"{HttpContext.Request.Host.Host}\"/{emailParts[1].ToQuotedLiteral()}");
            config.CreateXml(xml, xml.SelectSingleNode(ACCOUNT_NODE_XPATH) ?? throw new InvalidProgramException("Missing response XML account node"), emailParts);
            return new()
            {
                Content = xml.OuterXml,
                ContentType = XML_MIME_TYPE,
                StatusCode = (int)HttpStatusCode.OK
            };
        }
    }
}
