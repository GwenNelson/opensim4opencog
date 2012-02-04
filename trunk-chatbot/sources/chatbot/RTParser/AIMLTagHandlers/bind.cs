﻿using System;
using System.Xml;
using System.Text;
using RTParser.Utils;

namespace RTParser.AIMLTagHandlers
{
    /// <summary>
    /// IMPLEMENTED FOR COMPLETENESS REASONS
    /// </summary>
    public class bind : UnifibleTagHandler
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
        public bind(RTParser.RTPBot bot,
                        RTParser.User user,
                        RTParser.Utils.SubQuery query,
                        RTParser.Request request,
                        RTParser.Result result,
                        XmlNode templateNode)
            : base(bot, user, query, request, result, templateNode)
        {
        }

        public override float CanUnify(Unifiable with)
        {
            string srch = with.ToValue(query);
            if (templateNode.NodeType == XmlNodeType.Text)
            {
                bool unifyWithTextNode = UnifyWithTextNode(templateNode, srch);
                return unifyWithTextNode ? AND_TRUE : AND_FALSE;
            }
            if (templateNode.HasChildNodes)
            {
                // recursively check
                foreach (XmlNode childNode in templateNode.ChildNodes)
                {
                    try
                    {
                        if (childNode.NodeType == XmlNodeType.Text)
                        {
                            return UnifyWithTextNode(childNode, srch) ? AND_TRUE : AND_FALSE;
                        }
                        AIMLTagHandler part = GetChildTagHandler(childNode);
                        if (part.CallCanUnify(with) > 0) return AND_FALSE;
                    }
                    catch (Exception e)
                    {
                        Proc.writeToLog(e);
                        writeToLogWarn("" + e);
                    }
                }
                return AND_TRUE;
            }
            return AND_TRUE;
        }

        protected override Unifiable ProcessChange()
        {
            var vv =  base.Recurse();
            return vv;
        }
    }
}