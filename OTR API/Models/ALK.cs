using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace OTR_API.Models
{
    public class Address
    {
        public string StreetAddress { get; set; }
        public string City { get; set; }
        public string State { get; set; }
        public string Zip { get; set; }
        public string County { get; set; }
        public string Country { get; set; }
        public object SPLC { get; set; }
        public int CountryPostalFilter { get; set; }
        public int AbbreviationFormat { get; set; }
        public string StateName { get; set; }
        public string StateAbbreviation { get; set; }
        public string CountryAbbreviation { get; set; }
    }

    public class Coords
    {
        public string Lat { get; set; }
        public string Lon { get; set; }
    }

    public class ALKLocation
    {
        public Address Address { get; set; }
        public Coords Coords { get; set; }
        public int Region { get; set; }
        public string Label { get; set; }
        public string PlaceName { get; set; }
        public string TimeZone { get; set; }
        public List<object> Errors { get; set; }
        public object SpeedLimitInfo { get; set; }
        public string ConfidenceLevel { get; set; }
        public double DistanceFromRoad { get; set; }
        public object CrossStreet { get; set; }
    }
}