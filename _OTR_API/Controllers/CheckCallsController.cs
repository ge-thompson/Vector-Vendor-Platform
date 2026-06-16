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
    [RoutePrefix("api/checkcall")]

    public class CheckCallController : ApiController
    {
        //http://localhost:5129/api/checkcall/GetAllCheckCallsByDriverTrip/1
        [HttpGet]
        public IEnumerable<CheckCalls> GetAllCheckCallsByDriverTrip(int id)
        {
            DataCheckCalls dc = new DataCheckCalls();
            IEnumerable<CheckCalls> checkcallslist = dc.GetCheckCallsByDriverTripID(id);

            return checkcallslist;
        }

        //http://localhost:5129/api/checkcall/GetAllCheckCallsByTrip/1
        [HttpGet]
        public IEnumerable<CheckCalls> GetAllCheckCallsByTrip(int id)
        {
            DataCheckCalls dc = new DataCheckCalls();
            IEnumerable<CheckCalls> checkcallslist = dc.GetCheckCallsByTripID(id);

            return checkcallslist;
        }

        //http://localhost:5129/api/checkcall/GetAllCheckCallsByDriverTripDisplay/1
        [HttpGet]
        public IEnumerable<CheckCalls> GetAllCheckCallsByDriverTripDisplay(int id)
        {
            DataCheckCalls dc = new DataCheckCalls();
            IEnumerable<CheckCalls> checkcallslist = dc.GetCheckCallsByDriverTripDisplay(id);

            return checkcallslist;
        }

        //http://localhost:5129/api/checkcall/GetCheckCall/1
        [HttpGet]
        public CheckCalls GetCheckCall(int id)
        {
            CheckCalls checkcall = new CheckCalls();

            try
            {
                DataCheckCalls dc = new DataCheckCalls();
                checkcall = dc.GetCheckCallsByID(id);
            }
            catch
            {
                checkcall.Message = "Error Retrieving Check Call";
            }

            return checkcall;
        }

        //http://localhost:5129/api/checkcall/GetCheckCallTypes
        [HttpGet]
        public List<CheckCallTypes> GetCheckCallTypes()
        {
            DataCheckCalls dc = new DataCheckCalls();
            List<CheckCallTypes> checkcall = dc.GetCheckCallsTypes();

            return checkcall;
        }

        //http://localhost:5129/api/checkcall/GetCheckCallNextTypes/1
        [HttpGet]
        public List<CheckCallTypes> GetCheckCallNextTypes(int id)
        {
            DataCheckCalls dc = new DataCheckCalls();
            List<CheckCallTypes> checkcall = dc.GetCheckCallsNextTypes(id);

            return checkcall;
        }

        //http://localhost:5129/api/checkcall/SaveCheckCall
        [HttpPost]
        public CheckCalls SaveCheckCall([FromBody]CheckCalls CheckCall)
        {
            CheckCalls response = new CheckCalls();

            try
            {
                DataCheckCalls dc = new DataCheckCalls();
                int id = dc.InsertCheckCalls(CheckCall);

                response = dc.GetCheckCallsByID(id);

                try
                {
                    WebCallFunctions wc = new WebCallFunctions();
                    Task.Factory.StartNew(() => { wc.SaveCheckCalltoFBS(response); });
                }
                catch
                {

                }
            }
            catch(Exception ex)
            {
                response.Message = "Error Saving Check Call";
            }

            return response;
        }

        //http://localhost:5129/api/checkcall/SaveDriverTripLocation
        [HttpPost]
        public DriverTripLocation SaveDriverTripLocation([FromBody]DriverTripLocation TripLocation)
        {
            DriverTripLocation response = new DriverTripLocation();

            try
            {
                DataCheckCalls dc = new DataCheckCalls();
                int id = dc.InsertDriverTripLocation(TripLocation);
                response = dc.GetDriverTripLocationByID(id);

                response.Message = "Success";
            }
            catch(Exception ex)
            {
                response.Message = "Error Saving Driver Trip Location";
            }

            return response;
        }

        //http://localhost:5129/api/checkcall/UploadDocument
        [HttpPost]
        public HttpResponseMessage UploadDocument([FromBody] Documents document)
        {
            HttpResponseMessage response;

            try
            {

                DataCheckCalls dc = new DataCheckCalls();
                CheckCalls cc = dc.GetCheckCallsByID(document.CheckCallID);

                string fileloc = "DriverTrip/" + cc.DriverTripID.ToString();
                string path = System.Web.HttpContext.Current.Server.MapPath("~/Uploads/" + fileloc); //Path

                //Check if directory exist
                if (!System.IO.Directory.Exists(path))
                {
                    System.IO.Directory.CreateDirectory(path); //Create directory if it doesn't exist
                }

                string filetype = ".jpg";
                switch (document.FileType)
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
                string imageName = cc.DriverTripID.ToString() + "_" +document.CheckCallID.ToString() + "_" + dte.Year.ToString() + dte.Month.ToString() + dte.Day.ToString() + dte.Hour.ToString() + dte.Minute.ToString() + dte.Second.ToString() + filetype;

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

        //http://localhost:5129/api/checkcall/UploadDocumentPreLoaded
        [HttpPost]
        public Documents UploadDocumentPreLoaded([FromBody] Documents document)
        {
            DataDocuments dd = new DataDocuments();
            Documents doc = new Documents();
            try
            {
                DataCheckCalls dc = new DataCheckCalls();
                CheckCalls cc = dc.GetCheckCallsByID(document.CheckCallID);


                string fileloc = "DriverTrip/" + cc.DriverTripID.ToString();
                string path = System.Web.HttpContext.Current.Server.MapPath("~/Uploads/" + fileloc); 
                string temppath = System.Web.HttpContext.Current.Server.MapPath("~/Uploads/Temp/");

                //Check if directory exist
                if (!System.IO.Directory.Exists(path))
                {
                    System.IO.Directory.CreateDirectory(path); //Create directory if it doesn't exist
                }

                string filetype = dd.FileExtention(document.FileType);

                DateTime dte = DateTime.Now;
                string imageName = cc.DriverTripID.ToString() + "_" + dte.Year.ToString() + dte.Month.ToString() + dte.Day.ToString() + dte.Hour.ToString() + dte.Minute.ToString() + dte.Second.ToString() + filetype;
                string imgPath = System.IO.Path.Combine(path, imageName);

                Documents docU = dd.GetUpload(document.UploadID);

                string imgTempPath = System.IO.Path.Combine(temppath, docU.FileName);

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

        //http://localhost:5129/api/checkcall/UploadCheckCallFile
        [ValidateMimeMultipartContentFilter]
        [HttpPost]
        public Task<Documents> UploadCheckCallFile()
        {
            string path = System.Web.HttpContext.Current.Server.MapPath("~/Uploads/Temp/");
            string ip = System.Web.HttpContext.Current.Request.ServerVariables["REMOTE_ADDR"];

            var streamProvider = new MultipartFormDataStreamProvider(path);

            var task = Request.Content.ReadAsMultipartAsync(streamProvider).ContinueWith<Documents>(t =>
            {
                Documents doc = new Documents();
                var firstFile = streamProvider.FileData.FirstOrDefault();

                try
                {

                    if (firstFile != null)
                    {
                        var filename = firstFile.Headers.ContentDisposition.FileName;
                        int lastIndex = filename.Split('.').Count();
                        var ext = "." + filename.Split('.')[lastIndex - 1].Replace("\"", "");

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
                                    doc.DocumentType = DocumentTypeOption.CheckCall;
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

        //http://localhost:5129/api/checkcall/GetDocument
        [HttpPost]
        public Documents GetDocument([FromBody] Documents document)
        {
            //Requires CheckCallID & FileName

            DataCheckCalls dc = new DataCheckCalls();
            CheckCalls cc = dc.GetCheckCallsByID(document.CheckCallID);

            string fileloc = System.Web.HttpContext.Current.Server.MapPath("~/Uploads/DriverTrip/" + cc.DriverTripID.ToString() + @"/" + document.FileName);
            byte[] image = System.IO.File.ReadAllBytes(fileloc);
            document.FileBase64 = Convert.ToBase64String(image);

            return document;
        }

        //http://localhost:5129/api/checkcall/GetDocument
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

        //http://localhost:5129/api/checkcall/DeleteCheckCall
        [HttpDelete]
        public CheckCalls DeleteCheckCall([FromBody]CheckCalls checkCall)
        {
            try
            {
                DataCheckCalls dc = new DataCheckCalls();
                checkCall = dc.DeleteCheckCalls(checkCall);
            }
            catch
            {
                checkCall.Message = "Error";
            }

            return checkCall;
        }



        //REM OUT Update and Delete
        ////http://localhost:5129/api/checkcall/UpdateCheckCall
        //[HttpPut]
        //public void UpdateCheckCall(int id, [FromBody]CheckCalls CheckCall)
        //{
        //    DataCheckCalls dc = new DataCheckCalls();
        //    dc.UpdateCheckCalls(id, CheckCall);
        //}

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

        ////http://localhost:5129/api/checkcall/DeleteCheckCall
        //[HttpDelete]
        //public void DeleteCheckCall(int id)
        //{
        //    DataCheckCalls dc = new DataCheckCalls();
        //    dc.DeleteCheckCalls(id);
        //}
    }
}
