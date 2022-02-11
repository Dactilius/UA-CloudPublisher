
namespace UA.MQTT.Publisher.Controllers
{
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Mvc;
    using Opc.Ua;
    using Opc.Ua.Client;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading.Tasks;
    using UA.MQTT.Publisher;
    using UA.MQTT.Publisher.Models;

    public class BrowserController : Controller
    {
        private readonly OpcSessionHelper _helper;

        public BrowserController(OpcSessionHelper helper)
        {
            _helper = helper;
        }

        [HttpGet]
        public ActionResult Index()
        {
            OpcSessionModel sessionModel = new OpcSessionModel();
            sessionModel.SessionId = HttpContext.Session.Id;

            OpcSessionCacheData entry = null;
            if (_helper.OpcSessionCache.TryGetValue(HttpContext.Session.Id, out entry))
            {
                sessionModel.EndpointUrl = entry.EndpointURL;
                HttpContext.Session.SetString("EndpointUrl", entry.EndpointURL);
                return View("Browse", sessionModel);
            }

            return View("Index", sessionModel);
        }

        [HttpPost]
        public async Task<ActionResult> Connect(string endpointUrl)
        {
            OpcSessionModel sessionModel = new OpcSessionModel { EndpointUrl = endpointUrl };

            try
            {
                await _helper.GetSessionAsync(HttpContext.Session.Id, endpointUrl, true);
            }
            catch (Exception ex)
            {
                sessionModel.StatusMessage = ex.Message;
                return View("Index", sessionModel);
            }

            HttpContext.Session.SetString("EndpointUrl", endpointUrl);
            sessionModel.StatusMessage = "Connected to: " + endpointUrl;
            return View("Browse", sessionModel);
        }

        [HttpPost]
        public ActionResult Disconnect()
        {
            try
            {
                _helper.Disconnect(HttpContext.Session.Id);
                HttpContext.Session.SetString("EndpointUrl", string.Empty);
            }
            catch (Exception)
            {
                // do nothing
            }

            return View("Index", new OpcSessionModel());
        }

        [HttpPost]
        public async Task<ActionResult> GetRootNode()
        {
            ReferenceDescriptionCollection references;
            byte[] continuationPoint;
            var jsonTree = new List<object>();

            try
            {
                Session session = await _helper.GetSessionAsync(HttpContext.Session.Id, HttpContext.Session.GetString("EndpointUrl"));

                session.Browse(
                    null,
                    null,
                    ObjectIds.ObjectsFolder,
                    0u,
                    BrowseDirection.Forward,
                    ReferenceTypeIds.HierarchicalReferences,
                    true,
                    0,
                    out continuationPoint,
                    out references);
                jsonTree.Add(new { id = ObjectIds.ObjectsFolder.ToString(), text = "Root", children = (references?.Count != 0) });

                return Json(jsonTree);
            }
            catch (Exception ex)
            {
                OpcSessionModel sessionModel = new OpcSessionModel();
                sessionModel.StatusMessage = ex.Message;
                sessionModel.SessionId = HttpContext.Session.Id;
                sessionModel.EndpointUrl = HttpContext.Session.GetString("EndpointUrl");
                return View("Browse", sessionModel);
            }
        }

        [HttpPost]
        public async Task<ActionResult> GetChildren(string jstreeNode)
        {
            ReferenceDescriptionCollection references = null;
            byte[] continuationPoint;
            var jsonTree = new List<object>();

            Session session = null;
            try
            {
                Stopwatch stopwatch = new Stopwatch();
                stopwatch.Start();

                try
                {
                    session = await _helper.GetSessionAsync(HttpContext.Session.Id, HttpContext.Session.GetString("EndpointUrl"));

                    session.Browse(
                        null,
                        null,
                        GetNodeIDFromJSTreeNode(jstreeNode),
                        0u,
                        BrowseDirection.Forward,
                        ReferenceTypeIds.HierarchicalReferences,
                        true,
                        0,
                        out continuationPoint,
                        out references);
                }
                catch (Exception e)
                {
                    // skip this node
                    Trace.TraceError("Can not browse node '{0}'", GetNodeIDFromJSTreeNode(jstreeNode));
                    string errorMessage = string.Format("Error", e.Message,
                        e.InnerException?.Message ?? "--", e?.StackTrace ?? "--");
                    Trace.TraceError(errorMessage);
                }

                Trace.TraceInformation("Browsing node '{0}' data took {0} ms", GetNodeIDFromJSTreeNode(jstreeNode), stopwatch.ElapsedMilliseconds);

                if (references != null)
                {
                    var idList = new List<string>();
                    foreach (var nodeReference in references)
                    {
                        bool idFound = false;
                        foreach (var id in idList)
                        {
                            if (id == nodeReference.NodeId.ToString())
                            {
                                idFound = true;
                            }
                        }
                        if (idFound == true)
                        {
                            continue;
                        }

                        ReferenceDescriptionCollection childReferences = null;
                        byte[] childContinuationPoint;

                        Trace.TraceInformation("Browse '{0}' count: {1}", nodeReference.NodeId, jsonTree.Count);

                        INode currentNode = null;
                        try
                        {
                            session.Browse(
                                null,
                                null,
                                ExpandedNodeId.ToNodeId(nodeReference.NodeId, session.NamespaceUris),
                                0u,
                                BrowseDirection.Forward,
                                ReferenceTypeIds.HierarchicalReferences,
                                true,
                                0,
                                out childContinuationPoint,
                                out childReferences);

                            currentNode = session.ReadNode(ExpandedNodeId.ToNodeId(nodeReference.NodeId, session.NamespaceUris));
                        }
                        catch (Exception e)
                        {
                            // skip this node
                            Trace.TraceError("Can not browse or read node '{0}'", nodeReference.NodeId);
                            string errorMessage = string.Format("Error", e.Message,
                                e.InnerException?.Message ?? "--", e?.StackTrace ?? "--");
                            Trace.TraceError(errorMessage);

                            continue;
                        }

                        byte currentNodeAccessLevel = 0;
                        byte currentNodeEventNotifier = 0;
                        bool currentNodeExecutable = false;

                        VariableNode variableNode = currentNode as VariableNode;
                        if (variableNode != null)
                        {
                            currentNodeAccessLevel = variableNode.UserAccessLevel;
                        }

                        ObjectNode objectNode = currentNode as ObjectNode;
                        if (objectNode != null)
                        {
                            currentNodeEventNotifier = objectNode.EventNotifier;
                        }

                        ViewNode viewNode = currentNode as ViewNode;
                        if (viewNode != null)
                        {
                            currentNodeEventNotifier = viewNode.EventNotifier;
                        }

                        MethodNode methodNode = currentNode as MethodNode;
                        if (methodNode != null)
                        {
                            currentNodeExecutable = methodNode.UserExecutable;
                        }

                        jsonTree.Add(new
                        {
                            id = ("__" + GetNodeIDFromJSTreeNode(jstreeNode) + "__$__" + nodeReference.NodeId.ToString()),
                            text = nodeReference.DisplayName.ToString(),
                            nodeClass = nodeReference.NodeClass.ToString(),
                            accessLevel = currentNodeAccessLevel.ToString(),
                            eventNotifier = currentNodeEventNotifier.ToString(),
                            executable = currentNodeExecutable.ToString(),
                            children = (childReferences.Count == 0) ? false : true,
                            relevantNode = true
                        });

                        idList.Add(nodeReference.NodeId.ToString());
                    }

                    // If there are no children, then this is a call to read the properties of the node itself.
                    if (jsonTree.Count == 0)
                    {
                        INode currentNode = null;

                        try
                        {
                            currentNode = session.ReadNode(new NodeId(GetNodeIDFromJSTreeNode(jstreeNode)));
                        }
                        catch (Exception e)
                        {
                            string errorMessage = string.Format("Error", e.Message,
                                e.InnerException?.Message ?? "--", e?.StackTrace ?? "--");
                            Trace.TraceError(errorMessage);
                        }

                        if (currentNode == null)
                        {
                            byte currentNodeAccessLevel = 0;
                            byte currentNodeEventNotifier = 0;
                            bool currentNodeExecutable = false;

                            VariableNode variableNode = currentNode as VariableNode;

                            if (variableNode != null)
                            {
                                currentNodeAccessLevel = variableNode.UserAccessLevel;
                            }

                            ObjectNode objectNode = currentNode as ObjectNode;

                            if (objectNode != null)
                            {
                                currentNodeEventNotifier = objectNode.EventNotifier;
                            }

                            ViewNode viewNode = currentNode as ViewNode;

                            if (viewNode != null)
                            {
                                currentNodeEventNotifier = viewNode.EventNotifier;
                            }

                            MethodNode methodNode = currentNode as MethodNode;

                            if (methodNode != null)
                            {
                                currentNodeExecutable = methodNode.UserExecutable;
                            }

                            jsonTree.Add(new
                            {
                                id = jstreeNode,
                                text = currentNode.DisplayName.ToString(),
                                nodeClass = currentNode.NodeClass.ToString(),
                                accessLevel = currentNodeAccessLevel.ToString(),
                                eventNotifier = currentNodeEventNotifier.ToString(),
                                executable = currentNodeExecutable.ToString(),
                                children = false
                            });
                        }
                    }
                }

                stopwatch.Stop();
                Trace.TraceInformation("Browing all childeren info of node '{0}' took {0} ms", GetNodeIDFromJSTreeNode(jstreeNode), stopwatch.ElapsedMilliseconds);

                return Json(jsonTree);
            }
            catch (Exception ex)
            {
                OpcSessionModel sessionModel = new OpcSessionModel();
                sessionModel.StatusMessage = ex.Message;
                sessionModel.SessionId = HttpContext.Session.Id;
                sessionModel.EndpointUrl = HttpContext.Session.GetString("EndpointUrl");
                return View("Browse", sessionModel);
            }
        }

        [HttpPost]
        public async Task<ActionResult> VariableRead(string jstreeNode)
        {
            try
            { 
                DataValueCollection values = null;
                DiagnosticInfoCollection diagnosticInfos = null;
                ReadValueIdCollection nodesToRead = new ReadValueIdCollection();
                ReadValueId valueId = new ReadValueId();
                valueId.NodeId = new NodeId(GetNodeIDFromJSTreeNode(jstreeNode));
                valueId.AttributeId = Attributes.Value;
                valueId.IndexRange = null;
                valueId.DataEncoding = null;
                nodesToRead.Add(valueId);

                Session session = await _helper.GetSessionAsync(HttpContext.Session.Id, HttpContext.Session.GetString("EndpointUrl")).ConfigureAwait(false);
                ResponseHeader responseHeader = session.Read(null, 0, TimestampsToReturn.Both, nodesToRead, out values, out diagnosticInfos);
                string value = "";
                string actionResult;
                if (values.Count > 0 && values[0].Value != null)
                {
                    if (values[0].WrappedValue.ToString().Length > 40)
                    {
                        value = values[0].WrappedValue.ToString().Substring(0, 40);
                        value += "...";
                    }
                    else
                    {
                        value = values[0].WrappedValue.ToString();
                    }

                    ExpandedNodeId expandedNodeID = new ExpandedNodeId(valueId.NodeId, session.NamespaceUris.ToArray()[valueId.NodeId.NamespaceIndex]);
                    actionResult = $"{{ \"value\": \"{value}\", \"nodeid\": \"{expandedNodeID}\", \"sourceTimestamp\": \"{values[0].SourceTimestamp}\"}}";
                }
                else
                {
                    actionResult = string.Empty;
                }

                return Content(actionResult);
            }
            catch (Exception ex)
            {
                OpcSessionModel sessionModel = new OpcSessionModel();
                sessionModel.StatusMessage = ex.Message;
                sessionModel.SessionId = HttpContext.Session.Id;
                sessionModel.EndpointUrl = HttpContext.Session.GetString("EndpointUrl");
                return View("Browse", sessionModel);
            }
        }

        [HttpPost]
        public ActionResult VariablePublish(string jstreeNode, string method)
        {
            string actionResult = "";

            try
            {
                
                return Content(actionResult);
            }
            catch (Exception ex)
            {
                OpcSessionModel sessionModel = new OpcSessionModel();
                sessionModel.StatusMessage = ex.Message;
                sessionModel.SessionId = HttpContext.Session.Id;
                sessionModel.EndpointUrl = HttpContext.Session.GetString("EndpointUrl");
                return View("Browse", sessionModel);
            }
        }

        private string GetNodeIDFromJSTreeNode(string jstreeNode)
        {
            // This delimiter is used to allow the storing of the OPC UA parent node ID together with the OPC UA child node ID in jstree data structures and provide it as parameter to 
            // Ajax calls.
            string[] delimiter = { "__$__" };
            string[] jstreeNodeSplit = jstreeNode.Split(delimiter, 3, StringSplitOptions.None);

            if (jstreeNodeSplit.Length == 1)
            {
                return jstreeNodeSplit[0];
            }
            else
            {
                return jstreeNodeSplit[1];
            }
        }
    }
}
