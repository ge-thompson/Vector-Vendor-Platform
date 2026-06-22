using OTR_API.TruckerTools.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using OTR_API.TruckerTools.DataClasses;
using OTR_API.Filters;
using System.Threading.Tasks;

namespace OTR_API.Controllers
{
    [HMACAuthentication]
    [RoutePrefix("api/truckertools")]

    public class TruckerToolsController : ApiController
    {
        #region Carrier
        //http://localhost:5129/api/trukertools/GetCarriers/
        [HttpGet]
        public IEnumerable<Carrier> GetCarriers()
        {
            DataCarrierMatch dc = new DataCarrierMatch();
            IEnumerable<Carrier> carrierlist = dc.GetCarriers();

            return carrierlist;
        }

        //http://localhost:5129/api/trukertools/GetBookItNowCarriers/
        [HttpGet]
        public IEnumerable<Carrier> GetBookItNowCarriers()
        {
            DataCarrierMatch dc = new DataCarrierMatch();
            IEnumerable<Carrier> carrierlist = dc.GetCarriersBookItNow();

            return carrierlist;
        }

        //http://localhost:5129/api/trukertools/GetCarrierByID/1
        [HttpGet]
        public Carrier GetCarrierByID(int ID)
        {
            DataCarrierMatch dc = new DataCarrierMatch();
            Carrier carrier = dc.GetCarrierByID(ID);

            return carrier;
        }

        //http://localhost:5129/api/trukertools/SaveCarrier
        [HttpPost]
        public Carrier SaveCarrier([FromBody]Carrier carrier)
        {
            Carrier response = new Carrier();
            OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit();
            if (carrier == null)
            {
                da.InsertErrorAuditLog("Body Message is null", "SaveCarrier");
                response.Message = "Error Saving Carrier";
            }
            else {
                try
                {
                    DataCarrierMatch dc = new DataCarrierMatch();
                    int id = dc.InsertCarrier(carrier);

                    try
                    {
                        response = dc.GetCarrierByID(id);
                    }
                    catch (Exception ex)
                    {
                        da.InsertErrorAuditLog(ex.Message, "SaveCarrier_Get");
                        response.Message = "Error Saving Carrier";
                    }
                }
                catch (Exception ex)
                {
                    da.InsertErrorAuditLog(ex.Message, "SaveCarrier_Insert");
                    response.Message = "Error Saving Carrier";
                }
            }
            return response;
        }

        //http://localhost:5129/api/trukertools/UpdateCarrier
        [HttpPut]
        public Carrier UpdateCarrier([FromBody]Carrier carrier)
        {

            Carrier ucarrier = new Carrier();

            DataCarrierMatch dc = new DataCarrierMatch();

            int id = dc.UpdateCarrier(carrier);
            switch (id)
            {
                case 0:
                    ucarrier.Message = "Error Updating Carrier Information";
                    break;
                case 1:
                    ucarrier = dc.GetCarrierByID(id);
                    ucarrier.Message = "Sucessful";
                    break;
            }

            return ucarrier;
        }

        //http://localhost:5129/api/trukertools/DeleteCarrier
        [HttpDelete]
        public void DeleteCarrier(int id)
        {
            Carrier car = new Carrier() { VectorID = id };
            DataCarrierMatch dc = new DataCarrierMatch();
            dc.DeleteCarrier(car);
        }

        //http://localhost:5129/api/trukertools/updatebooknowcarrier
        [HttpPut]
        public Carrier UpdateBookNowCarrier([FromBody]Carrier carrier)
        {

            Carrier ucarrier = new Carrier();

            DataCarrierMatch dc = new DataCarrierMatch();

            int id = dc.UpdateBookNowCarrier(carrier);
            switch (id)
            {
                case 0:
                    ucarrier.Message = "Error Updating Carrier BookItNow Information";
                    break;
                case 1:
                    ucarrier = dc.GetCarrierByID(id);
                    ucarrier.Message = "Sucessful";
                    break;
            }

            return ucarrier;
        }


        #endregion



        #region Loads

        //http://localhost:5129/api/truckertools/GetLoadByStatus/string
        [HttpGet]
        public IEnumerable<Load> GetLoadByStatus(string status)
        {
            DataLoadMatch dc = new DataLoadMatch();
            IEnumerable<Load> Loadlist = dc.GetLoadsByStatus(status);

            return Loadlist;
        }

        //http://localhost:5129/api/truckertools/GetLoadByID/1
        [HttpGet]
        public Load GetLoadByID(int ID)
        {
            DataLoadMatch dc = new DataLoadMatch();
            Load load = dc.GetLoadByID(ID, "ID");

            return load;
        }

        //http://localhost:5129/api/truckertools/GetLoadByVectorID/1
        [HttpGet]
        public Load GetLoadByVectorID(int ID)
        {
            DataLoadMatch dc = new DataLoadMatch();
            Load load = dc.GetLoadByID(ID, "VectorID");

            return load;
        }

        //http://localhost:5129/api/truckertools/SaveLoad
        [HttpPost]
        public Load SaveLoad([FromBody]Load load)
        {
            Load response = new Load();
            OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit();
            if (load == null)
            {
                da.InsertErrorAuditLog("Body Message is null", "SaveLoad");
                response.Message = "Error Saving Load";
            }
            else {
                try
                {
                    DataLoadMatch dc = new DataLoadMatch();
                    int id = dc.InsertLoad(load);

                    try
                    {
                        response = dc.GetLoadByID(id, "ID");
                    }
                    catch (Exception ex)
                    {
                        da.InsertErrorAuditLog(ex.Message, "SaveLoad_Get");
                        response.Message = "Error Saving Load";
                    }
                }
                catch (Exception ex)
                {
                    da.InsertErrorAuditLog(ex.Message, "SaveLoad_Insert");
                    response.Message = "Error Saving Load";
                }
            }
            return response;
        }

        //http://localhost:5129/api/truckertools/UpdateLoad
        [HttpPut]
        public Load UpdateLoad([FromBody]Load load)
        {

            Load uload = new Load();

            DataLoadMatch dc = new DataLoadMatch();

            int id = dc.UpdateLoad(load);
            switch (id)
            {
                case 0:
                    uload.Message = "Error Updating Carrier Information";
                    break;
                case 1:
                    uload = dc.GetLoadByID(id, "ID");
                    uload.Message = "Sucessful";
                    break;
            }

            return uload;
        }

        //http://localhost:5129/api/truckertools/DeleteLoad
        [HttpDelete]
        public void DeleteLoad([FromBody]Load load)
        {
            DataLoadMatch dc = new DataLoadMatch();
            dc.DeleteLoad(load.ID);
        }


        //http://localhost:5129/api/truckertools/TestEncrypt
        [HttpPost]
        public string[] TestEncrypt(string input, string Secret)
        {
            string[] response = new string[10];

            WebCallFunctions wc = new WebCallFunctions();
            response = wc.TestEncryption(input, Secret);


            return response;
        }

        //http://localhost:5129/api/truckertools/TestDecrypt
        [HttpPost]
        public string[] TestDecrypt(string input, string inputSecret)
        {
            string[] response = new string[10];

            WebCallFunctions wc = new WebCallFunctions();
            response = wc.TestDecrypt(input, inputSecret);


            return response;
        }

        //http://localhost:5129/api/truckertools/PostLoad
        [HttpPost]
        public LoadResponse PostLoad([FromBody]Load load)
        {
            LoadResponse response = new LoadResponse();

            DataSettings ds = new DataSettings();
            if (!ds.IsEnabled("LoadboardEnabled"))
            {
                response.Status = true;
                response.Message = "Loadboard disabled";
                return response;
            }

            DataTruckerToolsMatch dtt = new DataTruckerToolsMatch();

            DataLoadMatch dl = new DataLoadMatch();

            try
            {
                int LoadID = dl.InsertLoad(load);

                try
                {
                    WebCallFunctions wc = new WebCallFunctions();

                    Task<LoadResponse> task1 = Task.Run(() => { return wc.PostTTLoad(load); });

                    response = task1.Result;
                    response.LoadID = LoadID;

                    dtt.InsertLoadResponse(response);
                }
                catch (Exception ex)
                {
                    response.Message = "Error Posting Load - " + ex.Message;
                }
            }
            catch(Exception ex)
            {
                response.Message = "Error Saving Load - " + ex.Message;
            }

            return response;
        }


        //http://localhost:5129/api/truckertools/GetAvailableLoads
        [HttpGet]
        public LoadResponse GetAvailableLoads()
        {
            LoadResponse response = new LoadResponse();

            DataSettings ds = new DataSettings();
            if (!ds.IsEnabled("LoadboardEnabled"))
            {
                response.Status = true;
                response.Message = "Loadboard disabled";
                return response;
            }

            DataTruckerToolsMatch dtt = new DataTruckerToolsMatch();


            try
            {
                WebCallFunctions wc = new WebCallFunctions();

                Task<LoadResponse> task1 = Task.Run(() => { return wc.GetAvailableTTLoads(); });

                response = task1.Result;

                if (response.Message == null)
                {
                    if (response.Status)
                    {
                        response.Message = "Successful Load List Request";
                    }
                }

                dtt.InsertLoadResponse(response);
            }
            catch (Exception ex)
            {
                response.Message = "Error Getting Available Loads - " + ex.Message;
            }


            return response;
        }

        //http://localhost:5129/api/truckertools/GetAvailableLoadsDetail
        [HttpGet]
        public List<Load> GetAvailableLoadsDetail()
        {
            LoadResponse response = new LoadResponse();

            List<Load> loadList = new List<Load>();

            DataSettings ds = new DataSettings();
            if (!ds.IsEnabled("LoadboardEnabled"))
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit();
                da.InsertErrorAuditLog("Loadboard disabled", "GetAvailableLoadsDetail");
                return loadList;
            }

            DataTruckerToolsMatch dtt = new DataTruckerToolsMatch();

            DataLoadMatch dl = new DataLoadMatch();
            try
            {
                WebCallFunctions wc = new WebCallFunctions();

                Task<LoadResponse> task1 = Task.Run(() => { return wc.GetAvailableTTLoads(); });

                response = task1.Result;

                if (response.Message == null)
                {
                    if (response.Status)
                    {
                        response.Message = "Successful Load List Request";
                    }
                }

                dtt.InsertLoadResponse(response);

                loadList = dl.GetLoadsWithDetail(response.loadNumbers);
            }
            catch (Exception ex)
            {
                response.Message = "Error Getting Available Loads - " + ex.Message;
            }


            return loadList;
        }
        #endregion


        #region Carrier

        //http://localhost:5129/api/trukertools/PostCarrier
        [HttpPost]
        public CarrierResponse PostCarrier([FromBody]Carrier carrier)
        {
            CarrierResponse response = new CarrierResponse();

            DataSettings ds = new DataSettings();
            if (!ds.IsEnabled("LoadboardEnabled"))
            {
                response.Status = true;
                response.Message = "Loadboard disabled";
                return response;
            }

            DataTruckerToolsMatch dtt = new DataTruckerToolsMatch();

            DataCarrierMatch cl = new DataCarrierMatch();
            try
            {
                int LoadID = cl.InsertCarrier(carrier);

                try
                {
                    WebCallFunctions wc = new WebCallFunctions();

                    Task<CarrierResponse> task1 = Task.Run(() => { return wc.PostTTCarrier(carrier); });

                    response = task1.Result;

                    response.ResponseDate = DateTime.Now;

                    dtt.InsertCarrierResponse(response);

                }
                catch (Exception ex)
                {
                    response.Message = "Error Posting Carrier - " + ex.Message;
                }

            }
            catch(Exception ex)
            {
                response.Message = "Error Saving Carrier - " + ex.Message;
            }

            return response;
        }


        //http://localhost:5129/api/truckertools/getraw
        [HttpPost]
        public List<RAW> GetRAW([FromBody]RAW rw)
        {
            List<RAW> response = new List<RAW>();

            OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit();

            if (rw == null)
            {
                da.InsertErrorAuditLog("GetRAW is null", "RAWRequest");
            }
            else
            {
                try
                {
                    response = da.GetRawAudit(rw);

                }
                catch (Exception ex)
                {
                    da.InsertErrorAuditLog(ex.Message, "GetTrackingStatus");
                }

            }

            return response;
        }
        #endregion

        //http://localhost:5129/api/truckertools/PostRateConfirm
        [HttpPost]
        public RateConfirmResponse PostRateConfirm([FromBody]RateConfirm rateconfirm)
        {
            RateConfirmResponse response = new RateConfirmResponse();

            DataRateConfirmMatch dl = new DataRateConfirmMatch();

            try
            {
                int RateConfirmID = dl.InsertRateConfirm(rateconfirm);

                try
                {
                    response = new RateConfirmResponse();
                    response.ResponseDate = DateTime.Now;
                    response.Status = true;
                    response.Message = "Rate Confirmation successfully processed.";
                    response.RateConfirmID = RateConfirmID;

                    dl.InsertRateConfirmResponse(response);
                }
                catch (Exception ex)
                {
                    response.Message = "Error Posting Rate Confirmation - " + ex.Message;
                }
            }
            catch (Exception ex)
            {
                response.Message = "Error Saving Rate Confirmation - " + ex.Message;
            }

            return response;
        }
    }
}