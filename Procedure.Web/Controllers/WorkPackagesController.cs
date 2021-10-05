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

        [Route("{id:int}/stepreport")]
        public ActionResult ProcedureStepReport(int id)
        {
            var viewmodel = GenerateProcedureStepReport(id);

            return View(viewmodel);
        }

        [Route("{id:int}/graph.dot")]
        public ContentResult GraphDot(int id)
        {
            return Content(GiveMeDotString(id, showLegend: false), "text/plain");
        }

        private StepCurrentState SetStepCurrentState(int StepTypeId, int StepId, int[] actualizedStepIds, List<BusinessItem> businessItemList)
        {
            StepCurrentState stepCurrentState;
            if (StepTypeId == 1)
            {
                if (actualizedStepIds.Contains(StepId))
                {
                    var bItems = businessItemList.Where(bi => bi.ActualisesProcedureStep.Select(step => step.StepId).Contains(StepId));
                    if (bItems.Count() == 1 && bItems.First().Date == null)
                    {
                        stepCurrentState = StepCurrentState.WithoutDate;
                    }
                    else
                    {
                        stepCurrentState = StepCurrentState.ScheduledToHappen;
                        foreach (var bItem in bItems)
                        {
                            if ((bItem.Date != null && bItem.Date <= DateTime.Today) || (bItem.Date == null))
                            {
                                stepCurrentState = StepCurrentState.Happened;
                                break;
                            }
                        }
                    }
                }
                else
                {
                    stepCurrentState = StepCurrentState.NotActualised;
                }
            }
            else
            {
                stepCurrentState = StepCurrentState.NonBusinessStep;
            }
            return stepCurrentState;
        }

        private void SetRouteStepState(IEnumerable<RouteItem> routes, int[] actualizedStepIds, List<BusinessItem> businessItemList)
        {
            foreach (RouteItem route in routes)
            {
                route.FromStepCurrentState = SetStepCurrentState(route.FromStepTypeId, route.FromStepId, actualizedStepIds, businessItemList);
                route.ToStepCurrentState = SetStepCurrentState(route.ToStepTypeId, route.ToStepId, actualizedStepIds, businessItemList);
                route.FromStepPotentialState = StepPotentialState.UnParsed;
                route.ToStepPotentialState = StepPotentialState.UnParsed;
            }
        }

        private List<ParsedRoute> PopulateRouteAndStepState(IEnumerable<RouteItem> routes, ref int depth)
        {
            List<ParsedRoute> parsedRoutes = new List<ParsedRoute>();
            bool ChangeMade = false;
            foreach (var route in routes)
            {
                RouteStatus newRouteStatus = route.RouteStatus;
                StepPotentialState newToStepStatus = route.ToStepPotentialState;

                if (route.FromStepTypeId == 1) // business step
                {
                    if (route.FromStepCurrentState == StepCurrentState.Happened)
                    {
                        newRouteStatus = RouteStatus.True;
                    }
                    else if (route.FromStepCurrentState == StepCurrentState.ScheduledToHappen || route.FromStepCurrentState == StepCurrentState.WithoutDate)
                    {
                        newRouteStatus = RouteStatus.Null;
                    }
                    else if (route.FromStepCurrentState == StepCurrentState.NotActualised)
                    {
                        newRouteStatus = RouteStatus.Null;
                    }
                }
                else if (route.FromStepTypeId == 4) // AND step
                {
                    var inputRoutes = routes.Where(r => r.ToStepId == route.FromStepId).ToList();
                    if (inputRoutes.Count() == 2)
                    {
                        if (inputRoutes[0].RouteStatus == RouteStatus.True && inputRoutes[1].RouteStatus == RouteStatus.True)
                        {
                            newRouteStatus = RouteStatus.True;
                        }
                        else if ((inputRoutes[0].RouteStatus == RouteStatus.False && inputRoutes[1].RouteStatus == RouteStatus.True) ||
                            (inputRoutes[0].RouteStatus == RouteStatus.True && inputRoutes[1].RouteStatus == RouteStatus.False) ||
                            (inputRoutes[0].RouteStatus == RouteStatus.False && inputRoutes[1].RouteStatus == RouteStatus.False))
                        {
                            newRouteStatus = RouteStatus.False;
                        }
                        else if ((inputRoutes[0].RouteStatus == RouteStatus.True && inputRoutes[1].RouteStatus == RouteStatus.Null) ||
                            (inputRoutes[0].RouteStatus == RouteStatus.Null && inputRoutes[1].RouteStatus == RouteStatus.True))
                        {
                            newRouteStatus = RouteStatus.True;
                        }
                        else if ((inputRoutes[0].RouteStatus == RouteStatus.False && inputRoutes[1].RouteStatus == RouteStatus.Null) ||
                            (inputRoutes[0].RouteStatus == RouteStatus.Null && inputRoutes[1].RouteStatus == RouteStatus.False))
                        {
                            newRouteStatus = RouteStatus.False;
                        }
                        else if (inputRoutes[0].RouteStatus == RouteStatus.Null && inputRoutes[1].RouteStatus == RouteStatus.Null)
                        {
                            newRouteStatus = RouteStatus.Null;
                        }
                    }

                }
                else if (route.FromStepTypeId == 5) // OR step
                {
                    var inputRoutes = routes.Where(r => r.ToStepId == route.FromStepId).ToList();
                    if (inputRoutes.Count() == 2)
                    {
                        if (inputRoutes[0].RouteStatus == RouteStatus.True && inputRoutes[1].RouteStatus == RouteStatus.True)
                        {
                            newRouteStatus = RouteStatus.True;
                        }
                        else if ((inputRoutes[0].RouteStatus == RouteStatus.False && inputRoutes[1].RouteStatus == RouteStatus.True) ||
                            (inputRoutes[0].RouteStatus == RouteStatus.True && inputRoutes[1].RouteStatus == RouteStatus.False))
                        {
                            newRouteStatus = RouteStatus.True;
                        }
                        else if (inputRoutes[0].RouteStatus == RouteStatus.False && inputRoutes[1].RouteStatus == RouteStatus.False)
                        {
                            newRouteStatus = RouteStatus.False;
                        }
                        else if ((inputRoutes[0].RouteStatus == RouteStatus.True && inputRoutes[1].RouteStatus == RouteStatus.Null) ||
                            (inputRoutes[0].RouteStatus == RouteStatus.Null && inputRoutes[1].RouteStatus == RouteStatus.True))
                        {
                            newRouteStatus = RouteStatus.True;
                        }
                        else if ((inputRoutes[0].RouteStatus == RouteStatus.False && inputRoutes[1].RouteStatus == RouteStatus.Null) ||
                            (inputRoutes[0].RouteStatus == RouteStatus.Null && inputRoutes[1].RouteStatus == RouteStatus.False))
                        {
                            newRouteStatus = RouteStatus.False;
                        }
                        else if (inputRoutes[0].RouteStatus == RouteStatus.Null && inputRoutes[1].RouteStatus == RouteStatus.Null)
                        {
                            newRouteStatus = RouteStatus.Null;
                        }
                    }
                }
                else if (route.FromStepTypeId == 3) // NOT step
                {
                    var inputRoutes = routes.Where(r => r.ToStepId == route.FromStepId).ToList();
                    if (inputRoutes.Count() == 1)
                    {
                        if (inputRoutes[0].RouteStatus == RouteStatus.True)
                        {
                            newRouteStatus = RouteStatus.False;
                        }
                        else if (inputRoutes[0].RouteStatus == RouteStatus.False)
                        {
                            newRouteStatus = RouteStatus.True;
                        }
                        else if (inputRoutes[0].RouteStatus == RouteStatus.Null)
                        {
                            newRouteStatus = RouteStatus.Null;
                        }
                    }
                }
                else if (route.FromStepTypeId == 2) // Decision step
                {
                    var inputRoutes = routes.Where(r => r.ToStepId == route.FromStepId).ToList();
                    if (inputRoutes.Count() == 1)
                    {
                        if (inputRoutes[0].RouteStatus == RouteStatus.True)
                        {
                            newRouteStatus = RouteStatus.Allows;
                        }
                        else if (inputRoutes[0].RouteStatus == RouteStatus.False)
                        {
                            newRouteStatus = RouteStatus.False;
                        }
                        else if (inputRoutes[0].RouteStatus == RouteStatus.Null)
                        {
                            newRouteStatus = RouteStatus.Null;
                        }
                    }
                }
                if (route.ToStepTypeId == 1)//business step
                {
                    if (route.RouteStatus == RouteStatus.True)
                    {
                        newToStepStatus = StepPotentialState.CausedToBeActualised;
                    }
                    else if (route.RouteStatus == RouteStatus.Allows)
                    {
                        newToStepStatus = StepPotentialState.AllowedToBeActualised;
                    }
                    else if (route.RouteStatus == RouteStatus.Null || route.RouteStatus == RouteStatus.False)
                    {
                        newToStepStatus = StepPotentialState.NotYetActualisable;
                    }
                }

                if (newRouteStatus != route.RouteStatus)
                {
                    route.RouteStatus = newRouteStatus;
                    ChangeMade = true;
                    ParsedRoute pr = new ParsedRoute() { FromStepName = route.FromStepName, ToStepName = route.ToStepName, Status = Enum.GetName(typeof(RouteStatus), newRouteStatus), Id = route.Id, Iteration = depth+1 };
                    parsedRoutes.Add(pr);
                }
                if(newToStepStatus != route.ToStepPotentialState)
                {
                    route.ToStepPotentialState = newToStepStatus;
                    ChangeMade = true;
                }
            }
            if (ChangeMade)
            {
                depth ++;
                parsedRoutes.AddRange(PopulateRouteAndStepState(routes, ref depth));
            }
            return parsedRoutes;
        }

        private void GetRoutesDotString(List<RouteItem> routes, StringBuilder builder)
        {
            foreach (RouteItem route in routes)
            {
                string edgeStyle = null;
                if (route.RouteStatus == RouteStatus.UNTRAVSERSABLE)
                    edgeStyle = "style=dotted, color=black";
                else if (route.RouteStatus == RouteStatus.Allows)
                    edgeStyle = "style=solid, color=green";
                else if (route.RouteStatus == RouteStatus.True)
                    edgeStyle = "style=solid, color=red";
                else if (route.RouteStatus == RouteStatus.False)
                    edgeStyle = "style=solid, color=black";
                else if (route.RouteStatus == RouteStatus.Null)
                    edgeStyle = "style=solid, color=yellow";
                else if (route.RouteStatus == RouteStatus.UnParsed)
                    edgeStyle = "style=solid, color=gray";
                if (edgeStyle != null)
                {
                    builder.Append($"edge [{edgeStyle}];\"{route.FromStepId}\"->\"{route.ToStepId}\";");
                }
            }
        }

        private void GetStepsDotString(List<RouteItem> routes, StringBuilder builder)
        {
            var uniqueSteps = routes.Select(r => (r.FromStepId, r.FromStepTypeId, r.FromStepName.ProcessName(), r.FromStepTypeId == 1 ? "" : r.FromStepTypeName.ProcessName(), r.FromStepHouseName, r.FromStepCurrentState, r.FromStepPotentialState)).
                Union(routes.Select(r => (r.ToStepId, r.ToStepTypeId, r.ToStepName.ProcessName(), r.ToStepTypeId == 1 ? "" : r.ToStepTypeName.ProcessName(), r.ToStepHouseName, r.ToStepCurrentState, r.ToStepPotentialState))).Distinct().ToArray();

            foreach ((int id, int typeId, string stepName, string stepTypeName, string houseName, StepCurrentState currentState, StepPotentialState potentialState) in uniqueSteps)
            {
                string tempStr = "";
                string shape = null;
                string fillcolor = null;
                switch (currentState)
                {
                    case StepCurrentState.Happened:
                        shape = "ellipse";
                        break;
                    case StepCurrentState.ScheduledToHappen:
                        shape = "rect";
                        break;
                    case StepCurrentState.WithoutDate:
                        shape = "trapezium";
                        break;
                    case StepCurrentState.NotActualised:
                        shape = "parallelogram";
                        break;
                }

                switch (potentialState)
                {
                    case StepPotentialState.AllowedToBeActualised:
                        fillcolor = "green";
                        break;
                    case StepPotentialState.CausedToBeActualised:
                        fillcolor = "yellow";
                        break;
                    case StepPotentialState.NotYetActualisable:
                        fillcolor = "orange";
                        break;
                    case StepPotentialState.UnParsed:
                        fillcolor = "gray";
                        break;
                }

                if (currentState == StepCurrentState.NonBusinessStep)
                {
                    tempStr = $"\"{id}\" [label=\"{stepName}{stepTypeName}({houseName})\", style=dotted];";
                }
                else
                {
                    tempStr = $"\"{id}\" [label=\"{stepName}{stepTypeName}({houseName})\", style=\"filled,bold\",shape={shape},fillcolor={fillcolor}];";
                }

                builder.Append(tempStr.Replace("()", ""));
            }
        }

        private void GetLegend(bool showLegend, StringBuilder builder)
        {
            if (showLegend == true)
            {
                var str = @"  subgraph cluster_01 { 
    label = ""Legend"";
    key [label=<<table border=""0"" cellpadding=""2"" cellspacing=""0"" cellborder=""0"">
      <tr><td align=""right"" port=""i1"">Allows</td></tr>
      <tr><td align=""right"" port=""i2"">True</td></tr>
      <tr><td align=""right"" port=""i3"">False</td></tr>
      <tr><td align=""right"" port=""i4"">Null</td></tr>
      <tr><td align=""right"" port=""i5"">Not Current</td></tr>
      <tr><td align=""right"" port=""i6"">Unparsed</td></tr>
      </table>>]
    key2 [label=<<table border=""0"" cellpadding=""2"" cellspacing=""0"" cellborder=""0"">
      <tr><td port=""i1"">&nbsp;</td></tr>
      <tr><td port=""i2"">&nbsp;</td></tr>
      <tr><td port=""i3"">&nbsp;</td></tr>
      <tr><td port=""i4"">&nbsp;</td></tr>
      <tr><td port=""i5"">&nbsp;</td></tr>
      <tr><td port=""i6"">&nbsp;</td></tr>
      </table>>]
    key:i1:e -> key2:i1:w [color=green]
    key:i2:e -> key2:i2:w [color=red]
    key:i3:e -> key2:i3:w [color=black]
    key:i4:e -> key2:i4:w [color=yellow]
    key:i5:e -> key2:i5:w [color=black, style=dotted]
    key:i6:e -> key2:i6:w [color=gray]
    
    happened [shape=ellipse, label= ""Happened""] ;
    scheduledToHappen [shape=rect, label= ""ScheduledToHappen""] ;
    withoutDate [shape=trapezium, label= ""WithoutDate""] ;
    notActualised [shape=parallelogram, label= ""NotActualised""] ;
    allowedToBeActualised [style=filled, fillcolor=""green"", label= ""AllowedToBeActualised"", color=""white""] ;
    causedToBeActualised [style=filled, fillcolor=""yellow"", label= ""CausedToBeActualised"", color=""white""] ;
    notActualisable [style=filled, fillcolor=""orange"", label= ""NotActualisable"", color=""white""] ;
    unParsed [style=filled, fillcolor=""gray"", label= ""UnParsed"", color=""white""] ;
    { rank = source; key key2}
  }
";
                builder.Append(str.Replace("\n", "").Replace("\r", ""));
            }
        }

        private WorkPackageStepReportViewModel GenerateProcedureStepReport(int workPackageId)
        {
            WorkPackageStepReportViewModel model = new WorkPackageStepReportViewModel();

            WorkPackageItem workPackage = getWorkPackage(workPackageId);
            model.WorkPackage = workPackage;

            if (workPackage.Id != 0)
            {
                IEnumerable<StepItem> stepList = getAllSteps(workPackage.ProcedureId);
                IEnumerable<StepItem> businessStepList = stepList.Where(s => s.StepTypeId == 1);

                IEnumerable<BusinessItem> businessItemList = getAllBusinessItems(workPackageId);
                BusinessItemStep[] actualizedSteps = businessItemList
                .SelectMany(bi => bi.ActualisesProcedureStep).Distinct()
                .ToArray();

                List<BusinessItem> happenedBusinessItems = new List<BusinessItem>();
                List<BusinessItem> scheduledToHappenBusinessItems = new List<BusinessItem>();
                List<BusinessItem> mayHappenBusinessItems = new List<BusinessItem>();
                foreach (var bi in businessItemList.OrderBy(b => b.Date))
                {
                    if (!string.IsNullOrWhiteSpace(bi.Weblink))
                    {
                        bi.WeblinkText = new Uri(bi.Weblink).Host;
                    }
                    foreach (var step in bi.ActualisesProcedureStep)
                    {
                        step.HouseName = stepList.Single(s => s.Id == step.StepId).Houses.First().HouseName;
                        if (step.HouseName.Contains(","))
                            step.HouseName = "House of Commons and House of Lords";
                        else if (string.IsNullOrWhiteSpace(step.HouseName))
                            step.HouseName = "Neither House";
                    }
                    if (bi.Date <= DateTime.Now)
                        happenedBusinessItems.Add(bi);
                    else if (bi.Date > DateTime.Now)
                        scheduledToHappenBusinessItems.Add(bi);
                    else
                        mayHappenBusinessItems.Add(bi);
                }

                model.HappenedBusinessItems = happenedBusinessItems;
                model.ScheduledToHappenBusinessItems = scheduledToHappenBusinessItems;
                model.MayHappenBusinessItems = mayHappenBusinessItems;

                List<RouteItem> routes = getAllRoutes(workPackage.ProcedureId);

                if (routes.Any())
                {
                    foreach (RouteItem route in routes)
                    {
                        if ((route.StartDate != null && route.StartDate > DateTime.Now) || (route.EndDate != null && route.EndDate < DateTime.Now))
                        {
                            route.RouteStatus = RouteStatus.UNTRAVSERSABLE;
                        }
                        else
                        {
                            route.RouteStatus = RouteStatus.UnParsed;
                        }
                    }

                    int[] actualizedStepIds = businessItemList
                        .SelectMany(bi => bi.ActualisesProcedureStep.Select(s => s.StepId))
                        .ToArray();
                    SetRouteStepState(routes, actualizedStepIds, businessItemList.ToList());

                    int depth = 0;
                    var parsedRoutes = PopulateRouteAndStepState(routes.Where(route => route.RouteStatus != RouteStatus.UNTRAVSERSABLE), ref depth);
                    List<StepItem> causedToBeActualisedSteps = new List<StepItem>();
                    List<StepItem> allowedToBeActualisedSteps = new List<StepItem>();
                    List<StepItem> notYetActualisedSteps = new List<StepItem>();
                    foreach (RouteItem route in routes)
                    {
                        switch (route.FromStepPotentialState)
                        {
                            case (StepPotentialState.CausedToBeActualised):
                                if (!causedToBeActualisedSteps.Select(s => s.Id).Contains(route.FromStepId))
                                {
                                    causedToBeActualisedSteps.Add(stepList.Single(s => s.Id == route.FromStepId));
                                }
                                break;
                            case (StepPotentialState.AllowedToBeActualised):
                                if (!allowedToBeActualisedSteps.Select(s => s.Id).Contains(route.FromStepId))
                                {
                                    allowedToBeActualisedSteps.Add(stepList.Single(s => s.Id == route.FromStepId));
                                }
                                break;
                            case (StepPotentialState.NotYetActualisable):
                                if (!notYetActualisedSteps.Select(s => s.Id).Contains(route.FromStepId))
                                {
                                    notYetActualisedSteps.Add(stepList.Single(s => s.Id == route.FromStepId));
                                }
                                break;
                            default:
                                break;
                        }

                        switch (route.ToStepPotentialState)
                        {
                            case (StepPotentialState.CausedToBeActualised):
                                if (!causedToBeActualisedSteps.Select(s => s.Id).Contains(route.ToStepId))
                                {
                                    causedToBeActualisedSteps.Add(stepList.Single(s => s.Id == route.ToStepId));
                                }
                                break;
                            case (StepPotentialState.AllowedToBeActualised):
                                if (!allowedToBeActualisedSteps.Select(s => s.Id).Contains(route.ToStepId))
                                {
                                    allowedToBeActualisedSteps.Add(stepList.Single(s => s.Id == route.ToStepId));
                                }
                                break;
                            case (StepPotentialState.NotYetActualisable):
                                if (!notYetActualisedSteps.Select(s => s.Id).Contains(route.ToStepId))
                                {
                                    notYetActualisedSteps.Add(stepList.Single(s => s.Id == route.ToStepId));
                                }
                                break;
                            default:
                                break;
                        }
                    }
                    model.AllowedToBeActualisedSteps = allowedToBeActualisedSteps.Where(x => !actualizedStepIds.Contains(x.Id)).ToList();
                    model.CausedToBeActualisedSteps = causedToBeActualisedSteps.Where(x => !actualizedStepIds.Contains(x.Id)).ToList();
                    model.NotYetActualisedSteps = notYetActualisedSteps.Where(x => !actualizedStepIds.Contains(x.Id)).ToList();
                    model.UntraversableSteps = stepList.Where(s => s.StepTypeId == 1)
                                                        .Except(model.AllowedToBeActualisedSteps)
                                                        .Except(model.CausedToBeActualisedSteps)
                                                        .Except(model.NotYetActualisedSteps)
                                                        .Where(x=>!actualizedStepIds.Contains(x.Id))
                                                        .ToList();
                    model.ParsedRoutes = parsedRoutes;
                }
            }
            return model;
        }

        private string GiveMeDotString(int workPackageId, bool showLegend)
        {
            WorkPackageItem workPackage = getWorkPackage(workPackageId);
            if (workPackage.Id != 0)
            {
                List<BusinessItem> businessItemList = getAllBusinessItems(workPackageId);
                int[] actualizedStepIds = businessItemList
                .SelectMany(bi => bi.ActualisesProcedureStep.Select(s => s.StepId))
                .ToArray();
                BusinessItemStep[] actualizedSteps = businessItemList
                .SelectMany(bi => bi.ActualisesProcedureStep).Distinct()
                .ToArray();

                int procedureId = workPackage.ProcedureId;
                List<RouteItem> routes = getAllRoutes(procedureId);

                if (routes.Any())
                {
                    StringBuilder builder = new StringBuilder("graph[fontname=\"calibri\"];node[fontname=\"calibri\"];edge[fontname=\"calibri\"];");

                    foreach (RouteItem route in routes)
                    {
                        if ((route.StartDate != null && route.StartDate > DateTime.Now) || (route.EndDate != null && route.EndDate < DateTime.Now))
                        {
                            route.RouteStatus = RouteStatus.UNTRAVSERSABLE;
                        }
                        else
                        {
                            route.RouteStatus = RouteStatus.UnParsed;
                        }
                    }

                    SetRouteStepState(routes, actualizedStepIds, businessItemList);

                    int depth = 0;
                    PopulateRouteAndStepState(routes.Where(route => route.RouteStatus != RouteStatus.UNTRAVSERSABLE), ref depth);

                    GetRoutesDotString(routes, builder);

                    GetStepsDotString(routes, builder);

                    GetLegend(showLegend, builder);

                    builder.Insert(0, "digraph{node [shape=plaintext] ");
                    builder.Append("}");

                    return builder.ToString();
                }
                else
                {
                    return string.Empty;
                }
            }
            else
            {
                return string.Empty;
            }
        }

        private string GiveMeDotStringOld(int workPackageId, bool showLegend)
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
                                //if (route.RouteKind == RouteType.Precludes)
                                //{
                                //    if (toStepMinDate > fromStepMaxDate)
                                //        redAlertStepIds.Add(route.ToStepId);
                                //}
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
                    if (nonSelfReferencedRoutesWithBothEndsActualized.Contains(route) || (!blackOutToStepsIds.Contains(route.ToStepId)))// && !new[] { RouteType.Precludes, RouteType.Requires }.Contains(route.RouteKind)))
                    {
                        //if (route.RouteKind == RouteType.Causes)
                        //{
                            builder.Append($"\"{route.FromStepId}\" -> \"{route.ToStepId}\" [label = \"Causes\"]; ");
                        //}
                        //if (route.RouteKind == RouteType.Allows)
                        //{
                        //    builder.Append($"edge [color=red]; \"{route.FromStepId}\" -> \"{route.ToStepId}\" [label = \"Allows\"]; edge [color=black];");
                        //}
                        //if (route.RouteKind == RouteType.Precludes)
                        //{
                        //    builder.Append($"edge [color=blue]; \"{route.FromStepId}\" -> \"{route.ToStepId}\" [label = \"Precludes\"]; edge [color=black];");
                        //}
                        //if (route.RouteKind == RouteType.Requires)
                        //{
                        //    builder.Append($"edge [color=yellow]; \"{route.FromStepId}\" -> \"{route.ToStepId}\" [label = \"Requires\"]; edge [color=black];");
                        //}
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