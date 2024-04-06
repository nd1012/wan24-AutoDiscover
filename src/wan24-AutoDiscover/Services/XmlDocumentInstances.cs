using System.Xml;
using wan24.AutoDiscover.Controllers;
using wan24.Core;

namespace wan24.AutoDiscover.Services
{
    /// <summary>
    /// <see cref="XmlDocument"/> response instance pool
    /// </summary>
    /// <remarks>
    /// Constructor
    /// </remarks>
    /// <param name="capacity">Capacity</param>
    public sealed class XmlDocumentInstances(in int capacity) : InstancePool<XmlDocument>(capacity, CreateXmlDocument)
    {
        /// <summary>
        /// Constructor
        /// </summary>
        public XmlDocumentInstances() : this(capacity: 100) { }

        /// <summary>
        /// Create an <see cref="XmlDocument"/>
        /// </summary>
        /// <param name="pool">Pool</param>
        /// <returns><see cref="XmlDocument"/></returns>
        private static XmlDocument CreateXmlDocument(IInstancePool<XmlDocument> pool)
        {
            if (Logging.Trace) Logging.WriteTrace("Pre-forking a new POX XML response");
            XmlDocument xml = new();
            XmlNode account = xml.AppendChild(xml.CreateNode(XmlNodeType.Element, DiscoveryController.AUTODISCOVER_NODE_NAME, DiscoveryController.AUTO_DISCOVER_NS))!
                .AppendChild(xml.CreateNode(XmlNodeType.Element, DiscoveryController.RESPONSE_NODE_NAME, Constants.RESPONSE_NS))!
                .AppendChild(xml.CreateElement(DiscoveryController.ACCOUNT_NODE_NAME, Constants.RESPONSE_NS))!;
            account.AppendChild(xml.CreateElement(DiscoveryController.ACCOUNTTYPE_NODE_NAME, Constants.RESPONSE_NS))!.InnerText = DiscoveryController.ACCOUNTTYPE;
            account.AppendChild(xml.CreateElement(DiscoveryController.ACTION_NODE_NAME, Constants.RESPONSE_NS))!.InnerText = DiscoveryController.ACTION;
            return xml;
        }
    }
}
