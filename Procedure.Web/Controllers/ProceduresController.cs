﻿using Parliament.Model;
using Parliament.Rdf.Serialization;
using Procedure.Web.Extensions;
using Procedure.Web.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Mvc;
using System.Xml;
using System.Xml.Linq;
using VDS.RDF;
using VDS.RDF.Parsing;
using VDS.RDF.Query;

namespace Procedure.Web.Controllers
{
    [RoutePrefix("Procedures")]
    public class ProceduresController : BaseController
    {
        [Route("~/")]
        [Route]
        public ActionResult Index()
        {
            return ShowList<ProcedureItem>(ProcedureItem.ListSql);
        }

        [Route("{id:int}")]
        public ActionResult Details(int id)
        {
            ProcedureDetailViewModel viewModel = new ProcedureDetailViewModel();

            ProcedureItem procedureItem = GetSqlItem<ProcedureItem>(ProcedureItem.ItemSql, new { Id = id });

            if (procedureItem.Id != 0)
            {
                viewModel.Procedure = procedureItem;
                viewModel.Tree = GenerateProcedureTree(id);
            }

            return View(viewModel);
        }

        // Renders DOT/Graphviz, an open graph description language (https://www.graphviz.org/doc/info/lang.html)
        [Route("{id:int}/graph")]
        public ActionResult GraphViz(int id)
        {
            GraphVizViewModel viewmodel = new GraphVizViewModel();
            viewmodel.DotString = GiveMeDotString(id, showLegend:true);

            return View(viewmodel);
        }

        // Return graph in DOT string
        [Route("{id:int}/graph.dot")]
        public ContentResult GraphDot(int id)
        {
            return Content(GiveMeDotString(id, showLegend:false), "text/plain");
        }

        private string GiveMeDotString(int procedureId, bool showLegend)
        {
            List<RouteItem> routes = getAllRoutes(procedureId);

            if (routes.Any())
            {
                StringBuilder builder = new StringBuilder("graph[fontname=\"calibri\"];node[fontname=\"calibri\"];edge[fontname=\"calibri\"];");
                foreach (RouteItem route in routes)
                {
                    string edgeStyle = null;
                    string label = null;
                    label = "";
                    if ((route.StartDate != null && route.StartDate > DateTime.Now) || (route.EndDate != null && route.EndDate < DateTime.Now))
                        edgeStyle = "style=dotted, color=black";
                    else
                        edgeStyle = "style=solid, color=black";
                    if (edgeStyle != null && label != null)
                    {
                        builder.Append($"edge [{edgeStyle}];\"{route.FromStepId}\"->\"{route.ToStepId}\"[label=\"{label}\"];edge[style=solid, color=black];");
                    }
                }

                var allStepIds = routes.Select(r => (r.FromStepId, r.FromStepTypeId, r.FromStepName.ProcessName(), r.FromStepTypeId == 1 ? "": r.FromStepTypeName.ProcessName(), r.FromStepHouseName)).
                    Union(routes.Select(r => (r.ToStepId, r.ToStepTypeId, r.ToStepName.ProcessName(), r.ToStepTypeId == 1 ? "" : r.ToStepTypeName.ProcessName(), r.ToStepHouseName))).Distinct().ToArray();
                foreach ((int id, int typeId, string stepName, string stepTypeName, string houseName) in allStepIds)
                {
                    string tempStr;
                    if (typeId == 1)
                    {
                        tempStr = $"\"{id}\" [label=\"{stepName}{stepTypeName}({houseName})\", style=bold];";
                    }
                    else
                    {
                        tempStr = $"\"{id}\" [label=\"{stepName}{stepTypeName}({houseName})\", style=dotted];";

                    }
                    builder.Append(tempStr.Replace("()", ""));
                }


                if (showLegend == true)
                {
                    builder.Append("subgraph cluster_key {" +
                        "label = \"Key\" ; labeljust=\"l\";" +
                        "shape = rectangle;" +
                        "color = black;" +
                        "k3[label=<<table border=\"0\" cellpadding=\"2\" cellspacing=\"0\" cellborder=\"0\"> " +
                        "<tr><td align=\"right\" port=\"i1\"> Open (current) route </td></tr>" +
                        "<tr><td align=\"right\" port=\"i2\"> Closed (non-current) route </td></tr>" +
                        "</table>>];" +

                        "k3e [label =<<table border=\"0\" cellpadding=\"2\" cellspacing=\"0\" cellborder=\"0\">" +
                        "<tr><td port=\"i1\" > &nbsp;</td></tr> <tr><td port=\"i2\"> &nbsp;</td></tr> " +
                        "</table>>];" +
                        "k3:i1:e->k3e:i1:w k3:i2:e-> k3e:i2:w [style=dotted]" +

                        "\"Business step\" -> ee [style=invis];" +
                        "\"Non-business step\" ->f [style=invis];" +
                        "\"Business step\" [style = solid];" +
                        "\"Non-business step\" [style = dotted];" +
                        "ee[style = invis];" +
                        "f[style = invis];" +
                        "k3[shape = plaintext];" +
                        "k3e[style = invis];" +
                        "{ rank = same; k3 k3e}" +
                        "}");
                }

                builder.Insert(0, "digraph{");
                builder.Append("}");

                return builder.ToString();
            }
            else
            {
                return string.Empty;
            }
        }


        // Return graph in GraphML, an XML-based graph format (http://graphml.graphdrawing.org/primer/graphml-primer.html) 
        [Route("{id:int}/graph.graphml")]
        public ActionResult GraphML(int id)
        {
            List<RouteItem> routes = getAllRoutes(id);

            // Using Parliament nuget packages, map user-defined classes into ontology-aligned interfaces which all implement iResource interface
            // This allows us to work with IGraph objects
            IEnumerable<ProcedureRoute> IRoutes = routes.Select(r => r.GiveMeMappedObject());

            RdfSerializer serializer = new RdfSerializer();
            IGraph graph = serializer.Serialize(IRoutes, typeof(ProcedureRoute).Assembly.GetTypes());

            SparqlQueryParser parser = new SparqlQueryParser();
            // Nodes 
            SparqlQuery q1 = parser.ParseFromString("PREFIX : <https://id.parliament.uk/schema/> SELECT ?step ?stepName WHERE {?step a :ProcedureStep; :procedureStepName ?stepName. }");
            SparqlResultSet nodes = (SparqlResultSet)graph.ExecuteQuery(q1);

            // Edges
            SparqlQuery q2 = parser.ParseFromString("PREFIX : <https://id.parliament.uk/schema/> SELECT ?route ?fromStep ?toStep WHERE {?route a :ProcedureRoute; :procedureRouteIsFromProcedureStep ?fromStep; :procedureRouteIsToProcedureStep ?toStep.}");
            SparqlResultSet edges = (SparqlResultSet)graph.ExecuteQuery(q2);

            // Create GraphML 
            StringWriter sw = new Utf8StringWriter();
            XmlWriterSettings xws = new XmlWriterSettings();
            xws.OmitXmlDeclaration = false;
            xws.Indent = true;

            using (XmlWriter xw = XmlWriter.Create(sw, xws))
            {
                XNamespace ns = "http://graphml.graphdrawing.org/xmlns";
                XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
                XNamespace xsiSchemaLocation = "http://graphml.graphdrawing.org/xmlns http://www.yworks.com/xml/schema/graphml/1.1/ygraphml.xsd";
                // For yEd, not essential to GraphML. But good for verification
                XNamespace y = "http://www.yworks.com/xml/graphml";

                XDocument doc = new XDocument(
                    new XDeclaration("1.0", "UTF-8", "yes"),
                    new XElement(ns + "graphml",
                        new XAttribute(XNamespace.Xmlns + "xsi", xsi),
                        new XAttribute(xsi + "schemaLocation", xsiSchemaLocation),
                        new XAttribute(XNamespace.Xmlns + "y", y),
                        new XElement(ns + "key", new XAttribute("for", "node"), new XAttribute("id", "d0"), new XAttribute("yfiles.type", "nodegraphics")),
                        new XElement(ns + "graph", new XAttribute("id", "G"), new XAttribute("edgedefault", "directed"),
                            nodes.Select(n => new XElement(ns + "node", new XAttribute("id", n["step"]), new XElement(ns + "data", new XAttribute("name", n["stepName"]), new XAttribute("key", "d0"), new XElement(y + "ShapeNode", new XElement(y + "NodeLabel", new XAttribute("visible", "true"), new XAttribute("autoSizePolicy", "content"), new XText(n["stepName"].ToString())))))),
                            edges.Select(e => new XElement(ns + "edge", new XAttribute("id", e["route"]), new XAttribute("source", e["fromStep"]), new XAttribute("target", e["toStep"])))
                        )));
                doc.WriteTo(xw);
            }

            return Content(sw.ToString(), "application/xml");

        }

        // Using LINQ to XML, if you choose to write to stream instead of disk, the XML doc will automatically be encoded to match 
        // the stream's default encoding, in this case utf-16, which is not what we want
        // See https://blogs.msdn.microsoft.com/ericwhite/2010/03/08/serializing-encoded-xml-documents-using-linq-to-xml/
        private class Utf8StringWriter : StringWriter
        {
            public override Encoding Encoding { get { return Encoding.UTF8; } }
        }






    }
}