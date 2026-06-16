using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;
using System.Web.Http.Filters;
using System.Web.Http.Results;

namespace OTR_API.Filters
{
    public class HMACAuthenticationAttribute : Attribute, IAuthenticationFilter
    {
        private static Dictionary<string, string> allowedApps = new Dictionary<string, string>();
        private readonly UInt64 requestMaxAgeInSeconds = 300;  //5 mins
        private readonly string authenticationScheme = "Basic";

        public HMACAuthenticationAttribute()
        {
            if (allowedApps.Count == 0)
            {
                allowedApps.Add("4d53bce03ec34c0a911182d4c228e96c", "A93reRTUJHsCuQSHR+L3GxqOJyDmQpCgps102ciuabc="); // In-Motion APP Dev
                allowedApps.Add("5308b4420df2474da6bf30cb42f9be86", "Df668F91cD305F3Aa0050284bE56f890F8127ca0abc="); // In-Motion APP
                allowedApps.Add("1376a4920df419da6de3cbc044a9b366", "AjsHYxBbHirk+OvMSsLCNp5^h9j7ChGfOs&Cp6WGwou="); // SYNC Service (Vector, Trucker Tools)
            }
        }

        public Task AuthenticateAsync(HttpAuthenticationContext context, CancellationToken cancellationToken)
        {
            var req = context.Request;

            if (req.Headers.Authorization != null && authenticationScheme.Equals(req.Headers.Authorization.Scheme, StringComparison.OrdinalIgnoreCase))
            {
                var rawAuthzHeader = req.Headers.Authorization.Parameter;

                var autherizationHeaderArray = GetAutherizationHeaderValues(rawAuthzHeader);

                if (autherizationHeaderArray != null)
                {
                    var APPId = autherizationHeaderArray[0];
                    var APPKey = autherizationHeaderArray[1];

                    var isValid = isValidRequest(req, APPId, APPKey);

                    if (isValid.Result)
                    {
                        var currentPrincipal = new GenericPrincipal(new GenericIdentity(APPId), null);
                        context.Principal = currentPrincipal;
                    }
                    else
                    {
                        context.ErrorResult = new UnauthorizedResult(new AuthenticationHeaderValue[0], context.Request);
                    }
                }
                else
                {
                    context.ErrorResult = new UnauthorizedResult(new AuthenticationHeaderValue[0], context.Request);
                }
            }
            else
            {
                context.ErrorResult = new UnauthorizedResult(new AuthenticationHeaderValue[0], context.Request);
            }

            return Task.FromResult(0);
        }

        public Task ChallengeAsync(HttpAuthenticationChallengeContext context, CancellationToken cancellationToken)
        {
            context.Result = new ResultWithChallenge(context.Result);
            return Task.FromResult(0);
        }

        public bool AllowMultiple
        {
            get { return false; }
        }

        private string[] GetAutherizationHeaderValues(string rawAuthzHeader)
        {

            var credArray = rawAuthzHeader.Split(':');

            if (credArray.Length >= 2)
            {
                return credArray;
            }
            else
            {
                return null;
            }

        }

        private async Task<bool> isValidRequest(HttpRequestMessage req, string APPId, string APPKey)
        {
            if (!allowedApps.ContainsKey(APPId))
            {
                return false;
            }

            var sharedKey = allowedApps[APPId];

            if(sharedKey != APPKey)
            {
                return false;
            }

            return true;
        }
    }

    public class ResultWithChallenge : IHttpActionResult
    {
        private readonly string authenticationScheme = "Basic";
        private readonly IHttpActionResult next;

        public ResultWithChallenge(IHttpActionResult next)
        {
            this.next = next;
        }

        public async Task<HttpResponseMessage> ExecuteAsync(CancellationToken cancellationToken)
        {
            var response = await next.ExecuteAsync(cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                response.Headers.WwwAuthenticate.Add(new AuthenticationHeaderValue(authenticationScheme));
            }

            return response;
        }
    }
}