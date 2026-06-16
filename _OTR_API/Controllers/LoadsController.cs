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
    [RoutePrefix("api/loads")]
    public class LoadsController : ApiController
    {
        //http://localhost:5129/api/loads/ConfirmLoad
        [HttpPost]
        public DriverTrip ConfirmLoad([FromBody]LoadConfirms Confirm)
        {
            DriverTrip load = new DriverTrip();

            string ip = System.Web.HttpContext.Current.Request.ServerVariables["REMOTE_ADDR"];

            DataLoads dc = new DataLoads();
            int LoadID = dc.ConfirmLoad(Confirm, ip);

            switch(LoadID)
            {
                case 0:
                    load.Message = "Confirm Code Invalid or Expired";
                    break;
                case 1:
                    load.Message = "No Valid Load";
                    break;
                default:

                    load = dc.GetDriverActiveLoad(Confirm.Driver.ID);
                    load.Message = "Successful";
                    break;
            }

            return load;
        }

        //http://localhost:5129/api/loads/GetDriverActiveLoad
        [HttpPost]
        public DriverTrip GetDriverActiveLoad([FromBody]Driver driver)
        {
            DataLoads dc = new DataLoads();
            DriverTrip load = dc.GetDriverActiveLoad(driver.ID);

            return load;
        }

        //http://localhost:5129/api/loads/GetDriverLoadHistory/1
        [HttpGet]
        public List<DriverTrip> GetDriverLoadHistory(int id)
        {
            DataLoads dc = new DataLoads();
            List<DriverTrip> loads = dc.GetDriverLoadHistory(id);

            return loads;
        }

        //http://localhost:5129/api/loads/SaveLoad
        [HttpPost]
        public Loads SaveLoad([FromBody]Loads Load)
        {
            DataLoads dc = new DataLoads();
            int LoadID = dc.InsertLoad(Load);

            switch(LoadID)
            {
                case 0:
                    Load.Message = "Load Already Exists";
                    break;

                default:
                    Load.Message = "Successful";
                    Load.ID = LoadID;
                    break;
            }

            return Load;
        }

        //http://localhost:5129/api/loads/UpdateLoad
        [HttpPut]
        public void UpdateLoad([FromBody]Loads Load)
        {
            DataLoads dc = new DataLoads();
            dc.UpdateLoad(Load);
        }

        //http://localhost:5129/api/loads/DeleteLoad/1
        [HttpDelete]
        public void DeleteLoad([FromBody]Loads Load)
        {
            DataLoads dc = new DataLoads();
            dc.DeleteLoad(Load);
        }

        //http://localhost:5129/api/loads/GetLoadStops
        [HttpPost]
        public List<LoadStops> GetLoadStops([FromBody]Loads Load)
        {
            DataLoads dc = new DataLoads();
            List<LoadStops> loads = dc.GetLoadStops(Load);

            return loads;
        }

        //http://localhost:5129/api/loads/SaveLoadStop
        [HttpPost]
        public void SaveLoadStop([FromBody]LoadStops LoadStop)
        {
            DataLoads dc = new DataLoads();
            dc.InsertLoadStop(LoadStop);
        }

        //http://localhost:5129/api/loads/UpdateLoadStop
        [HttpPut]
        public void UpdateLoadStop([FromBody]LoadStops LoadStop)
        {
            DataLoads dc = new DataLoads();
            dc.UpdateLoadStop(LoadStop);
        }

        //http://localhost:5129/api/loads/DeleteLoadStop
        [HttpDelete]
        public void DeleteLoadStop([FromBody]LoadStops LoadStop)
        {
            DataLoads dc = new DataLoads();
            dc.DeleteLoadStop(LoadStop);
        }

        //http://localhost:5129/api/loads/SaveReferenceNumber
        [HttpPost]
        public void SaveReferenceNumber([FromBody]ReferenceNumbers refnumber)
        {
            DataLoads dc = new DataLoads();
            dc.InsertReferenceNumber(refnumber);
        }

        //http://localhost:5129/api/loads/UpdateReferenceNumber
        [HttpPut]
        public void UpdateReferenceNumber([FromBody]ReferenceNumbers refnumber)
        {
            DataLoads dc = new DataLoads();
            dc.UpdateReferenceNumber(refnumber);
        }

        //http://localhost:5129/api/loads/DeleteReferenceNumber
        [HttpDelete]
        public void DeleteReferenceNumber([FromBody]ReferenceNumbers refnumber)
        {
            DataLoads dc = new DataLoads();
            dc.DeleteReferenceNumber(refnumber);
        }

        //http://localhost:5129/api/loads/GetDriverTripPath/1
        [HttpGet]
        public List<TripPath> GetDriverTripPath(int id)
        {
            DataLoads dc = new DataLoads();
            List<TripPath> trippath = dc.DriverTripPath(id);

            return trippath;
        }

        //http://localhost:5129/api/loads/GetLoadConfirms
        [HttpGet]
        public List<LoadConfirms> GetLoadConfirms()
        {
            DataLoads dc = new DataLoads();
            List<LoadConfirms> loads = dc.GetLoadConfirms();

            return loads;
        }

        //http://localhost:5129/api/loads/GetActiveLoadConfirms
        [HttpGet]
        public List<LoadConfirms> GetActiveLoadConfirms()
        {
            DataLoads dc = new DataLoads();
            List<LoadConfirms> loads = dc.GetActiveLoadConfirms();

            return loads;
        }

        //http://localhost:5129/api/loads/SaveLoadConfirm
        [HttpPost]
        public LoadConfirms SaveLoadConfirm([FromBody]LoadConfirms Confirm)
        {
            LoadConfirms lc = new LoadConfirms();
            DataLoads dc = new DataLoads();
            lc = dc.InsertLoadConfirm(Confirm);
            return lc;
        }

        //http://localhost:5129/api/loads/GetLoadConfirmHistory/1
        [HttpGet]
        public List<LoadConfirms> GetLoadConfirmHistory(int id)
        {
            DataLoads dc = new DataLoads();
            List<LoadConfirms> loads = dc.GetLoadConfirmHistory(id);

            return loads;
        }

        //http://localhost:5129/api/loads/GetLoads
        [HttpGet]
        public List<Loads> GetLoads()
        {
            DataLoads dc = new DataLoads();
            List<Loads> loads = dc.GetLoads();

            return loads;
        }

        //http://localhost:5129/api/loads/GetLoadswDriver
        [HttpGet]
        public List<Loads> GetLoadswDriver()
        {
            DataLoads dc = new DataLoads();
            List<Loads> loads = dc.GetLoadswDriver();

            return loads;
        }

        //http://localhost:5129/api/loads/GetLoadswDriverByDriverID/1
        [HttpGet]
        public List<Loads> GetLoadswDriverByDriverID(int id)
        {
            DataLoads dc = new DataLoads();
            List<Loads> loads = dc.GetLoadswDriverByDriverID(id);

            return loads;
        }

        //http://localhost:5129/api/loads/GetLoadByLoadID/1
        [HttpGet]
        public Loads GetLoadByLoadID(int id)
        {
            DataLoads dc = new DataLoads();
            Loads load = dc.GetLoadByLoadID(id);

            return load;
        }

        //http://localhost:5129/api/loads/GetLoadByTripID/1
        [HttpGet]
        public Loads GetLoadByTripID(int id)
        {
            DataLoads dc = new DataLoads();
            Loads load = dc.GetLoadByTripID(id);

            return load;
        }

        //http://localhost:5129/api/loads/EndDriverTrip
        [HttpPost]
        public DriverTrip EndDriverTrip([FromBody]LoadConfirms Confirm)
        {
            DriverTrip load = new DriverTrip();

            string ip = System.Web.HttpContext.Current.Request.ServerVariables["REMOTE_ADDR"];

            DataLoads dc = new DataLoads();
            int LoadID = dc.ConfirmLoad(Confirm, ip);

            switch (LoadID)
            {
                case 0:
                    load.Message = "Confirm Code Invalid or Expired";
                    break;
                case 1:
                    load.Message = "No Valid Load";
                    break;
                default:

                    load = dc.GetDriverActiveLoad(Confirm.Driver.ID);
                    load.Message = "Successful";
                    break;
            }

            return load;
        }

        //http://localhost:5129/api/loads/UpdateLoadRep
        [HttpPut]
        public void UpdateLoadRep([FromBody]Loads Load)
        {
            DataLoads dc = new DataLoads();
            dc.UpdateLoadRep(Load);
        }
        ////http://localhost:5129/api/message/GetLoadMessagesByTrip/1
        //[HttpGet]
        //public IEnumerable<LoadMessages> GetLoadMessagesByTrip(int id)
        //{
        //    DataMessages dc = new DataMessages();
        //    IEnumerable<LoadMessages> messagelist = dc.GetLoadMessagesByTrip(id);

        //    return messagelist;
        //}

        ////http://localhost:5129/api/message/GetLoadMessage/1
        //[HttpGet]
        //public LoadMessages GetLoadMessage(int id, [FromBody]MessageViewer viewer)
        //{
        //    DataMessages dc = new DataMessages();
        //    LoadMessages message = dc.GetLoadMessage(id, viewer);

        //    return message;
        //}



        ////http://localhost:5129/api/driver/DeleteDriver
        //[HttpDelete]
        //public void DeleteDriver(int id)
        //{
        //    DataDrivers dc = new DataDrivers();
        //    dc.DeleteDriver(id);
        //}

        ////http://localhost:5129/api/message/GetViewerTypes
        //[HttpGet]
        //public IEnumerable<MessageViewerType> GetViewerTypes(int id)
        //{
        //    DataMessages dc = new DataMessages();
        //    IEnumerable<MessageViewerType> viewertypelist = dc.GetMessageViewerTypes();

        //    return viewertypelist;
        //}


        //http://localhost:5129/api/loads/SaveLoad
    }
}
