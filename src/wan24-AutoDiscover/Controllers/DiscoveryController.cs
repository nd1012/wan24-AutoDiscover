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
    public sealed class DiscoveryController(XmlResponseInstances responses) : ControllerBase()
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
        /// Missing email address message
        /// </summary>
        private static readonly byte[] MissingEmailMessage = "Missing email address in request".GetBytes();
        /// <summary>
        /// Invalid email address message
        /// </summary>
        private static readonly byte[] InvalidEmailMessage = "Invalid email address in request".GetBytes();
        /// <summary>
        /// Unknown domain name message
        /// </summary>
        private static readonly byte[] UnknownDomainMessage = "Unknown domain name".GetBytes();

        /// <summary>
        /// Responses
        /// </summary>
        private readonly XmlResponseInstances Responses = responses;

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
                using XmlReader xmlRequest = XmlReader.Create(ms);
                while (xmlRequest.Read())
                {
                    if (xmlRequest.Name != EMAIL_NODE_NAME) continue;
                    emailAddress = xmlRequest.ReadElementContentAsString();
                    break;
                }
            }
            if(emailAddress is null)
            {
                await BadRequestAsync(MissingEmailMessage).DynamicContext();
                return;
            }
            if (Logging.Trace)
                Logging.WriteTrace($"POX request from {HttpContext.Connection.RemoteIpAddress}:{HttpContext.Connection.RemotePort} for email address {emailAddress.ToQuotedLiteral()}");
            string[] emailParts = emailAddress.Split('@', 2);// @ splitted email alias and domain name
            if (emailParts.Length != 2 || !MailAddress.TryCreate(emailAddress, out _))
            {
                await BadRequestAsync(InvalidEmailMessage).DynamicContext();
                return;
            }
            // Generate the response
            using XmlResponse xml = await Responses.GetOneAsync(HttpContext.RequestAborted).DynamicContext();// Response XML
            if (DomainConfig.GetConfig(HttpContext.Request.Host.Host, emailParts) is not DomainConfig config)
            {
                await BadRequestAsync(UnknownDomainMessage).DynamicContext();
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
            if (Logging.Trace)
                Logging.WriteTrace($"Invalid POX request from {HttpContext.Connection.RemoteIpAddress}:{HttpContext.Connection.RemotePort}: \"{message.ToUtf8String()}\"");
            HttpContext.Response.StatusCode = BAD_REQUEST_STATUS_CODE;
            HttpContext.Response.ContentType = ExceptionHandler.TEXT_MIME_TYPE;
            await HttpContext.Response.Body.WriteAsync(message, HttpContext.RequestAborted).DynamicContext();
        }
    }
}
