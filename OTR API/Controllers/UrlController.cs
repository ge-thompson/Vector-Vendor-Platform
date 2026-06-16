using OTR_API.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using System.Threading.Tasks;
using OTR_API.DataClasses;
using OTR_API.Filters;


namespace OTR_API.Controllers
{
    [HMACAuthentication]
    [RoutePrefix("api/url")]

    public class UrlController : ApiController
    {
        //http://localhost:5129/api/url/gettinyurl
        [HttpPost]
        public Urls GetTinyUrl([FromBody]Urls url)
        {
            Urls newurl = new Urls();

            #region Verify
            if (url.TinyUrl.Length == 0)
            {
                newurl.FullUrl = "You must provide a shortened url.";
                return newurl;
            }
            #endregion

            DataUrl dc = new DataUrl();
            newurl = dc.GetUrl(url.TinyUrl, 0);
            return newurl;
        }

        //http://localhost:5129/api/url/createtinyurl
        [HttpPost]
        public Urls CreateTinyUrl([FromBody]Urls url)
        {
            Urls response = new Urls();

            try
            {
                DataUrl du = new DataUrl();
                response = du.CreateUrl(url.FullUrl);

                //if(response.TinyUrl == "ERROR")
                //    response.TinyUrl = "Error Saving Check Call";

            }
            catch (Exception ex)
            {
                response.TinyUrl = "ERROR";
            }

            return response;
        }

        //http://localhost:5129/api/url/inmotionurl/id
        [HttpPost]
        public InMotionUrl InMotionURL(int id)
        {
            InMotionUrl response = new InMotionUrl();

            try
            {
                DataUrl du = new DataUrl();

                response = du.GetDriverUrl(id);
                //response.DriverID = id;
                //response.Url = @"http://access.vectortransport.com";
                //response.Menu = "Loadboard";
            }
            catch (Exception ex)
            {
                response.DriverID = id;
                response.Url = @"http://access.vectortransport.com";
                response.Menu = "Loadboard";
            }

            return response;
        }


    }







}
