using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Net.Http;
using System.Net.Http.Headers;

namespace OTR_API
{
    public class Notifications
    {
        public string SendNotification(string serverApiKey, string senderId, string RegId, string GooglePushKey, string MsgText, string Action = "0", string sender = "", string badgeCount = "0", string actionData = "")
        {

            //try
            //{
            //    RestClient client = new RestClient("https://fcm.googleapis.com/fcm");
            //    var reqBoby = new
            //    {
            //        to = RegId,
            //        data = new
            //        {
            //            Action = Action,
            //            sender = sender,
            //            badgeCount = badgeCount,
            //            actionData = actionData,
            //            message = MsgText
            //        },
            //        content_available = true,
            //        priority = "high"
            //    };

            //    var request = new RestRequest("/send", Method.POST);
            //    request.AddHeader("Content-Type", "application/json");
            //    request.AddHeader("Authorization", "key=" + serverApiKey);
            //    request.AddHeader("Sender", "id=" + senderId);
            //    request.AddJsonBody(reqBoby);

            //    IRestResponse response = client.Execute<dynamic>(request);
            //    return response.Content;
            //}
            //catch
            //{ }

            string strhere = "";

            try
            {
                using (var client = new HttpClient())
                {
                    //client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", "4d53bce03ec34c0a911182d4c228ee6c:A93reRTUJHsCuQSHR+L3GxqOJyDmQpCgps102ciuabc=");

                    client.DefaultRequestHeaders.Add("Content-Type", "application/json");
                    client.DefaultRequestHeaders.Add("Authorization", "key=" + serverApiKey);
                    client.DefaultRequestHeaders.Add("Sender", "id=" + senderId);

                    client.BaseAddress = new Uri("https://fcm.googleapis.com/fcm");

                    var reqBoby = new
                    {
                        to = RegId,
                        data = new
                        {
                            Action = Action,
                            sender = sender,
                            badgeCount = badgeCount,
                            actionData = actionData,
                            message = MsgText
                        },
                        content_available = true,
                        priority = "high"
                    };

                    var responseTask = client.PostAsJsonAsync("/send", reqBoby);

                    strhere = "Create Response";

                    responseTask.Wait();

                    strhere = "Wait";

                    var result = responseTask.Result;

                    strhere = "Result";

                    if (result.IsSuccessStatusCode)
                    {
                        strhere = "Success";

                        //lblSuccess.Text = "Successful - you should receive an email with a password to " + email;

                        //var readTask = result.Content.ReadAsAsync<Object[]>();
                        //readTask.Wait();

                        // var success = readTask.Result;
                    }
                    else
                    {
                        strhere = "Failure";

                        //lblSuccess.Text = result.Content.ToString();
                    }


                    strhere = responseTask.Result.Content.ToString();
                }


                return strhere;
            }
            catch
            { return strhere;  }

        }



        //public static string SendNotification(string serverApiKey, string senderId, string RegId, string GooglePushKey, string MsgText, string Action = "0", string sender = "", string badgeCount = "0", string actionData = "")
        //{
        //    try
        //    {
        //        RestClient client = new RestClient("https://fcm.googleapis.com/fcm");
        //        var reqBoby = new
        //        {
        //            to = RegId,
        //            data = new
        //            {
        //                Action = Action,
        //                sender = sender,
        //                badgeCount = badgeCount,
        //                actionData = actionData,
        //                message = MsgText
        //            },
        //            content_available = true,
        //            priority = "high"
        //        };

        //        var request = new RestRequest("/send", Method.POST);
        //        request.AddHeader("Content-Type", "application/json");
        //        request.AddHeader("Authorization", "key=" + serverApiKey);
        //        request.AddHeader("Sender", "id=" + senderId);
        //        request.AddJsonBody(reqBoby);

        //        IRestResponse response = client.Execute<dynamic>(request);
        //        return response.Content;
        //    }

        //}

    }
}