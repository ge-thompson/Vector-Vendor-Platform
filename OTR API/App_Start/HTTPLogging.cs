using Newtonsoft.Json;
using OTR_API.DataClasses;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web;


namespace OTR_API 
{
    public class HTTPLogging : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string uid = Guid.NewGuid().ToString();


            if (request.Content != null)
            {
                // log request body
                string requestBody = await request.Content.ReadAsStringAsync();
                await SendToLog(requestBody);
            }
            // let other handlers process the request
            var result = await base.SendAsync(request, cancellationToken);

            if (result.Content != null)
            {
                // once response body is ready, log it
                var responseBody = await result.Content.ReadAsStringAsync();
                await SendToLog(responseBody);
            }

            return result;
        }

        private async Task<bool> SendToLog(string body)
        {

            DataAudit dc = new DataAudit();
            dc.InsertRawHTTP(body);

            return true;
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
            public Task<string> RequestContent { get; set; }
            public DateTime? RequestTimestamp { get; set; }
            public string ResponseContentType { get; set; }
            public HttpStatusCode ResponseStatusCode { get; set; }
            public DateTime? ResponseTimestamp { get; set; }
            public Task<string> ResponseContent { get; set; }
        }
    }
}