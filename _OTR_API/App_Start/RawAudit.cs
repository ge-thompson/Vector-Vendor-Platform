using Newtonsoft.Json;
using OTR_API.DataClasses;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace OTR_API
{
    public class RawLogHandler : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            try
            {
                //if (System.Configuration.ConfigurationManager.AppSettings["Debug"].ToUpper() == "TRUE")
                //{


                if (request.RequestUri.ToString().Contains("swagger") || request.RequestUri.ToString().Contains("GetAudit") || request.RequestUri.ToString().Contains("ClearAudit"))

                {
                    var response = await base.SendAsync(request, cancellationToken);
                    return response;
                }
                else
                {
                    var logMetadata = BuildRequestMetadata(request);
                    var response = await base.SendAsync(request, cancellationToken);
                    logMetadata = BuildResponseMetadata(logMetadata, response);
                    await SendToLog(logMetadata);
                    return response;
                }
            }
            catch(Exception ex)
            { 
                HttpResponseMessage response = request.CreateResponse(HttpStatusCode.BadRequest, ex.Message.ToString());
                return response;
            }

        }
        private LogMetadata BuildRequestMetadata(HttpRequestMessage request)
        {
            if (request.RequestUri.ToString().Contains("UploadDriverFile") || request.RequestUri.ToString().Contains("UploadCheckCallFile") )
            {
                LogMetadata log = new LogMetadata
                {
                    RequestMethod = request.Method.Method,
                    RequestTimestamp = DateTime.Now,
                    RequestUri = request.RequestUri.ToString()
                };

                if (request.Content.Headers.ContentType != null)
                    log.RequestContentType = request.Content.Headers.ContentType.MediaType;

                return log;
            }
            else
            {
                LogMetadata log = new LogMetadata
                {
                    RequestMethod = request.Method.Method,
                    RequestTimestamp = DateTime.Now,
                    RequestUri = request.RequestUri.ToString(),
                    RequestHeader = request.Headers.ToString().Replace(Environment.NewLine," | "),
                    RequestContent = request.Content.ReadAsStringAsync()
                };

                if (request.Content.Headers.ContentType != null)
                    log.RequestContentType = request.Content.Headers.ContentType.MediaType;

                return log;
            }
        }
        private LogMetadata BuildResponseMetadata(LogMetadata logMetadata, HttpResponseMessage response)
        {
            logMetadata.ResponseStatusCode = response.StatusCode;
            logMetadata.ResponseTimestamp = DateTime.Now;
            if(response.Content != null)
            {
                logMetadata.ResponseContentType = response.Content.Headers.ContentType.MediaType;
                logMetadata.ResponseContent = response.Content.ReadAsStringAsync();
            }

            return logMetadata;
        }
        private async Task<bool> SendToLog(LogMetadata logMetadata)
        {

            string output = JsonConvert.SerializeObject(logMetadata);

            DataAudit dc = new DataAudit();
            dc.InsertRawAudit(output);


            return true;
        }

        private async Task<List<LogMetaEntry>> ViewLog()
        {

            DataAudit dc = new DataAudit();
            List<LogMetaEntry> Results = dc.ViewRawAudit();

            foreach(LogMetaEntry r in Results)
            {
                r.LogMetadata = (LogMetadata)JsonConvert.DeserializeObject(r.RawJson);

            }
           
            return Results;
        }

        private async Task<bool> ClearLog(LogMetadata logMetadata)
        {

            DataAudit dc = new DataAudit();
            dc.ClearRawAudit();

            return true;
        }
    }

    public class LogMetaEntry
    {
        public int ID { get; set; }
        public LogMetadata LogMetadata { get; set; }
        public string RawJson { get; set; }
        public DateTime EntryDate { get; set; }
    }
    public class LogMetadata
    {
        public string RequestContentType { get; set; }
        public string RequestUri { get; set; }
        public string RequestMethod { get; set; }
        public string RequestHeader { get; set; }
        public Task<string> RequestContent { get; set; }
        public DateTime? RequestTimestamp { get; set; }
        public string ResponseContentType { get; set; }
        public HttpStatusCode ResponseStatusCode { get; set; }
        public DateTime? ResponseTimestamp { get; set; }
        public Task<string> ResponseContent { get; set; }
    }
}