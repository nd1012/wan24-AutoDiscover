using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Net.Mail;
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
    public sealed class DiscoveryController(InstancePool<XmlResponse> responses) : ControllerBase()
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
        /// <c>EMailAddress</c> node name
        /// </summary>
        private const string EMAIL_NODE_NAME = "EMailAddress";

        /// <summary>
        /// Invalid request XML message bytes
        /// </summary>
        private static readonly byte[] InvalidRequestMessage = "Invalid Request XML".GetBytes();
        /// <summary>
        /// Missing email address message bytes
        /// </summary>
        private static readonly byte[] MissingEmailMessage = "Missing Email Address".GetBytes();
        /// <summary>
        /// Invalid email address message bytes
        /// </summary>
        private static readonly byte[] InvalidEmailMessage = "Invalid Email Address".GetBytes();
        /// <summary>
        /// Unknown domain name message bytes
        /// </summary>
        private static readonly byte[] UnknownDomainMessage = "Unknown Domain Name".GetBytes();

        /// <summary>
        /// Responses
        /// </summary>
        private readonly InstancePool<XmlResponse> Responses = responses;

        /// <summary>
        /// Autodiscover (POX request body required)
        /// </summary>
        /// <returns>POX response</returns>
        [HttpPost("autodiscover.xml"), Consumes(XML_MIME_TYPE, IsOptional = false), RequestSizeLimit(MAX_REQUEST_LEN), Produces(XML_MIME_TYPE)]
        public async Task AutoDiscoverAsync()
        {
            // Validate the request and try getting the email address
            if (Logging.Trace)
                Logging.WriteTrace($"POX request from {HttpContext.Connection.RemoteIpAddress}:{HttpContext.Connection.RemotePort}");
            string? emailAddress = null;// Full email address
            using (MemoryPoolStream ms = new())
            {
                await HttpContext.Request.Body.CopyToAsync(ms, HttpContext.RequestAborted).DynamicContext();
                ms.Position = 0;
                try
                {
                    using XmlReader requestXml = XmlReader.Create(ms);
                    while (requestXml.Read())
                    {
                        if (!requestXml.Name.Equals(EMAIL_NODE_NAME, StringComparison.OrdinalIgnoreCase))
                            continue;
                        emailAddress = requestXml.ReadElementContentAsString();
                        break;
                    }
                }
                catch(XmlException ex)
                {
                    if (Logging.Debug)
                        Logging.WriteDebug($"Parsing POX request from {HttpContext.Connection.RemoteIpAddress}:{HttpContext.Connection.RemotePort} failed: {ex}");
                    RespondBadRequest(InvalidRequestMessage);
                    return;
                }
            }
            if(emailAddress is null)
            {
                RespondBadRequest(MissingEmailMessage);
                return;
            }
            if (Logging.Trace)
                Logging.WriteTrace($"POX request from {HttpContext.Connection.RemoteIpAddress}:{HttpContext.Connection.RemotePort} for email address {emailAddress.ToQuotedLiteral()}");
            string[] emailParts = emailAddress.Split('@', 2);// @ splitted email alias and domain name
            if (emailParts.Length != 2 || !MailAddress.TryCreate(emailAddress, out _))
            {
                RespondBadRequest(InvalidEmailMessage);
                return;
            }
            // Generate the response
            if (DomainConfig.GetConfig(HttpContext.Request.Host.Host, emailParts) is not DomainConfig config)
            {
                RespondBadRequest(UnknownDomainMessage);
                return;
            }
            if (Logging.Trace)
                Logging.WriteTrace($"Creating POX response for \"{emailAddress}\" request from {HttpContext.Connection.RemoteIpAddress}:{HttpContext.Connection.RemotePort}");
            HttpContext.Response.StatusCode = OK_STATUS_CODE;
            HttpContext.Response.ContentType = XML_MIME_TYPE;
            using XmlResponse responseXml = await Responses.GetOneAsync(HttpContext.RequestAborted).DynamicContext();// Response XML
            Task sendXmlOutput = responseXml.XmlOutput.CopyToAsync(HttpContext.Response.Body, HttpContext.RequestAborted);
            try
            {
                config.CreateXml(responseXml.XML, emailParts);
                responseXml.FinalizeXmlOutput();
            }
            finally
            {
                await sendXmlOutput.DynamicContext();
                if (Logging.Trace)
                    Logging.WriteTrace($"POX response for \"{emailAddress}\" request from {HttpContext.Connection.RemoteIpAddress}:{HttpContext.Connection.RemotePort} sent");
            }
        }

        /// <summary>
        /// Respond with a bad request message
        /// </summary>
        /// <param name="message">Message</param>
        private void RespondBadRequest(byte[] message)
        {
            if (Logging.Trace)
                Logging.WriteTrace($"Invalid POX request from {HttpContext.Connection.RemoteIpAddress}:{HttpContext.Connection.RemotePort}: \"{message.ToUtf8String()}\"");
            HttpContext.Response.StatusCode = BAD_REQUEST_STATUS_CODE;
            HttpContext.Response.ContentType = ExceptionHandler.TEXT_MIME_TYPE;
            HttpContext.Response.Body = new MemoryStream(message);
        }
    }
}
