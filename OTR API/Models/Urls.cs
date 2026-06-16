using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace OTR_API.Models
{
    public class Urls
    {
        public int ID { get; set; }
        public String FullUrl { get; set; }
        public String TinyUrl { get; set; }
        //public String Keyword { get; set; }
        public DateTime UrlDate { get; set; }
    }

    public class UrlAudit
    {
        public int ID { get; set; }
        public String IP { get; set; }
        public DateTime VisitDate { get; set; }
    }

    public class InMotionUrl
    {
        public int ID { get; set; }
        public String Menu { get; set; }
        public String Url { get; set; }
        public int DriverID { get; set; }
    }
}