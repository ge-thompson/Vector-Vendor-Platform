using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace OTR_API.Models
{
    public class CompanyReps
    {
        public int ID { get; set; }

        public int RepID { get; set; }

        public string FullName { get; set; }

        public string EmailAddress { get; set; }

        public string Phone { get; set; }

    }
}