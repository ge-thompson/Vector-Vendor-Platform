using Swashbuckle.Swagger;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Description;
using System.Web.Http.Filters;

namespace OTR_API.Models
{
    public enum DocumentTypeOption : int { Driver = 1, CheckCall = 2, Other = 3 };
    public enum FileTypeOption : int { NA = 0, JPG = 1, PNG = 2, PDF = 3, DOC = 4, DOCX = 5, XLS = 6, XLSX = 7, RTF = 8, CSV = 9, TXT = 10, ODT = 11, ODS = 12, TIF = 13 };
    public class Documents
    {
        public int ID { get; set; }
        public DocumentTypeOption DocumentType { get; set; }
        public int DriverID { get; set; }
        public int CheckCallID { get; set; }
        public string Comments { get; set; }
        public FileTypeOption FileType { get; set; }
        public string FileBase64 { get; set; }
        public string FileName { get; set; }
        public string FileLocation { get; set; }
        public DateTime Created { get; set; }
        public string Message { get; set; }
        public string Link { get; set; }
        public int UploadID { get; set; }
        public bool Deleted { get; set; }

    }

    //public class CustomMultipartFileStreamProvider : MultipartMemoryStreamProvider
    //{
    //    public List<Documents> DocumentData { get; set; }

    //    public CustomMultipartFileStreamProvider()
    //    {
    //        DocumentData = new List<Documents>();
    //    }

    //    public override Task ExecutePostProcessingAsync()
    //    {
    //        foreach (var file in Contents)
    //        {
    //            var parameters = file.Headers.ContentDisposition.Parameters;
    //            //var data = new Documents
    //            //{
    //            //    DocumentType = (DocumentTypeOption)Enum.Parse(typeof(DocumentTypeOption), GetNameHeaderValue(parameters, "DocumentType"), true),
    //            //    DriverID = int.Parse(GetNameHeaderValue(parameters, "DriverID")),
    //            //    LoadID = int.Parse(GetNameHeaderValue(parameters, "LoadID")),
    //            //    Comments = GetNameHeaderValue(parameters, "Comments"),
    //            //};

    //            //DocumentData.Add(data);
    //        }

    //        return base.ExecutePostProcessingAsync();
    //    }

    //    private static string GetNameHeaderValue(ICollection<NameValueHeaderValue> headerValues, string name)
    //    {
    //        var nameValueHeader = headerValues.FirstOrDefault(
    //            x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    //        return nameValueHeader != null ? nameValueHeader.Value : null;
    //    }
    //}

    //public class ValidateMimeMultipartContentFilter : ActionFilterAttribute
    //{
    //    public override void OnActionExecuting(HttpActionContext actionContext)
    //    {
    //        if (!actionContext.Request.Content.IsMimeMultipartContent())
    //        {
    //            throw new HttpResponseException(HttpStatusCode.UnsupportedMediaType);
    //        }
    //    }

    //    public override void OnActionExecuted(HttpActionExecutedContext actionExecutedContext)
    //    {

    //    }

    //}
    public class FileOperationFilter : IOperationFilter
    {
        public void Apply(Operation operation, SchemaRegistry schemaRegistry, ApiDescription apiDescription)
        {
            if (operation.operationId.ToLower() == "driver_uploaddriverfile" || operation.operationId.ToLower() == "checkcall_uploadcheckcallfile")
            {
                if (operation.parameters == null)
                    operation.parameters = new List<Parameter>(1);
                else
                    operation.parameters.Clear();

                operation.parameters.Add(new Parameter
                {
                    name = "File",
                    @in = "formData",
                    description = "Upload Document",
                    required = true,
                    type = "file"
                });
                operation.consumes.Add("application/form-data");
            }
        }
    }

    public class ValidateMimeMultipartContentFilter : ActionFilterAttribute
    {
        public override void OnActionExecuting(HttpActionContext actionContext)
        {
            if (!actionContext.Request.Content.IsMimeMultipartContent())
            {
                throw new HttpResponseException(HttpStatusCode.UnsupportedMediaType);
            }
        }

        public override void OnActionExecuted(HttpActionExecutedContext actionExecutedContext)
        {

        }

    }
}