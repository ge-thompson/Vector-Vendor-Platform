using System.Net;
using System.Net.Http;
using System.Web.Http;

namespace FourKitesIntegration.OutboundService.Controllers
{
    [RoutePrefix("health")]
    public class HealthController : ApiController
    {
        [HttpGet, Route("")]
        public HttpResponseMessage Get()
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\":\"ok\",\"service\":\"FourKitesOutbound\"}",
                    System.Text.Encoding.UTF8, "application/json")
            };
        }
    }
}
