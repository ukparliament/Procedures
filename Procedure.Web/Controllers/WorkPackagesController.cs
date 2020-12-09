using Procedure.Web.Extensions;
using Procedure.Web.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web;
using System.Web.Mvc;

namespace Procedure.Web.Controllers
{
    [RoutePrefix("WorkPackages")]
    public class WorkPackagesController : BaseController
    {
        [Route]
        public ActionResult Index()
        {
            return ShowList<WorkPackageItem>(WorkPackageItem.ListSql);
        }

        [Route("{id:int}")]
        public ActionResult Details(int id)
        {
            WorkPackageDetailViewModel viewModel = new WorkPackageDetailViewModel();
            WorkPackageItem workPackage = getWorkPackage(id);
            if (workPackage.Id != 0)
            {
                viewModel.WorkPackage = workPackage;
                viewModel.BusinessItems = getAllBusinessItems(id);
                viewModel.Tree = giveMeTheTree(id, viewModel.WorkPackage.ProcedureId);
            }

            return View(viewModel);
        }

        [Route("{id:int}/graph")]
        public ActionResult GraphViz(int id)
        {
            GraphVizViewModel viewmodel = new GraphVizViewModel();
            viewmodel.DotString = GiveMeDotString(id, showLegend: true);

            return View(viewmodel);
        }

        [Route("{id:int}/graph.dot")]
        public ContentResult GraphDot(int id)
        {
            return Content(GiveMeDotString(id, showLegend: false), "text/plain");
        }

        private string GiveMeDotString(int workPackageId, bool showLegend)
        {
            WorkPackageItem workPackage = getWorkPackage(workPackageId);
            if (workPackage.Id != 0)
            {
                int procedureId = workPackage.ProcedureId;

                List<BusinessItem> businessItemList = getAllBusinessItems(workPackageId);
                int[] actualizedStepIds = businessItemList
                .SelectMany(bi => bi.ActualisesProcedureStep.Select(s => s.StepId))
                .ToArray();
                BusinessItemStep[] actualizedSteps = businessItemList
                .SelectMany(bi => bi.ActualisesProcedureStep).Distinct()
                .ToArray();

                List<RouteItem> routes = getAllRoutes(procedureId);
                List<int> redAlertStepIds = new List<int>();

                List<RouteItem> routesWithActualizedFromSteps = new List<RouteItem>();
                foreach (var route in routes.Where(route => actualizedStepIds.Contains(route.FromStepId)).ToList())
                {
                    var routeStartDate = route.StartDate == null ? DateTime.MinValue : route.StartDate;
                    var routeEndDate = route.EndDate == null ? DateTime.MaxValue : route.EndDate;

                    var fromStepDates = businessItemList.Where(bi => bi.ActualisesProcedureStep.Select(s => s.StepId).Contains(route.FromStepId)).Select(bi=>bi.Date);
                    var toStepDates = businessItemList.Where(bi => bi.ActualisesProcedureStep.Select(s => s.StepId).Contains(route.ToStepId)).Select(bi => bi.Date);
                     if (fromStepDates.Any())
                    {
                        var fromStepMaxDate = fromStepDates.Max() == null ? DateTime.MaxValue : fromStepDates.Max();
                        var fromStepMinDate = fromStepDates.Min() == null ? DateTime.MinValue : fromStepDates.Min();

                        if (toStepDates.Any())
                        {
                            var toStepMaxDate = toStepDates.Max() == null ? DateTime.MaxValue : toStepDates.Max();
                            var toStepMinDate = toStepDates.Min() == null ? DateTime.MinValue : toStepDates.Min();
                            if (routeStartDate <= fromStepMaxDate && routeEndDate >= fromStepMinDate && routeStartDate <= toStepMaxDate && routeEndDate >= toStepMinDate)
                            {
                                routesWithActualizedFromSteps.Add(route);
                                if (route.RouteKind == RouteType.Precludes)
                                {
                                    if (toStepMinDate > fromStepMaxDate)
                                        redAlertStepIds.Add(route.ToStepId);
                                }
                            }
                        }
                        else
                        {
                            if (routeStartDate <= fromStepMaxDate && routeEndDate >= fromStepMinDate && DateTime.Today <= routeEndDate)
                                routesWithActualizedFromSteps.Add(route);
                        }
                    }
                }
                
                List<RouteItem> routesWithActualizedToSteps = routes.Where(route => actualizedStepIds.Contains(route.ToStepId)).ToList();
                List<BusinessItemStep> orphanSteps = actualizedSteps.Where(step => !routesWithActualizedFromSteps.Select(route => route.FromStepId).Contains(step.StepId) &&
                                                        !routes.Select(route => route.ToStepId).Contains(step.StepId)).ToList();

                List<RouteItem> nonSelfReferencedRoutesWithBothEndsActualized = routesWithActualizedFromSteps.Where(route => actualizedStepIds.Contains(route.ToStepId) && actualizedStepIds.Contains(route.FromStepId) && route.FromStepId != route.ToStepId).ToList();
                List<RouteItem> precludeOrRequireRoutes = routes.Where(route => route.RouteKind == RouteType.Precludes || route.RouteKind == RouteType.Requires).ToList();
                List<RouteItem> routesWithStepsPrecludingThemselves = routes.Where(r => r.FromStepId == r.ToStepId && r.RouteKind == RouteType.Precludes).ToList();

                int[] allStepIds = routes.Select(r => r.FromStepId).Union(routes.Select(r => r.ToStepId)).Distinct().ToArray();
                int[] precludeSelfStepIds = routesWithStepsPrecludingThemselves.Select(r => r.FromStepId).ToArray();
                int[] canActualizeSelfAgainStepIds = allStepIds.Except(precludeSelfStepIds).ToArray();

                int[] blackOutFromStepIds = nonSelfReferencedRoutesWithBothEndsActualized.Select(r => r.FromStepId).ToArray();
                int[] blackOutToStepsIds = routesWithActualizedFromSteps.Where(r => r.RouteKind == RouteType.Precludes).Select(r => r.ToStepId).ToArray();

                IEnumerable<int> unBlackOut = routes.Except(precludeOrRequireRoutes).Except(routesWithActualizedToSteps).GroupBy(r => r.FromStepId).Select(group => new { fromStep = group.Key, routeCount = group.Count()}).Where(g => g.routeCount >= 1).Select(g => g.fromStep);

                StringBuilder builder = new StringBuilder("graph [fontname = \"calibri\"]; node[fontname = \"calibri\"]; edge[fontname = \"calibri\"];");

                foreach (RouteItem route in routesWithActualizedFromSteps)
                {
                    if (nonSelfReferencedRoutesWithBothEndsActualized.Contains(route) || (!blackOutToStepsIds.Contains(route.ToStepId) && !new[] { RouteType.Precludes, RouteType.Requires }.Contains(route.RouteKind)))
                    {
                        if (route.RouteKind == RouteType.Causes)
                        {
                            builder.Append($"\"{route.FromStepId}\" -> \"{route.ToStepId}\" [label = \"Causes\"]; ");
                        }
                        if (route.RouteKind == RouteType.Allows)
                        {
                            builder.Append($"edge [color=red]; \"{route.FromStepId}\" -> \"{route.ToStepId}\" [label = \"Allows\"]; edge [color=black];");
                        }
                        if (route.RouteKind == RouteType.Precludes)
                        {
                            builder.Append($"edge [color=blue]; \"{route.FromStepId}\" -> \"{route.ToStepId}\" [label = \"Precludes\"]; edge [color=black];");
                        }
                        if (route.RouteKind == RouteType.Requires)
                        {
                            builder.Append($"edge [color=yellow]; \"{route.FromStepId}\" -> \"{route.ToStepId}\" [label = \"Requires\"]; edge [color=black];");
                        }
                    }
                    if (!blackOutFromStepIds.Except(unBlackOut).Contains(route.FromStepId) && !blackOutToStepsIds.Contains(route.ToStepId) && !new[] { RouteType.Precludes, RouteType.Requires }.Contains(route.RouteKind))
                    {
                        string str = $"\"{route.ToStepId}\" [label=\"{route.ToStepName.ProcessName()}\",style=filled,fillcolor=white,color=orange,peripheries=2];";
                        builderAppendIfNotFound(builder, str);
                    }
                    if (actualizedStepIds.Contains(route.FromStepId))
                    {
                        string str = $"\"{route.FromStepId}\" [label=\"{route.FromStepName.ProcessName()}\",style=filled,color=gray];";
                        builderAppendIfNotFound(builder, str);
                    }

                    if (actualizedStepIds.Contains(route.ToStepId) && canActualizeSelfAgainStepIds.Contains(route.ToStepId))
                    {
                        string findString = $"\"{route.ToStepId}\" [label=\"{route.ToStepName.ProcessName()}\",style=filled,fillcolor=white,color=orange,peripheries=2];";
                        string replaceString = $"\"{route.ToStepId}\" [label=\"{route.ToStepName.ProcessName()}\",style=filled,color=lemonchiffon2];";

                        builderReplaceIfNotFound(builder, findString, replaceString);
                    }

                    if (actualizedStepIds.Contains(route.FromStepId) && canActualizeSelfAgainStepIds.Contains(route.FromStepId))
                    {
                        string findString = $"\"{route.FromStepId}\" [label=\"{route.FromStepName.ProcessName()}\",style=filled,color=gray];";
                        string replaceString = $"\"{route.FromStepId}\" [label=\"{route.FromStepName.ProcessName()}\",style=filled,color=lemonchiffon2];";

                        builderReplaceIfNotFound(builder, findString, replaceString);
                    }

                    if (redAlertStepIds.Contains(route.ToStepId))
                    {
                        string findString = $"\"{route.ToStepId}\" [label=\"{route.ToStepName.ProcessName()}\",style=filled,color=gray];";
                        string replaceString = $"\"{route.ToStepId}\" [label=\"{route.ToStepName.ProcessName()}\",style=filled,color=red];";

                        builderReplaceIfNotFound(builder, findString, replaceString);
                    }
                    if (redAlertStepIds.Contains(route.FromStepId))
                    {
                        string findString = $"\"{route.FromStepId}\" [label=\"{route.FromStepName.ProcessName()}\",style=filled,color=gray];";
                        string replaceString = $"\"{route.FromStepId}\" [label=\"{route.FromStepName.ProcessName()}\",style=filled,color=red];";

                        builderReplaceIfNotFound(builder, findString, replaceString);
                    }
                }

                foreach(var step in orphanSteps)
                {
                    builder.Append($"\"{step.StepName.ProcessName()}\" [style=filled,color=gray];");
                }

                builder.Append($"labelloc=\"t\"; fontsize = \"25\"; label = \"{HttpUtility.HtmlEncode(workPackage.Title)} \\n Subject to: {workPackage.ProcedureName}\"");

                if (showLegend == true)
                {
                    builder.Append("subgraph cluster_key {" +
                    "label=\"Key\"; labeljust=\"l\" " +
                    "k1[label=\"Actualised step\", style=filled, color=gray]" +
                    "k2[label=\"Actualised step that can be actualised again\", style=filled, color=lemonchiffon2]" +
                    "k3[label=\"Possible next step yet to be actualised\" style=filled,fillcolor=white, color=orange, peripheries=2]; node [shape=plaintext];" +
                    "ktable [label=<<table border=\"0\" cellpadding=\"2\" cellspacing=\"0\" cellborder=\"0\"> " +
                    "<tr><td align=\"right\" port=\"i1\" > Causes </td></tr>" +
                    "<tr><td align=\"right\" port=\"i2\"> Allows </td></tr>" +
                    "<tr><td align=\"right\" port=\"i3\" > Precludes </td></tr>" +
                    "<tr><td align=\"right\" port=\"i4\" > Requires </td></tr> </table>>];" +
                    "ktabledest [label =<<table border=\"0\" cellpadding=\"2\" cellspacing=\"0\" cellborder=\"0\">" +
                    "<tr><td port=\"i1\" > &nbsp;</td></tr> <tr><td port=\"i2\"> &nbsp;</td></tr> <tr><td port=\"i3\"> &nbsp;</td></tr> <tr><td port=\"i4\"> &nbsp;</td></tr> </table>>];" +
                    "ktable:i1:e->ktabledest:i1:w ktable:i2:e->ktabledest:i2:w [color=red] ktable:i3:e->ktabledest:i3:w [color = blue] ktable:i4:e->ktabledest:i4:w [color = yellow] {rank = sink; k1 k2 k3}  { rank = same; ktable ktabledest } };");
                }

                builder.Insert(0, "digraph{");
                builder.Append("}");

                return builder.ToString();
            }
            else
            {
                return "";
            }
        }

        private void builderAppendIfNotFound (StringBuilder builder, string str)
        {
            if (!builder.ToString().Contains(str))
                builder.Append(str);
        }
        private void builderReplaceIfNotFound(StringBuilder builder, string findString, string replaceString)
        {
            if (builder.ToString().Contains(findString))
            {
                builder.Replace(findString, replaceString);
            }
            else
            {
                if (!builder.ToString().Contains(replaceString))
                    builder.Append(replaceString);
            }
        }
        private List<WorkPackageRouteTree> giveMeTheTree(int workPackageId, int procedureId)
        {
            List<WorkPackageRouteTree> result = new List<WorkPackageRouteTree>();

            List<BusinessItem> allBusinessItems = getAllBusinessItems(workPackageId);

            int[] stepsDone = allBusinessItems
                .SelectMany(bi => bi.ActualisesProcedureStep.Select(s => s.StepId))
                .ToArray();

            List<ProcedureRouteTree> procedureTree = GenerateProcedureTree(procedureId);
            List<int> precludedSteps = giveMePrecludedSteps(null, stepsDone, procedureTree);

            foreach (ProcedureRouteTree procedureRouteTreeItem in procedureTree)
            {
                List<BusinessItem> businessItems = allBusinessItems
                    .Where(bi => bi.ActualisesProcedureStep.Any(s => s.StepId == procedureRouteTreeItem.Step.Id))
                    .ToList();
                if (businessItems.Any())
                {
                    foreach (BusinessItem businessItem in allBusinessItems.Where(bi => businessItems.Exists(b => b.Id == bi.Id)))
                        businessItem.ActualisesProcedureStep.RemoveAll(s => s.StepId== procedureRouteTreeItem.Step.Id);
                    allBusinessItems.RemoveAll(bi => bi.ActualisesProcedureStep.Any() == false);
                    result.Add(new WorkPackageRouteTree()
                    {
                        BusinessItems = businessItems,
                        IsPrecluded = precludedSteps.Contains(procedureRouteTreeItem.Step.Id),
                        SelfReferencedRouteKind = procedureRouteTreeItem.SelfReferencedRouteKind,
                        RouteKind = procedureRouteTreeItem.RouteKind,
                        Step = procedureRouteTreeItem.Step,
                        ChildrenRoutes = giveMeFilteredChildren(allBusinessItems, procedureRouteTreeItem.ChildrenRoutes, precludedSteps)
                    });
                }
            }

            return result;
        }

        private List<WorkPackageRouteTree> giveMeFilteredChildren(List<BusinessItem> allBusinessItems, List<ProcedureRouteTree> procedureTree, List<int> precludedSteps)
        {
            List<WorkPackageRouteTree> result = new List<WorkPackageRouteTree>();

            foreach (ProcedureRouteTree procedureRouteTreeItem in procedureTree)
            {
                List<BusinessItem> businessItems = allBusinessItems
                    .Where(bi => bi.ActualisesProcedureStep.Any(s => s.StepId == procedureRouteTreeItem.Step.Id))
                    .ToList();
                bool isPrecluded = precludedSteps.Contains(procedureRouteTreeItem.Step.Id);
                foreach (BusinessItem businessItem in allBusinessItems.Where(bi => businessItems.Exists(b => b.Id == bi.Id)))
                    businessItem.ActualisesProcedureStep.RemoveAll(s => s.StepId == procedureRouteTreeItem.Step.Id);
                allBusinessItems.RemoveAll(bi => bi.ActualisesProcedureStep.Any() == false);
                result.Add(new WorkPackageRouteTree()
                {
                    BusinessItems = businessItems,
                    IsPrecluded = isPrecluded,
                    SelfReferencedRouteKind = procedureRouteTreeItem.SelfReferencedRouteKind,
                    RouteKind = procedureRouteTreeItem.RouteKind,
                    Step = procedureRouteTreeItem.Step,
                    ChildrenRoutes = isPrecluded ? new List<WorkPackageRouteTree>() : giveMeFilteredChildren(allBusinessItems, procedureRouteTreeItem.ChildrenRoutes, precludedSteps)
                });
            }
            return result;
        }

        private List<int> giveMePrecludedSteps(int? parentStepId, int[] stepsDone, List<ProcedureRouteTree> procedureTree)
        {
            List<int> result = new List<int>();
            foreach (ProcedureRouteTree procedureRouteTreeItem in procedureTree)
            {
                if ((procedureRouteTreeItem.RouteKind == RouteType.Precludes) &&
                    (parentStepId.HasValue) && (stepsDone.Contains(parentStepId.Value)))
                    result.Add(procedureRouteTreeItem.Step.Id);
                if (stepsDone.Contains(procedureRouteTreeItem.Step.Id))
                    result.AddRange(giveMePrecludedSteps(procedureRouteTreeItem.Step.Id, stepsDone, procedureRouteTreeItem.ChildrenRoutes));
            }

            return result;
        }

        
    }
}