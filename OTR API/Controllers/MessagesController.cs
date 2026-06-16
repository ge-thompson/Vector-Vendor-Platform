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
    [RoutePrefix("api/messages")]

    public class MessagesController : ApiController
    {
        //http://localhost:5129/api/message/GetLoadMessagesByDriver/1
        [HttpGet]
        public IEnumerable<LoadMessages> GetLoadMessagesByDriver(int id)
        {
            DataMessages dc = new DataMessages();
            IEnumerable<LoadMessages> messagelist = dc.GetLoadMessagesByDriver(id);

            return messagelist;
        }


        //http://localhost:5129/api/message/GetLoadMessagesByDriverTrip/1
        [HttpGet]
        public IEnumerable<LoadMessages> GetLoadMessagesByDriverTrip(int id)
        {
            DataMessages dc = new DataMessages();
            IEnumerable<LoadMessages> messagelist = dc.GetLoadMessagesByDriverTrip(id);

            return messagelist;
        }

        //http://localhost:5129/api/message/GetLoadMessagesByLoad/1
        [HttpGet]
        public IEnumerable<LoadMessages> GetLoadMessagesByLoad(int id)
        {
            DataMessages dc = new DataMessages();
            IEnumerable<LoadMessages> messagelist = dc.GetLoadMessagesByLoad(id);

            return messagelist;
        }

        //http://localhost:5129/api/message/GetLoadMessageUnReadByDriver/1
        [HttpGet]
        public IEnumerable<LoadMessages> GetLoadMessageUnReadByDriver(int id)
        {
            DataMessages dc = new DataMessages();
            IEnumerable<LoadMessages> messagelist = dc.GetLoadMessagesUnReadByDriver(id);

            return messagelist;
        }


        //http://localhost:5129/api/message/GetLoadMessage/1
        [HttpGet]
        public LoadMessages GetLoadMessage(int id)
        {
            LoadMessages message = new LoadMessages();
            try
            {
                DataMessages dc = new DataMessages();
                message = dc.GetLoadMessage(id);
            }
            catch
            {
                message.Message = "Error Retrieving Message";
            }

            return message;
        }

        //http://localhost:5129/api/message/SaveLoadMessage
        [HttpPost]
        public LoadMessages SaveLoadMessage([FromBody]LoadMessages message)
        {
            LoadMessages response = new LoadMessages();
            DataAudit da = new DataAudit();
            if (message == null)
            {
                da.InsertErrorAuditLog("Body Message is null", "SaveLoadMessage");
                response.ResultMessage = "Error Saving Message";
            }
            else { 
                try
                {
                    DataMessages dc = new DataMessages();
                    int id = dc.InsertLoadMessage(message);

                    try
                    {
                        response = dc.GetLoadMessage(id);
                    }
                    catch (Exception ex)
                    {
                        da.InsertErrorAuditLog(ex.Message, "SaveLoadMessage_Get");
                        response.ResultMessage = "Error Saving Message";
                    }
                }
                catch (Exception ex)
                {
                    da.InsertErrorAuditLog(ex.Message, "SaveLoadMessage_Insert");
                    response.ResultMessage = "Error Saving Message";
                }
            }
            return response;
        }

        //http://localhost:5129/api/driver/DeleteLoadMessage
        [HttpDelete]
        public LoadMessages DeleteLoadMessage([FromBody]LoadMessages message)
        {
            try
            {
                DataMessages dc = new DataMessages();
                message = dc.DeleteLoadMessage(message);
            }
            catch
            {
                message.ResultMessage = "Error";
            }

            return message;

        }


        //http://localhost:5129/api/message/GetLoadMessagesUnReadByRep/1
        [HttpGet]
        public IEnumerable<LoadMessages> GetLoadMessagesUnReadByRep(int id)
        {
            DataMessages dc = new DataMessages();
            IEnumerable<LoadMessages> messagelist = dc.GetLoadMessagesUnReadByRep(id);

            return messagelist;
        }


        //http://localhost:5129/api/message/MessageViewedByDriver
        [HttpPost]
        public LoadMessages MessageViewedByDriver([FromBody]LoadMessages message)
        {

            try
            {
                DataMessages dc = new DataMessages();
                int id = dc.DriverViewedLoadMessage(message);

                if (id == 0)
                {
                    message.ResultMessage = "Unsuccessful to Update Message";
                }
                if (id > 0)
                {
                    message.ResultMessage = "Successfully Marked Message Viewed ";
                }

            }
            catch (Exception ex)
            {
                message.ResultMessage = "Error Marking Message Viewed";
            }

            return message;
        }


        //http://localhost:5129/api/message/MessageListViewedByDriver
        [HttpPost]
        public bool MessageListViewedByDriver([FromBody]List<LoadMessages> messagelist)
        {
            foreach (LoadMessages message in messagelist)
            {
                try
                {
                    DataMessages dc = new DataMessages();


                    int id = dc.DriverViewedLoadMessage(message);

                    if (id == 0)
                    {
                        message.ResultMessage = "Unsuccessful to Update Message";
                    }
                    if (id > 0)
                    {
                        message.ResultMessage = "Successfully Marked Message Viewed ";
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    message.ResultMessage = "Error Marking Message Viewed";

                    return false;
                }
            }

            return true;
        }

        //http://localhost:5129/api/message/MessageViewedByRep
        [HttpPost]
        public LoadMessages MessageViewedByRep([FromBody]LoadMessages message)
        {

            try
            {
                DataMessages dc = new DataMessages();
                int id = dc.RepViewedLoadMessage(message);

                if (id == 0)
                {
                    message.ResultMessage = "Unsuccessful to Update Message";
                }
                if (id > 0)
                {
                    message.ResultMessage = "Successfully Marked Message Viewed ";
                }

            }
            catch (Exception ex)
            {
                message.ResultMessage = "Error Marking Message Viewed";
            }

            return message;
        }

        //http://localhost:5129/api/message/MessageListViewedByRep
        [HttpPost]
        public bool MessageListViewedByRep([FromBody]List<LoadMessages> messagelist)
        {
            foreach (LoadMessages message in messagelist)
            {
                try
                {
                    DataMessages dc = new DataMessages();


                    int id = dc.RepViewedLoadMessage(message);

                    if (id == 0)
                    {
                        message.ResultMessage = "Unsuccessful to Update Message";
                    }
                    if (id > 0)
                    {
                        message.ResultMessage = "Successfully Marked Message Viewed ";
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    message.ResultMessage = "Error Marking Message Viewed";

                    return false;
                }
            }

            return true;
        }

        //http://localhost:5129/api/message/GetCountAwaitingDriverMessage/1
        [HttpGet]
        public int GetCountAwaitingDriverMessage(int id)
        {
            DataMessages dc = new DataMessages();
            int messagecnt = dc.GetCountAwaitingDriverMessage(id);

            return messagecnt;
        }

        //http://localhost:5129/api/message/GetCountAwaitingRepMessage/1
        [HttpGet]
        public int GetCountAwaitingRepMessage(int id)
        {
            DataMessages dc = new DataMessages();
            int messagecnt = dc.GetCountAwaitingRepMessage(id);

            return messagecnt;
        }

        //http://localhost:5129/api/message/GetCountAwaitingLoadMessage/1
        [HttpGet]
        public int GetCountAwaitingLoadMessage(int id)
        {
            DataMessages dc = new DataMessages();
            int messagecnt = dc.GetCountAwaitingLoadMessage(id);

            return messagecnt;
        }

        //http://localhost:5129/api/message/GetCountAwaitingCompanyMessage
        [HttpGet]
        public int GetCountAwaitingCompanyMessage()
        {
            DataMessages dc = new DataMessages();
            int messagecnt = dc.GetCountAwaitingCompanyMessage();

            return messagecnt;
        }



        //http://localhost:5129/api/message/GetDriverMessagesByDriver/1
        [HttpGet]
        public IEnumerable<LoadMessages> GetDriverMessagesByDriver(int id)
        {
            DataDrivers dc = new DataDrivers();
            IEnumerable<LoadMessages> messagelist = dc.GetDriverMessagesByDriver(id);

            return messagelist;
        }

        //http://localhost:5129/api/message/SaveDriverMessage
        [HttpPost]
        public LoadMessages SaveDriverMessage([FromBody]LoadMessages message)
        {
            LoadMessages response = new LoadMessages();
            try
            {
                DataDrivers dc = new DataDrivers();
                int id = dc.InsertDriverMessage(message);

                response = dc.GetDriverMessage(id);

            }
            catch(Exception ex)
            {
                DataAudit da = new DataAudit();
                da.InsertErrorAuditLog(ex.Message, "SaveLoadMessage");
                response.Message = "Error Saving Message";
            }

            return response;
        }



        ////http://localhost:5129/api/message/GetAudit
        //[HttpGet]
        //public List<LogMetaEntry> GetAudit()
        //{
        //    DataAudit dc = new DataAudit();
        //    List<LogMetaEntry> Results = dc.ViewRawAudit();

        //    foreach (LogMetaEntry r in Results)
        //    {
        //        LogMetadata l = Newtonsoft.Json.JsonConvert.DeserializeObject<LogMetadata>(r.RawJson);
        //        r.LogMetadata = l;

        //    }

        //    return Results;
        //}

        ////http://localhost:5129/api/message/ClearAudit
        //[HttpGet]
        //public bool ClearAudit()
        //{
        //    DataAudit dc = new DataAudit();
        //    dc.ClearRawAudit();

        //    return true;
        //}


    }
}
