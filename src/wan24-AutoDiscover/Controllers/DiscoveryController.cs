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
    /// <param name="streamPool">Stream pool</param>
    [ApiController, Route("autodiscover")]
    public sealed class DiscoveryController(XmlResponseInstances responses, MemoryPoolStreamPool streamPool) : ControllerBase()
    {
        /// <summary>
        /// Max. request length in bytes
        /// </summary>
        private const int MAX_REQUEST_LEN = byte.MaxValue << 1;
        /// <summary>
        /// OK http status code
        /// </summary>
        private const int OK_STATUS_CODE = (int)HttpStatusCode.OK;
        /// <summary>
        /// Bad request http status code
        /// </summary>
        private const int BAD_REQUEST_STATUS_CODE = (int)HttpStatusCode.BadRequest;
        /// <summary>
        /// XML response MIME type
        /// </summary>
        private const string XML_MIME_TYPE = "application/xml";

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
        /// Responses
        /// </summary>
        private readonly XmlResponseInstances Responses = responses;
        /// <summary>
        /// Stream pool
        /// </summary>
        private readonly MemoryPoolStreamPool StreamPool = streamPool;

        /// <summary>
        /// Autodiscover (POX request body required)
        /// </summary>
        /// <returns>POX response</returns>
        [HttpPost("autodiscover.xml"), Consumes(XML_MIME_TYPE, IsOptional = false), RequestSizeLimit(MAX_REQUEST_LEN), Produces(XML_MIME_TYPE)]
        public async Task AutoDiscoverAsync()
        {
            if (Logging.Trace)
                Logging.WriteTrace($"POX request from {HttpContext.Connection.RemoteIpAddress}:{HttpContext.Connection.RemotePort}");
            // Validate the request and try getting the email address
            XPathNavigator requestNavigator,// Whole request XML
                requestNode,// Request node
                acceptableResponseSchema,// AcceptableResponseSchema node
                emailNode;// EMailAddress node
            using (RentedObject<PooledMemoryStream> rentedStream = new(StreamPool)
            {
                Reset = true
            })
            {
                Stream requestBody = HttpContext.Request.Body;
                await using (requestBody.DynamicContext())
                    await requestBody.CopyToAsync(rentedStream.Object, bufferSize: MAX_REQUEST_LEN, HttpContext.RequestAborted).DynamicContext();
                rentedStream.Object.Position = 0;
                try
                {
                    requestNavigator = new XPathDocument(rentedStream.Object).CreateNavigator();
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
            string emailAddress = emailNode.Value.Trim().ToLower();// Full email address (lower case)
            if (Logging.Trace)
                Logging.WriteTrace($"POX request from {HttpContext.Connection.RemoteIpAddress}:{HttpContext.Connection.RemotePort} for email address {emailAddress.ToQuotedLiteral()}");
            string[] emailParts = emailAddress.Split('@', 2);// @ splitted email alias and domain name
            if (emailParts.Length != 2 || !MailAddress.TryCreate(emailAddress, out _))
                throw new BadHttpRequestException("Invalid email address in request");
            // Generate the response
            using XmlResponse xml = await Responses.GetOneAsync(HttpContext.RequestAborted).DynamicContext();// Response XML
            if (DomainConfig.GetConfig(HttpContext.Request.Host.Host, emailParts) is not DomainConfig config)
            {
                await BadRequestAsync($"Unknown domain name {HttpContext.Request.Host.Host} / {emailParts[1]}".GetBytes()).DynamicContext();
                return;
            }
            if (Logging.Trace)
                Logging.WriteTrace($"Creating POX response for \"{emailAddress}\" request from {HttpContext.Connection.RemoteIpAddress}:{HttpContext.Connection.RemotePort}");
            HttpContext.Response.StatusCode = OK_STATUS_CODE;
            HttpContext.Response.ContentType = XML_MIME_TYPE;
            await HttpContext.Response.StartAsync(HttpContext.RequestAborted).DynamicContext();
            Task sendXmlOutput = xml.XmlOutput.CopyToAsync(HttpContext.Response.Body, HttpContext.RequestAborted);
            config.CreateXml(xml.XML, emailParts);
            xml.FinalizeXmlOutput();
            await sendXmlOutput.DynamicContext();
            await HttpContext.Response.CompleteAsync().DynamicContext();
            if (Logging.Trace)
                Logging.WriteTrace($"POX response for \"{emailAddress}\" request from {HttpContext.Connection.RemoteIpAddress}:{HttpContext.Connection.RemotePort} sent");
        }

        /// <summary>
        /// Respond with a bad request message
        /// </summary>
        /// <param name="message">Message</param>
        private async Task BadRequestAsync(ReadOnlyMemory<byte> message)
        {
            HttpContext.Response.StatusCode = BAD_REQUEST_STATUS_CODE;
            HttpContext.Response.ContentType = ExceptionHandler.TEXT_MIME_TYPE;
            await HttpContext.Response.Body.WriteAsync(message, HttpContext.RequestAborted).DynamicContext();
        }
    }
}
