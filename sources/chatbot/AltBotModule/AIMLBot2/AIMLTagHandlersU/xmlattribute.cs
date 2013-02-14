using System;
using System.Xml;
using System.Text;
using AltAIMLParser;
using AltAIMLbot;
using RTParser.Utils;

namespace RTParser.AIMLTagHandlers
{
    /// <summary>
    /// The sr element is a shortcut for: 
    /// 
    /// <srai><star/></srai> 
    /// 
    /// The atomic sr does not have any content. 
    /// </summary>
    public class xmlattribute : RTParser.Utils.AIMLTagHandler
    {
        /// <summary>
        /// Ctor
        /// </summary>
        /// <param name="bot">The bot involved in this request</param>
        /// <param name="user">The user making the request</param>
        /// <param name="query">The query that originated this node</param>
        /// <param name="request">The request inputted into the system</param>
        /// <param name="result">The result to be passed to the user</param>
        /// <param name="templateNode">The node to be processed</param>
        public xmlattribute(RTParser.AltBot bot,
                        RTParser.User user,
                        RTParser.Utils.SubQuery query,
                        Request request,
                        Result result,
                        XmlNode templateNode)
            : base(bot, user, query, request, result, templateNode)
        {
        }

        protected override Unifiable ProcessChange()
        {
            if (CheckNode("xmlattribute"))
            {
                string name = GetAttribValue(templateNode, "name,var", null);
                string value = GetAttribValue<string>(templateNode, "value", GetTemplateNodeInnerText, null);
                var ats = Parent.templateNode.Attributes.Append(templateNode.OwnerDocument.CreateAttribute(name, value));
            }
            return Unifiable.Empty;
        }
    }
}
