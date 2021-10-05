using Parliament.Model;
using System.Collections.Generic;

namespace Procedure.Web.Models
{
    public class ParsedRoute : BaseSharepointItem
    {
        //ID Source step Target step Current Parse pass count Parsed  Status
        public string FromStepName { get; set; }
        public string ToStepName { get; set; }
        public int Iteration { get; set; }
        public string Status { get; set; }
    }
}