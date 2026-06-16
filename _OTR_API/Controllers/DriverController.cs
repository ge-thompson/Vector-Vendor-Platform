using OTR_API.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using OTR_API.DataClasses;
using OTR_API.Filters;
using System.Threading.Tasks;

namespace OTR_API.Controllers
{
    [HMACAuthentication]
    [RoutePrefix("api/driver")]

    public class DriverController : ApiController
    {
        //http://localhost:5129/api/driver/GetAllDrivers
        [HttpGet]
        public IEnumerable<Driver> GetAllDrivers()
        {
            DataDrivers dc = new DataDrivers();
            IEnumerable<Driver> driverlist = dc.GetAllDrivers();

            return driverlist;
        }


        //http://localhost:5129/api/driver/GetDriverByLoad/1
        [HttpGet]
        public Driver GetDriverByLoad(int id)
        {
            Driver logindriver = new Driver();

            #region Verify
            if (id == 0)
            {
                logindriver.Message = "You must provide an ID";
                return logindriver;
            }
            #endregion

            DataDrivers dc = new DataDrivers();
            logindriver = dc.GetDriverByLoad(id);
            return logindriver;
        }


        //http://localhost:5129/api/driver/GetDriver/1
        [HttpGet]
        public Driver GetDriver(int id)
        {
            Driver logindriver = new Driver();

            #region Verify
            if (id == 0)
            {
                logindriver.Message = "You must provide an ID";
                return logindriver;
            }
            #endregion

            DataDrivers dc = new DataDrivers();
            logindriver = dc.GetDriver(id, true);
            return logindriver;
        }


        //http://localhost:5129/api/driver/SaveDriver
        [HttpPost]
        public Driver SaveDriver([FromBody]Driver driver)
        {
            Driver logindriver = new Driver();

            #region Verify
            if (driver.FirstName.Length == 0)
            {
                logindriver.Message = "You need a First Name";
                return logindriver;
            }
            #endregion

            DataDrivers dc = new DataDrivers();
            int id = dc.InsertDriver(driver);

                
            switch (id)
            {
                case 0:
                    logindriver.Message = "Error Inserting Driver";
                    break;
                case 1:
                    logindriver.Message = "Duplicate Email";
                    break;
                case 2:
                    logindriver.Message = "mc Number already in Use";
                    break;
                default:

                    try
                    {
                        DriverDevice device = driver.Devices;
                        device.DriverID = id;
                        dc.InsertDriverToken(device);
                    }
                    catch
                    { }


                    logindriver = dc.GetDriver(id, true);
                    logindriver.Message = "Sucessful";
                    break;
            }

            return logindriver;
        }


        //http://localhost:5129/api/driver/UpdateDriver
        [HttpPut]
        public Driver UpdateDriver([FromBody]Driver driver)
        {

            Driver logindriver = new Driver();

            #region Verify
            if (driver.FirstName.Length == 0)
            {
                logindriver.Message = "You need a First Name";
                return logindriver;
            }
            #endregion

            DataDrivers dc = new DataDrivers();

            int id = dc.UpdateDriver(driver);
            switch (id)
            {
                case 0:
                    logindriver.Message = "Error Updating Driver Information";
                    break;
                case 1:
                    logindriver.Message = "mc Number already in Use";
                    break;
                case 2:
                    logindriver.Message = "mc Number already in Use";
                    break;
                default:

                    logindriver = dc.GetDriver(id, true);
                    logindriver.Message = "Sucessful";
                    break;
            }

            return logindriver;
        }


        //http://localhost:5129/api/driver/UpdatePassword
        [HttpPut]
        public Driver UpdatePassword([FromBody]Driver driver)
        {
            Driver logindriver = new Driver();

            #region Verify
            if (driver.FirstName.Length == 0)
            {
                logindriver.Message = "You must provide a First Name";
                return logindriver;
            }
            #endregion

            DataDrivers dc = new DataDrivers();
            string ip = System.Web.HttpContext.Current.Request.ServerVariables["REMOTE_ADDR"];

            int id = dc.UpdatePassword(driver, ip);
            switch (id)
            {
                case 0:
                    logindriver.Message = "Error Updating Driver Password";
                    break;
                case 1:
                    logindriver.Message = "Email and ID does not match Driver";
                    break;
                default:

                    logindriver = dc.GetDriver(id, true);
                    logindriver.Message = "Sucessful";
                    break;
            }

            return logindriver;
        }


        //http://localhost:5129/api/driver/ResetPasswordQuest

        //Generate Reset Link for new password based on email address with token that expires
        //Send Email Address an email with reset link
        //Respond if Email has been sent.
        [HttpPut]
        public ResponseMessage ResetPasswordRequest([FromBody]Driver driver)
        {

            string msg = "";
            ResponseMessage response = new ResponseMessage();

            #region Verify

            if (driver.EmailAddress.Length == 0)
            {
                response.Message = "You must provide an Email Address";
            }

            #endregion

            DataDrivers dc = new DataDrivers();
            string ip = System.Web.HttpContext.Current.Request.ServerVariables["REMOTE_ADDR"];

            byte[] token = Guid.NewGuid().ToByteArray();

            int id = dc.ResetPasswordRequest(driver, token, ip);

            switch (id)
            {
                case 0:
                    response.Message = "Error Creating Reset";
                    break;

                case 1:
                    response.Message = "No Matching Email";
                    break;

                default:
                    msg = Convert.ToBase64String(token);
                    msg = System.Convert.ToBase64String(token).TrimEnd('=').Replace('+', '-').Replace('/', '_');
                    try
                    {
                        Communicate cc = new Communicate();
                        string Body = "A request has been made to reset the password for your email address. If you did not make this request then you can ignore this email. If you did request to reset your password the following link." + Environment.NewLine + Environment.NewLine;
                        response = cc.SendEmail(driver.EmailAddress, Body + "http://inmotion.vectortransport.com/inmotionreset.aspx?email=" + driver.EmailAddress +"&reset=" + msg, "In Motion App - Password Reset Request");
                    }
                    catch(Exception ex)
                    {
                        response.Message = ex.Message;
                    }

                    break;
            }



            return response;
        }


        //http://localhost:5129/api/driver/ResetPassword
        [HttpPut]
        public ResponseMessage ResetPassword([FromBody]Driver driver)
        {

            ResponseMessage response = new ResponseMessage();
            bool result = false;

            #region Verify
            if (driver.EmailAddress.Length == 0)
            {
                response.Message = "You must provide an Email Address";
            }

            if(driver.ResetToken.Length == 0)
            {
                response.Message = "You must provide a Reset Token";
            }
            #endregion

            DataDrivers dc = new DataDrivers();
            string ip = System.Web.HttpContext.Current.Request.ServerVariables["REMOTE_ADDR"];



            string incoming = driver.ResetToken.Replace('_', '/').Replace('-', '+');
            switch (driver.ResetToken.Length % 4)
            {
                case 2: incoming += "=="; break;
                case 3: incoming += "="; break;
            }
            byte[] bytetoken = Convert.FromBase64String(incoming);
            string originalText = System.Text.Encoding.ASCII.GetString(bytetoken);

            string msg = dc.ResetDriverPassword(driver, bytetoken, ip);
            
            switch(response.Message)
            {
                case "Error Searching Reset Token":
                case "No Matching Reset Token":
                case "Error Saving New Password":
                case "Email and ID Does Not Match":
                    response.Message = msg;
                    break;

                default:
                    try
                    {
                        Communicate cc = new Communicate();
                        string Body = "Your password has been reset. " + Environment.NewLine + Environment.NewLine + "Your new password is: " + msg;
                        response = cc.SendEmail(driver.EmailAddress, Body, "In Motion App - Password Reset");
                    }
                    catch (Exception ex)
                    {
                        response.Message = ex.Message;
                    }

                    break;
            }

            return response;
        }


        //http://localhost:5129/api/driver/DeleteDriver
        [HttpDelete]
        public void DeleteDriver(int id)
        {
            DataDrivers dc = new DataDrivers();
            dc.DeleteDriver(id);
        }


        //http://localhost:5129/api/driver/LoginDriver
        [HttpPost]
        public Driver LoginDriver([FromBody]Driver driver)
        {
            string email = driver.EmailAddress;
            string password = driver.Password;
            string ip = System.Web.HttpContext.Current.Request.ServerVariables["REMOTE_ADDR"];

            DataDrivers dc = new DataDrivers();
            int id = dc.LoginDriver(email, password, ip);

            Driver logindriver = new Driver();
            switch (id)
            {
                case 0:
                    logindriver.Message = "Email Does Not Exist";
                    break;
                case 1:
                    logindriver.Message = "Incorrect Password";
                    break;
                default:

                    DriverDevice device = driver.Devices;
                    device.DriverID = id;


                    dc.InsertDriverToken(device);

                    logindriver = dc.GetDriver(id, true);
                    logindriver.Message = "Sucessful";
                    break;
             }

            return logindriver;

        }


        //http://localhost:5129/api/driver/UploadDocument
        [HttpPost]
        public HttpResponseMessage UploadDocument([FromBody] Documents document)
        {
            HttpResponseMessage response;

            try
            {
                string fileloc = "Driver/" + document.DriverID.ToString();
                String path = System.Web.HttpContext.Current.Server.MapPath("~/Uploads/" + fileloc); //Path

                //Check if directory exist
                if (!System.IO.Directory.Exists(path))
                {
                    System.IO.Directory.CreateDirectory(path); //Create directory if it doesn't exist
                }

                string filetype = ".jpg";
                switch(document.FileType)
                {
                    case FileTypeOption.JPG:
                        filetype = ".jpg";
                        break;
                    case FileTypeOption.PNG:
                        filetype = ".png";
                        break;
                    case FileTypeOption.DOC:
                    case FileTypeOption.DOCX:
                        filetype = ".docx";
                        break;
                    case FileTypeOption.PDF:
                        filetype = ".pdf";
                        break;
                    default:
                        filetype = ".jpg";
                        break;
                }

                DateTime dte = DateTime.Now;
                string imageName = document.DriverID.ToString() +"_" + dte.Year.ToString() + dte.Month.ToString() + dte.Day.ToString() + dte.Hour.ToString() + dte.Minute.ToString() + dte.Second.ToString() + filetype ;

                //set the image path
                string imgPath = System.IO.Path.Combine(path, imageName);

                byte[] imageBytes = Convert.FromBase64String(document.FileBase64);

                System.IO.File.WriteAllBytes(imgPath, imageBytes);

                document.FileName = imageName;
                document.FileLocation = fileloc;

                DataDocuments dd = new DataDocuments();
                dd.InsertDocument(document);


                response = Request.CreateResponse(HttpStatusCode.OK);
            }
            catch
            {
                response = Request.CreateResponse(HttpStatusCode.BadRequest);
            }

            return response;
        }


        //http://localhost:5129/api/driver/UploadDocumentPreLoaded
        [HttpPost]
        public Documents UploadDocumentPreLoaded([FromBody] Documents document)
        {
            DataDocuments dd = new DataDocuments();
            Documents doc = new Documents();
            try
            {
                string fileloc = "Driver/" + document.DriverID.ToString();
                string path = System.Web.HttpContext.Current.Server.MapPath("~/Uploads/" + fileloc); //Path
                string temppath = System.Web.HttpContext.Current.Server.MapPath("~/Uploads/Temp/");

                //Check if directory exist
                if (!System.IO.Directory.Exists(path))
                {
                    System.IO.Directory.CreateDirectory(path); //Create directory if it doesn't exist
                }

                string filetype = dd.FileExtention(document.FileType);

                DateTime dte = DateTime.Now;
                string imageName = document.DriverID.ToString() + "_" + dte.Year.ToString() + dte.Month.ToString() + dte.Day.ToString() + dte.Hour.ToString() + dte.Minute.ToString() + dte.Second.ToString() + filetype;
                string imgPath = System.IO.Path.Combine(path, imageName);


                Documents dc = dd.GetUpload(document.UploadID);

                string imgTempPath = System.IO.Path.Combine(temppath, dc.FileName);


                System.IO.File.Copy(imgTempPath, imgPath);

                document.FileName = imageName;
                document.FileLocation = fileloc;

                
                int docid = dd.InsertDocument(document);

                switch (docid)
                {
                    case 0:
                        doc.Message = "Upload Failed";
                        break;
                    case 1:
                        doc.Message = "Upload Failed";
                        break;
                    default:

                        doc = dd.GetDocumentsByID(docid);
                        doc.Message = "Successful";

                        dd.ClaimUpload(document.UploadID);
                        System.IO.File.Delete(imgTempPath);
                        break;
                }
            }
            catch
            {
                doc.Message = "Error Saving Document";
            }

            return doc;
        }

        //http://localhost:5129/api/driver/UploadDocumentPreLoaded
        [HttpPost]
        public Documents UploadProfilePreLoaded([FromBody] Documents document)
        {
            DataDocuments dd = new DataDocuments();
            Documents doc = new Documents();
            try
            {
                string fileloc = "Driver/" + document.DriverID.ToString() + "/Profile";
                string path = System.Web.HttpContext.Current.Server.MapPath("~/Uploads/" + fileloc); //Path
                string temppath = System.Web.HttpContext.Current.Server.MapPath("~/Uploads/Temp/");

                //Check if directory exist
                if (!System.IO.Directory.Exists(path))
                {
                    System.IO.Directory.CreateDirectory(path); //Create directory if it doesn't exist
                }

                string filetype = dd.FileExtention(document.FileType);

                DateTime dte = DateTime.Now;
                string imageName = "Profile" + filetype;
                string imgPath = System.IO.Path.Combine(path, imageName);

                Documents dc = dd.GetUpload(document.UploadID);

                string imgTempPath = System.IO.Path.Combine(temppath, dc.FileName);


                System.IO.File.Copy(imgTempPath, imgPath, true);

                document.FileName = imageName;
                document.FileLocation = fileloc;

                doc.Message = "Successful";


                Driver dr = new Driver();
                dr.ID = document.DriverID;
                dr.Profile = fileloc + "/" + imageName;
                DataDrivers ddr = new DataDrivers();
                int id = ddr.UpdateDriverProfile(dr);


                dd.ClaimUpload(document.UploadID);
                System.IO.File.Delete(imgTempPath);


            }
            catch
            {
                doc.Message = "Error Saving Document";
            }

            return doc;
        }

        //http://localhost:5129/api/driver/UploadDriverFile
        [ValidateMimeMultipartContentFilter]
        [HttpPost]
        public Task<Documents> UploadDriverFile()
        {

                string path = System.Web.HttpContext.Current.Server.MapPath("~/Uploads/Temp/");
                string ip = System.Web.HttpContext.Current.Request.ServerVariables["REMOTE_ADDR"];

                var streamProvider = new MultipartFormDataStreamProvider(path);



                var task = Request.Content.ReadAsMultipartAsync(streamProvider).ContinueWith<Documents>(t =>
                {
                    Documents doc = new Documents();
                    if (streamProvider.FileData.Count == 0)
                    {
                        doc.Message = "No File - ";
                        return doc;
                    }

                    var firstFile = streamProvider.FileData.FirstOrDefault();


                    try
                    {
                        if (firstFile != null)
                        {
                            var filename = firstFile.Headers.ContentDisposition.FileName;
                            int lastIndex = filename.Split('.').Count();
                            var ext = "." + filename.Split('.')[lastIndex-1].Replace("\"", "");

                            DataDocuments dd = new DataDocuments();
                            FileTypeOption FTO = dd.FileType(ext);

                            if (FTO == FileTypeOption.NA)
                                doc.Message = "Invalid File Type";
                            else
                            {
                                System.Random random = new System.Random();
                                int randomNumber = random.Next(0, 100);

                                DateTime dte = DateTime.Now;
                                string imageName = randomNumber.ToString() + "_" + dte.Year.ToString() + dte.Month.ToString() + dte.Day.ToString() + dte.Hour.ToString() + dte.Minute.ToString() + dte.Second.ToString() + ext;

                                string imgPath = System.IO.Path.Combine(path, imageName);
                                System.IO.File.Move(firstFile.LocalFileName, imgPath);

                                int uploadID = dd.InsertUpload(imageName, ip);

                                switch (uploadID)
                                {
                                    case 0:
                                        doc.Message = "Upload Failed";
                                        break;
                                    case 1:
                                        doc.Message = "Upload Failed";
                                        break;
                                    default:
                                        doc.FileType = FTO;
                                        doc.DocumentType = DocumentTypeOption.Driver;
                                        doc.FileName = imageName;
                                        doc.UploadID = uploadID;
                                        doc.Message = "Successful";
                                        break;
                                }
                            }
                        }
                        else
                            doc.Message = "No File";

                    }
                    catch (Exception ex)
                    {
                        doc.Message = ex.Message;
                    }

                    return doc;
                });

            return task;
        }

        //http://localhost:5129/api/driver/GetDocument
        [HttpPost]
        public Documents GetDocument([FromBody] Documents document)
        {
            try
            {
                //Requires DriverID & FileName

                string fileloc = System.Web.HttpContext.Current.Server.MapPath("~/Uploads/Driver/" + document.DriverID.ToString() + @"/" + document.FileName);
                byte[] image = System.IO.File.ReadAllBytes(fileloc);
                document.FileBase64 = Convert.ToBase64String(image);
            }
            catch
            { }


            return document;
        }

        //http://localhost:5129/api/driver/GetDocument
        [HttpPost]
        public Documents GetDocumentByID([FromBody] Documents document)
        {
            Documents doc = new Documents();
            try
            {
                DataDocuments dd = new DataDocuments();
                doc = dd.GetDocumentsByID(document.ID);
            }
            catch
            { doc.Message = "Error"; }


            return doc;
        }

        //http://localhost:5129/api/driver/GetDriverDevices/1
        [HttpGet]
        public IEnumerable<DriverDevice> GetDriverDevices(int id)
        {
            DataDrivers dc = new DataDrivers();
            IEnumerable<DriverDevice> devicelist = dc.GetDriverDevices(id);

            return devicelist;
        }

        //http://localhost:5129/api/driver/GetDriverDocuments/1
        [HttpGet]
        public IEnumerable<Documents> GetDriverDocuments(int id)
        {
            DataDocuments dc = new DataDocuments();
            IEnumerable<Documents> doclist = dc.GetDocumentsByDriver(id);

            return doclist;
        }

        //http://localhost:5129/api/driver/SendNotification
        [HttpPut]
        public DriverNotification SendNotification(DriverNotification Notification)
        {
            //0, Default
            //1, Trip (Data: DriverTripID)
            //2, Check Call (Data: DriverTripID)
            //3, Messages (Data: DriverTripID)
            //4, Profile (Data: DriverID)

            FireBasePush push = new FireBasePush("AAAAWXOtC1A:APA91bFBx6YIW5wEQ6RE0UhZWgN5cdWGdejSAD1GMZQW5ElqJC0equqb07UDAyokcUGW7Cg4v7hDPIlHmVSne6AR9hLe_aNO8ySHLZ4eza1KKyRZA3sfWijEPyh48WB5RYGGuqxPfm5E", "384192809808");
            //FireBasePush push = new FireBasePush("AAAA0oAysnA:APA91bEYWM5O2s9U6nV7cxR6_EKvZJxVdSCXa4_OVfLEjCXs9ojMqX0gLKzA8M6Pm_1krs--VbnZG2h7tm5mBJq_AzNw0K9qEvWZ1Id_29fg2riK24cQs7s7W8je9YaBnkLhROCvi-zr", "904093938288");


            PushMessage pm = new PushMessage();

            if(Notification.DeviceType.ToUpper() == "I")
            {
                pm = new PushMessage()
                {
                    to = Notification.Token,
                    notification = new
                    {
                        title = Notification.Title,
                        body = Notification.Message,
                        image = "https://firebase.google.com/images/social.png",
                        sound = "default",
                        click_action = ""
                    },
                    data = new
                    {
                        MessageType = Notification.MessageType,
                        MessageData = Notification.MessageData 
                    }
                };
            }
            if (Notification.DeviceType.ToUpper() == "A")
            {
               pm = new PushMessage()
                {
                    to = Notification.Token,
                    notification = new
                    {
                        title = Notification.Title,
                        body = Notification.Message,
                        image = "https://firebase.google.com/images/social.png",
                        sound = "default",
                        click_action = "",
                        show_in_foreground = "true",
                        MessageType = Notification.MessageType,
                        MessageData = Notification.MessageData
                    },
                    data = new
                    {
                        title = Notification.Title,
                        body = Notification.Message,
                        MessageType = Notification.MessageType,
                        MessageData = Notification.MessageData
                    },
                    priority ="high"
                };
            }


            Newtonsoft.Json.Linq.JObject res = push.SendPush(pm);

            NotificationResult result = res.ToObject<NotificationResult>();

            if (result.success == 1)
            {
                Notification.message_id = result.results[0].message_id;
                Notification.multicast_id = result.multicast_id.ToString();
                Notification.MessageResult = "Message Sent Successfully";

                Notification.SentMessageJson = Newtonsoft.Json.JsonConvert.SerializeObject(pm);
                Notification.ResultMessageJson = Newtonsoft.Json.JsonConvert.SerializeObject(result);

                DataMessages dc = new DataMessages();
                int id = dc.InsertPushNotification(Notification);
            }
            else
                Notification.MessageResult = "UnSuccessful Message";


            return Notification;
        }

        //http://localhost:5129/api/driver/NotificationViewed
        [HttpPut]
        public DriverNotification NotificationViewed(DriverNotification Notification)
        {

                DataMessages dc = new DataMessages();
                int id = dc.PushNotificationViewed(Notification);

                if(id == 0)
                {
                    Notification.MessageResult = "Unsuccessful to Update Notification";
                }
                if(id > 0)
                {
                    Notification.MessageResult = "Successfully Marked Notification Viewed ";
                }

            return Notification;
        }





        //http://localhost:5129/api/driver/TestEmail/{emailaddress}
        [HttpPut]
        public ResponseMessage TestEmail(string emailaddress)
        {
            HttpResponseMessage response;
            ResponseMessage smsg = new ResponseMessage();

            try
            {
                //Requires DriverID & FileName
                Communicate cc = new Communicate();

                string Body = "A request has been made to reset the password for your email address. If you did not make this request then you can ignore this email. If you did request to reset your password the following link." + Environment.NewLine + Environment.NewLine;
                smsg = cc.SendEmail(emailaddress, Body + "http://inmotion.vectortransport.com/inmotionreset.aspx?email=" + emailaddress + "&reset=test", "In Motion App - Password Reset Request");


                response = Request.CreateResponse(HttpStatusCode.OK);
            }
            catch
            {
                response = Request.CreateResponse(HttpStatusCode.BadRequest);
                smsg.Message = response.ToString();
            }


            return smsg;
        }

        //

        //Update with a Return
        //public IEnumerable<CheckCalls> UpdateCheckCall(int id, [FromBody]CheckCalls CheckCall)
        //{
        //    var checkcall = CheckCallList.FirstOrDefault((p) => p.ID == id);
        //    if (checkcall != null)
        //    {
        //        checkcall.CheckStatus = CheckCall.CheckStatus;
        //        checkcall.Comments = CheckCall.Comments;
        //    }

        //    return CheckCallList;
        //}


        ////http://localhost:5129/api/checkcall/UpdateCheckCallValue
        //[HttpPatch]
        //public void UpdateCheckCallValue(int id, [FromBody]string value)
        //{
        //    DataCheckCalls dc = new DataCheckCalls();
        //    dc.UpdateCheckCalls(id);
        //}


        //public HttpResponseMessage UploadDocument([FromBody] Documents document)
        //{
        //    HttpResponseMessage response;

        //    try
        //    {
        //        string fileloc = "Driver/" + document.DriverID.ToString();
        //        String path = System.Web.HttpContext.Current.Server.MapPath("~/Uploads/" + fileloc); //Path

        //        //Check if directory exist
        //        if (!System.IO.Directory.Exists(path))
        //        {
        //            System.IO.Directory.CreateDirectory(path); //Create directory if it doesn't exist
        //        }

        //        string filetype = ".jpg";
        //        switch (document.FileType)
        //        {
        //            case FileTypeOption.JPG:
        //                filetype = ".jpg";
        //                break;
        //            case FileTypeOption.PNG:
        //                filetype = ".png";
        //                break;
        //            case FileTypeOption.DOC:
        //            case FileTypeOption.DOCX:
        //                filetype = ".docx";
        //                break;
        //            case FileTypeOption.PDF:
        //                filetype = ".pdf";
        //                break;
        //            default:
        //                filetype = ".jpg";
        //                break;
        //        }

        //        DateTime dte = DateTime.Now;
        //        string imageName = document.DriverID.ToString() + "_" + dte.Year.ToString() + dte.Month.ToString() + dte.Day.ToString() + dte.Hour.ToString() + dte.Minute.ToString() + dte.Second.ToString() + filetype;

        //        //set the image path
        //        string imgPath = System.IO.Path.Combine(path, imageName);

        //        byte[] imageBytes = Convert.FromBase64String(document.FileBase64);

        //        System.IO.File.WriteAllBytes(imgPath, imageBytes);

        //        document.FileName = imageName;
        //        document.FileLocation = fileloc;

        //        DataDocuments dd = new DataDocuments();
        //        dd.InsertDocument(document);


        //        response = Request.CreateResponse(HttpStatusCode.OK);
        //    }
        //    catch
        //    {
        //        response = Request.CreateResponse(HttpStatusCode.BadRequest);
        //    }

        //    return response;
        //}
    }
}
