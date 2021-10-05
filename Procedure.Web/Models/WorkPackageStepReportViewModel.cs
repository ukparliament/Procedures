using System.Collections.Generic;

namespace Procedure.Web.Models
{
    public class WorkPackageStepReportViewModel
    {
        public WorkPackageItem WorkPackage { get; set; }
        public List<WorkPackageRouteTree> Tree { get; set; }
        public List<BusinessItem> BusinessItems { get; set; }
        public List<BusinessItem> HappenedBusinessItems { get; set; }
        public List<BusinessItem> ScheduledToHappenBusinessItems { get; set; }
        public List<BusinessItem> MayHappenBusinessItems { get; set; }
        public List<StepItem> CausedToBeActualisedSteps { get; set; }
        public List<StepItem> AllowedToBeActualisedSteps { get; set; }
        public List<StepItem> NotYetActualisedSteps { get; set; }
        public List<StepItem> UntraversableSteps { get; set; }
        public List<ParsedRoute> ParsedRoutes { get; set; }
    }
}