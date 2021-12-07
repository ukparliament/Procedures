using Parliament.Model;
using System;

namespace Procedure.Web.Models
{
    public class RouteItem
    {
        public int Id { get; set; }

        public string TripleStoreId { get; set; }

        public int ActualisationCount { get; set; }
        public int FromStepId { get; set; }
        //public int FromStepActualisationCount { get; set; }
        public int FromStepTypeId { get; set; }

        public StepCurrentState FromStepCurrentState { get; set; }
        public StepPotentialState FromStepPotentialState { get; set; }
        public string FromStepTripleStoreId { get; set; }

        public string FromStepName { get; set; }
        public string FromStepTypeName { get; set; }

        public string FromStepHouseName { get; set; }

        public int ToStepId { get; set; }
        //public int ToStepActualisationCount { get; set; }
        public int ToStepTypeId { get; set; }
        public string ToStepTripleStoreId { get; set; }
        public StepCurrentState ToStepCurrentState { get; set; }
        public StepPotentialState ToStepPotentialState { get; set; }
        public string ToStepName { get; set; }
        public string ToStepTypeName { get; set; }

        public string ToStepHouseName { get; set; }

        public string RouteTypeName { get; set; }
        public DateTimeOffset? StartDate { get; set; }
        public DateTimeOffset? EndDate { get; set; }
        public RouteStatus RouteStatus { get; set; }

        public RouteType RouteKind
        {
            get
            {
                return (RouteType)Enum.Parse(typeof(RouteType), RouteTypeName ?? RouteType.None.ToString());
            }
        }

        public ProcedureRoute GiveMeMappedObject()
        {
            ProcedureRoute result = new ProcedureRoute();
            result.Id = new System.Uri($"https://id.parliament.uk/{TripleStoreId}");
            result.ProcedureRouteIsToProcedureStep = new ProcedureStep[]
            {
                new ProcedureStep()
                {
                    Id=new System.Uri($"https://id.parliament.uk/{FromStepTripleStoreId}"),
                    ProcedureStepName=FromStepName
                }
            };
            result.ProcedureRouteIsFromProcedureStep = new ProcedureStep[]
            {
                new ProcedureStep()
                {
                    Id=new System.Uri($"https://id.parliament.uk/{ToStepTripleStoreId}"),
                    ProcedureStepName=ToStepName
                }
            };

            return result;
        }

        public static string ListByProcedureSql = @"select pr.Id, pr.TripleStoreId, fs.Id as FromStepId, fs.ProcedureStepTypeId as FromStepTypeId,
	            fs.ProcedureStepName as FromStepName, fs.TripleStoreId as FromStepTripleStoreId,
                ts.Id as ToStepId, ts.ProcedureStepTypeId as ToStepTypeId, ts.ProcedureStepName as ToStepName, ts.TripleStoreId as ToStepTripleStoreId,
	            rt.ProcedureRouteTypeName as RouteTypeName, pr.StartDate as StartDate, pr.EndDate as EndDate,
                fst.ProcedureStepTypeName as FromStepTypeName,
                tst.ProcedureStepTypeName as ToStepTypeName
            from ProcedureRoute pr
            join ProcedureRouteProcedure prp on prp.ProcedureRouteId=pr.Id
            join ProcedureStep fs on fs.Id=pr.FromProcedureStepId
            join ProcedureStep ts on ts.Id=pr.ToProcedureStepId
            join ProcedureRouteType rt on rt.Id=pr.ProcedureRouteTypeId
            join ProcedureStepType fst on fst.Id=fs.ProcedureStepTypeId
            join ProcedureStepType tst on tst.Id=ts.ProcedureStepTypeId
            where prp.ProcedureId=@ProcedureId;
            select sh.ProcedureStepId, h.HouseName from ProcedureStepHouse sh
			join House h on h.Id=sh.HouseId
			join ProcedureRoute pr on sh.ProcedureStepId=pr.FromProcedureStepId or sh.ProcedureStepId=pr.ToProcedureStepId
            join ProcedureRouteProcedure prp on prp.ProcedureRouteId=pr.Id
       		where prp.ProcedureId=@ProcedureId
			group by sh.ProcedureStepId, h.HouseName";

        public static string ListByStepSql = @"select pr.Id, pr.TripleStoreId, fs.Id as FromStepId,, fs.ProcedureStepTypeId as FromStepTypeId,
	            fs.ProcedureStepName as FromStepName, fs.TripleStoreId as FromStepTripleStoreId,
                ts.Id as ToStepId, ts.ProcedureStepTypeId as ToStepTypeId, ts.ProcedureStepName as ToStepName, ts.TripleStoreId as ToStepTripleStoreId,
	            rt.ProcedureRouteTypeName as RouteTypeName 
                fst.ProcedureStepTypeName as FromStepTypeName,
                tst.ProcedureStepTypeName as ToStepTypeName
            from ProcedureRoute pr
            join ProcedureStep fs on fs.Id=pr.FromProcedureStepId
            join ProcedureStep ts on ts.Id=pr.ToProcedureStepId
            join ProcedureRouteType rt on rt.Id=pr.ProcedureRouteTypeId
            join ProcedureStepType fst on fst.Id=fs.ProcedureStepTypeId
            join ProcedureStepType tst on tst.Id=ts.ProcedureStepTypeId
            where ((pr.FromProcedureStepId=@StepId) or (pr.ToProcedureStepId=@StepId));
            select sh.ProcedureStepId, h.HouseName from ProcedureStepHouse sh
			join House h on h.Id=sh.HouseId
			join ProcedureRoute pr on sh.ProcedureStepId=pr.FromProcedureStepId or sh.ProcedureStepId=pr.ToProcedureStepId
       		where ((pr.FromProcedureStepId=@StepId) or (pr.ToProcedureStepId=@StepId))
			group by sh.ProcedureStepId, h.HouseName";

    }

}