using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Newtonsoft.Json;

using System.Net.Http;
using System.Net.Http.Headers;

using OTR_API.Models;
using OTR_API.DataClasses;
using OTR_API.TruckerTools.DataClasses;

namespace OTR_API
{
    public class WebCallFunctions
    {
        private string apiKey = "3b67b8867b1fb3dccaa95fc30496ea02:B1a649590812+4716L9f8587251eB71f7971660d6a8=";

        public string baseurl = "http://inmotion.vectortransport.com";

        private string FBSkey = "Kyw?R]^0,u`sk@QQq26xyEfah`A4OT";


        private string TTapiKey = "3283887cee3cf679cd3e08be2feff98b"; //Production
        //private string TTapiKey = "9e7a98a5132f51f03e57429cc1a9f2a8"; //Staging

        public string TTbaseUrl = "https://scapi.truckertools.com"; //Production
        //public string TTbaseUrl = "http://scapi-staging.truckertools.com"; //Staging

        private string TTAccountID = "384935"; //Production
        //private string TTAccountID = "172287"; //Staging

        private string TTSecret = "bd335d6e28df6e5fc94eb94e68a5c899e95ba302ab16d65792ad801e90d56d63"; //Production
        //private string TTSecret = "bd335d6e28df6e5fc94eb94e68a5c899e95ba302ab16d65792ad801e90d56d63"; //Staging

        private string TTIntegrationId = "3283887cee3cf679cd3e0828df6e5"; //Production
        //private string TTIntegrationId = "03e57429c6e5fc94eb"; //Staging

        private string TTUserName = "susan.zewicke@vectortransport.com"; //Production
        //private string TTUserName = "susan.zewicke@vectortransport.com";

        private string TTPassword = "m4Tchygx0"; //Production
                                                 //private string TTPassword = "test1234"; //Staging


        private string TTTrackingbaseurl = "http://loadtracking.truckertools.com";
        private string TTTrackingbaseurl2 = "http://api.truckertools.com";
        private string TTTrackingAccountID = "4Ly94/tNcLoELyhI1+6WXA==";
        private int TTTrackingPartnerID = 143;


        public async Task<CheckCalls> SaveCheckCall(CheckCalls CheckCall)
        {
            DataDrivers dd = new DataDrivers();
            DataLoads dl = new DataLoads();

            FBSCheckCall FBS = new FBSCheckCall();
            FBS.CheckCall = CheckCall;
            FBS.Driver = dd.GetDriverByDriverTrip(FBS.CheckCall.DriverTripID);
            FBS.Load = dl.GetLoadByDriverTripID(FBS.CheckCall.DriverTripID);


            CheckCalls chck = new CheckCalls();
            using (var client = new HttpClient())
            {

                client.BaseAddress = new Uri(baseurl);

                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", apiKey);

                try
                {
                    string JSONresult = JsonConvert.SerializeObject(FBS, Formatting.None, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

                    DataTruckerToolsTracking dtt = new DataTruckerToolsTracking();
                    dtt.InsertTTPayload(JSONresult, baseurl + "/api/checkcall/SaveCheckCall", "Post");


                    HttpResponseMessage responseTask = await client.PostAsJsonAsync("/api/checkcall/SaveCheckCall", FBS).ConfigureAwait(false);

                    if (responseTask.IsSuccessStatusCode)
                    {
                        chck = await responseTask.Content.ReadAsAsync<CheckCalls>();
                    }

                }
                catch (Exception ex)
                {
                    chck.Message = ex.Message;
                }

            }

            return chck;
        }

        public async Task SaveCheckCalltoFBS(CheckCalls cc)
        {

            DataDrivers dd = new DataDrivers();
            DataLoads dl = new DataLoads();


#region "Build FBSCheckCall"
            FBSCheckCallSrv.CheckCalls fbscc = new FBSCheckCallSrv.CheckCalls();
            FBSCheckCallSrv.Driver fbsdr = new FBSCheckCallSrv.Driver();
            FBSCheckCallSrv.Loads fbsld = new FBSCheckCallSrv.Loads();

            #region "Check Call"
            fbscc.CheckDate = cc.CheckDate;
            fbscc.CheckType = cc.CheckType;
            fbscc.CheckTypeID = cc.CheckTypeID;
            fbscc.Comments = cc.Comments;
            fbscc.DriverTripID = cc.DriverTripID;
            fbscc.ID = cc.ID;
            fbscc.Message = cc.Message;
            fbscc.Offset = cc.Offset;
            fbscc.Timezone = cc.Timezone;
            fbscc.GPSCoordinates = new FBSCheckCallSrv.GPSLocation();
            fbscc.GPSCoordinates.Lat = cc.GPSCoordinates.Lat;
            fbscc.GPSCoordinates.Long = cc.GPSCoordinates.Long;
            fbscc.City = "";
            fbscc.State = "";
            #endregion

            #region "Driver"
            OTR_API.Models.Driver dr = dd.GetDriverByDriverTrip(cc.DriverTripID);
            fbsdr.ID = dr.ID;
            fbsdr.Cellphone = dr.Cellphone;
            fbsdr.CompanyID = dr.CompanyID;
            fbsdr.DriversLicense = dr.DriversLicense;
            fbsdr.DeviceID = dr.DeviceID;
            fbsdr.EmailAddress = dr.EmailAddress;
            fbsdr.FirstName = dr.FirstName;
            fbsdr.LastName = dr.LastName;
            fbsdr.MCNumber = dr.MCNumber;
            fbsdr.Message = dr.Message;
            fbsdr.TrailerNumber = dr.TrailerNumber;
            fbsdr.TrailerType = dr.TrailerType;
            fbsdr.TruckColor = dr.TruckColor;
            fbsdr.TruckMake = dr.TruckMake;
            fbsdr.TruckNumber = dr.TruckNumber;
            fbsdr.TruckTag = dr.TruckTag;
            #endregion

            #region "Load"
            OTR_API.Models.Loads ld = dl.GetLoadByDriverTripID(cc.DriverTripID);
            fbsld.LoadID = ld.LoadID;
            fbsld.ID = ld.ID;
            fbsld.TripID = ld.TripID;
            fbsld.LoadStatus = ld.LoadStatus;
            fbsld.Temp = ld.Temp;
            fbsld.TotalPallets = ld.TotalPallets;
            fbsld.TotalWeight = ld.TotalWeight;
            fbsld.TotalPieces = ld.TotalPieces;
            fbsld.TotalMiles = ld.TotalMiles;
            fbsld.HazMat = ld.HazMat;

            fbsld.CompanyRep = new FBSCheckCallSrv.CompanyReps();
            fbsld.CompanyRep.RepID = ld.CompanyRep.RepID;
            fbsld.CompanyRep.FullName = ld.CompanyRep.FullName;
            fbsld.CompanyRep.EmailAddress = ld.CompanyRep.EmailAddress;


            fbsld.Message = ld.Message;
            fbsld.DriverTripID = ld.DriverTripID;
            fbsld.DriverID = ld.DriverID;
            fbsld.Driver = ld.Driver;
            fbsld.Active = ld.Active;
            fbsld.ActiveDate = ld.ActiveDate;
            #endregion

            FBSCheckCallSrv.FBSCheckCall FBS = new FBSCheckCallSrv.FBSCheckCall();
            FBS.CheckCall = fbscc;
            FBS.Driver = fbsdr;
            FBS.Load = fbsld;

#endregion
            CheckCalls returncc = new CheckCalls();

            try
            {
                try
                {
                    ALKLocation alk = new ALKLocation();
                    alk = CityState(FBS.CheckCall.GPSCoordinates);
                    FBS.CheckCall.City = OTR_API.DataClasses.DataAccess.isStringNull(alk.Address.City);
                    FBS.CheckCall.State = OTR_API.DataClasses.DataAccess.isStringNull(alk.Address.StateAbbreviation);

                }
                catch (Exception ex)
                {
                    DataAudit da = new DataAudit(); da.InsertErrorAuditLog("ALK Web Service - Error Pulling City State: " + ex.Message, "WebCallFunctions.SaveCheckCalltoFBS");
                }

                try
                {
                    string JSONresult = JsonConvert.SerializeObject(FBS, Formatting.None, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

                    DataTruckerToolsTracking dtt = new DataTruckerToolsTracking();
                    dtt.InsertTTPayload(JSONresult,"FBS Web Service/SaveCheckCall", "Post");

                    FBSCheckCallSrv.FBSCheckCall_InterfaceSoapClient i = new FBSCheckCallSrv.FBSCheckCall_InterfaceSoapClient();
                    FBSCheckCallSrv.CheckCalls returnfbscc = await Task.Run(() => i.SaveCheckCall(FBS, FBSkey));
                    returncc.ID = returnfbscc.ID;
                    returncc.Message = returnfbscc.Message;

                    DataAudit da = new DataAudit(); da.InsertAuditLog(80, "FBS Web Service", returnfbscc.Message, "WebCallFunctions.SaveCheckCalltoFBS");
                }
                catch(Exception ex)
                {
                    DataAudit da = new DataAudit(); da.InsertErrorAuditLog("FBS Web Service - Error Pulling City State: " + ex.Message, "WebCallFunctions.SaveCheckCalltoFBS");
                }

            }
            catch
            {


            }

            return;

        }


        public ALKLocation CityState(FBSCheckCallSrv.GPSLocation gps)
        {
            string ALKKey = "5736109DA014244F94A410A7BAA6EAE3";

            string ALKurl = "https://pcmiler.alk.com/apis/rest/v1.0/Service.svc";
            //https://pcmiler.alk.com/apis/rest/v1.0/Service.svc/locations/reverse?Coords=-75.163244%2C40.958188&region=NA&dataset=Current
    
            ALKLocation citystate = new ALKLocation();

            string resource = "/locations/reverse";
            string queryString = "?coords=" + gps.Long + "," + gps.Lat; // -85.9747,37.8928";
            Uri requestUri = new Uri(ALKurl + resource + queryString);
            System.Net.HttpWebRequest req = System.Net.WebRequest.Create(requestUri) as System.Net.HttpWebRequest;

            req.Headers["Authorization"] = ALKKey;
            req.ContentType = "application/json";

            try
            {
                using (System.Net.HttpWebResponse response = (System.Net.HttpWebResponse)req.GetResponse())
                {
                    using (System.IO.StreamReader sr = new System.IO.StreamReader(response.GetResponseStream()))
                    {
                        string result = sr.ReadToEnd();

                        citystate = Newtonsoft.Json.JsonConvert.DeserializeObject<ALKLocation>(result);
                    }
                }
            }
            catch
            {

            }

            return citystate;
        }







        public string[] TestEncryption(string input, string inputSecret)
        {
            string[] response = new string[10];

            // The sample encryption key. Must be 32 characters.
            byte[] secretbyte = AES256CBCEncrypter.MD5Byte(inputSecret);

            StringBuilder secretkey = new StringBuilder();
            for (int i = 0; i < secretbyte.Length; i++)
            {
                secretkey.Append(secretbyte[i].ToString("x2"));
            }

            string iv = inputSecret.Substring(0, 16);

            // Encrypt and decrypt the sample text via the Aes256CbcEncrypter class.
            string Encrypted = AES256CBCEncrypter.Encrypt(input, secretkey.ToString(), iv);
            string Decrypted = AES256CBCEncrypter.Decrypt(Encrypted, secretkey.ToString(), iv);

            string friendly = AES256CBCEncrypter.Friendly(Encrypted);

            byte[] urlencode = Encoding.UTF8.GetBytes(AES256CBCEncrypter.Friendly(Encrypted));
            string urlencode64 = Convert.ToBase64String(urlencode);

            //Could be an error here - the suggested encoding would include LF as the new line character but by default dotnet is using CRLF - not sure how to change this.


            byte[] base64EncodedBytes = Convert.FromBase64String(urlencode64);
            string urldecode64 = Encoding.UTF8.GetString(base64EncodedBytes);


            // Show the encrypted and decrypted data and the key used.

            response[0] = "Original: " + input;
            response[1] = "IV: " + iv;
            response[2] = "Secret Key: " + inputSecret;
            response[3] = "Secret Key Hash: " + secretkey.ToString();
            response[4] = "Encrypted: " + Encrypted;
            response[5] = "Encrypted Friendly: " + friendly;
            response[6] = "Decrypted: " + Decrypted;
            response[7] = "URL Base64 Endocde: " + urlencode64;
            response[8] = "URL Base64 Decode: " + urldecode64;

            return response;
        }

        public string[] TestDecrypt(string input, string inputSecret)
        {
            string[] response = new string[10];

            byte[] base64EncodedBytes = Convert.FromBase64String(input);
            string urldecode64 = Encoding.UTF8.GetString(base64EncodedBytes);

            string unfriendly = AES256CBCEncrypter.UnFriendly(urldecode64);

            // The sample encryption key. Must be 32 characters.
            byte[] secretbyte = AES256CBCEncrypter.MD5Byte(inputSecret);

            StringBuilder secretkey = new StringBuilder();
            for (int i = 0; i < secretbyte.Length; i++)
            {
                secretkey.Append(secretbyte[i].ToString("x2"));
            }

            string iv = inputSecret.Substring(0, 16);


            string Decrypted = AES256CBCEncrypter.Decrypt(unfriendly, secretkey.ToString(), iv);

            // Show the encrypted and decrypted data and the key used.

            response[0] = "URL Base64 Endocded: " + input;
            response[1] = "URL Base64 Decode: " + urldecode64;
            response[2] = "Encrypted: " + unfriendly;
            response[3] = "Decrypted: " + Decrypted;
            response[4] = "IV: " + iv;
            response[5] = "Secret Key: " + inputSecret;
            response[6] = "Secret Key Hash: " + secretkey.ToString();

            return response;
        }



        public async Task<OTR_API.TruckerTools.Models.LoadResponse> PostTTLoad(OTR_API.TruckerTools.Models.Load load)
        {
            OTR_API.TruckerTools.Models.LoadResponse lr = new OTR_API.TruckerTools.Models.LoadResponse();

            try
            {
                OTR_API.TruckerTools.Models.LoadSync loadSync = new OTR_API.TruckerTools.Models.LoadSync();
                loadSync.integrationId = TTIntegrationId;
                loadSync.accountId = TTAccountID;
                loadSync.loads = new List<OTR_API.TruckerTools.Models.Load>();
                loadSync.loads.Add(load);

                using (HttpClient client = HeaderGenerate(TTbaseUrl))
                {

                    try
                    {
                        string JSONresult = JsonConvert.SerializeObject(loadSync, Formatting.None, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

                        DataTruckerToolsTracking dtt = new DataTruckerToolsTracking();
                        dtt.InsertTTPayload(JSONresult, TTbaseUrl + "/api/postLoads", "Post");


                        HttpResponseMessage responseTask = await client.PostAsJsonAsync("/api/postLoads", loadSync).ConfigureAwait(false);

                        if (responseTask.IsSuccessStatusCode)
                        {
                            var result = await responseTask.Content.ReadAsAsync<OTR_API.TruckerTools.Models.LoadResponse>();
                            lr = result;
                        }

                    }
                    catch (Exception ex)
                    {
                        lr.Message = ex.Message;
                    }

                }
            }
            catch(Exception ex)
            {
                lr.Message = ex.Message;
            }

            return lr;
        }

        public async Task<OTR_API.TruckerTools.Models.LoadResponse> GetAvailableTTLoads()
        {
            OTR_API.TruckerTools.Models.LoadResponse lr = new OTR_API.TruckerTools.Models.LoadResponse();

            try
            {

                using (HttpClient client = HeadersForAccessTokenGenerate())
                {

                    try
                    {
                        DataTruckerToolsTracking dtt = new DataTruckerToolsTracking();
                        dtt.InsertTTPayload("", TTbaseUrl + "/api/broker/getAvailableLoads", "Get");

                        HttpResponseMessage responseTask = await client.GetAsync("/api/broker/getAvailableLoads").ConfigureAwait(false);

                        if (responseTask.IsSuccessStatusCode)
                        {
                            var result = await responseTask.Content.ReadAsAsync<OTR_API.TruckerTools.Models.LoadResponse>();
                            lr = result;
                        }

                    }
                    catch (Exception ex)
                    {
                        lr.Message = ex.Message;
                    }

                }
            }
            catch (Exception ex)
            {
                lr.Message = ex.Message;
            }

            return lr;
        }

        public async Task<OTR_API.TruckerTools.Models.CarrierResponse> PostTTCarrier(OTR_API.TruckerTools.Models.Carrier carrier)
        {
            OTR_API.TruckerTools.Models.CarrierResponse lr = new OTR_API.TruckerTools.Models.CarrierResponse();

            try
            {
                OTR_API.TruckerTools.Models.CarrierSync carrierSync = new OTR_API.TruckerTools.Models.CarrierSync();
                carrierSync.accountId = TTAccountID;
                carrierSync.integrationId = TTIntegrationId;
                carrierSync.carriers = new List<OTR_API.TruckerTools.Models.Carrier>();
                carrierSync.carriers.Add(carrier);



                using (HttpClient client = HeaderGenerate(TTbaseUrl))
                {
                    try
                    {
                        string JSONresult = JsonConvert.SerializeObject(carrierSync, Formatting.None, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

                        DataTruckerToolsTracking dtt = new DataTruckerToolsTracking();
                        dtt.InsertTTPayload(JSONresult, TTbaseUrl + "/api/qualifiedCarriers", "Post");


                        HttpResponseMessage responseTask = await client.PostAsJsonAsync("/api/qualifiedCarriers", carrierSync).ConfigureAwait(false);

                        if (responseTask.IsSuccessStatusCode)
                        {
                            var result = await responseTask.Content.ReadAsAsync<OTR_API.TruckerTools.Models.CarrierResponse>();
                            lr = result;
                        }

                    }
                    catch (Exception ex)
                    {
                        lr.Message = ex.Message;
                    }

                }
            }
            catch (Exception ex)
            {
                lr.Message = ex.Message;
            }

            return lr;
        }




        public async Task<OTR_API.TruckerToolsTracking.Models.TrackingResponse> PostTrackLoad(OTR_API.TruckerToolsTracking.Models.Load load)
        {
            OTR_API.TruckerToolsTracking.Models.TrackingResponse lr = new OTR_API.TruckerToolsTracking.Models.TrackingResponse();

            try
            {
                load.accountId = TTTrackingAccountID;
                load.partnerId = TTTrackingPartnerID;


                using (HttpClient client = HeaderGenerate(TTTrackingbaseurl))
                {
                    try
                    {
                        string JSONresult = JsonConvert.SerializeObject(load, Formatting.None, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
                        var resultJSON = JsonConvert.DeserializeObject<dynamic>(JSONresult);

                        DataTruckerToolsTracking dtt = new DataTruckerToolsTracking();
                        dtt.InsertTTPayload(JSONresult, TTTrackingbaseurl + "/loadtrackservice/LTL", "Post");


                        HttpContent httpContent = new StringContent(JSONresult);
                        httpContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

                        //HttpResponseMessage responseTask = await client.PostAsJsonAsync("/loadtrackservice/LTL", load).ConfigureAwait(false);
                        HttpResponseMessage responseTask = await client.PostAsync("/loadtrackservice/LTL", httpContent).ConfigureAwait(false);


                        if (responseTask.IsSuccessStatusCode)
                        {
                            var result = await responseTask.Content.ReadAsAsync<OTR_API.TruckerToolsTracking.Models.TrackingResponse>();
                            lr = result;
                        }

                    }
                    catch (Exception ex)
                    {
                        lr.response.Message = ex.Message;
                    }

                }
            }
            catch (Exception ex)
            {
                lr.response.Message = ex.Message;
            }

            return lr;
        }

        public async Task<OTR_API.TruckerToolsTracking.Models.TrackingResponse> PutUpdateTrackLoad(OTR_API.TruckerToolsTracking.Models.Load load)
        {
            OTR_API.TruckerToolsTracking.Models.TrackingResponse lr = new OTR_API.TruckerToolsTracking.Models.TrackingResponse();

            try
            {
                load.accountId = TTTrackingAccountID;
                load.partnerId = TTTrackingPartnerID;

                using (HttpClient client = HeaderGenerate(TTTrackingbaseurl))
                {
                    try
                    {
                        string JSONresult = JsonConvert.SerializeObject(load, Formatting.None, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
                        var resultJSON = JsonConvert.DeserializeObject<dynamic>(JSONresult);

                        DataTruckerToolsTracking dtt = new DataTruckerToolsTracking();
                        dtt.InsertTTPayload(JSONresult, TTTrackingbaseurl + "/loadtrackservice/LTL", "Put");

                        HttpContent httpContent = new StringContent(JSONresult);
                        httpContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

                        //HttpResponseMessage responseTask = await client.PutAsJsonAsync("/loadtrackservice/LTL", load).ConfigureAwait(false);
                        HttpResponseMessage responseTask = await client.PutAsync("/loadtrackservice/LTL", httpContent).ConfigureAwait(false);

                        if (responseTask.IsSuccessStatusCode)
                        {
                            //var resultstr = await responseTask.Content.ReadAsStringAsync();
                            var result = await responseTask.Content.ReadAsAsync<OTR_API.TruckerToolsTracking.Models.TrackingResponse>();
                            lr = result;
                        }

                    }
                    catch (Exception ex)
                    {
                        lr.response.Message = ex.Message;
                    }

                }
            }
            catch (Exception ex)
            {
                lr.response.Message = ex.Message;
            }

            return lr;
        }

        public async Task<OTR_API.TruckerToolsTracking.Models.TrackingResponse> CancelLoadTracking(OTR_API.TruckerToolsTracking.Models.Load load)
        {
            OTR_API.TruckerToolsTracking.Models.TrackingResponse lr = new OTR_API.TruckerToolsTracking.Models.TrackingResponse();

            try
            {
                load.accountId = TTTrackingAccountID;
                load.partnerId = TTTrackingPartnerID;

                using (HttpClient client = HeaderGenerate(TTTrackingbaseurl))
                {
                    try
                    {
                        DataTruckerToolsTracking dtt = new DataTruckerToolsTracking();
                        dtt.InsertTTPayload("", TTTrackingbaseurl + "/loadtrackservice/cancelLoadTrack", "Post");

                        HttpResponseMessage responseTask = await client.PostAsJsonAsync("/loadtrackservice/cancelLoadTrack", load).ConfigureAwait(false);

                        if (responseTask.IsSuccessStatusCode)
                        {
                            var result = await responseTask.Content.ReadAsAsync<OTR_API.TruckerToolsTracking.Models.TrackingResponse>();
                            lr = result;
                        }

                    }
                    catch (Exception ex)
                    {
                        lr.response.Message = ex.Message;
                    }

                }
            }
            catch (Exception ex)
            {
                lr.response.Message = ex.Message;
            }

            return lr;
        }


        public async Task<OTR_API.TruckerToolsTracking.Models.TrackingResponse> GetLoadsTracked()
        {
            OTR_API.TruckerToolsTracking.Models.TrackingResponse lr = new OTR_API.TruckerToolsTracking.Models.TrackingResponse();

            try
            {
                using (HttpClient client = HeaderGenerate(TTTrackingbaseurl2))
                {

                    try
                    {
                        DataTruckerToolsTracking dtt = new DataTruckerToolsTracking();
                        dtt.InsertTTPayload("", TTTrackingbaseurl + "/loadtrackservice/getLoadTrackDetailsServicsV2", "Get");

                        HttpResponseMessage responseTask = await client.GetAsync("/loadtrackservice/getLoadTrackDetailsServicsV2").ConfigureAwait(false);

                        if (responseTask.IsSuccessStatusCode)
                        {
                            var result = await responseTask.Content.ReadAsAsync<OTR_API.TruckerToolsTracking.Models.TrackingResponse>();
                            //lr = JsonConvert.DeserializeObject<List<OTR_API.TruckerToolsTracking.Models.Load>>(result);
                            lr = result;
                        }

                    }
                    catch (Exception ex)
                    {
                        //lr.Message = ex.Message;
                    }

                }
            }
            catch (Exception ex)
            {
                //lr.Message = ex.Message;
            }

            return lr;
        }

        //public async Task<List<OTR_API.TruckerToolsTracking.Models.Load>> GetLoadsTracked()
        //{
        //    List<OTR_API.TruckerToolsTracking.Models.Load> lr = new List<OTR_API.TruckerToolsTracking.Models.Load>();

        //    try
        //    {
        //        using (HttpClient client = HeaderGenerate(TTTrackingbaseurl))
        //        {

        //            try
        //            {
        //                HttpResponseMessage responseTask = await client.GetAsync("/loadtrackservice/getLoadTrackDetailsServicsV2").ConfigureAwait(false);

        //                if (responseTask.IsSuccessStatusCode)
        //                {
        //                    var result = await responseTask.Content.ReadAsAsync<List<OTR_API.TruckerToolsTracking.Models.Load>>();
        //                    //lr = JsonConvert.DeserializeObject<List<OTR_API.TruckerToolsTracking.Models.Load>>(result);
        //                    lr = result;
        //                }

        //            }
        //            catch (Exception ex)
        //            {
        //                //lr.Message = ex.Message;
        //            }

        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        //lr.Message = ex.Message;
        //    }

        //    return lr;
        //}



        private HttpClient HeaderGenerate(string baseurl)
        {
            HttpClientHandler handler = new HttpClientHandler() { UseDefaultCredentials = false };
            HttpClient client = new HttpClient(handler);
            try
            {
                client.BaseAddress = new Uri(baseurl);
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            }
            catch (Exception ex)
            {
                throw ex;
            }
            return client;
        }

        private HttpClient HeadersForAccessTokenGenerate()
        {

            string authsign = "accountId=" + TTAccountID + "&userName=" + TTUserName + "&timestamp=" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"); ;

            AES256CBCEncrypter aes = new AES256CBCEncrypter();
            string encodedsignature = aes.TTAuthentication(authsign, TTSecret);


            HttpClientHandler handler = new HttpClientHandler() { UseDefaultCredentials = false };
            HttpClient client = new HttpClient(handler);
            try
            {
                client.BaseAddress = new Uri(TTbaseUrl);
                client.DefaultRequestHeaders.Accept.Clear();
                //client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-www-form-urlencoded"));
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                //client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", apiKey);
                //client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", apiKey);
                client.DefaultRequestHeaders.Add("X-Api-Key", TTapiKey);
                client.DefaultRequestHeaders.Add("X-Signature", encodedsignature);

            }
            catch (Exception ex)
            {
                throw ex;
            }
            return client;
        }

        public bool VerifyTrackingAccount(OTR_API.TruckerToolsTracking.Models.StatusUpdate status)
        {
            bool results = false;

            if(status.partnerid == TTTrackingPartnerID)
            {
                if(status.accountid == TTTrackingAccountID)
                {
                    results = true;
                }
            }

            return results;
        }


        


    }
}