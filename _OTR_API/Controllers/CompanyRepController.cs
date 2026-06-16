using OTR_API.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using OTR_API.DataClasses;
using OTR_API.Filters;

namespace OTR_API.Controllers
{
    [HMACAuthentication]
    [RoutePrefix("api/companyrep")]

    public class CompanyRepController : ApiController
    {
        //http://localhost:5129/api/companyrep/GetAllCompanyReps
        [HttpGet]
        public IEnumerable<CompanyReps> GetAllCompanyReps()
        {
            DataCompanyReps dc = new DataCompanyReps();
            IEnumerable<CompanyReps> replist = dc.GetAllCompanyReps();

            return replist;
        }

        //http://localhost:5129/api/companyrep/GetCompanyRep/1
        [HttpGet]
        public CompanyReps GetCompanyRep(int id)
        {
            DataCompanyReps dc = new DataCompanyReps();
            CompanyReps rep = dc.GetCompanyRep(id);
            return rep;
        }


        //http://localhost:5129/api/companyrep/GetCompanyRepByTrip/1
        [HttpGet]
        public CompanyReps GetCompanyRepByTrip(int id)
        {
            DataCompanyReps dc = new DataCompanyReps();
            CompanyReps rep = dc.GetCompanyRepByTrip(id);
            return rep;
        }

        //http://localhost:5129/api/companyrep/SaveCompanyRep
        [HttpPost]
        public HttpResponseMessage SaveCompanyRep([FromBody]CompanyReps companyrep)
        {
            HttpResponseMessage response;
            try
            {
                DataCompanyReps dc = new DataCompanyReps();
                dc.InsertCompanyRep(companyrep);
                response = Request.CreateResponse(HttpStatusCode.OK);
            }
            catch
            {
                response = Request.CreateResponse(HttpStatusCode.BadRequest);
            }

            return response;
        }


        //http://localhost:5129/api/companyrep/UpdateCompanyRep
        [HttpPut]
        public void UpdateCompanyRep([FromBody]CompanyReps companyrep)
        {
            DataCompanyReps dc = new DataCompanyReps();
            dc.UpdateCompanyRep(companyrep);
        }


        //http://localhost:5129/api/companyrep/DeleteCompanyRep/1
        [HttpDelete]
        public void DeleteCompanyRep(int id)
        {
            DataCompanyReps dc = new DataCompanyReps();
            dc.DeleteCompanyRep(id);
        }
    }
}
