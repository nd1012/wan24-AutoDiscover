using System.Text;
using System.Xml;
using wan24.Core;

namespace wan24.AutoDiscover
{
    /// <summary>
    /// XML response
    /// </summary>
    public class XmlResponse : DisposableBase
    {
        /// <summary>
        /// Buffer size in bytes
        /// </summary>
        private const int BUFFER_SIZE = 1024;

        /// <summary>
        /// XML writer settings
        /// </summary>
        private static readonly XmlWriterSettings Settings = new()
        {
            Indent = false,
            Encoding = Encoding.UTF8,
            OmitXmlDeclaration = true
        };

        /// <summary>
        /// Constructor
        /// </summary>
        public XmlResponse() : base(asyncDisposing: false)
        {
            XmlOutput = new(BUFFER_SIZE)
            {
                AggressiveReadBlocking = false
            };
            XML = XmlWriter.Create(XmlOutput, Settings);
            XML.WriteStartElement(Constants.AUTODISCOVER_NODE_NAME, Constants.AUTO_DISCOVER_NS);
            XML.WriteStartElement(Constants.RESPONSE_NODE_NAME, Constants.RESPONSE_NS);
            XML.WriteStartElement(Constants.ACCOUNT_NODE_NAME);
            XML.WriteElementString(Constants.ACCOUNTTYPE_NODE_NAME, Constants.ACCOUNTTYPE);
            XML.WriteElementString(Constants.ACTION_NODE_NAME, Constants.ACTION);
            XML.Flush();
        }

        /// <summary>
        /// XML
        /// </summary>
        public XmlWriter XML { get; }

        /// <summary>
        /// XML output
        /// </summary>
        public BlockingBufferStream XmlOutput { get; }

        /// <summary>
        /// Finalize
        /// </summary>
        public virtual void FinalizeXmlOutput()
        {
            EnsureUndisposed();
            XML.WriteEndElement();
            XML.WriteEndElement();
            XML.WriteEndElement();
            XML.Flush();
            XmlOutput.IsEndOfFile = true;
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            XML.Dispose();
            XmlOutput.Dispose();
        }
    }
}
