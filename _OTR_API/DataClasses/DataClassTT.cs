using System;
using System.Text.RegularExpressions;
using System.Data;
using System.Data.SqlClient;
using GeoTimeZone;
using TimeZoneConverter;
using TimeZoneNames;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Configuration;
using System.Security.Cryptography;
//using OTR_API.TruckerTools.Models;
using System.Net.Mail;
using System.Net.Mime;
using System.Net;
using System.Net.Http;

namespace OTR_API.TruckerTools.DataClasses
{
    public class DataAccess
    {
        public enum Mask { None, DateOnly, PhoneWithArea, IpAddress, SSN, Decimal, Digit, Initials };

        protected SqlConnection cnn;

        protected SqlCommand cmd;

        protected void Connect()
        {
            string str = ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString;

            cnn = new SqlConnection(str);

            try
            {
                cnn.Open();
            }
            catch (Exception ec)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ec.Message, "DataAccess.TT.Connect");

            }
        }

        protected void Disconnect()
        {
            if (cnn.State != ConnectionState.Closed)
                cnn.Close();
        }

        internal static Boolean isBoolNull(object obj)
        {
            if (Convert.IsDBNull(obj))
                return false;
            else
                return Convert.ToBoolean(obj);
        }

        internal static Decimal isDecNull(object obj)
        {
            if (Convert.IsDBNull(obj))
                return Convert.ToDecimal("0.00");
            else
                return Convert.ToDecimal(obj);
        }

        internal static String isStringNull(object obj)
        {
            if (Convert.IsDBNull(obj))
                return "";
            else
                return Convert.ToString(obj);
        }

        internal static Int32 isIntNull(object obj)
        {
            if (Convert.IsDBNull(obj))
                return 0;
            else
                return Convert.ToInt32(obj);
        }

        internal static DateTime isDateNull(object obj)
        {
            if (Convert.IsDBNull(obj))
                return DateTime.Now;
            else
                return Convert.ToDateTime(obj);
        }

        internal static Byte isByteNull(object obj)
        {
            if (Convert.IsDBNull(obj))
                return new Byte();
            else
                return Convert.ToByte(obj);
        }

        internal static Byte[] isBytesNull(object obj)
        {
            if (Convert.IsDBNull(obj))
                return new Byte[] { };
            else
                return (Byte[])(obj);
        }

        internal String FormatText(string text, Mask m_mask)
        {
            string strText = ""; ;
            Regex regStr;
            switch (m_mask)
            {
                case Mask.DateOnly:
                    regStr = new Regex(@"\d{2}/\d{2}/\d{4}");
                    if (!regStr.IsMatch(text))
                        strText = "*";
                    else
                        strText = regStr.Match(text).ToString();
                    break;

                case Mask.PhoneWithArea:
                    regStr = new Regex(@"\d{3}-\d{3}-\d{4}");

                    if (!regStr.IsMatch(text))
                        strText = "*";
                    else
                        strText = regStr.Match(text).ToString();
                    break;

                case Mask.IpAddress:
                    short cnt = 0;
                    int len = text.Length;
                    for (short i = 0; i < len; i++)
                        if (text[i] == '.')
                        {
                            cnt++;
                            if (i + 1 < len)
                                if (text[i + 1] == '.')
                                {
                                    strText = "*";
                                    break;
                                }
                        }
                    if (cnt < 3 || text[len - 1] == '.')
                        strText = "*";
                    else
                        strText = text;
                    break;

                case Mask.SSN:
                    regStr = new Regex(@"\d{3}-\d{2}-\d{4}");
                    if (!regStr.IsMatch(text))
                        strText = "*";
                    else
                        strText = regStr.Match(text).ToString();
                    break;

                case Mask.Decimal:
                    break;

                case Mask.Digit:
                    break;

                case Mask.Initials:
                    strText = Initials(text);
                    break;
            }
            return strText;
        }

        internal string FormatPhoneNumber(string number)
        {
            if (number == null)
            {
                return "() -";
            }
            else
            {
                System.Text.RegularExpressions.Regex pattern = new System.Text.RegularExpressions.Regex(@"(?<areaCode>([\d]{3}))?[\s.-]?(?<leadingThree>([\d]{3}))[\s.-]?(?<lastFour>([\d]{4}))[x]?(?<extension>[\d]{1,})?");
                //("^\\(?([1-9]\\d{2})\\)?\\D*?([1-9]\\d{2})\\D*?(\\d{4})$");
                Match re = Regex.Match(number, pattern.ToString());
                return "(" + Convert.ToString(re.Groups["areaCode"].Value) + ") " + Convert.ToString(re.Groups["leadingThree"].Value) + "-" + Convert.ToString(re.Groups["lastFour"].Value) + " x " + Convert.ToString(re.Groups["extension"].Value);
            }
        }

        internal static bool IsNumeric(object ObjectToTest)
        {
            if (ObjectToTest == null)
            {
                return false;

            }
            else
            {
                double OutValue;
                return double.TryParse(ObjectToTest.ToString().Trim(),
                    System.Globalization.NumberStyles.Any,

                    System.Globalization.CultureInfo.CurrentCulture,

                    out OutValue);
            }
        }

        internal static String Initials(String strParam)
        {
            string ch = "";

            if (strParam != null)
            {
                if (strParam.Length == 0) { return ""; }

                string[] segments = strParam.Split(' ');

                if (segments.Length <= 1) { return Left(segments[0], 1); }


                for (int i = 0; i <= segments.Length - 1; i++)
                {
                    ch += Left(segments[i], 1);
                }
            }

            return ch;
        }

        internal static String Lower(String strParam)
        {
            return strParam.ToLower();
        }

        //Convert String to UpperCase
        internal static String Upper(String strParam)
        {
            return strParam.ToUpper();
        }

        //Convert String to ProperCase
        internal static String PCase(String strParam)
        {
            String strProper = strParam.Substring(0, 1).ToUpper();
            strParam = strParam.Substring(1).ToLower();
            String strPrev = "";

            for (int iIndex = 0; iIndex < strParam.Length; iIndex++)
            {
                if (iIndex > 1)
                {
                    strPrev = strParam.Substring(iIndex - 1, 1);
                }
                if (strPrev.Equals(" ") ||
                    strPrev.Equals("\t") ||
                    strPrev.Equals("\n") ||
                    strPrev.Equals("."))
                {
                    strProper += strParam.Substring(iIndex, 1).ToUpper();
                }
                else
                {
                    strProper += strParam.Substring(iIndex, 1);
                }
            }
            return strProper;
        }

        // Function to Reverse the String
        internal static String Reverse(String strParam)
        {
            if (strParam.Length == 1)
            {
                return strParam;
            }
            else
            {
                return Reverse(strParam.Substring(1)) + strParam.Substring(0, 1);
            }
        }

        // Function to Test for Palindrome
        internal static bool IsPalindrome(String strParam)
        {
            int iLength, iHalfLen;
            iLength = strParam.Length - 1;
            iHalfLen = iLength / 2;
            for (int iIndex = 0; iIndex <= iHalfLen; iIndex++)
            {
                if (strParam.Substring(iIndex, 1) != strParam.Substring(iLength - iIndex, 1))
                {
                    return false;
                }
            }
            return true;
        }

        // Function to get string from beginning.

        internal static String Left(String strParam, int iLen)
        {
            if (iLen > 0)
                return strParam.Substring(0, iLen);
            else
                return strParam;
        }

        //Function to get string from end
        internal static String Right(String strParam, int iLen)
        {
            if (iLen > 0)
                return strParam.Substring(strParam.Length - iLen, iLen);
            else
                return strParam;
        }

        //Function to count no.of occurences of Substring in Main string
        internal static int CharCount(String strSource, String strToCount)
        {
            int iCount = 0;
            int iPos = strSource.IndexOf(strToCount);
            while (iPos != -1)
            {
                iCount++;
                strSource = strSource.Substring(iPos + 1);
                iPos = strSource.IndexOf(strToCount);
            }
            return iCount;
        }

        //Not available in C#
        //Function to count no.of occurences of Substring in Main string
        internal static int CharCount(String strSource, String strToCount, bool IgnoreCase)
        {
            if (IgnoreCase)
            {
                return CharCount(strSource.ToLower(), strToCount.ToLower());
            }
            else
            {
                return CharCount(strSource, strToCount);
            }
        }

        //Useful Function can be used whitespace stripping programs
        //Function Trim the string to contain Single between words
        internal static String ToSingleSpace(String strParam)
        {
            int iPosition = strParam.IndexOf("  ");
            if (iPosition == -1)
            {
                return strParam;
            }
            else
            {
                return ToSingleSpace(strParam.Substring(0, iPosition) + strParam.Substring(iPosition + 1));
            }
        }

        //Function Replace string function.

        // Currently Not Available in C#
        internal static String Replace(String strText, String strFind, String strReplace)
        {
            int iPos = strText.IndexOf(strFind);
            String strReturn = "";
            while (iPos != -1)
            {
                strReturn += strText.Substring(0, iPos) + strReplace;
                strText = strText.Substring(iPos + strFind.Length);
                iPos = strText.IndexOf(strFind);
            }
            if (strText.Length > 0)
                strReturn += strText;
            return strReturn;
        }

        public static string ToTitleCase(string inputString)
        {

            System.Globalization.CultureInfo cultureInfo = System.Threading.Thread.CurrentThread.CurrentCulture;

            System.Globalization.TextInfo textInfo = cultureInfo.TextInfo;

            string str = textInfo.ToTitleCase(inputString.ToLower());

            /* post fixes */
            /* Recall that "Replace()" is case sensitive */
            str = str.Replace(" Of ", " of ");
            str = str.Replace(" And ", " and ");
            str = str.Replace("'S ", "'s ");
            str = str.Replace(" At ", " at ");
            str = str.Replace(" The ", " the ");
            str = str.Replace(" For ", " for ");
            str = str.Replace(" De ", " de ");
            str = str.Replace(" Y ", " y ");
            str = str.Replace(" In ", " in ");

            /* roman numerals */
            str = str.Replace(" Iii", " III");
            str = str.Replace(" Ii", " II");

            /* specific cases of acronyms */
            str = str.Replace("Abc ", "ABC ");
            str = str.Replace("Abcd", "ABCD ");
            str = str.Replace("Aaa ", "AAA ");
            str = str.Replace("Cbe ", "CBE ");
            str = str.Replace("Cei ", "CEI ");
            str = str.Replace("Itt ", "ITT ");
            str = str.Replace("Mbti ", "MBTI ");
            str = str.Replace("Cuny ", "CUNY ");
            str = str.Replace("Suny ", "SUNY ");
            str = str.Replace("Mta ", "MTA ");
            str = str.Replace("Mti ", "MTI ");
            str = str.Replace("Qpe ", "QPE ");
            str = str.Replace(" Ogc ", " OGC ");
            str = str.Replace("Tci ", "TCI ");
            str = str.Replace("The Cdl ", "The CDL ");
            str = str.Replace("The Mbf ", "The MBF");
            str = str.Replace("Lpn", "LPN");
            str = str.Replace("Cvph ", "CVPH ");
            str = str.Replace("Dch ", "DCH ");
            str = str.Replace("Bmr ", "BMR ");
            str = str.Replace("Isim ", "ISIM ");

            /* contractions */
            str = str.Replace(" Mgt", " Management");
            str = str.Replace("Trng", "Training");
            str = str.Replace("Xray", "X-Ray");
            str = str.Replace(" Sch ", " School ");
            str = str.Replace(" Dba ", " dba ");

            /* specific names with special case */
            str = str.Replace("Mcc", "McC");
            str = str.Replace("Mcd", "McD");
            str = str.Replace("Mch", "McH");
            str = str.Replace("Mcg", "McG");
            str = str.Replace("Mci", "McI");
            str = str.Replace("Mck", "McK");
            str = str.Replace("Mcl", "McL");
            str = str.Replace("Mcm", "McM");
            str = str.Replace("Mcn", "McN");
            str = str.Replace("Mcp", "McP");

            /* adding punctuation */
            str = str.Replace(" Inc", ", Inc");
            str = str.Replace("Ft ", "Ft. ");
            str = str.Replace("St ", "St. ");
            str = str.Replace("Mt ", "Mt. ");

            /* U.S. state abbreviations */
            str = str.Replace(" Ak ", " AK ");
            str = str.Replace(" As ", " AS ");
            str = str.Replace(" Az ", " AZ ");
            str = str.Replace(" Ar ", " AR ");
            str = str.Replace(" Ca ", " CA ");
            str = str.Replace(" Co ", " CO ");
            str = str.Replace(" Ct ", " CT ");
            str = str.Replace(" De ", " DE ");
            str = str.Replace(" Dc ", " DC ");
            str = str.Replace(" Fl ", " FL ");
            str = str.Replace(" Ga ", " GA ");
            str = str.Replace(" Gu ", " GU ");
            str = str.Replace(" Hi ", " HI ");
            str = str.Replace(" Id ", " ID ");
            str = str.Replace(" Il ", " IL ");
            str = str.Replace(" In ", " IN ");
            str = str.Replace(" Ia ", " IA ");
            str = str.Replace(" Ks ", " KS ");
            str = str.Replace(" Ky ", " KY ");
            str = str.Replace(" La ", " LA ");
            str = str.Replace(" Me ", " ME ");
            str = str.Replace(" Md ", " MD ");
            str = str.Replace(" Mh ", " MH ");
            str = str.Replace(" Ma ", " MA ");
            str = str.Replace(" Mi ", " MI ");
            str = str.Replace(" Fm ", " FM ");
            str = str.Replace(" Mn ", " MN ");
            str = str.Replace(" Ms ", " MS ");
            str = str.Replace(" Mo ", " MO ");
            str = str.Replace(" Mt ", " MT ");
            str = str.Replace(" Ne ", " NE ");
            str = str.Replace(" Nv ", " NV ");
            str = str.Replace(" Nh ", " NH ");
            str = str.Replace(" Nj ", " NJ ");
            str = str.Replace(" Nm ", " NM ");
            str = str.Replace(" Ny ", " NY ");
            str = str.Replace(" Nc ", " NC ");
            str = str.Replace(" Nd ", " ND ");
            str = str.Replace(" Mp ", " MP ");
            str = str.Replace(" Oh ", " OH ");
            str = str.Replace(" Ok ", " OK ");
            str = str.Replace(" Or ", " OR ");
            str = str.Replace(" Pw ", " PW ");
            str = str.Replace(" Pa ", " PA ");
            str = str.Replace(" Pr ", " PR ");
            str = str.Replace(" Ri ", " RI ");
            str = str.Replace(" Sc ", " SC ");
            str = str.Replace(" Sd ", " SD ");
            str = str.Replace(" Tn ", " TN ");
            str = str.Replace(" Tx ", " TX ");
            str = str.Replace(" Ut ", " UT ");
            str = str.Replace(" Vt ", " VT ");
            str = str.Replace(" Va ", " VA ");
            str = str.Replace(" Vi ", " VI ");
            str = str.Replace(" Wa ", " WA ");
            str = str.Replace(" Wv ", " WV ");
            str = str.Replace(" Wi ", " WI ");
            str = str.Replace(" Wy ", " WY ");

            return str;


        }
    }

    public static class PasswordGenerator
    {
        /// <summary>
        /// Generates a random password based on the rules passed in the parameters
        /// </summary>
        /// <param name="includeLowercase">Bool to say if lowercase are required</param>
        /// <param name="includeUppercase">Bool to say if uppercase are required</param>
        /// <param name="includeNumeric">Bool to say if numerics are required</param>
        /// <param name="includeSpecial">Bool to say if special characters are required</param>
        /// <param name="includeSpaces">Bool to say if spaces are required</param>
        /// <param name="lengthOfPassword">Length of password required. Should be between 8 and 128</param>
        /// <returns></returns>
        public static string GeneratePassword(bool includeLowercase, bool includeUppercase, bool includeNumeric, bool includeSpecial, bool includeSpaces, int lengthOfPassword)
        {
            const int MAXIMUM_IDENTICAL_CONSECUTIVE_CHARS = 2;
            const string LOWERCASE_CHARACTERS = "abcdefghijklmnopqrstuvwxyz";
            const string UPPERCASE_CHARACTERS = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            const string NUMERIC_CHARACTERS = "0123456789";
            const string SPECIAL_CHARACTERS = @"!#$%&*@\";
            const string SPACE_CHARACTER = " ";
            const int PASSWORD_LENGTH_MIN = 8;
            const int PASSWORD_LENGTH_MAX = 128;

            if (lengthOfPassword < PASSWORD_LENGTH_MIN || lengthOfPassword > PASSWORD_LENGTH_MAX)
            {
                return "Password length must be between 8 and 128.";
            }

            string characterSet = "";

            if (includeLowercase)
            {
                characterSet += LOWERCASE_CHARACTERS;
            }

            if (includeUppercase)
            {
                characterSet += UPPERCASE_CHARACTERS;
            }

            if (includeNumeric)
            {
                characterSet += NUMERIC_CHARACTERS;
            }

            if (includeSpecial)
            {
                characterSet += SPECIAL_CHARACTERS;
            }

            if (includeSpaces)
            {
                characterSet += SPACE_CHARACTER;
            }

            char[] password = new char[lengthOfPassword];
            int characterSetLength = characterSet.Length;

            System.Random random = new System.Random();
            for (int characterPosition = 0; characterPosition < lengthOfPassword; characterPosition++)
            {
                password[characterPosition] = characterSet[random.Next(characterSetLength - 1)];

                bool moreThanTwoIdenticalInARow =
                    characterPosition > MAXIMUM_IDENTICAL_CONSECUTIVE_CHARS
                    && password[characterPosition] == password[characterPosition - 1]
                    && password[characterPosition - 1] == password[characterPosition - 2];

                if (moreThanTwoIdenticalInARow)
                {
                    characterPosition--;
                }
            }

            return string.Join(null, password);
        }

        /// <summary>
        /// Checks if the password created is valid
        /// </summary>
        /// <param name="includeLowercase">Bool to say if lowercase are required</param>
        /// <param name="includeUppercase">Bool to say if uppercase are required</param>
        /// <param name="includeNumeric">Bool to say if numerics are required</param>
        /// <param name="includeSpecial">Bool to say if special characters are required</param>
        /// <param name="includeSpaces">Bool to say if spaces are required</param>
        /// <param name="password">Generated password</param>
        /// <returns>True or False to say if the password is valid or not</returns>
        public static bool PasswordIsValid(bool includeLowercase, bool includeUppercase, bool includeNumeric, bool includeSpecial, bool includeSpaces, string password)
        {
            const string REGEX_LOWERCASE = @"[a-z]";
            const string REGEX_UPPERCASE = @"[A-Z]";
            const string REGEX_NUMERIC = @"[\d]";
            const string REGEX_SPECIAL = @"([!#$%&*@\\])+";
            const string REGEX_SPACE = @"([ ])+";

            bool lowerCaseIsValid = !includeLowercase || (includeLowercase && Regex.IsMatch(password, REGEX_LOWERCASE));
            bool upperCaseIsValid = !includeUppercase || (includeUppercase && Regex.IsMatch(password, REGEX_UPPERCASE));
            bool numericIsValid = !includeNumeric || (includeNumeric && Regex.IsMatch(password, REGEX_NUMERIC));
            bool symbolsAreValid = !includeSpecial || (includeSpecial && Regex.IsMatch(password, REGEX_SPECIAL));
            bool spacesAreValid = !includeSpaces || (includeSpaces && Regex.IsMatch(password, REGEX_SPACE));

            return lowerCaseIsValid && upperCaseIsValid && numericIsValid && symbolsAreValid && spacesAreValid;
        }
    }

    public class DataLoadMatch : DataAccess
    {
        public OTR_API.TruckerTools.Models.Load GetLoadByID(int ID, string type)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "";
            string paraName = "";
            switch (type.ToUpper())
            {
                case "ID":
                    strsql = "spLoadByID_Get";
                    paraName = "@ID";
                    break;

                case "VECTORID":
                default:

                    strsql = "spLoadByVectorID_Get";
                    paraName = "@VectorID";
                    break;
            }

            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue(paraName, ID);

            OTR_API.TruckerTools.Models.Load obj = new OTR_API.TruckerTools.Models.Load();

            try
            {
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {

                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["status"] != DBNull.Value) { obj.status = (string)reader["status"]; }
                        if (reader["equipmentType"] != DBNull.Value) { obj.equipmentType = (string)reader["equipmentType"]; }
                        if (reader["loadNumber"] != DBNull.Value) { obj.loadNumber = (string)reader["loadNumber"]; }
                        if (reader["externalId"] != DBNull.Value) { obj.externalId = (string)reader["externalId"]; }

                        obj.pickup = new OTR_API.TruckerTools.Models.Pickup();
                        obj.delivery = new OTR_API.TruckerTools.Models.Delivery();
                        obj.additionalStops = new List<OTR_API.TruckerTools.Models.AdditionalStop>();

                        if (reader["loadType"] != DBNull.Value) { obj.loadType = (string)reader["loadType"]; }

                        obj.revenue = new OTR_API.TruckerTools.Models.Revenue();
                        obj.carrierPay = new OTR_API.TruckerTools.Models.CarrierPay();
                        obj.targetPay = new OTR_API.TruckerTools.Models.TargetPay();

                        obj.loadContact = new OTR_API.TruckerTools.Models.LoadContact();
                        obj.operationUser = new OTR_API.TruckerTools.Models.OperationUser();
                        obj.salesPerson = new OTR_API.TruckerTools.Models.SalesPerson();
                        obj.broker = new OTR_API.TruckerTools.Models.Broker();
                        obj.carrier = new OTR_API.TruckerTools.Models.LoadCarrier();
                        obj.driver = new OTR_API.TruckerTools.Models.Driver();
                        obj.dispatcher = new OTR_API.TruckerTools.Models.Dispatcher();
                        obj.shipper = new OTR_API.TruckerTools.Models.Shipper();

                        if (reader["trucksCount"] != DBNull.Value) { obj.trucksCount = (int)reader["trucksCount"]; }
                        if (reader["length"] != DBNull.Value) { obj.length = (string)reader["length"]; }
                        if (reader["weight"] != DBNull.Value) { obj.weight = (string)reader["weight"]; }
                        if (reader["quantity"] != DBNull.Value) { obj.quantity = (string)reader["quantity"]; }
                        if (reader["rate"] != DBNull.Value) { obj.rate = (string)reader["rate"]; }
                        if (reader["billToId"] != DBNull.Value) { obj.billToId = (string)reader["billToId"]; }
                        if (reader["orderType"] != DBNull.Value) { obj.orderType = (string)reader["orderType"]; }
                        if (reader["temperatureMinimum"] != DBNull.Value) { obj.temperatureMinimum = (string)reader["temperatureMinimum"]; }
                        if (reader["temperatureMaximum"] != DBNull.Value) { obj.temperatureMaximum = (string)reader["temperatureMaximum"]; }
                        if (reader["commodityId"] != DBNull.Value) { obj.commodityId = (string)reader["commodityId"]; }
                        if (reader["hazmat"] != DBNull.Value) { obj.hazmat = (bool)reader["hazmat"]; }
                        if (reader["highValue"] != DBNull.Value) { obj.highValue = (bool)reader["highValue"]; }
                        if (reader["teamsRequired"] != DBNull.Value) { obj.teamsRequired = (bool)reader["teamsRequired"]; }
                        if (reader["comments"] != DBNull.Value) { obj.comments = (string)reader["comments"]; }
                        if (reader["numberOfAdditionalStops"] != DBNull.Value) { obj.numberOfAdditionalStops = (int)reader["numberOfAdditionalStops"]; }
                        if (reader["shipperLoadNumber"] != DBNull.Value) { obj.shipperLoadNumber = (string)reader["shipperLoadNumber"]; }
                        if (reader["tractorNumber"] != DBNull.Value) { obj.tractorNumber = (string)reader["tractorNumber"]; }
                        if (reader["trailerNumber"] != DBNull.Value) { obj.trailerNumber = (string)reader["trailerNumber"]; }
                        if (reader["publishToCarrier"] != DBNull.Value) { obj.publishToCarrier = (bool)reader["publishToCarrier"]; }
                        if (reader["bookItNowPrice"] != DBNull.Value) { obj.bookItNowPrice = (string)reader["bookItNowPrice"]; }
                        if (reader["totalMiles"] != DBNull.Value) { obj.totalMiles = (decimal)reader["totalMiles"]; }
                        if (reader["ratePerMile"] != DBNull.Value) { obj.ratePerMile = (decimal)reader["ratePerMile"]; }
                        if (reader["ratePerMileFuel"] != DBNull.Value) { obj.ratePerMileFuel = (decimal)reader["ratePerMileFuel"]; }
                        if (reader["triggerTracking"] != DBNull.Value) { obj.triggerTracking = (bool)reader["triggerTracking"]; }

                        if (reader["VectorID"] != DBNull.Value) { obj.VectorID = (int)reader["VectorID"]; }
                        if (reader["VectorCarrierID"] != DBNull.Value) { obj.VectorCarrierID = (int)reader["VectorCarrierID"]; }

                        obj.extras = new List<OTR_API.TruckerTools.Models.Extra>();

                        List<OTR_API.TruckerTools.Models.Extra> extras = GetExtras(obj.ID, obj.ID, OTR_API.TruckerTools.Models.Stop.StopType.None);
                        if (extras.Count > 0)
                            obj.extras = extras;

                    }
                }
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.GetLoad");
            }



            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            OTR_API.TruckerTools.Models.LoadStops loadstops = GetLoadStops(obj.ID);

            obj.pickup = loadstops.pickUp;
            obj.delivery = loadstops.delivery;

            if (loadstops.additionalstops.Count > 0)
                obj.additionalStops = loadstops.additionalstops;



            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            OTR_API.TruckerTools.Models.LoadContacts loadcontacts = GetLoadContacts(obj.ID);

            obj.loadContact = loadcontacts.loadcontact;
            obj.salesPerson = loadcontacts.salesperson;
            obj.operationUser = loadcontacts.operationuser;
            obj.driver = loadcontacts.driver;
            obj.dispatcher = loadcontacts.dispatcher;
            obj.shipper = loadcontacts.shipper;
            obj.broker = loadcontacts.broker;



            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            OTR_API.TruckerTools.Models.LoadPays loadpays = GetLoadPay(obj.ID);

            obj.revenue = loadpays.revenue;
            obj.targetPay = loadpays.targetpay;
            obj.carrierPay = loadpays.carrierpay;


            if (obj.VectorCarrierID > 0)
            {
                if (cn.State != ConnectionState.Closed) { cn.Close(); }

                DataCarrierMatch dc = new DataCarrierMatch();
                OTR_API.TruckerTools.Models.Carrier car = dc.GetCarrierByVectorID(obj.ID);

                obj.carrier.companyName = car.carrier_name;
                obj.carrier.mc = car.mc;
                obj.carrier.contactPhone = car.contact_phone;
                obj.carrier.contactEmail = car.contact_email;
                obj.carrier.dotNumber = car.dot;
                obj.carrier.scac = car.scac;
                obj.carrier.numberOfTrucks = car.NumberofTrucks;
            }


            return obj;

        }

        public OTR_API.TruckerTools.Models.LoadStops GetLoadStops(int LoadID)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "spStopByLoadID_Get";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@LoadID", LoadID);

            OTR_API.TruckerTools.Models.LoadStops obj = new OTR_API.TruckerTools.Models.LoadStops();
            obj.pickUp = new OTR_API.TruckerTools.Models.Pickup();
            obj.delivery = new OTR_API.TruckerTools.Models.Delivery();
            obj.additionalstops = new List<OTR_API.TruckerTools.Models.AdditionalStop>();

            List<OTR_API.TruckerTools.Models.Stop> Stops = new List<OTR_API.TruckerTools.Models.Stop>();
            try
            {
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {
                        OTR_API.TruckerTools.Models.Stop stp = new OTR_API.TruckerTools.Models.Stop();

                        if (reader["ID"] != DBNull.Value) { stp.ID = (int)reader["ID"]; }
                        if (reader["LoadID"] != DBNull.Value) { stp.LoadID = (int)reader["LoadID"]; }
                        if (reader["StopType"] != DBNull.Value) { stp.Type = (OTR_API.TruckerTools.Models.Stop.StopType)(int)reader["StopType"]; }
                        if (reader["address"] != DBNull.Value) { stp.Address = (string)reader["address"]; }
                        if (reader["city"] != DBNull.Value) { stp.City = (string)reader["city"]; }
                        if (reader["state"] != DBNull.Value) { stp.State = (string)reader["state"]; }
                        if (reader["postalCode"] != DBNull.Value) { stp.PostalCode = (string)reader["postalCode"]; }
                        if (reader["latitude"] != DBNull.Value) { stp.Latitude = (string)reader["latitude"]; }
                        if (reader["longitude"] != DBNull.Value) { stp.Longitude = (string)reader["longitude"]; }
                        if (reader["timeZone"] != DBNull.Value) { stp.TimeZone = (string)reader["timeZone"]; }
                        if (reader["sequence"] != DBNull.Value) { stp.Sequence = (int)reader["sequence"]; }

                        if (reader["stopExternalId"] != DBNull.Value) { stp.StopExternalID = (string)reader["stopExternalId"]; }
                        if (reader["scheduledAtEarlyDateTime"] != DBNull.Value) { stp.ScheduledAtEarlyDateTime = (string)reader["scheduledAtEarlyDateTime"]; }
                        if (reader["scheduledAtLateDateTime"] != DBNull.Value) { stp.ScheduledAtLateDateTime = (string)reader["scheduledAtLateDateTime"]; }
                        if (reader["appointmentRequired"] != DBNull.Value) { stp.AppointmentRequired = (bool)reader["appointmentRequired"]; }
                        if (reader["appointmentConfirmed"] != DBNull.Value) { stp.AppointmentConfirmed = (bool)reader["appointmentConfirmed"]; }
                        if (reader["VectorID"] != DBNull.Value) { stp.VectorID = (int)reader["VectorID"]; }

                        Stops.Add(stp);
                    }

                }

                if (cn.State != ConnectionState.Closed) { cn.Close(); }

                foreach (OTR_API.TruckerTools.Models.Stop stop in Stops)
                {
                    switch (stop.Type)
                    {
                        case OTR_API.TruckerTools.Models.Stop.StopType.Pickup:
                            OTR_API.TruckerTools.Models.Pickup pick = new OTR_API.TruckerTools.Models.Pickup()
                            {
                                ID = stop.ID, LoadID = stop.LoadID, Type = stop.Type,
                                Address = stop.Address,City = stop.City,State = stop.State,PostalCode = stop.PostalCode,Latitude = stop.Latitude,Longitude = stop.Longitude,
                                TimeZone = stop.TimeZone, Sequence = stop.Sequence,StopExternalID = stop.StopExternalID,ScheduledAtEarlyDateTime = stop.ScheduledAtEarlyDateTime,
                                ScheduledAtLateDateTime = stop.ScheduledAtLateDateTime,AppointmentRequired = stop.AppointmentRequired, AppointmentConfirmed = stop.AppointmentConfirmed,
                                VectorID = stop.VectorID
                            };


                            List<OTR_API.TruckerTools.Models.Extra> pickextras = GetExtras(pick.ID, pick.LoadID, OTR_API.TruckerTools.Models.Stop.StopType.Pickup);
                            if (pickextras.Count > 0)
                                pick.Extras = pickextras;

                            obj.pickUp = pick;

                            break;

                        case OTR_API.TruckerTools.Models.Stop.StopType.Delivery:
                            OTR_API.TruckerTools.Models.Delivery del = new OTR_API.TruckerTools.Models.Delivery()
                            {
                                ID = stop.ID,LoadID = stop.LoadID,Type = stop.Type,
                                Address = stop.Address,City = stop.City,State = stop.State,PostalCode = stop.PostalCode,Latitude = stop.Latitude,Longitude = stop.Longitude,
                                TimeZone = stop.TimeZone,Sequence = stop.Sequence,StopExternalID = stop.StopExternalID,ScheduledAtEarlyDateTime = stop.ScheduledAtEarlyDateTime,
                                ScheduledAtLateDateTime = stop.ScheduledAtLateDateTime,AppointmentRequired = stop.AppointmentRequired, AppointmentConfirmed = stop.AppointmentConfirmed,
                                VectorID = stop.VectorID
                            };

                            List<OTR_API.TruckerTools.Models.Extra> delextras = GetExtras(del.ID, del.LoadID, OTR_API.TruckerTools.Models.Stop.StopType.Delivery);
                            if (delextras.Count > 0)
                                del.Extras = delextras;

                            obj.delivery = del;

                            break;

                        case OTR_API.TruckerTools.Models.Stop.StopType.AdditionalStops:
                        default:

                            OTR_API.TruckerTools.Models.AdditionalStop ads = new OTR_API.TruckerTools.Models.AdditionalStop()
                            {   ID = stop.ID,LoadID = stop.LoadID,Type = stop.Type,
                                Address = stop.Address,City = stop.City,State = stop.State,PostalCode = stop.PostalCode,Latitude = stop.Latitude,Longitude = stop.Longitude,
                                TimeZone = stop.TimeZone,Sequence = stop.Sequence,StopExternalID = stop.StopExternalID,ScheduledAtEarlyDateTime = stop.ScheduledAtEarlyDateTime,
                                ScheduledAtLateDateTime = stop.ScheduledAtLateDateTime,AppointmentRequired = stop.AppointmentRequired,AppointmentConfirmed = stop.AppointmentConfirmed,
                                VectorID = stop.VectorID
                            };


                            List<OTR_API.TruckerTools.Models.Extra> extras = GetExtras(ads.ID, ads.LoadID, OTR_API.TruckerTools.Models.Stop.StopType.AdditionalStops);
                            if (extras.Count > 0)
                                ads.Extras = extras;


                            obj.additionalstops.Add(ads);


                            break;

                    }
                }
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.GetStops");
            }


            return obj;
        }

        public OTR_API.TruckerTools.Models.LoadContacts GetLoadContacts(int LoadID)
        {
            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "spContactByLoadID_Get";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@LoadID", LoadID);

            OTR_API.TruckerTools.Models.LoadContacts obj = new OTR_API.TruckerTools.Models.LoadContacts();
            obj.loadcontact = new OTR_API.TruckerTools.Models.LoadContact();
            obj.salesperson = new OTR_API.TruckerTools.Models.SalesPerson();
            obj.operationuser = new OTR_API.TruckerTools.Models.OperationUser();
            obj.driver = new OTR_API.TruckerTools.Models.Driver();
            obj.dispatcher = new OTR_API.TruckerTools.Models.Dispatcher();
            obj.shipper = new OTR_API.TruckerTools.Models.Shipper();
            obj.broker = new OTR_API.TruckerTools.Models.Broker();


            List<OTR_API.TruckerTools.Models.Contact> contacts = new List<OTR_API.TruckerTools.Models.Contact>();
            try
            {
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {
                        OTR_API.TruckerTools.Models.Contact stp = new OTR_API.TruckerTools.Models.Contact();

                        if (reader["ID"] != DBNull.Value) { stp.ID = (int)reader["ID"]; }
                        if (reader["LoadID"] != DBNull.Value) { stp.LoadID = (int)reader["LoadID"]; }
                        if (reader["Type"] != DBNull.Value) { stp.Type = (OTR_API.TruckerTools.Models.Contact.ContactType)(int)reader["Type"]; }
                        if (reader["name"] != DBNull.Value) { stp.Name = (string)reader["name"]; }
                        if (reader["phone"] != DBNull.Value) { stp.Phone = (string)reader["phone"]; }
                        if (reader["extension"] != DBNull.Value) { stp.Extension = (string)reader["extension"]; }
                        if (reader["email"] != DBNull.Value) { stp.Email = (string)reader["email"]; }
                        if (reader["team"] != DBNull.Value) { stp.Team = (string)reader["team"]; }
                        if (reader["memberid"] != DBNull.Value) { stp.MemberID = (string)reader["memberid"]; }
                        if (reader["deviceID"] != DBNull.Value) { stp.DeviceID = (string)reader["deviceID"]; }
                        if (reader["mc"] != DBNull.Value) { stp.mc = (string)reader["mc"]; }
                        if (reader["dot"] != DBNull.Value) { stp.dot = (string)reader["dot"]; }
                        if (reader["scac"] != DBNull.Value) { stp.scac = (string)reader["scac"]; }
                        if (reader["NumberofTrucks"] != DBNull.Value) { stp.NumberofTrucks = (string)reader["NumberofTrucks"]; }
                        if (reader["VectorID"] != DBNull.Value) { stp.VectorID = (int)reader["VectorID"]; }

                        contacts.Add(stp);
                    }

                }

                if (cn.State != ConnectionState.Closed) { cn.Close(); }


                // Load=1, Operation=2, Sales=3, Broker=4, Driver=5, Dispatcher=6, Shipper=7
                foreach (OTR_API.TruckerTools.Models.Contact contact in contacts)
                {
                    switch (contact.Type)
                    {
                        case OTR_API.TruckerTools.Models.Contact.ContactType.Shipper:

                            obj.shipper.CompanyName = contact.Name;
                            obj.shipper.ContactPhone = contact.Phone;
                            obj.shipper.ContactEmail = contact.Email;

                            break;

                        case OTR_API.TruckerTools.Models.Contact.ContactType.Operation:

                            obj.operationuser.ID = contact.MemberID;
                            obj.operationuser.Team = contact.Team;
                            obj.operationuser.Name = contact.Name;
                            obj.operationuser.ContactPhone = contact.Phone;
                            obj.operationuser.ContactEmail = contact.Email;
                            obj.operationuser.PhoneExtension = contact.Phone;

                            break;

                        case OTR_API.TruckerTools.Models.Contact.ContactType.Sales:

                            obj.salesperson.Name = contact.Name;
                            obj.salesperson.ContactPhone = contact.Phone;
                            obj.salesperson.PhoneExtension = contact.Extension;
                            obj.salesperson.ContactEmail = contact.Email;

                            break;

                        case OTR_API.TruckerTools.Models.Contact.ContactType.Broker:
                            obj.broker.CompanyName = contact.Name;
                            obj.broker.ContactPhone = contact.Phone;
                            obj.broker.ContactEmail = contact.Email;
                            obj.broker.mc = contact.mc;
                            obj.broker.dot = contact.dot;

                            break;

                        case OTR_API.TruckerTools.Models.Contact.ContactType.Driver:
                            obj.driver.Name = contact.Name;
                            obj.driver.Phone = contact.Phone;
                            obj.driver.DeviceID = contact.DeviceID;

                            break;

                        case OTR_API.TruckerTools.Models.Contact.ContactType.Dispatcher:
                            obj.dispatcher.ID = contact.MemberID;
                            obj.dispatcher.Name = contact.Name;
                            obj.dispatcher.ContactPhone = contact.Phone;
                            obj.dispatcher.ContactEmail = contact.Email;

                            break;

                        case OTR_API.TruckerTools.Models.Contact.ContactType.Load:


                            obj.loadcontact.Name = contact.Name;
                            obj.loadcontact.ContactPhone = contact.Phone;
                            obj.loadcontact.PhoneExtension = contact.Extension;
                            obj.loadcontact.ContactEmail = contact.Email;

                            break;


                        default:
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.GetStops");
            }

            return obj;
        }

        public List<OTR_API.TruckerTools.Models.Extra> GetExtras(int AssociatedID, int LoadID, OTR_API.TruckerTools.Models.Stop.StopType stopType)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "spExtraByLoadIDType_Get";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@AssociatedID", AssociatedID);
            cd.Parameters.AddWithValue("@LoadID", LoadID);
            cd.Parameters.AddWithValue("@Type", (int)stopType);

            List<OTR_API.TruckerTools.Models.Extra> extras = new List<OTR_API.TruckerTools.Models.Extra>();

            try
            {
                using (SqlDataReader reader = cd.ExecuteReader())
                {


                    while (reader.Read())
                    {

                        OTR_API.TruckerTools.Models.Extra obj = new OTR_API.TruckerTools.Models.Extra();

                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["LoadID"] != DBNull.Value) { obj.LoadID = (int)reader["LoadID"]; }
                        if (reader["AssociatedID"] != DBNull.Value) { obj.AssociatedID = (int)reader["AssociatedID"]; }
                        if (reader["Type"] != DBNull.Value) { obj.Type = (OTR_API.TruckerTools.Models.Extra.ExtraType)(int)reader["Type"]; }
                        if (reader["Name"] != DBNull.Value) { obj.Name = (string)reader["Name"]; }
                        if (reader["Value"] != DBNull.Value) { obj.Value = (string)reader["Value"]; }

                        extras.Add(obj);
                    }


                }
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.GetExtra");
            }


            if (cn.State != ConnectionState.Closed) { cn.Close(); }


            return extras;

        }

        public OTR_API.TruckerTools.Models.LoadPays GetLoadPay(int LoadID)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "spPayByLoadID_Get";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@LoadID", LoadID);

            OTR_API.TruckerTools.Models.LoadPays obj = new OTR_API.TruckerTools.Models.LoadPays();
            obj.revenue = new OTR_API.TruckerTools.Models.Revenue();
            obj.carrierpay = new OTR_API.TruckerTools.Models.CarrierPay();
            obj.targetpay = new OTR_API.TruckerTools.Models.TargetPay();

            List<OTR_API.TruckerTools.Models.Pay> Pays = new List<OTR_API.TruckerTools.Models.Pay>();
            try
            {
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {
                        OTR_API.TruckerTools.Models.Pay py = new OTR_API.TruckerTools.Models.Pay();

                        if (reader["ID"] != DBNull.Value) { py.ID = (int)reader["ID"]; }
                        if (reader["LoadID"] != DBNull.Value) { py.LoadID = (int)reader["LoadID"]; }
                        if (reader["Type"] != DBNull.Value) { py.Type = (OTR_API.TruckerTools.Models.Pay.PayType)(int)reader["Type"]; }
                        if (reader["freight"] != DBNull.Value) { py.Freight = (string)reader["freight"]; }
                        if (reader["extra"] != DBNull.Value) { py.Extra = (string)reader["extra"]; }
                        if (reader["total"] != DBNull.Value) { py.Total = (string)reader["total"]; }
                        if (reader["maximum"] != DBNull.Value) { py.MaximumPay = (string)reader["maximum"]; }
                        if (reader["minimum"] != DBNull.Value) { py.MinimumPay = (string)reader["minimum"]; }

                        Pays.Add(py);
                    }

                }

                if (cn.State != ConnectionState.Closed) { cn.Close(); }

                foreach (OTR_API.TruckerTools.Models.Pay pay in Pays)
                {
                    switch (pay.Type)
                    {
                        case OTR_API.TruckerTools.Models.Pay.PayType.Revenue:
                            OTR_API.TruckerTools.Models.Revenue rev = new OTR_API.TruckerTools.Models.Revenue();
                            rev.Freight = pay.Freight;
                            rev.Extra = pay.Extra;
                            rev.Total = pay.Total;

                            obj.revenue = rev;
                            break;

                        case OTR_API.TruckerTools.Models.Pay.PayType.TargetPay:
                            OTR_API.TruckerTools.Models.TargetPay tpay = new OTR_API.TruckerTools.Models.TargetPay();
                            tpay.MaximumPay = pay.MaximumPay;
                            tpay.MinimumPay = pay.MinimumPay;

                            obj.targetpay = tpay;
                            break;

                        case OTR_API.TruckerTools.Models.Pay.PayType.CarrierPay:
                        default:
                            OTR_API.TruckerTools.Models.CarrierPay cpay = new OTR_API.TruckerTools.Models.CarrierPay();
                            cpay.Freight = pay.Freight;
                            cpay.Extra = pay.Extra;
                            cpay.Total = pay.Total;

                            obj.carrierpay = cpay;

                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.GetStops");
            }


            return obj;
        }

        public List<OTR_API.TruckerTools.Models.Load> GetLoadsByStatus(string status)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "spLoadByStatus_Get";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@status", status);



            List<OTR_API.TruckerTools.Models.Load> loadlist = new List<OTR_API.TruckerTools.Models.Load>();

            try
            {
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {
                        OTR_API.TruckerTools.Models.Load obj = new OTR_API.TruckerTools.Models.Load();

                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["status"] != DBNull.Value) { obj.status = (string)reader["status"]; }
                        if (reader["equipmentType"] != DBNull.Value) { obj.equipmentType = (string)reader["equipmentType"]; }
                        if (reader["loadNumber"] != DBNull.Value) { obj.loadNumber = (string)reader["loadNumber"]; }
                        if (reader["externalId"] != DBNull.Value) { obj.externalId = (string)reader["externalId"]; }

                        obj.pickup = new OTR_API.TruckerTools.Models.Pickup();
                        obj.delivery = new OTR_API.TruckerTools.Models.Delivery();
                        obj.additionalStops = new List<OTR_API.TruckerTools.Models.AdditionalStop>();

                        if (reader["loadType"] != DBNull.Value) { obj.loadType = (string)reader["loadType"]; }

                        obj.revenue = new OTR_API.TruckerTools.Models.Revenue();
                        obj.carrierPay = new OTR_API.TruckerTools.Models.CarrierPay();
                        obj.targetPay = new OTR_API.TruckerTools.Models.TargetPay();

                        obj.loadContact = new OTR_API.TruckerTools.Models.LoadContact();
                        obj.operationUser = new OTR_API.TruckerTools.Models.OperationUser();
                        obj.salesPerson = new OTR_API.TruckerTools.Models.SalesPerson();
                        obj.broker = new OTR_API.TruckerTools.Models.Broker();
                        obj.carrier = new OTR_API.TruckerTools.Models.LoadCarrier();
                        obj.driver = new OTR_API.TruckerTools.Models.Driver();
                        obj.dispatcher = new OTR_API.TruckerTools.Models.Dispatcher();
                        obj.shipper = new OTR_API.TruckerTools.Models.Shipper();


                        if (reader["trucksCount"] != DBNull.Value) { obj.trucksCount = (int)reader["trucksCount"]; }
                        if (reader["length"] != DBNull.Value) { obj.length = (string)reader["length"]; }
                        if (reader["weight"] != DBNull.Value) { obj.weight = (string)reader["weight"]; }
                        if (reader["quantity"] != DBNull.Value) { obj.quantity = (string)reader["quantity"]; }
                        if (reader["rate"] != DBNull.Value) { obj.rate = (string)reader["rate"]; }
                        if (reader["billToId"] != DBNull.Value) { obj.billToId = (string)reader["billToId"]; }
                        if (reader["orderType"] != DBNull.Value) { obj.orderType = (string)reader["orderType"]; }
                        if (reader["temperatureMinimum"] != DBNull.Value) { obj.temperatureMinimum = (string)reader["temperatureMinimum"]; }
                        if (reader["temperatureMaximum"] != DBNull.Value) { obj.temperatureMaximum = (string)reader["temperatureMaximum"]; }
                        if (reader["commodityId"] != DBNull.Value) { obj.commodityId = (string)reader["commodityId"]; }
                        if (reader["hazmat"] != DBNull.Value) { obj.hazmat = (bool)reader["hazmat"]; }
                        if (reader["highValue"] != DBNull.Value) { obj.highValue = (bool)reader["highValue"]; }
                        if (reader["teamsRequired"] != DBNull.Value) { obj.teamsRequired = (bool)reader["teamsRequired"]; }
                        if (reader["comments"] != DBNull.Value) { obj.comments = (string)reader["comments"]; }
                        if (reader["numberOfAdditionalStops"] != DBNull.Value) { obj.numberOfAdditionalStops = (int)reader["numberOfAdditionalStops"]; }
                        if (reader["shipperLoadNumber"] != DBNull.Value) { obj.shipperLoadNumber = (string)reader["shipperLoadNumber"]; }
                        if (reader["tractorNumber"] != DBNull.Value) { obj.tractorNumber = (string)reader["tractorNumber"]; }
                        if (reader["trailerNumber"] != DBNull.Value) { obj.trailerNumber = (string)reader["trailerNumber"]; }
                        if (reader["publishToCarrier"] != DBNull.Value) { obj.publishToCarrier = (bool)reader["publishToCarrier"]; }
                        if (reader["bookItNowPrice"] != DBNull.Value) { obj.bookItNowPrice = (string)reader["bookItNowPrice"]; }
                        if (reader["totalMiles"] != DBNull.Value) { obj.totalMiles = (decimal)reader["totalMiles"]; }
                        if (reader["ratePerMile"] != DBNull.Value) { obj.ratePerMile = (decimal)reader["ratePerMile"]; }
                        if (reader["ratePerMileFuel"] != DBNull.Value) { obj.ratePerMileFuel = (decimal)reader["ratePerMileFuel"]; }
                        if (reader["triggerTracking"] != DBNull.Value) { obj.triggerTracking = (bool)reader["triggerTracking"]; }

                        if (reader["VectorID"] != DBNull.Value) { obj.VectorID = (int)reader["VectorID"]; }
                        if (reader["VectorCarrierID"] != DBNull.Value) { obj.VectorCarrierID = (int)reader["VectorCarrierID"]; }

                        obj.extras = new List<OTR_API.TruckerTools.Models.Extra>();

                        List<OTR_API.TruckerTools.Models.Extra> extras = GetExtras(obj.ID, obj.ID, OTR_API.TruckerTools.Models.Stop.StopType.None);
                        if (extras.Count > 0)
                            obj.extras = extras;

                        loadlist.Add(obj);
                    }
                }
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.GetLoad");
            }



            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            foreach (OTR_API.TruckerTools.Models.Load ld in loadlist)
            {

                OTR_API.TruckerTools.Models.LoadStops loadstops = GetLoadStops(ld.ID);

                ld.pickup = loadstops.pickUp;
                ld.delivery = loadstops.delivery;

                if (loadstops.additionalstops.Count > 0)
                    ld.additionalStops = loadstops.additionalstops;



                if (cn.State != ConnectionState.Closed) { cn.Close(); }

                OTR_API.TruckerTools.Models.LoadContacts loadcontacts = GetLoadContacts(ld.ID);

                ld.loadContact = loadcontacts.loadcontact;
                ld.salesPerson = loadcontacts.salesperson;
                ld.operationUser = loadcontacts.operationuser;
                ld.carrier = loadcontacts.carrier;
                ld.driver = loadcontacts.driver;
                ld.dispatcher = loadcontacts.dispatcher;
                ld.shipper = loadcontacts.shipper;
                ld.broker = loadcontacts.broker;



                if (cn.State != ConnectionState.Closed) { cn.Close(); }

                OTR_API.TruckerTools.Models.LoadPays loadpays = GetLoadPay(ld.ID);

                ld.revenue = loadpays.revenue;
                ld.targetPay = loadpays.targetpay;
                ld.carrierPay = loadpays.carrierpay;


                if (ld.VectorCarrierID > 0)
                {
                    if (cn.State != ConnectionState.Closed) { cn.Close(); }

                    DataCarrierMatch dc = new DataCarrierMatch();
                    OTR_API.TruckerTools.Models.Carrier car = dc.GetCarrierByVectorID(ld.ID);

                    ld.carrier.companyName = car.carrier_name;
                    ld.carrier.mc = car.mc;
                    ld.carrier.contactPhone = car.contact_phone;
                    ld.carrier.contactEmail = car.contact_email;
                    ld.carrier.dotNumber = car.dot;
                    ld.carrier.scac = car.scac;
                    ld.carrier.numberOfTrucks = car.NumberofTrucks;
                }
            }

            return loadlist;

        }

        public List<OTR_API.TruckerTools.Models.Load> GetLoadsWithDetail(string[] loadnumbers)
        {
            List<OTR_API.TruckerTools.Models.Load> LoadList = new List<OTR_API.TruckerTools.Models.Load>();

            foreach (string num in loadnumbers)
            {
                int a;
                bool res = int.TryParse(num, out a);
                if (res)
                {
                    OTR_API.TruckerTools.Models.Load newload = GetLoadByID(Convert.ToInt32(num), "VectorID");
                    LoadList.Add(newload);
                }
            }

            return LoadList;
        }


        public int InsertLoad(OTR_API.TruckerTools.Models.Load load)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "spLoads_Insert";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;


            cd.Parameters.AddWithValue("@status", load.status);
            cd.Parameters.AddWithValue("@equipmentType", load.equipmentType);
            cd.Parameters.AddWithValue("@loadNumber", load.loadNumber);
            cd.Parameters.AddWithValue("@externalId", load.externalId);
            cd.Parameters.AddWithValue("@loadType", load.loadType);
            cd.Parameters.AddWithValue("@trucksCount", load.trucksCount);
            cd.Parameters.AddWithValue("@length", load.length);
            cd.Parameters.AddWithValue("@weight", load.weight);
            cd.Parameters.AddWithValue("@quantity", load.quantity);
            cd.Parameters.AddWithValue("@rate", load.rate);


            cd.Parameters.AddWithValue("@billToId", load.billToId);
            cd.Parameters.AddWithValue("@orderType", load.orderType);
            cd.Parameters.AddWithValue("@temperatureMinimum", load.temperatureMinimum);
            cd.Parameters.AddWithValue("@temperatureMaximum", load.temperatureMaximum);
            cd.Parameters.AddWithValue("@commodityid", load.commodityId);
            cd.Parameters.AddWithValue("@hazmat", load.hazmat);
            cd.Parameters.AddWithValue("@highValue", load.highValue);
            cd.Parameters.AddWithValue("@teamsRequired", load.teamsRequired);
            cd.Parameters.AddWithValue("@comments", load.comments);
            cd.Parameters.AddWithValue("@numberOfAdditionalStops", load.numberOfAdditionalStops);
            cd.Parameters.AddWithValue("@shipperLoadNumber", load.shipperLoadNumber);


            cd.Parameters.AddWithValue("@tractorNumber", load.tractorNumber);
            cd.Parameters.AddWithValue("@trailerNumber", load.trailerNumber);
            cd.Parameters.AddWithValue("@publishToCarrier", load.publishToCarrier);
            cd.Parameters.AddWithValue("@bookItNowPrice", load.bookItNowPrice);
            cd.Parameters.AddWithValue("@totalMiles", load.totalMiles);
            cd.Parameters.AddWithValue("@ratePerMile", load.ratePerMile);
            cd.Parameters.AddWithValue("@ratePerMileFuel", load.ratePerMileFuel);
            cd.Parameters.AddWithValue("@triggerTracking", load.triggerTracking);

            cd.Parameters.AddWithValue("@VectorID", load.VectorID);
            cd.Parameters.AddWithValue("@VectorCarrierID", load.VectorCarrierID);


            SqlParameter outputparm = new SqlParameter("@responseMessage", SqlDbType.Int)
            {
                Direction = ParameterDirection.Output
            };

            cd.Parameters.Add(outputparm);

            try
            {
                cd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.InsertLoad");
            }


            int results = Convert.ToInt32(outputparm.Value);


            //what to do if the Loadialready exists - a return of 0


            if (cn.State != ConnectionState.Closed) { cn.Close(); }


            if (results > 0)
            {
                load.ID = results;
                if (load.extras != null)
                {
                    if (load.extras.Count > 0)
                    {
                        foreach (OTR_API.TruckerTools.Models.Extra le in load.extras)
                        {
                            le.LoadID = load.ID;
                            le.AssociatedID = load.ID;
                            le.Type = OTR_API.TruckerTools.Models.Extra.ExtraType.Load;
                            InsertExtra(le);
                        }
                    }
                }


                load.pickup.LoadID = load.ID;
                load.pickup.Type = OTR_API.TruckerTools.Models.Stop.StopType.Pickup;
                int pickupid = InsertLoadStop(load.pickup);
                if (load.pickup.Extras != null)
                {
                    if (load.pickup.Extras.Count > 0)
                    {
                        foreach (OTR_API.TruckerTools.Models.Extra lp in load.pickup.Extras)
                        {
                            lp.LoadID = load.ID;
                            lp.AssociatedID = pickupid;
                            lp.Type = OTR_API.TruckerTools.Models.Extra.ExtraType.Pickup;
                            InsertExtra(lp);
                        }
                    }
                }

                load.delivery.LoadID = load.ID;
                load.delivery.Type = OTR_API.TruckerTools.Models.Stop.StopType.Delivery;
                int deliveryid = InsertLoadStop(load.delivery);
                if (load.delivery.Extras != null)
                {
                    if (load.delivery.Extras.Count > 0)
                    {
                        foreach (OTR_API.TruckerTools.Models.Extra ld in load.delivery.Extras)
                        {
                            ld.LoadID = load.ID;
                            ld.AssociatedID = deliveryid;
                            ld.Type = OTR_API.TruckerTools.Models.Extra.ExtraType.Delivery;
                            InsertExtra(ld);
                        }
                    }
                }

                if (load.additionalStops != null)
                {
                    foreach (OTR_API.TruckerTools.Models.Stop ls in load.additionalStops)
                    {
                        ls.LoadID = load.ID;
                        ls.Type = OTR_API.TruckerTools.Models.Stop.StopType.AdditionalStops;
                        int addid = InsertLoadStop(ls);
                        if (ls.Extras != null)
                        {
                            if (ls.Extras.Count > 0)
                            {
                                foreach (OTR_API.TruckerTools.Models.Extra la in ls.Extras)
                                {
                                    la.LoadID = load.ID;
                                    la.AssociatedID = addid;
                                    la.Type = OTR_API.TruckerTools.Models.Extra.ExtraType.AdditionalStops;
                                    InsertExtra(la);
                                }
                            }
                        }
                    }
                }

                if(load.revenue != null)
                {
                    OTR_API.TruckerTools.Models.Pay revPay = new OTR_API.TruckerTools.Models.Pay() { LoadID = load.ID, Type = OTR_API.TruckerTools.Models.Pay.PayType.Revenue, Freight = load.revenue.Freight, Extra = load.revenue.Extra, Total = load.revenue.Total };
                    InsertPay(revPay);
                }

                if (load.carrierPay != null)
                {
                    OTR_API.TruckerTools.Models.Pay carPay = new OTR_API.TruckerTools.Models.Pay() { LoadID = load.ID, Type = OTR_API.TruckerTools.Models.Pay.PayType.CarrierPay, Freight = load.carrierPay.Freight, Extra = load.carrierPay.Extra, Total = load.carrierPay.Total };
                    InsertPay(carPay);
                }

                if (load.targetPay != null)
                {
                    OTR_API.TruckerTools.Models.Pay tarPay = new OTR_API.TruckerTools.Models.Pay() { LoadID = load.ID, Type = OTR_API.TruckerTools.Models.Pay.PayType.TargetPay, MinimumPay = load.targetPay.MinimumPay, MaximumPay = load.targetPay.MaximumPay };
                    InsertPay(tarPay);
                }


                if (load.loadContact != null)
                {
                    OTR_API.TruckerTools.Models.Contact lcont = new OTR_API.TruckerTools.Models.Contact() { LoadID = load.ID, Type = OTR_API.TruckerTools.Models.Contact.ContactType.Load, Name = load.loadContact.Name, Phone = load.loadContact.ContactPhone, Email = load.loadContact.ContactEmail, Extension = load.loadContact.PhoneExtension };
                    InsertContact(lcont);
                }

                if (load.operationUser != null)
                {
                    OTR_API.TruckerTools.Models.Contact ouser = new OTR_API.TruckerTools.Models.Contact() { LoadID = load.ID, Type = OTR_API.TruckerTools.Models.Contact.ContactType.Operation, Name = load.operationUser.Name, Phone = load.operationUser.ContactPhone, Email = load.operationUser.ContactEmail, Extension = load.operationUser.PhoneExtension, Team = load.operationUser.Team, MemberID = load.operationUser.ID, VectorID = load.operationUser.VectorID };
                    InsertContact(ouser);
                }

                if (load.salesPerson != null)
                {
                    OTR_API.TruckerTools.Models.Contact sper = new OTR_API.TruckerTools.Models.Contact() { LoadID = load.ID, Type = OTR_API.TruckerTools.Models.Contact.ContactType.Sales, Name = load.salesPerson.Name, Phone = load.salesPerson.ContactPhone, Email = load.salesPerson.ContactEmail, Extension = load.salesPerson.PhoneExtension, VectorID = load.salesPerson.VectorID };
                    InsertContact(sper);
                }

                if (load.broker != null)
                {
                    OTR_API.TruckerTools.Models.Contact brok = new OTR_API.TruckerTools.Models.Contact() { LoadID = load.ID, Type = OTR_API.TruckerTools.Models.Contact.ContactType.Broker, Name = load.broker.CompanyName, Phone = load.broker.ContactPhone, Email = load.broker.ContactEmail, dot = load.broker.dot, mc = load.broker.mc };
                    InsertContact(brok);
                }

                if (load.driver != null)
                {
                    OTR_API.TruckerTools.Models.Contact drv = new OTR_API.TruckerTools.Models.Contact() { LoadID = load.ID, Type = OTR_API.TruckerTools.Models.Contact.ContactType.Driver, Name = load.driver.Name, Phone = load.driver.Phone, DeviceID = load.driver.DeviceID };
                    InsertContact(drv);
                }

                if (load.dispatcher != null)
                {
                    OTR_API.TruckerTools.Models.Contact disp = new OTR_API.TruckerTools.Models.Contact() { LoadID = load.ID, Type = OTR_API.TruckerTools.Models.Contact.ContactType.Dispatcher, Name = load.dispatcher.Name, Phone = load.dispatcher.ContactPhone, Email = load.dispatcher.ContactEmail, MemberID = load.dispatcher.ID };
                    InsertContact(disp);
                }

                if (load.shipper != null)
                {
                    OTR_API.TruckerTools.Models.Contact ship = new OTR_API.TruckerTools.Models.Contact() { LoadID = load.ID, Type = OTR_API.TruckerTools.Models.Contact.ContactType.Shipper, Name = load.shipper.CompanyName, Phone = load.shipper.ContactPhone, Email = load.shipper.ContactEmail, VectorID = load.shipper.VectorID };
                    InsertContact(ship);
                }


            }

            return results;

        }

        public int UpdateLoad(OTR_API.TruckerTools.Models.Load load)
        {
            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "spLoads_Update";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;


            cd.Parameters.AddWithValue("@status", load.status);
            cd.Parameters.AddWithValue("@equipmentType", load.equipmentType);
            cd.Parameters.AddWithValue("@loadNumber", load.loadNumber);
            cd.Parameters.AddWithValue("@externalId", load.externalId);
            cd.Parameters.AddWithValue("@loadType", load.loadType);
            cd.Parameters.AddWithValue("@trucksCount", load.trucksCount);
            cd.Parameters.AddWithValue("@length", load.length);
            cd.Parameters.AddWithValue("@weight", load.weight);
            cd.Parameters.AddWithValue("@quantity", load.quantity);
            cd.Parameters.AddWithValue("@rate", load.rate);


            cd.Parameters.AddWithValue("@billToId", load.billToId);
            cd.Parameters.AddWithValue("@orderType", load.orderType);
            cd.Parameters.AddWithValue("@temperatureMinimum", load.temperatureMinimum);
            cd.Parameters.AddWithValue("@temperatureMaximum", load.temperatureMaximum);
            cd.Parameters.AddWithValue("@commodityid", load.commodityId);
            cd.Parameters.AddWithValue("@hazmat", load.hazmat);
            cd.Parameters.AddWithValue("@highValue", load.highValue);
            cd.Parameters.AddWithValue("@teamsRequired", load.teamsRequired);
            cd.Parameters.AddWithValue("@comments", load.comments);
            cd.Parameters.AddWithValue("@numberOfAdditionalStops", load.numberOfAdditionalStops);
            cd.Parameters.AddWithValue("@shipperLoadNumber", load.shipperLoadNumber);


            cd.Parameters.AddWithValue("@tractorNumber", load.tractorNumber);
            cd.Parameters.AddWithValue("@trailerNumber", load.trailerNumber);
            cd.Parameters.AddWithValue("@publishToCarrier", load.publishToCarrier);
            cd.Parameters.AddWithValue("@bookItNowPrice", load.bookItNowPrice);
            cd.Parameters.AddWithValue("@totalMiles", load.totalMiles);
            cd.Parameters.AddWithValue("@ratePerMile", load.ratePerMile);
            cd.Parameters.AddWithValue("@ratePerMileFuel", load.ratePerMileFuel);
            cd.Parameters.AddWithValue("@triggerTracking", load.triggerTracking);

            cd.Parameters.AddWithValue("@VectorCarrierID", load.VectorCarrierID);

            cd.Parameters.Add(new SqlParameter("@ID", load.ID));


            SqlParameter outputparm = new SqlParameter("@responseMessage", SqlDbType.Int)
            {
                Direction = ParameterDirection.Output
            };

            cd.Parameters.Add(outputparm);

            try
            {
                cd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.UpdateLoad");
            }


            int results = Convert.ToInt32(outputparm.Value);
           
            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            if (results > 0)
            {

                DeleteExtraByLoadID(load.ID);

                if (load.extras.Count > 0)
                {
                    foreach (OTR_API.TruckerTools.Models.Extra le in load.extras)
                    {
                        le.LoadID = load.ID;
                        InsertExtra(le);
                    }
                }



        
                DeleteLoadStopByLoadID(load.ID);

                load.pickup.LoadID = load.ID;
                load.pickup.VectorID = load.VectorID;
                load.pickup.Type = OTR_API.TruckerTools.Models.Stop.StopType.Pickup;

                load.delivery.LoadID = load.ID;
                load.delivery.VectorID = load.VectorID;
                load.delivery.Type = OTR_API.TruckerTools.Models.Stop.StopType.Delivery;

                InsertLoadStop(load.pickup);
                InsertLoadStop(load.delivery);


                foreach (OTR_API.TruckerTools.Models.Stop ls in load.additionalStops)
                {
                    ls.LoadID = load.ID;
                    ls.VectorID = load.VectorID;
                    ls.Type = OTR_API.TruckerTools.Models.Stop.StopType.AdditionalStops;
                    InsertLoadStop(ls);
                }


                DeletePayByLoadID(load.ID);

                OTR_API.TruckerTools.Models.Pay revPay = new OTR_API.TruckerTools.Models.Pay() { LoadID = load.ID, Type = OTR_API.TruckerTools.Models.Pay.PayType.Revenue, Freight = load.revenue.Freight, Extra = load.revenue.Extra, Total = load.revenue.Total };
                InsertPay(revPay);

                OTR_API.TruckerTools.Models.Pay carPay = new OTR_API.TruckerTools.Models.Pay() { LoadID = load.ID, Type = OTR_API.TruckerTools.Models.Pay.PayType.CarrierPay, Freight = load.carrierPay.Freight, Extra = load.carrierPay.Extra, Total = load.carrierPay.Total };
                InsertPay(carPay);

                OTR_API.TruckerTools.Models.Pay tarPay = new OTR_API.TruckerTools.Models.Pay() { LoadID = load.ID, Type = OTR_API.TruckerTools.Models.Pay.PayType.TargetPay, MinimumPay = load.targetPay.MinimumPay, MaximumPay = load.targetPay.MaximumPay };
                InsertPay(tarPay);




                DeleteContactByLoadID(load.ID);

                OTR_API.TruckerTools.Models.Contact lcont = new OTR_API.TruckerTools.Models.Contact() { LoadID = load.ID, Type = OTR_API.TruckerTools.Models.Contact.ContactType.Load, Name = load.loadContact.Name, Phone = load.loadContact.ContactPhone, Email = load.loadContact.ContactEmail, Extension = load.loadContact.PhoneExtension };
                InsertContact(lcont);

                OTR_API.TruckerTools.Models.Contact ouser = new OTR_API.TruckerTools.Models.Contact() { LoadID = load.ID, Type = OTR_API.TruckerTools.Models.Contact.ContactType.Operation, Name = load.operationUser.Name, Phone = load.operationUser.ContactPhone, Email = load.operationUser.ContactEmail, Extension = load.operationUser.PhoneExtension, Team = load.operationUser.Team, MemberID = load.operationUser.ID, VectorID = load.operationUser.VectorID };
                InsertContact(ouser);

                OTR_API.TruckerTools.Models.Contact sper = new OTR_API.TruckerTools.Models.Contact() { LoadID = load.ID, Type = OTR_API.TruckerTools.Models.Contact.ContactType.Sales, Name = load.salesPerson.Name, Phone = load.salesPerson.ContactPhone, Email = load.salesPerson.ContactEmail, Extension = load.salesPerson.PhoneExtension, VectorID = load.salesPerson.VectorID };
                InsertContact(sper);

                OTR_API.TruckerTools.Models.Contact brok = new OTR_API.TruckerTools.Models.Contact() { LoadID = load.ID, Type = OTR_API.TruckerTools.Models.Contact.ContactType.Broker, Name = load.broker.CompanyName, Phone = load.broker.ContactPhone, Email = load.broker.ContactEmail, dot = load.broker.dot, mc = load.broker.mc };
                InsertContact(brok);

                OTR_API.TruckerTools.Models.Contact drv = new OTR_API.TruckerTools.Models.Contact() { LoadID = load.ID, Type = OTR_API.TruckerTools.Models.Contact.ContactType.Driver, Name = load.driver.Name, Phone = load.driver.Phone, DeviceID = load.driver.DeviceID };
                InsertContact(drv);

                OTR_API.TruckerTools.Models.Contact disp = new OTR_API.TruckerTools.Models.Contact() { LoadID = load.ID, Type = OTR_API.TruckerTools.Models.Contact.ContactType.Dispatcher, Name = load.dispatcher.Name, Phone = load.dispatcher.ContactPhone, Email = load.dispatcher.ContactEmail, MemberID = load.dispatcher.ID };
                InsertContact(disp);

                OTR_API.TruckerTools.Models.Contact ship = new OTR_API.TruckerTools.Models.Contact() { LoadID = load.ID, Type = OTR_API.TruckerTools.Models.Contact.ContactType.Shipper, Name = load.shipper.CompanyName, Phone = load.shipper.ContactPhone, Email = load.shipper.ContactEmail, VectorID = load.shipper.VectorID };
                InsertContact(ship);

            }

            return results;
        }

        public bool DeleteLoad(int LoadID)
        {
            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "spLoads_Delete";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;

            cd.Parameters.AddWithValue("@ID", LoadID);


            bool i = false;
            try
            {
                i = (cd.ExecuteNonQuery() == 1);
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.DeleteLoad");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return i;
        }



        public int InsertLoadStop(OTR_API.TruckerTools.Models.Stop stop)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "spStop_Insert";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;

            cd.Parameters.AddWithValue("@LoadID", stop.LoadID);
            cd.Parameters.AddWithValue("@StopType", (int)stop.Type);

            cd.Parameters.AddWithValue("@Address", stop.Address);
            cd.Parameters.AddWithValue("@City", stop.City);
            cd.Parameters.AddWithValue("@State", stop.State);
            cd.Parameters.AddWithValue("@PostalCode", stop.PostalCode);
            cd.Parameters.AddWithValue("@Latitude", stop.Latitude);
            cd.Parameters.AddWithValue("@Longitude", stop.Longitude);

            cd.Parameters.AddWithValue("@timeZone", stop.TimeZone);
            cd.Parameters.AddWithValue("@sequence", stop.Sequence);

            cd.Parameters.AddWithValue("@stopExternalID", stop.StopExternalID);
            cd.Parameters.AddWithValue("@scheduledAtEarlyDateTime", stop.ScheduledAtEarlyDateTime);
            cd.Parameters.AddWithValue("@scheduledAtLateDateTime", stop.ScheduledAtLateDateTime);

            cd.Parameters.AddWithValue("@appointmentRequired", stop.AppointmentRequired);
            cd.Parameters.AddWithValue("@appointmentConfirmed", stop.AppointmentConfirmed);

            cd.Parameters.AddWithValue("@VectorID", stop.VectorID);


            SqlParameter outputparm = new SqlParameter("@responseMessage", SqlDbType.Int)
            {
                Direction = ParameterDirection.Output
            };

            cd.Parameters.Add(outputparm);

            try
            {
                cd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.InsertStop");
            }

            int results = Convert.ToInt32(outputparm.Value);

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return results;
        }

        public bool UpdateLoadStop(OTR_API.TruckerTools.Models.Stop stop)
        {
            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "spStop_Update";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;

            cd.Parameters.AddWithValue("@ID", stop.ID);

            cd.Parameters.AddWithValue("@LoadID", stop.LoadID);
            cd.Parameters.AddWithValue("@StopType", (int)stop.Type);

            cd.Parameters.AddWithValue("@Address", stop.Address);
            cd.Parameters.AddWithValue("@City", stop.City);
            cd.Parameters.AddWithValue("@State", stop.State);
            cd.Parameters.AddWithValue("@PostalCode", stop.PostalCode);
            cd.Parameters.AddWithValue("@Latitude", stop.Latitude);
            cd.Parameters.AddWithValue("@Longitude", stop.Longitude);

            cd.Parameters.AddWithValue("@timeZone", stop.TimeZone);
            cd.Parameters.AddWithValue("@sequence", stop.Sequence);

            cd.Parameters.AddWithValue("@stopExternalID", stop.StopExternalID);
            cd.Parameters.AddWithValue("@scheduledAtEarlyDateTime", stop.ScheduledAtEarlyDateTime);
            cd.Parameters.AddWithValue("@scheduledAtLateDateTime", stop.ScheduledAtLateDateTime);

            cd.Parameters.AddWithValue("@appointmentRequired", stop.AppointmentRequired);
            cd.Parameters.AddWithValue("@appointmentConfirmed", stop.AppointmentConfirmed);

            cd.Parameters.AddWithValue("@VectorID", stop.VectorID);

            bool i = false;
            try
            {
                i = (cd.ExecuteNonQuery() == 1);
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.UpdateLoadStop");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return i;
        }

        public bool DeleteLoadStop(OTR_API.TruckerTools.Models.Stop stop)
        {
            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "spStop_Delete";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;

            cd.Parameters.Add(new SqlParameter("@ID", stop.ID));
            cd.Parameters.AddWithValue("@LoadID", stop.LoadID);


            bool i = false;
            try
            {
                i = (cd.ExecuteNonQuery() == 1);
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT,DeleteLoadStop");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return i;
        }
        
        public bool DeleteLoadStopByLoadID(int LoadID)
        {
            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "spStopByLoadID_Delete";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;

            cd.Parameters.AddWithValue("@LoadID", LoadID);


            bool i = false;
            try
            {
                i = (cd.ExecuteNonQuery() == 1);
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT,DeleteLoadStopByLoadID");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return i;
        }




        public int InsertExtra(OTR_API.TruckerTools.Models.Extra extra)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "spExtra_Insert";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;

            cd.Parameters.AddWithValue("@LoadID", extra.LoadID);
            cd.Parameters.AddWithValue("@AssociatedID", extra.AssociatedID);
            cd.Parameters.AddWithValue("@Type", (int)extra.Type);
            cd.Parameters.AddWithValue("@Name", extra.Name);
            cd.Parameters.AddWithValue("@Value", extra.Value);

            SqlParameter outputparm = new SqlParameter("@responseMessage", SqlDbType.Int)
            {
                Direction = ParameterDirection.Output
            };

            cd.Parameters.Add(outputparm);

            try
            {
                cd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.InsertExtra");
            }

            int results = Convert.ToInt32(outputparm.Value);

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return results;
        }

        public bool UpdateExtra(OTR_API.TruckerTools.Models.Extra extra)
        {
            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "spExtra_Update";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;

            cd.Parameters.AddWithValue("@ID", extra.ID);
            cd.Parameters.AddWithValue("@LoadID", extra.LoadID);
            cd.Parameters.AddWithValue("@AssociatedID", extra.AssociatedID);
            cd.Parameters.AddWithValue("@Type", (int)extra.Type);
            cd.Parameters.AddWithValue("@Name", extra.Name);
            cd.Parameters.AddWithValue("@Value", extra.Value);

            bool i = false;
            try
            {
                i = (cd.ExecuteNonQuery() == 1);
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.UpdateExtra");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return i;
        }

        public bool DeleteExtra(OTR_API.TruckerTools.Models.Extra extra)
        {
            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "spExtra_Delete";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;

            cd.Parameters.AddWithValue("@ID", extra.ID);
            cd.Parameters.AddWithValue("@LoadID", extra.LoadID);

            bool i = false;
            try
            {
                i = (cd.ExecuteNonQuery() == 1);
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.DeleteExtra");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return i;
        }

        public bool DeleteExtraByLoadID(int LoadID)
        {
            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "spExtraByLoadID_Delete";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;

            cd.Parameters.AddWithValue("@LoadID", LoadID);

            bool i = false;
            try
            {
                i = (cd.ExecuteNonQuery() == 1);
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.DeleteExtraByLoadID");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return i;
        }





        public int InsertPay(OTR_API.TruckerTools.Models.Pay pay)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "spPay_Insert";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;

            cd.Parameters.AddWithValue("@LoadID", pay.LoadID);
            cd.Parameters.AddWithValue("@type", (int)pay.Type);
            cd.Parameters.AddWithValue("@freight", pay.Freight);
            cd.Parameters.AddWithValue("@extra", pay.Extra);
            cd.Parameters.AddWithValue("@total", pay.Total);
            cd.Parameters.AddWithValue("@Maximum", pay.MaximumPay);
            cd.Parameters.AddWithValue("@Minimum", pay.MinimumPay);

            SqlParameter outputparm = new SqlParameter("@responseMessage", SqlDbType.Int)
            {
                Direction = ParameterDirection.Output
            };

            cd.Parameters.Add(outputparm);

            try
            {
                cd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.InsertPay");
            }

            int results = Convert.ToInt32(outputparm.Value);

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return results;
        }

        public bool UpdatePay(OTR_API.TruckerTools.Models.Pay pay)
        {
            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "spPay_Update";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;

            cd.Parameters.AddWithValue("@ID", pay.ID);
            cd.Parameters.AddWithValue("@LoadID", pay.LoadID);
            cd.Parameters.AddWithValue("@type", (int)pay.Type);
            cd.Parameters.AddWithValue("@freight", pay.Freight);
            cd.Parameters.AddWithValue("@extra", pay.Extra);
            cd.Parameters.AddWithValue("@total", pay.Total);
            cd.Parameters.AddWithValue("@maximum", pay.MaximumPay);
            cd.Parameters.AddWithValue("@minimum", pay.MinimumPay);


            bool i = false;
            try
            {
                i = (cd.ExecuteNonQuery() == 1);
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.UpdatePay");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return i;
        }

        public bool DeletePay(OTR_API.TruckerTools.Models.Pay pay)
        {
            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "spPay_Delete";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;

            cd.Parameters.AddWithValue("@ID", pay.ID);


            bool i = false;
            try
            {
                i = (cd.ExecuteNonQuery() == 1);
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.DeletePay");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return i;
        }

        public bool DeletePayByLoadID(int LoadID)
        {
            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "spPayByLoadID_Delete";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;

            cd.Parameters.AddWithValue("@LoadID", LoadID);


            bool i = false;
            try
            {
                i = (cd.ExecuteNonQuery() == 1);
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.DeletePayByLoadID");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return i;
        }






        public int InsertContact(OTR_API.TruckerTools.Models.Contact contact)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "spContact_Insert";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;


            cd.Parameters.AddWithValue("@LoadID", contact.LoadID);
            cd.Parameters.AddWithValue("@type", (int)contact.Type);
            cd.Parameters.AddWithValue("@team", contact.Team);
            cd.Parameters.AddWithValue("@memberid", contact.MemberID);
            cd.Parameters.AddWithValue("@name", contact.Name);
            cd.Parameters.AddWithValue("@phone", contact.Phone);
            cd.Parameters.AddWithValue("@email", contact.Email);
            cd.Parameters.AddWithValue("@extension", contact.Extension);
            cd.Parameters.AddWithValue("@deviceid", contact.DeviceID);
            cd.Parameters.AddWithValue("@mc", contact.mc);
            cd.Parameters.AddWithValue("@dot", contact.dot);
            cd.Parameters.AddWithValue("@scac", contact.scac);
            cd.Parameters.AddWithValue("@numberoftrucks", contact.NumberofTrucks);
            cd.Parameters.AddWithValue("@vectorid", contact.VectorID);


            SqlParameter outputparm = new SqlParameter("@responseMessage", SqlDbType.Int)
            {
                Direction = ParameterDirection.Output
            };

            cd.Parameters.Add(outputparm);

            try
            {
                cd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.InsertContact");
            }

            int results = Convert.ToInt32(outputparm.Value);

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return results;
        }

        public bool UpdateContact(OTR_API.TruckerTools.Models.Contact contact)
        {
            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "spContact_Update";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;

            cd.Parameters.AddWithValue("@ID", contact.ID);
            cd.Parameters.AddWithValue("@LoadID", contact.LoadID);
            cd.Parameters.AddWithValue("@type", (int)contact.Type);
            cd.Parameters.AddWithValue("@team", contact.Team);
            cd.Parameters.AddWithValue("@memberid", contact.MemberID);
            cd.Parameters.AddWithValue("@name", contact.Name);
            cd.Parameters.AddWithValue("@phone", contact.Phone);
            cd.Parameters.AddWithValue("@email", contact.Email);
            cd.Parameters.AddWithValue("@extension", contact.Extension);
            cd.Parameters.AddWithValue("@deviceid", contact.DeviceID);
            cd.Parameters.AddWithValue("@mc", contact.mc);
            cd.Parameters.AddWithValue("@dot", contact.dot);
            cd.Parameters.AddWithValue("@scac", contact.scac);
            cd.Parameters.AddWithValue("@numberoftrucks", contact.NumberofTrucks);
            cd.Parameters.AddWithValue("@vectorid", contact.VectorID);

            bool i = false;
            try
            {
                i = (cd.ExecuteNonQuery() == 1);
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.UpdateContact");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return i;
        }

        public bool DeleteContact(OTR_API.TruckerTools.Models.Contact contact)
        {
            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "spContact_Delete";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;

            cd.Parameters.AddWithValue("@ID", contact.ID);

            bool i = false;
            try
            {
                i = (cd.ExecuteNonQuery() == 1);
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.DeleteContact");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return i;
        }

        public bool DeleteContactByLoadID(int LoadID)
        {
            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "spContactByLoadID_Delete";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;

            cd.Parameters.AddWithValue("@LoadID", LoadID);

            bool i = false;
            try
            {
                i = (cd.ExecuteNonQuery() == 1);
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.DeleteContactByLoadID");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return i;
        }




    }

    public class DataCarrierMatch : DataAccess
    {
        public List<OTR_API.TruckerTools.Models.Carrier> GetCarriers()
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "spCarrierAll_Get";
            SqlCommand cd = new SqlCommand(strsql, cn); 
            cd.CommandType = CommandType.StoredProcedure;

            List<OTR_API.TruckerTools.Models.Carrier> list = new List<OTR_API.TruckerTools.Models.Carrier>();

            try
            { 
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {

                        OTR_API.TruckerTools.Models.Carrier carrier = new OTR_API.TruckerTools.Models.Carrier();

                        if (reader["ID"] != DBNull.Value) { carrier.ID = (int)reader["ID"]; }
                        if (reader["carrier_name"] != DBNull.Value) { carrier.carrier_name = (string)reader["carrier_name"]; }
                        if (reader["mc"] != DBNull.Value) { carrier.mc = (string)reader["mc"]; }
                        if (reader["dot"] != DBNull.Value) { carrier.dot = (string)reader["dot"]; }
                        if (reader["scac"] != DBNull.Value) { carrier.scac = (string)reader["scac"]; }
                        if (reader["external_id"] != DBNull.Value) { carrier.external_id = (string)reader["external_id"]; }
                        if (reader["contact_name"] != DBNull.Value) { carrier.contact_name = (string)reader["contact_name"]; }
                        if (reader["contact_phone"] != DBNull.Value) { carrier.contact_phone = (string)reader["contact_phone"]; }
                        if (reader["contact_email"] != DBNull.Value) { carrier.contact_email = (string)reader["contact_email"]; }
                        if (reader["in_network"] != DBNull.Value) { carrier.in_network = (bool)reader["in_network"]; }
                        if (reader["rejected"] != DBNull.Value) { carrier.rejected = (bool)reader["rejected"]; }
                        if (reader["carrierLevel"] != DBNull.Value) { carrier.carrierLevel = (int)reader["carrierLevel"]; }
                        if (reader["book_it_now"] != DBNull.Value) { carrier.book_it_now = (bool)reader["book_it_now"]; }
                        if (reader["truck_numbers_range"] != DBNull.Value) { carrier.truck_numbers_range = (int)reader["truck_numbers_range"]; }
                        if (reader["truck_numbers"] != DBNull.Value) { carrier.truck_numbers = (int)reader["truck_numbers"]; }
                        if (reader["NumberofTrucks"] != DBNull.Value) { carrier.NumberofTrucks = (string)reader["NumberofTrucks"]; }
                        if (reader["DateAdded"] != DBNull.Value) { carrier.DateAdded = (DateTime)reader["DateAdded"]; }
                        if (reader["LastEvent"] != DBNull.Value) { carrier.LastEvent = (string)reader["LastEvent"]; }
                        if (reader["LastUpdate"] != DBNull.Value) { carrier.LastUpdate = (DateTime)reader["LastUpdate"]; }

                        if (reader["VectorID"] != DBNull.Value) { carrier.VectorID = (int)reader["VectorID"]; }

                        list.Add(carrier);

                    }

                }
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataCarriers.TT.GetCarriers");
            }


            if (cn.State != ConnectionState.Closed) { cn.Close(); }


            return list;

        }

        public List<OTR_API.TruckerTools.Models.Carrier> GetCarriersBookItNow()
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "spCarrierBookItNow_Get";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;

            List<OTR_API.TruckerTools.Models.Carrier> list = new List<OTR_API.TruckerTools.Models.Carrier>();

            try
            {
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {

                        OTR_API.TruckerTools.Models.Carrier carrier = new OTR_API.TruckerTools.Models.Carrier();

                        if (reader["ID"] != DBNull.Value) { carrier.ID = (int)reader["ID"]; }
                        if (reader["carrier_name"] != DBNull.Value) { carrier.carrier_name = (string)reader["carrier_name"]; }
                        if (reader["mc"] != DBNull.Value) { carrier.mc = (string)reader["mc"]; }
                        if (reader["dot"] != DBNull.Value) { carrier.dot = (string)reader["dot"]; }
                        if (reader["scac"] != DBNull.Value) { carrier.scac = (string)reader["scac"]; }
                        if (reader["external_id"] != DBNull.Value) { carrier.external_id = (string)reader["external_id"]; }
                        if (reader["contact_name"] != DBNull.Value) { carrier.contact_name = (string)reader["contact_name"]; }
                        if (reader["contact_phone"] != DBNull.Value) { carrier.contact_phone = (string)reader["contact_phone"]; }
                        if (reader["contact_email"] != DBNull.Value) { carrier.contact_email = (string)reader["contact_email"]; }
                        if (reader["in_network"] != DBNull.Value) { carrier.in_network = (bool)reader["in_network"]; }
                        if (reader["rejected"] != DBNull.Value) { carrier.rejected = (bool)reader["rejected"]; }
                        if (reader["carrierLevel"] != DBNull.Value) { carrier.carrierLevel = (int)reader["carrierLevel"]; }
                        if (reader["book_it_now"] != DBNull.Value) { carrier.book_it_now = (bool)reader["book_it_now"]; }
                        if (reader["truck_numbers_range"] != DBNull.Value) { carrier.truck_numbers_range = (int)reader["truck_numbers_range"]; }
                        if (reader["truck_numbers"] != DBNull.Value) { carrier.truck_numbers = (int)reader["truck_numbers"]; }
                        if (reader["NumberofTrucks"] != DBNull.Value) { carrier.NumberofTrucks = (string)reader["NumberofTrucks"]; }
                        if (reader["DateAdded"] != DBNull.Value) { carrier.DateAdded = (DateTime)reader["DateAdded"]; }
                        if (reader["LastEvent"] != DBNull.Value) { carrier.LastEvent = (string)reader["LastEvent"]; }
                        if (reader["LastUpdate"] != DBNull.Value) { carrier.LastUpdate = (DateTime)reader["LastUpdate"]; }

                        if (reader["VectorID"] != DBNull.Value) { carrier.VectorID = (int)reader["VectorID"]; }

                        list.Add(carrier);

                    }

                }
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataCarriers.TT.GetCarriers");
            }


            if (cn.State != ConnectionState.Closed) { cn.Close(); }


            return list;

        }

        public OTR_API.TruckerTools.Models.Carrier GetCarrierByID(int ID)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "spCarrierByID_Get";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;

            cd.Parameters.AddWithValue("@ID", ID);

            OTR_API.TruckerTools.Models.Carrier carrier = new OTR_API.TruckerTools.Models.Carrier();

            try
            { 
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {

                        if (reader["ID"] != DBNull.Value) { carrier.ID = (int)reader["ID"]; }
                        if (reader["carrier_name"] != DBNull.Value) { carrier.carrier_name = (string)reader["carrier_name"]; }
                        if (reader["mc"] != DBNull.Value) { carrier.mc = (string)reader["mc"]; }
                        if (reader["dot"] != DBNull.Value) { carrier.dot = (string)reader["dot"]; }
                        if (reader["scac"] != DBNull.Value) { carrier.scac = (string)reader["scac"]; }
                        if (reader["external_id"] != DBNull.Value) { carrier.external_id = (string)reader["external_id"]; }
                        if (reader["contact_name"] != DBNull.Value) { carrier.contact_name = (string)reader["contact_name"]; }
                        if (reader["contact_phone"] != DBNull.Value) { carrier.contact_phone = (string)reader["contact_phone"]; }
                        if (reader["contact_email"] != DBNull.Value) { carrier.contact_email = (string)reader["contact_email"]; }
                        if (reader["in_network"] != DBNull.Value) { carrier.in_network = (bool)reader["in_network"]; }
                        if (reader["rejected"] != DBNull.Value) { carrier.rejected = (bool)reader["rejected"]; }
                        if (reader["carrierLevel"] != DBNull.Value) { carrier.carrierLevel = (int)reader["carrierLevel"]; }
                        if (reader["book_it_now"] != DBNull.Value) { carrier.book_it_now = (bool)reader["book_it_now"]; }
                        if (reader["truck_numbers_range"] != DBNull.Value) { carrier.truck_numbers_range = (int)reader["truck_numbers_range"]; }
                        if (reader["truck_numbers"] != DBNull.Value) { carrier.truck_numbers = (int)reader["truck_numbers"]; }
                        if (reader["NumberofTrucks"] != DBNull.Value) { carrier.NumberofTrucks = (string)reader["NumberofTrucks"]; }
                        if (reader["DateAdded"] != DBNull.Value) { carrier.DateAdded = (DateTime)reader["DateAdded"]; }
                        if (reader["LastEvent"] != DBNull.Value) { carrier.LastEvent = (string)reader["LastEvent"]; }
                        if (reader["LastUpdate"] != DBNull.Value) { carrier.LastUpdate = (DateTime)reader["LastUpdate"]; }

                        if (reader["VectorID"] != DBNull.Value) { carrier.VectorID = (int)reader["VectorID"]; }

                    }

                }
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataCarriers.TT.GetCarrierByID");
            }


            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return carrier;

        }

        public OTR_API.TruckerTools.Models.Carrier GetCarrierByVectorID(int ID)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "spCarrierByVectorID_Get";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;

            cd.Parameters.AddWithValue("@VectorID", ID);

            OTR_API.TruckerTools.Models.Carrier carrier = new OTR_API.TruckerTools.Models.Carrier();

            try
            {
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {

                        if (reader["ID"] != DBNull.Value) { carrier.ID = (int)reader["ID"]; }
                        if (reader["carrier_name"] != DBNull.Value) { carrier.carrier_name = (string)reader["carrier_name"]; }
                        if (reader["mc"] != DBNull.Value) { carrier.mc = (string)reader["mc"]; }
                        if (reader["dot"] != DBNull.Value) { carrier.dot = (string)reader["dot"]; }
                        if (reader["scac"] != DBNull.Value) { carrier.scac = (string)reader["scac"]; }
                        if (reader["external_id"] != DBNull.Value) { carrier.external_id = (string)reader["external_id"]; }
                        if (reader["contact_name"] != DBNull.Value) { carrier.contact_name = (string)reader["contact_name"]; }
                        if (reader["contact_phone"] != DBNull.Value) { carrier.contact_phone = (string)reader["contact_phone"]; }
                        if (reader["contact_email"] != DBNull.Value) { carrier.contact_email = (string)reader["contact_email"]; }
                        if (reader["in_network"] != DBNull.Value) { carrier.in_network = (bool)reader["in_network"]; }
                        if (reader["rejected"] != DBNull.Value) { carrier.rejected = (bool)reader["rejected"]; }
                        if (reader["carrierLevel"] != DBNull.Value) { carrier.carrierLevel = (int)reader["carrierLevel"]; }
                        if (reader["book_it_now"] != DBNull.Value) { carrier.book_it_now = (bool)reader["book_it_now"]; }
                        if (reader["truck_numbers_range"] != DBNull.Value) { carrier.truck_numbers_range = (int)reader["truck_numbers_range"]; }
                        if (reader["truck_numbers"] != DBNull.Value) { carrier.truck_numbers = (int)reader["truck_numbers"]; }
                        if (reader["NumberofTrucks"] != DBNull.Value) { carrier.NumberofTrucks = (string)reader["NumberofTrucks"]; }
                        if (reader["DateAdded"] != DBNull.Value) { carrier.DateAdded = (DateTime)reader["DateAdded"]; }
                        if (reader["LastEvent"] != DBNull.Value) { carrier.LastEvent = (string)reader["LastEvent"]; }
                        if (reader["LastUpdate"] != DBNull.Value) { carrier.LastUpdate = (DateTime)reader["LastUpdate"]; }

                        if (reader["VectorID"] != DBNull.Value) { carrier.VectorID = (int)reader["VectorID"]; }

                    }

                }
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataCarriers.TT.GetCarrierByVectorID");
            }


            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return carrier;

        }


        public int InsertCarrier(OTR_API.TruckerTools.Models.Carrier carrier)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "spCarrier_Insert";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;

            cd.Parameters.AddWithValue("@carrier_name", carrier.carrier_name);
            cd.Parameters.AddWithValue("@mc", carrier.mc);
            cd.Parameters.AddWithValue("@dot", carrier.dot);
            cd.Parameters.AddWithValue("@scac", carrier.scac);
            cd.Parameters.AddWithValue("@external_id", carrier.external_id);
            cd.Parameters.AddWithValue("@contact_name", carrier.contact_name);
            cd.Parameters.AddWithValue("@contact_phone", carrier.contact_phone);
            cd.Parameters.AddWithValue("@in_network", carrier.in_network);
            cd.Parameters.AddWithValue("@rejected", carrier.rejected);
            cd.Parameters.AddWithValue("@carrierLevel", carrier.carrierLevel);
            cd.Parameters.AddWithValue("@book_it_now", carrier.book_it_now);
            cd.Parameters.AddWithValue("@truck_numbers_range", carrier.truck_numbers_range);
            cd.Parameters.AddWithValue("@truck_numbers", carrier.truck_numbers);
            cd.Parameters.AddWithValue("@NumberofTrucks", carrier.NumberofTrucks);
            cd.Parameters.AddWithValue("@VectorID", carrier.VectorID);

            SqlParameter outputparm = new SqlParameter("@responseMessage", SqlDbType.Int)
            {
                Direction = ParameterDirection.Output
            };

            cd.Parameters.Add(outputparm);

            try
            {
                cd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataCheckCalls.TT.InsertCarrier");
            }

            int results = Convert.ToInt32(outputparm.Value);

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return results;
        }

        public int UpdateCarrier(OTR_API.TruckerTools.Models.Carrier carrier)
        {
            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "spCarrier_Update";
            SqlCommand cd = new SqlCommand(strsql, cn);

            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@ID", carrier.ID);
            cd.Parameters.AddWithValue("@carrier_name", carrier.carrier_name);
            cd.Parameters.AddWithValue("@mc", carrier.mc);
            cd.Parameters.AddWithValue("@dot", carrier.dot);
            cd.Parameters.AddWithValue("@scac", carrier.scac);
            cd.Parameters.AddWithValue("@external_id", carrier.external_id);
            cd.Parameters.AddWithValue("@contact_name", carrier.contact_name);
            cd.Parameters.AddWithValue("@contact_phone", carrier.contact_phone);
            cd.Parameters.AddWithValue("@in_network", carrier.in_network);
            cd.Parameters.AddWithValue("@rejected", carrier.rejected);
            cd.Parameters.AddWithValue("@carrierLevel", carrier.carrierLevel);
            cd.Parameters.AddWithValue("@book_it_now", carrier.book_it_now);
            cd.Parameters.AddWithValue("@truck_numbers_range", carrier.truck_numbers_range);
            cd.Parameters.AddWithValue("@truck_numbers", carrier.truck_numbers);
            cd.Parameters.AddWithValue("@NumberofTrucks", carrier.NumberofTrucks);

            SqlParameter outputparm = new SqlParameter("@responseMessage", SqlDbType.Int)
            {
                Direction = ParameterDirection.Output
            };

            cd.Parameters.Add(outputparm);

            try
            {
                cd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataCheckCalls.TT.UpdateCarrier");
            }

            int results = Convert.ToInt32(outputparm.Value);

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return results;
        }

        public int UpdateBookNowCarrier(OTR_API.TruckerTools.Models.Carrier carrier)
        {
            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "spCarrierBookNow_Update";
            SqlCommand cd = new SqlCommand(strsql, cn);

            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@VectorID", carrier.VectorID);
            cd.Parameters.AddWithValue("@book_it_now", carrier.book_it_now);

            SqlParameter outputparm = new SqlParameter("@responseMessage", SqlDbType.Int)
            {
                Direction = ParameterDirection.Output
            };

            cd.Parameters.Add(outputparm);

            try
            {
                cd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataCheckCalls.TT.BookNowCarrier");
            }

            int results = Convert.ToInt32(outputparm.Value);

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return results;
        }

        public bool DeleteCarrier(OTR_API.TruckerTools.Models.Carrier carrier)
        {
            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "spCarrier_Delete";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;

            cd.Parameters.AddWithValue("@VectorID", carrier.VectorID);


            bool i = false;
            try
            {
                i = (cd.ExecuteNonQuery() == 1);
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.DeleteCarrier");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return i;
        }


    }

    public class DataRateConfirmMatch : DataAccess
    {
        public int InsertRateConfirm(OTR_API.TruckerTools.Models.RateConfirm rateconfirm)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTTRate"].ConnectionString); cn.Open();

            string strsql = "spRateConfirm_Insert";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;

            cd.Parameters.AddWithValue("@RateConfirmID", rateconfirm.RateConfirmID);
            cd.Parameters.AddWithValue("@LoadID", rateconfirm.LoadID);
	        cd.Parameters.AddWithValue("@TripID", rateconfirm.TripID);
            cd.Parameters.AddWithValue("@CarrierName", rateconfirm.CarrierName);
	        cd.Parameters.AddWithValue("@CarrierCS", rateconfirm.CarrierCS);
	        cd.Parameters.AddWithValue("@PUCS", rateconfirm.PUCS);
	        cd.Parameters.AddWithValue("@PUDate", rateconfirm.PUDate);
	        cd.Parameters.AddWithValue("@PUFrom", rateconfirm.PUFrom);
	        cd.Parameters.AddWithValue("@PUTo", rateconfirm.PUTo);
	        cd.Parameters.AddWithValue("@DeliveryCS", rateconfirm.DeliveryCS);
	        cd.Parameters.AddWithValue("@DeliveryDate", rateconfirm.DeliveryDate);
	        cd.Parameters.AddWithValue("@DeliveryFrom", rateconfirm.DeliveryFrom);
	        cd.Parameters.AddWithValue("@DeliveryTo", rateconfirm.DeliveryTo);
	        cd.Parameters.AddWithValue("@CarrierPay", rateconfirm.CarrierPay);
	        cd.Parameters.AddWithValue("@PUPay", rateconfirm.PUPay);
	        cd.Parameters.AddWithValue("@DeliveryPay", rateconfirm.DeliveryPay);
	        cd.Parameters.AddWithValue("@LoadPay", rateconfirm.LoadPay);
	        cd.Parameters.AddWithValue("@TruckPay", rateconfirm.TruckPay);
	        cd.Parameters.AddWithValue("@Commodity", rateconfirm.Commodity);
	        cd.Parameters.AddWithValue("@Weight", rateconfirm.Weight);
	        cd.Parameters.AddWithValue("@Pallets", rateconfirm.Pallets);
	        cd.Parameters.AddWithValue("@Equipment", rateconfirm.Equipment);
	        cd.Parameters.AddWithValue("@Temp", rateconfirm.Temp);
	        cd.Parameters.AddWithValue("@CarrierContact", rateconfirm.CarrierContact);
	        cd.Parameters.AddWithValue("@VectorRep", rateconfirm.VectorRep);
	        cd.Parameters.AddWithValue("@Pieces", rateconfirm.Pieces);
	        cd.Parameters.AddWithValue("@Faxnumber", rateconfirm.Faxnumber);
	        cd.Parameters.AddWithValue("@Greeting", rateconfirm.Greeting);
	        cd.Parameters.AddWithValue("@RateConfirmDate", rateconfirm.RateConfirmDate);
            cd.Parameters.AddWithValue("@MiscPay", rateconfirm.MiscPay);
	        cd.Parameters.AddWithValue("@UsePortal", rateconfirm.UsePortal);
            cd.Parameters.AddWithValue("@RepEmail", rateconfirm.RepEmail);
	        cd.Parameters.AddWithValue("@CarrierEmail", rateconfirm.CarrierEmail);
	        cd.Parameters.AddWithValue("@CarrierID", rateconfirm.CarrierID);
            cd.Parameters.AddWithValue("@Reason", rateconfirm.Reason);
	        cd.Parameters.AddWithValue("@ConfirmType", rateconfirm.ConfirmType);
	        cd.Parameters.AddWithValue("@AssistPay", rateconfirm.AssistPay);
	        cd.Parameters.AddWithValue("@LayoverPay", rateconfirm.LayoverPay);
	        cd.Parameters.AddWithValue("@RCVersion", rateconfirm.RCVersion);
	        cd.Parameters.AddWithValue("@PUCS2", rateconfirm.PUCS2);
	        cd.Parameters.AddWithValue("@PUDate2", rateconfirm.PUDate2);
	        cd.Parameters.AddWithValue("@PUFrom2", rateconfirm.PUFrom2);
	        cd.Parameters.AddWithValue("@PUTo2", rateconfirm.PUTo2);
	        cd.Parameters.AddWithValue("@PUCS3", rateconfirm.PUCS3);
	        cd.Parameters.AddWithValue("@PUDate3", rateconfirm.PUDate3);
	        cd.Parameters.AddWithValue("@PUFrom3", rateconfirm.PUFrom3);
	        cd.Parameters.AddWithValue("@PUTo3", rateconfirm.PUTo3);
	        cd.Parameters.AddWithValue("@DeliveryCS2", rateconfirm.DeliveryCS2);
	        cd.Parameters.AddWithValue("@DeliveryDate2", rateconfirm.DeliveryDate2);
	        cd.Parameters.AddWithValue("@DeliveryFrom2", rateconfirm.DeliveryFrom2);
	        cd.Parameters.AddWithValue("@DeliveryTo2", rateconfirm.DeliveryTo2);
	        cd.Parameters.AddWithValue("@DeliveryCS3", rateconfirm.DeliveryCS3);
	        cd.Parameters.AddWithValue("@DeliveryDate3", rateconfirm.DeliveryDate3);
	        cd.Parameters.AddWithValue("@DeliveryFrom3", rateconfirm.DeliveryFrom3);
	        cd.Parameters.AddWithValue("@DeliveryTo3", rateconfirm.DeliveryTo3);
	        cd.Parameters.AddWithValue("@Instructions", rateconfirm.Instructions);

            int i = 0;

            try
            {
                i = cd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.InsertRateConfirm");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            try
            {
                InsertLoadDetails(rateconfirm.LoadDetails);
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.InsertRateConfirm");
            }

            return i;
        }

        public int InsertRateConfirmResponse(OTR_API.TruckerTools.Models.RateConfirmResponse rateconfrimResponse)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTTRate"].ConnectionString); cn.Open();

            string strsql = "spRateConfirmResponse_Insert";

            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;

            cd.Parameters.AddWithValue("@RateConfirmID", rateconfrimResponse.RateConfirmID);
            cd.Parameters.AddWithValue("@ResponseDate", rateconfrimResponse.ResponseDate);
            cd.Parameters.AddWithValue("@Status", rateconfrimResponse.Status);
            cd.Parameters.AddWithValue("@Message", rateconfrimResponse.Message);

            SqlParameter outputparm = new SqlParameter("@responseMessage", SqlDbType.Int)
            {
                Direction = ParameterDirection.Output
            };

            cd.Parameters.Add(outputparm);

            try
            {
                cd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.InsertCarrierResponse");
            }

            int results = Convert.ToInt32(outputparm.Value);

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return results;

        }

        public void InsertLoadDetails(List<OTR_API.TruckerTools.Models.LoadDetail> list)
        {
            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTTRate"].ConnectionString); 

            foreach (OTR_API.TruckerTools.Models.LoadDetail ld in list)
            {
                cn.Open();

                string strsql = "spLoadDetail_Insert";
                SqlCommand cd = new SqlCommand(strsql, cn);
                cd.CommandType = CommandType.StoredProcedure;

                cd.Parameters.AddWithValue("@LoadDetailID", ld.LoadDetailID);

                cd.Parameters.AddWithValue("@LoadID", ld.LoadID);
                cd.Parameters.AddWithValue("@LoadStopNumber", ld.LoadStopNumber);
                cd.Parameters.AddWithValue("@StopType", ld.StopType);
                cd.Parameters.AddWithValue("@TripID", ld.TripID);
                cd.Parameters.AddWithValue("@LoadStopReferenceID", ld.LoadStopReferenceID);
                cd.Parameters.AddWithValue("@LoadStopAddressID", ld.LoadStopAddressID);
                cd.Parameters.AddWithValue("@ScheduleDate", ld.ScheduleDate);
                cd.Parameters.AddWithValue("@ScheduleTime", ld.ScheduleTime);
                cd.Parameters.AddWithValue("@ScheduleTime2", ld.ScheduleTime2);
                cd.Parameters.AddWithValue("@ActualDate", ld.ActualDate);
                cd.Parameters.AddWithValue("@ActualTime", ld.ActualTime);
                cd.Parameters.AddWithValue("@Name", ld.Name);
                cd.Parameters.AddWithValue("@Address1", ld.Address1);
                cd.Parameters.AddWithValue("@Address2", ld.Address2);
                cd.Parameters.AddWithValue("@City", ld.City);
                cd.Parameters.AddWithValue("@State", ld.State);
                cd.Parameters.AddWithValue("@Zip", ld.Zip);
                cd.Parameters.AddWithValue("@County", ld.County);
                cd.Parameters.AddWithValue("@Phone", ld.Phone);
                cd.Parameters.AddWithValue("@Contact", ld.Contact);
                cd.Parameters.AddWithValue("@AlternatePhone", ld.AlternatePhone);
                cd.Parameters.AddWithValue("@EmailAddress", ld.EmailAddress);
                cd.Parameters.AddWithValue("@Weight", ld.Weight);
                cd.Parameters.AddWithValue("@Units", ld.Units);
                cd.Parameters.AddWithValue("@Pieces", ld.Pieces);
                cd.Parameters.AddWithValue("@CommodityDesc", ld.CommodityDesc);
                cd.Parameters.AddWithValue("@DateCreated", ld.DateCreated);
                cd.Parameters.AddWithValue("@CreatedByID", ld.CreatedByID);
                cd.Parameters.AddWithValue("@DateModified", ld.DateModified);
                cd.Parameters.AddWithValue("@ModifiedByID", ld.ModifiedByID);
                cd.Parameters.AddWithValue("@Lat", ld.Lat);
                cd.Parameters.AddWithValue("@Long", ld.Long);
                cd.Parameters.AddWithValue("@CoordByAddress", ld.CoordByAddress);
                cd.Parameters.AddWithValue("@CoverageAmt", ld.CoverageAmt);

                int i = 0;

                try
                {
                    i = cd.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.InsertRateConfirm");
                }

                if (cn.State != ConnectionState.Closed) { cn.Close(); }

                InsertLoadDetailRefNumbers(ld.LoadDetailRefNumbers);

            }


        }

        public void InsertLoadDetailRefNumbers(List<OTR_API.TruckerTools.Models.LoadDetailRefNumber> list)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTTRate"].ConnectionString); cn.Open();

            foreach (OTR_API.TruckerTools.Models.LoadDetailRefNumber ld in list)
            {
                string strsql = "spLoadDetailRefNumber_Insert";
                SqlCommand cd = new SqlCommand(strsql, cn);
                cd.CommandType = CommandType.StoredProcedure;

                cd.Parameters.AddWithValue("@ReferenceNumberID", ld.ReferenceNumberID);
                cd.Parameters.AddWithValue("@LoadDetailId", ld.LoadDetailId);
                cd.Parameters.AddWithValue("@LoadId", ld.LoadId);
                cd.Parameters.AddWithValue("@ReferenceNumber", ld.ReferenceNumber);
                cd.Parameters.AddWithValue("@ReferenceNumberTypeID", ld.ReferenceNumberTypeID);
                cd.Parameters.AddWithValue("@DateAdded", ld.DateAdded);
                cd.Parameters.AddWithValue("@AddedByID", ld.AddedByID);
                cd.Parameters.AddWithValue("@DateModified", ld.DateModified);
                cd.Parameters.AddWithValue("@ModifiedByID", ld.ModifiedByID);

                int i = 0;

                try
                {
                    i = cd.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.InsertRateConfirm");
                }

            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }
        }
    }

    public class DataTruckerToolsMatch : DataAccess
    {
        public int InsertCarrierResponse(OTR_API.TruckerTools.Models.CarrierResponse carrierResponse)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "spCarrierResponse_Insert";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;


            cd.Parameters.AddWithValue("@CarrierID", carrierResponse.CarrierID);
            cd.Parameters.AddWithValue("@ResponseDate", carrierResponse.ResponseDate);
            cd.Parameters.AddWithValue("@Status", carrierResponse.Status);
            cd.Parameters.AddWithValue("@Message", carrierResponse.Message);


            SqlParameter outputparm = new SqlParameter("@responseMessage", SqlDbType.Int)
            {
                Direction = ParameterDirection.Output
            };

            cd.Parameters.Add(outputparm);

            try
            {
                cd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.InsertCarrierResponse");
            }


            int results = Convert.ToInt32(outputparm.Value);

            if (cn.State != ConnectionState.Closed) { cn.Close(); }


            if (results > 0)
            {
                carrierResponse.ID = results;

                foreach (OTR_API.TruckerTools.Models.CarrierResponseDetail ls in carrierResponse.Details)
                {
                    ls.CarrierResponseID = carrierResponse.ID;
                    InsertCarrierResponseDetail(ls);
                }
            }

            return results;

        }

        public int InsertCarrierResponseDetail(OTR_API.TruckerTools.Models.CarrierResponseDetail carrierResponseDetail)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "spCarrierResponseDetail_Insert";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;


            cd.Parameters.AddWithValue("@CarrierResponseID", carrierResponseDetail.CarrierResponseID);
            cd.Parameters.AddWithValue("@Status", carrierResponseDetail.Status);
            cd.Parameters.AddWithValue("@Company", carrierResponseDetail.Company);
            cd.Parameters.AddWithValue("@external_id", carrierResponseDetail.carrier_ext_id);
            cd.Parameters.AddWithValue("@Message", carrierResponseDetail.Message);

            SqlParameter outputparm = new SqlParameter("@responseMessage", SqlDbType.Int)
            {
                Direction = ParameterDirection.Output
            };

            cd.Parameters.Add(outputparm);

            try
            {
                cd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.InsertCarrierResponseDetail");
            }


            int results = Convert.ToInt32(outputparm.Value);

            return results;

        }

        public int InsertLoadResponse(OTR_API.TruckerTools.Models.LoadResponse loadResponse)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "spLoadResponse_Insert";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;


            if (loadResponse.Message == null)
                loadResponse.Message = "";

            if (loadResponse.loadNumbers == null)
                loadResponse.loadNumbers = new string[] { "" };

            cd.Parameters.AddWithValue("@LoadID", loadResponse.LoadID);
            cd.Parameters.AddWithValue("@ResponseDate", loadResponse.ResponseDate > Convert.ToDateTime("1/1/2022") ? loadResponse.ResponseDate : DateTime.Now);
            cd.Parameters.AddWithValue("@Status", loadResponse.Status);
            cd.Parameters.AddWithValue("@Message", loadResponse.Message);
            cd.Parameters.AddWithValue("@LoadNumbers", String.Join(",",loadResponse.loadNumbers));


            SqlParameter outputparm = new SqlParameter("@responseMessage", SqlDbType.Int)
            {
                Direction = ParameterDirection.Output
            };

            cd.Parameters.Add(outputparm);

            try
            {
                cd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.InsertLoadResponse");
            }


            int results = Convert.ToInt32(outputparm.Value);

            if (cn.State != ConnectionState.Closed) { cn.Close(); }


            if (results > 0)
            {
                loadResponse.ID = results;

                if (loadResponse.Details != null)
                {
                    foreach (OTR_API.TruckerTools.Models.LoadResponseDetail ls in loadResponse.Details)
                    {
                        ls.LoadResponseID = loadResponse.ID;
                        InsertLoadResponseDetail(ls);
                    }
                }
            }

            return results;

        }

        public int InsertLoadResponseDetail(OTR_API.TruckerTools.Models.LoadResponseDetail loadResponseDetail)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "spLoadResponseDetail_Insert";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;


            cd.Parameters.AddWithValue("@LoadResponseID", loadResponseDetail.LoadResponseID);
            cd.Parameters.AddWithValue("@Status", loadResponseDetail.Status);
            cd.Parameters.AddWithValue("@LoadNumber", loadResponseDetail.LoadNumber);
            cd.Parameters.AddWithValue("@Message", loadResponseDetail.Message);

            SqlParameter outputparm = new SqlParameter("@responseMessage", SqlDbType.Int)
            {
                Direction = ParameterDirection.Output
            };

            cd.Parameters.Add(outputparm);

            try
            {
                cd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.InsertLoadResponse");
            }


            int results = Convert.ToInt32(outputparm.Value);

            return results;

        }


    }

    public class DataTruckerToolsTracking
    {
        public int InsertLoadResponse(OTR_API.TruckerToolsTracking.Models.TrackingResponse loadResponse)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "spTrackingResponse_Insert";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;

            if (loadResponse.response.Message == null)
                loadResponse.response.Message = "";

            cd.Parameters.AddWithValue("@LoadID", loadResponse.response.loadID);
            cd.Parameters.AddWithValue("@TrackingID", loadResponse.response.TrackingID);
            cd.Parameters.AddWithValue("@ResponseDate", loadResponse.response.ResponseDate > Convert.ToDateTime("1/1/2022") ? loadResponse.response.ResponseDate : DateTime.Now);
            cd.Parameters.AddWithValue("@Status", loadResponse.response.status);
            cd.Parameters.AddWithValue("@TimeStamp", loadResponse.response.timeStamp);
            cd.Parameters.AddWithValue("@MapLink", loadResponse.response.mapLink);
            cd.Parameters.AddWithValue("@carrierLink", loadResponse.response.carrierLink);
            cd.Parameters.AddWithValue("@shipperLink", loadResponse.response.shipperLink);
            cd.Parameters.AddWithValue("@StatusPageLink", loadResponse.response.statusPageLink);
            cd.Parameters.AddWithValue("@DetailsLink", loadResponse.response.detailsLink);
            cd.Parameters.AddWithValue("@DetailsLinkNoAuth", loadResponse.response.detailsLinkNoAuth);
            cd.Parameters.AddWithValue("@TrackingMethod", loadResponse.response.trackingMethod);
            cd.Parameters.AddWithValue("@ErrorCode", loadResponse.response.ErrorCode);
            cd.Parameters.AddWithValue("@ErrorMessage", loadResponse.response.ErrorMessage);
            cd.Parameters.AddWithValue("@Message", loadResponse.response.Message);

            SqlParameter outputparm = new SqlParameter("@responseMessage", SqlDbType.Int)
            {
                Direction = ParameterDirection.Output
            };

            cd.Parameters.Add(outputparm);

            try
            {
                cd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.InsertLoadResponse");
            }


            int results = Convert.ToInt32(outputparm.Value);

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return results;

        }

        public int InsertLoadTracking(OTR_API.TruckerToolsTracking.Models.Load load)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "spTracking_Insert";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;


            //cd.Parameters.AddWithValue("@partnerId", load.partnerId);
            //cd.Parameters.AddWithValue("@accountId", load.accountId);
            cd.Parameters.AddWithValue("@loadTrackExternalId", load.loadTrackExternalId);
            cd.Parameters.AddWithValue("@loadNumber", load.loadNumber);
            cd.Parameters.AddWithValue("@dispatcherId", load.dispatcherId);
            cd.Parameters.AddWithValue("@dispatcherEmail", load.dispatcherEmail);
            cd.Parameters.AddWithValue("@dispatcherPhoneNumber", load.dispatcherPhoneNumber);
            cd.Parameters.AddWithValue("@textmessage", load.textmessage);
            cd.Parameters.AddWithValue("@loadType", load.loadType);
            cd.Parameters.AddWithValue("@trailerType", load.trailerType);

            cd.Parameters.AddWithValue("@driverCell", load.driverCell);
            cd.Parameters.AddWithValue("@trailerNumber", load.trailerNumber);
            cd.Parameters.AddWithValue("@truckNumber", load.truckNumber);
            cd.Parameters.AddWithValue("@driverName", load.driverName);
            cd.Parameters.AddWithValue("@driverType", load.driverType);
            cd.Parameters.AddWithValue("@driverComments", load.driverComments);
            cd.Parameters.AddWithValue("@loadNotes", load.loadNotes);
            cd.Parameters.AddWithValue("@isTeamLoad", load.isTeamLoad);
            cd.Parameters.AddWithValue("@carrierDispatcherEmail", load.carrierDispatcherEmail);
            cd.Parameters.AddWithValue("@ShipmentID", (load.shipper != null && load.shipper.loadNumber != null ? load.shipper.loadNumber : ""));
            cd.Parameters.AddWithValue("@VectorID", load.VectorID);
            cd.Parameters.AddWithValue("@BillToID", load.BillToID);

            SqlParameter outputparm = new SqlParameter("@responseMessage", SqlDbType.Int)
            {
                Direction = ParameterDirection.Output
            };

            cd.Parameters.Add(outputparm);

            try
            {
                cd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.InsertLoadTracking");
            }


            int results = Convert.ToInt32(outputparm.Value);


            if (cn.State != ConnectionState.Closed) { cn.Close(); }


            if (results > 0)
            {
                load.ID = results;
                if (load.metadata != null)
                {
                    if (load.metadata.Count > 0)
                    {
                        foreach (OTR_API.TruckerToolsTracking.Models.Metadata le in load.metadata)
                        {
                            le.TrackingID = load.ID;
                            InsertTrackingMetadata(le);
                        }
                    }
                }

                if (load.stops != null)
                {
                    if (load.stops.Count > 0)
                    {
                        foreach (OTR_API.TruckerToolsTracking.Models.Stop ls in load.stops)
                        {
                            ls.TrackingID = load.ID;
                            ls.loadNumber = Convert.ToInt32(load.loadNumber);
                            int stopID = InsertTrackingStop(ls);
                        }
                    }
                }


                if (load.broker != null)
                {
                    load.broker.TrackingID = load.ID;
                    int BrokerID = InsertTrackingBroker(load.broker);
                    load.broker.ID = BrokerID;
                }

                if (load.carrier != null)
                {
                    load.carrier.TrackingID = load.ID;
                    int CarrierID = InsertTrackingCarrier(load.carrier);
                    load.carrier.ID = CarrierID;
                }

                if (load.shipper != null && load.shipper.shipperId != null && load.shipper.shipperId.Length > 0)
                {
                    load.shipper.TrackingID = load.ID;
                    int ShipperID = InsertTrackingShipper(load.shipper);
                    load.shipper.ID = ShipperID;
                }

               


            }

            return results;

        }

        public int InsertTrackingMetadata(OTR_API.TruckerToolsTracking.Models.Metadata meta)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "spTrackingMetadata_Insert";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;

            cd.Parameters.AddWithValue("@TrackingID", meta.TrackingID);
            cd.Parameters.AddWithValue("@name", meta.name);
            cd.Parameters.AddWithValue("@value", meta.value);

            SqlParameter outputparm = new SqlParameter("@responseMessage", SqlDbType.Int)
            {
                Direction = ParameterDirection.Output
            };

            cd.Parameters.Add(outputparm);

            try
            {
                cd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.InsertTrackingMetadata");
            }


            int results = Convert.ToInt32(outputparm.Value);

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return results;

        }


        public int InsertTrackingBroker(OTR_API.TruckerToolsTracking.Models.Broker broker)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "spTrackingBroker_Insert";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;

            cd.Parameters.AddWithValue("@TrackingID", broker.TrackingID);
            cd.Parameters.AddWithValue("@companyName", broker.companyName);
            cd.Parameters.AddWithValue("@docketNumber", broker.docketNumber);
            cd.Parameters.AddWithValue("@contactName", broker.contactName);
            cd.Parameters.AddWithValue("@contactPhone", broker.contactPhone);
            cd.Parameters.AddWithValue("@contactPhoneExt", broker.contactPhoneExt);
            cd.Parameters.AddWithValue("@contactEmail", broker.contactEmail);


            SqlParameter outputparm = new SqlParameter("@responseMessage", SqlDbType.Int)
            {
                Direction = ParameterDirection.Output
            };

            cd.Parameters.Add(outputparm);

            try
            {
                cd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.InsertTrackingBroker");
            }


            int results = Convert.ToInt32(outputparm.Value);

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return results;

        }

        public int InsertTrackingCarrier(OTR_API.TruckerToolsTracking.Models.Carrier carrier)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "spTrackingCarrier_Insert";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;

            cd.Parameters.AddWithValue("@TrackingID", carrier.TrackingID);
            cd.Parameters.AddWithValue("@companyName", carrier.companyName);
            cd.Parameters.AddWithValue("@docketNumber", carrier.docketNumber);
            cd.Parameters.AddWithValue("@contactName", carrier.contactName);
            cd.Parameters.AddWithValue("@contactPhone", carrier.contactPhone);
            cd.Parameters.AddWithValue("@contactPhoneExt", carrier.contactPhoneExt);
            cd.Parameters.AddWithValue("@contactEmail", carrier.contactEmail != null ? carrier.contactEmail : "");


            SqlParameter outputparm = new SqlParameter("@responseMessage", SqlDbType.Int)
            {
                Direction = ParameterDirection.Output
            };

            cd.Parameters.Add(outputparm);

            try
            {
                cd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.InsertTrackingCarrier");
            }


            int results = Convert.ToInt32(outputparm.Value);

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return results;

        }

        public int InsertTrackingShipper(OTR_API.TruckerToolsTracking.Models.Shipper shipper)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "spTrackingShipper_Insert";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;

            cd.Parameters.AddWithValue("@TrackingID", shipper.TrackingID);
            cd.Parameters.AddWithValue("@shipperid", shipper.shipperId);
            cd.Parameters.AddWithValue("@loadNumber", shipper.loadNumber);
            cd.Parameters.AddWithValue("@emails", shipper.emails);
            cd.Parameters.AddWithValue("@emailInterval", shipper.emailInterval);
            cd.Parameters.AddWithValue("@referenceNumber", shipper.referenceNumber);

            SqlParameter outputparm = new SqlParameter("@responseMessage", SqlDbType.Int)
            {
                Direction = ParameterDirection.Output
            };

            cd.Parameters.Add(outputparm);

            try
            {
                cd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.InsertTrackingShipper");
            }


            int results = Convert.ToInt32(outputparm.Value);

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return results;

        }


        public int InsertTrackingStop(OTR_API.TruckerToolsTracking.Models.Stop stop)
        {

            int TSresults = 0;

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "spTrackingStop_Insert";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;

            cd.Parameters.AddWithValue("@TrackingID", stop.TrackingID);
            cd.Parameters.AddWithValue("@loadNumber", stop.loadNumber);
            cd.Parameters.AddWithValue("@orderNumber", stop.orderNumber);
            cd.Parameters.AddWithValue("@address", stop.address);
            cd.Parameters.AddWithValue("@city", stop.city);
            cd.Parameters.AddWithValue("@state", stop.state);
            cd.Parameters.AddWithValue("@zipcode", stop.zipcode);
            cd.Parameters.AddWithValue("@lat", stop.lat);
            cd.Parameters.AddWithValue("@lon", stop.lon);
            cd.Parameters.AddWithValue("@datetime", stop.datetime);
            cd.Parameters.AddWithValue("@datetimeExit", stop.datetimeExit);
            cd.Parameters.AddWithValue("@geofenceRadius", stop.geofenceRadius);
            cd.Parameters.AddWithValue("@notes", stop.notes);
            cd.Parameters.AddWithValue("@stopExternalId", stop.stopExternalId);


            SqlParameter outputparm = new SqlParameter("@responseMessage", SqlDbType.Int)
            {
                Direction = ParameterDirection.Output
            };

            cd.Parameters.Add(outputparm);

            try
            {
                cd.ExecuteNonQuery();
                TSresults = Convert.ToInt32(outputparm.Value);

                if(TSresults > 0)
                {
                    stop.ID = TSresults;
                    if (stop.actions != null)
                    {
                        if (stop.actions.Count > 0)
                        {
                            foreach (OTR_API.TruckerToolsTracking.Models.Action la in stop.actions)
                            {
                                la.TrackingStopID = stop.ID;
                                int ActionID = InsertTrackingStopAction(la);
                            }
                        }
                    }

                    if (stop.metadata != null)
                    {
                        if (stop.metadata.Count > 0)
                        {
                            foreach (OTR_API.TruckerToolsTracking.Models.Metadata lm in stop.metadata)
                            {
                                lm.TrackingStopID = stop.ID;
                                int MetaID = InsertTrackingStopMetadata(lm);
                            }
                        }
                    }
                }


            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.InsertTrackingStop");
            }


           

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return TSresults;

        }

        public int InsertTrackingStopAction(OTR_API.TruckerToolsTracking.Models.Action action)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "spTrackingStopAction_Insert";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;

            cd.Parameters.AddWithValue("@TrackingStopID", action.TrackingStopID);
            cd.Parameters.AddWithValue("@id", action.id);
            cd.Parameters.AddWithValue("@name", action.name);
            cd.Parameters.AddWithValue("@item", action.item);
            cd.Parameters.AddWithValue("@isLastAction", action.isLastAction);
            cd.Parameters.AddWithValue("@required", action.required);
            cd.Parameters.AddWithValue("@driverInput", action.driverInput);

            SqlParameter outputparm = new SqlParameter("@responseMessage", SqlDbType.Int)
            {
                Direction = ParameterDirection.Output
            };

            cd.Parameters.Add(outputparm);

            try
            {
                cd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.InsertTrackingStopAction");
            }


            int results = Convert.ToInt32(outputparm.Value);

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return results;

        }

        public int InsertTrackingStopMetadata(OTR_API.TruckerToolsTracking.Models.Metadata meta)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "spTrackingStopMetadata_Insert";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;

            cd.Parameters.AddWithValue("@TrackingStopID", meta.TrackingStopID);
            cd.Parameters.AddWithValue("@name", meta.name);
            cd.Parameters.AddWithValue("@value", meta.value);

            SqlParameter outputparm = new SqlParameter("@responseMessage", SqlDbType.Int)
            {
                Direction = ParameterDirection.Output
            };

            cd.Parameters.Add(outputparm);

            try
            {
                cd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.InsertTrackingStopMetadata");
            }


            int results = Convert.ToInt32(outputparm.Value);

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return results;

        }

        public int InsertTrackingCancellation(OTR_API.TruckerToolsTracking.Models.Load load)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "spTrackingCancelled_Insert";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;

            cd.Parameters.AddWithValue("@VectorID", load.VectorID);
            //cd.Parameters.AddWithValue("@partnerid", load.partnerId);
            cd.Parameters.AddWithValue("@loadTrackExternalId", load.loadTrackExternalId);
            //cd.Parameters.AddWithValue("@accountId", load.accountId);

            SqlParameter outputparm = new SqlParameter("@responseMessage", SqlDbType.Int)
            {
                Direction = ParameterDirection.Output
            };

            cd.Parameters.Add(outputparm);

            try
            {
                cd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.InsertTrackingCancellation");
            }


            int results = Convert.ToInt32(outputparm.Value);

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return results;

        }


        public int UpdateLoadTracking(OTR_API.TruckerToolsTracking.Models.Load load)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "spTracking_Insert";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;


            //cd.Parameters.AddWithValue("@partnerId", load.partnerId);
            //cd.Parameters.AddWithValue("@accountId", load.accountId);
            cd.Parameters.AddWithValue("@loadTrackExternalId", load.loadTrackExternalId);
            cd.Parameters.AddWithValue("@loadNumber", load.loadNumber);
            cd.Parameters.AddWithValue("@dispatcherId", load.dispatcherId);
            cd.Parameters.AddWithValue("@dispatcherEmail", load.dispatcherEmail);
            cd.Parameters.AddWithValue("@dispatcherPhoneNumber", load.dispatcherPhoneNumber);
            cd.Parameters.AddWithValue("@textmessage", load.textmessage);
            cd.Parameters.AddWithValue("@loadType", load.loadType);
            cd.Parameters.AddWithValue("@trailerType", load.trailerType);

            cd.Parameters.AddWithValue("@driverCell", load.driverCell);
            cd.Parameters.AddWithValue("@trailerNumber", load.trailerNumber);
            cd.Parameters.AddWithValue("@truckNumber", load.truckNumber);
            cd.Parameters.AddWithValue("@driverName", load.driverName);
            cd.Parameters.AddWithValue("@driverType", load.driverType);
            cd.Parameters.AddWithValue("@driverComments", load.driverComments);
            cd.Parameters.AddWithValue("@loadNotes", load.loadNotes);
            cd.Parameters.AddWithValue("@isTeamLoad", load.isTeamLoad);
            cd.Parameters.AddWithValue("@carrierDispatcherEmail", load.carrierDispatcherEmail);

            cd.Parameters.AddWithValue("@ShipmentID", (load.shipper != null && load.shipper.loadNumber != null ? load.shipper.loadNumber : ""));
            cd.Parameters.AddWithValue("@VectorID", load.VectorID);
            cd.Parameters.AddWithValue("@BillToID", load.BillToID);


            SqlParameter outputparm = new SqlParameter("@responseMessage", SqlDbType.Int)
            {
                Direction = ParameterDirection.Output
            };

            cd.Parameters.Add(outputparm);

            try
            {
                cd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.InsertLoadTracking");
            }


            int results = Convert.ToInt32(outputparm.Value);


            if (cn.State != ConnectionState.Closed) { cn.Close(); }


            if (results > 0)
            {
                load.ID = results;
                if (load.metadata != null)
                {
                    if (load.metadata.Count > 0)
                    {
                        foreach (OTR_API.TruckerToolsTracking.Models.Metadata le in load.metadata)
                        {
                            le.TrackingID = load.ID;
                            InsertTrackingMetadata(le);
                        }
                    }
                }

                if (load.stops != null)
                {
                    if (load.stops.Count > 0)
                    {
                        foreach (OTR_API.TruckerToolsTracking.Models.Stop ls in load.stops)
                        {
                            ls.TrackingID = load.ID;
                            ls.loadNumber = Convert.ToInt32(load.loadNumber);
                            int stopID = InsertTrackingStop(ls);
                        }
                    }
                }


                if (load.broker != null)
                {
                    load.broker.TrackingID = load.ID;
                    int BrokerID = InsertTrackingBroker(load.broker);
                    load.broker.ID = BrokerID;
                }

                if (load.carrier != null)
                {
                    load.carrier.TrackingID = load.ID;
                    int CarrierID = InsertTrackingCarrier(load.carrier);
                    load.carrier.ID = CarrierID;
                }

                if (load.shipper != null)
                {
                    load.shipper.TrackingID = load.ID;
                    int ShipperID = InsertTrackingShipper(load.shipper);
                    load.shipper.ID = ShipperID;
                }




            }

            return results;

        }


        public OTR_API.TruckerToolsTracking.Models.Load GetLoadTracking(OTR_API.TruckerToolsTracking.Models.Load load)
        {
            OTR_API.TruckerToolsTracking.Models.Load obj = new TruckerToolsTracking.Models.Load();

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            SqlCommand cd = new SqlCommand("spTrackingByLoadID_Get", cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@loadNumber", load.loadNumber);

            try
            {
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {

                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["VectorID"] != DBNull.Value) { obj.VectorID = (int)reader["VectorID"]; }
                        if (reader["BillToID"] != DBNull.Value) { obj.BillToID = (int)reader["BillToID"]; }

                        if (reader["ShipmentID"] != DBNull.Value)
                        {
                            // Hydrate shipper.loadNumber from Tracking.ShipmentID so downstream
                            // dispatch code can populate VendorEvent.ShipmentNumber.
                            if (obj.shipper == null) obj.shipper = new OTR_API.TruckerToolsTracking.Models.Shipper();
                            obj.shipper.loadNumber = (string)reader["ShipmentID"];
                            obj.shipper.ID = obj.BillToID; 
                        }

                        if (reader["loadTrackExternalId"] != DBNull.Value) { obj.loadTrackExternalId = (string)reader["loadTrackExternalId"]; }
                        if (reader["loadNumber"] != DBNull.Value) { obj.loadNumber = (string)reader["loadNumber"]; }
                        if (reader["dispatcherId"] != DBNull.Value) { obj.dispatcherId = (string)reader["dispatcherId"]; }
                        if (reader["dispatcherEmail"] != DBNull.Value) { obj.dispatcherEmail = (string)reader["dispatcherEmail"]; }
                        if (reader["dispatcherPhoneNumber"] != DBNull.Value) { obj.loadNumber = (string)reader["dispatcherPhoneNumber"]; }
                        if (reader["textmessage"] != DBNull.Value) { obj.loadNumber = (string)reader["textmessage"]; }
                        if (reader["loadType"] != DBNull.Value) { obj.loadType = (string)reader["loadType"]; }
                        if (reader["trailerType"] != DBNull.Value) { obj.trailerType = (string)reader["trailerType"]; }
                        if (reader["driverCell"] != DBNull.Value) { obj.driverCell = (string)reader["driverCell"]; }
                        if (reader["trailerNumber"] != DBNull.Value) { obj.trailerNumber = (string)reader["trailerNumber"]; }
                        if (reader["truckNumber"] != DBNull.Value) { obj.truckNumber = (string)reader["truckNumber"]; }
                        if (reader["driverName"] != DBNull.Value) { obj.driverName = (string)reader["driverName"]; }
                        if (reader["driverType"] != DBNull.Value) { obj.driverType = (string)reader["driverType"]; }
                        if (reader["driverComments"] != DBNull.Value) { obj.driverComments = (string)reader["driverComments"]; }
                        if (reader["isTeamLoad"] != DBNull.Value) { obj.isTeamLoad = (bool)reader["isTeamLoad"]; }
                        if (reader["carrierDispatcherEmail"] != DBNull.Value) { obj.carrierDispatcherEmail = (string)reader["carrierDispatcherEmail"]; }
                        
                    }
                }
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.GetLoadTracking");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

           
            return obj;
        }






        public OTR_API.TruckerToolsTracking.Models.Load AddLoadTrackingTimeZones(OTR_API.TruckerToolsTracking.Models.Load load)
        {
            foreach(OTR_API.TruckerToolsTracking.Models.Stop stop in load.stops)
            {
                string tz = TimeZoneByGeoCoord(stop, stop.datetime);
                stop.datetime += " " + tz;

                string tz2 = TimeZoneByGeoCoord(stop, stop.datetimeExit);
                stop.datetimeExit += " " + tz2;
            }

            return load;
        }

        public string TimeZoneByGeoCoord(OTR_API.TruckerToolsTracking.Models.Stop stop, string dte)
        {
            string tzIana = TimeZoneLookup.GetTimeZone(Convert.ToDouble(stop.lat), Convert.ToDouble(stop.lon)).Result;
            string TZResult = "";
            try
            {
                TimeZoneInfo tzInfo = TZConvert.GetTimeZoneInfo(tzIana);
                string strStandard = tzInfo.StandardName;
                string strDaylight = tzInfo.DaylightName;

                var abbreviations = TZNames.GetAbbreviationsForTimeZone(tzInfo.Id, "en-US");


                if (tzInfo.IsDaylightSavingTime(Convert.ToDateTime(dte)))
                {
                    TZResult = abbreviations.Daylight;
                }
                else
                {
                    TZResult = abbreviations.Standard;
                }
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.TimeZoneByZipcode");
                //textBox4.Text += "Geo Error: " + ex.Message + Environment.NewLine;
            }


            return TZResult;
             
        }

        //public string TimeZoneByZipcode(string zipcode)
        //{

        //    SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

        //    string strsql = "spZipcodeTimeZone_Get";
        //    SqlCommand cd = new SqlCommand(strsql, cn);
        //    cd.CommandType = CommandType.StoredProcedure;

        //    cd.Parameters.AddWithValue("@Zipcode", zipcode);

        //    string results = "";

        //    try
        //    {
        //        results = Convert.ToString(cd.ExecuteScalar());
        //    }
        //    catch (Exception ex)
        //    {
        //        OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.GetTimeZone");
        //    }

        //    if (cn.State != ConnectionState.Closed) { cn.Close(); }

        //    return results;
        //}





        public bool ValidateGeoCoordinate(string geo)
        {
            bool blngeo1 = false;

            if (geo.Length > 0)
            {
                decimal valuelat1;
                if (Decimal.TryParse(geo, out valuelat1))
                {
                    blngeo1 = true;
                }
            }

            return blngeo1;
        }

        public int InsertLoadTrackingStatus(OTR_API.TruckerToolsTracking.Models.StatusUpdate status)
        {
            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "spStatusLoadTracking_Insert";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;

            cd.Parameters.AddWithValue("@partnerid", status.partnerid);
            cd.Parameters.AddWithValue("@accountid", status.accountid);
            cd.Parameters.AddWithValue("@loadTrackExternalID", status.loadTrackExternalId);
            cd.Parameters.AddWithValue("@ltExternalId", status.ltExternalId);
            cd.Parameters.AddWithValue("@driverPhone", status.driverPhone);
            cd.Parameters.AddWithValue("@loadNumber", status.loadNumber);
            cd.Parameters.AddWithValue("@eventType", status.eventType);

            SqlParameter outputparm = new SqlParameter("@responseMessage", SqlDbType.Int)
            {
                Direction = ParameterDirection.Output
            };

            cd.Parameters.Add(outputparm);

            try
            {
                cd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.InsertTrackingUpdateStatus");
            }


            int results = Convert.ToInt32(outputparm.Value);

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return results;
        }

        public int InsertLoadTrackingStatusLocation(OTR_API.TruckerToolsTracking.Models.Location location)
        {
            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "spStatusLocation_Insert";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;

            cd.Parameters.AddWithValue("@lat", location.lat);
            cd.Parameters.AddWithValue("@lon", location.lon);
            cd.Parameters.AddWithValue("@accuracy", location.accuracy);
            cd.Parameters.AddWithValue("@timeStamp", location.timeStamp);
            cd.Parameters.AddWithValue("@timeStampSec", location.timeStampSec);
            cd.Parameters.AddWithValue("@city", location.city);
            cd.Parameters.AddWithValue("@state", location.state);
            cd.Parameters.AddWithValue("@country", location.country);
            cd.Parameters.AddWithValue("@AssociatedID", location.associatedId);
            cd.Parameters.AddWithValue("@type", location.type);

            SqlParameter outputparm = new SqlParameter("@responseMessage", SqlDbType.Int)
            {
                Direction = ParameterDirection.Output
            };

            cd.Parameters.Add(outputparm);

            try
            {
                cd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.InsertTrackingLocation");
            }


            int results = Convert.ToInt32(outputparm.Value);

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return results;

        }

        public int InsertLoadTrackingStatusInfo(OTR_API.TruckerToolsTracking.Models.Status status)
        {
            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "spStatusStatus_Insert";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;

  
            cd.Parameters.AddWithValue("@name", status.name);
            cd.Parameters.AddWithValue("@code", status.code);
            cd.Parameters.AddWithValue("@timeStamp", status.timeStamp);
            cd.Parameters.AddWithValue("@timeStampSec", status.timeStampSec);
            cd.Parameters.AddWithValue("@associatedId", status.associatedId);
            cd.Parameters.AddWithValue("@type", status.type);

            SqlParameter outputparm = new SqlParameter("@responseMessage", SqlDbType.Int)
            {
                Direction = ParameterDirection.Output
            };

            cd.Parameters.Add(outputparm);

            try
            {
                cd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.InsertTrackingStatus");
            }


            int results = Convert.ToInt32(outputparm.Value);

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return results;

        }

        public int InsertLoadTrackingStatusResponse(OTR_API.TruckerToolsTracking.Models.StatusResponse response)
        {
            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "spStatusResponse_Insert";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;

            cd.Parameters.AddWithValue("@StatusID", response.StatusID);
            cd.Parameters.AddWithValue("@LoadID", response.loadID);
            cd.Parameters.AddWithValue("@Status", response.status);
            cd.Parameters.AddWithValue("@timeStamp", response.timeStamp);
            cd.Parameters.AddWithValue("@errorCode", response.errorCode);
            cd.Parameters.AddWithValue("@errorMessage", response.errorMessage);
            cd.Parameters.AddWithValue("@Message", response.Message);


            SqlParameter outputparm = new SqlParameter("@responseMessage", SqlDbType.Int)
            {
                Direction = ParameterDirection.Output
            };

            cd.Parameters.Add(outputparm);

            try
            {
                cd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.InsertTrackingStatusResponse");
            }


            int results = Convert.ToInt32(outputparm.Value);

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return results;

        }

        public OTR_API.TruckerToolsTracking.Models.Load GetTrackedLoad(int VectorID)
        {
            OTR_API.TruckerToolsTracking.Models.Load obj = new OTR_API.TruckerToolsTracking.Models.Load();

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString);
            cn.Open();

            SqlCommand cd = new SqlCommand("spTrackingByVectorID_Get", cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@VectorID", VectorID);

            try
            {
                using (SqlDataReader reader = cd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["VectorID"] != DBNull.Value) { obj.VectorID = (int)reader["VectorID"]; }
                        if (reader["BillToID"] != DBNull.Value) { obj.BillToID = (int)reader["BillToID"]; }
                        if (reader["loadTrackExternalId"] != DBNull.Value) { obj.loadTrackExternalId = (string)reader["loadTrackExternalId"]; }
                        if (reader["loadNumber"] != DBNull.Value) { obj.loadNumber = (string)reader["loadNumber"]; }
                        if (reader["dispatcherId"] != DBNull.Value) { obj.dispatcherId = (string)reader["dispatcherId"]; }
                        if (reader["dispatcherEmail"] != DBNull.Value) { obj.dispatcherEmail = (string)reader["dispatcherEmail"]; }
                        if (reader["dispatcherPhoneNumber"] != DBNull.Value) { obj.dispatcherPhoneNumber = (string)reader["dispatcherPhoneNumber"]; }
                        if (reader["textmessage"] != DBNull.Value) { obj.textmessage = (string)reader["textmessage"]; }
                        if (reader["loadType"] != DBNull.Value) { obj.loadType = (string)reader["loadType"]; }
                        if (reader["trailerType"] != DBNull.Value) { obj.trailerType = (string)reader["trailerType"]; }
                        if (reader["driverCell"] != DBNull.Value) { obj.driverCell = (string)reader["driverCell"]; }
                        if (reader["trailerNumber"] != DBNull.Value) { obj.trailerNumber = (string)reader["trailerNumber"]; }
                        if (reader["truckNumber"] != DBNull.Value) { obj.truckNumber = (string)reader["truckNumber"]; }
                        if (reader["driverName"] != DBNull.Value) { obj.driverName = (string)reader["driverName"]; }
                        if (reader["driverType"] != DBNull.Value) { obj.driverType = (string)reader["driverType"]; }
                        if (reader["driverComments"] != DBNull.Value) { obj.driverComments = (string)reader["driverComments"]; }
                        if (reader["loadNotes"] != DBNull.Value) { obj.loadNotes = (string)reader["loadNotes"]; }
                        if (reader["isTeamLoad"] != DBNull.Value) { obj.isTeamLoad = (bool)reader["isTeamLoad"]; }
                        if (reader["carrierDispatcherEmail"] != DBNull.Value) { obj.carrierDispatcherEmail = (string)reader["carrierDispatcherEmail"]; }
                    }
                }
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit();
                da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.GetTrackedLoad");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            if (obj.ID == 0) return null;

            // Hydrate child objects using TrackingID
            obj.shipper = GetTrackedShipper(obj.ID);
            obj.carrier = GetTrackedCarrier(obj.ID);
            obj.broker = GetTrackedBroker(obj.ID);
            obj.stops = GetTrackedStops(obj.ID);

            return obj;
        }

        public OTR_API.TruckerToolsTracking.Models.Shipper GetTrackedShipper(int TrackingID)
        {
            OTR_API.TruckerToolsTracking.Models.Shipper obj = null;

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString);
            cn.Open();

            SqlCommand cd = new SqlCommand(
                "SELECT TOP 1 ID, TrackingID, shipperId, loadNumber, emails, emailInterval, referenceNumber " +
                "FROM TrackingShipper WHERE TrackingID = @TrackingID AND Deleted = 0", cn);
            cd.Parameters.AddWithValue("@TrackingID", TrackingID);

            try
            {
                using (SqlDataReader reader = cd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        obj = new OTR_API.TruckerToolsTracking.Models.Shipper();
                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["TrackingID"] != DBNull.Value) { obj.TrackingID = (int)reader["TrackingID"]; }
                        if (reader["shipperId"] != DBNull.Value) { obj.shipperId = (string)reader["shipperId"]; }
                        if (reader["loadNumber"] != DBNull.Value) { obj.loadNumber = (string)reader["loadNumber"]; }
                        if (reader["emails"] != DBNull.Value) { obj.emails = (string)reader["emails"]; }
                        if (reader["emailInterval"] != DBNull.Value) { obj.emailInterval = (int)reader["emailInterval"]; }
                        if (reader["referenceNumber"] != DBNull.Value) { obj.referenceNumber = (string)reader["referenceNumber"]; }
                    }
                }
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit();
                da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.GetTrackedShipper");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }
            return obj;
        }

        public OTR_API.TruckerToolsTracking.Models.Carrier GetTrackedCarrier(int TrackingID)
        {
            OTR_API.TruckerToolsTracking.Models.Carrier obj = null;

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString);
            cn.Open();

            SqlCommand cd = new SqlCommand(
                "SELECT TOP 1 ID, TrackingID, companyName, docketNumber, contactName, contactPhone, contactPhoneExt, contactEmail " +
                "FROM TrackingCarrier WHERE TrackingID = @TrackingID AND Deleted = 0", cn);
            cd.Parameters.AddWithValue("@TrackingID", TrackingID);

            try
            {
                using (SqlDataReader reader = cd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        obj = new OTR_API.TruckerToolsTracking.Models.Carrier();
                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["TrackingID"] != DBNull.Value) { obj.TrackingID = (int)reader["TrackingID"]; }
                        if (reader["companyName"] != DBNull.Value) { obj.companyName = (string)reader["companyName"]; }
                        if (reader["docketNumber"] != DBNull.Value) { obj.docketNumber = (string)reader["docketNumber"]; }
                        if (reader["contactName"] != DBNull.Value) { obj.contactName = (string)reader["contactName"]; }
                        if (reader["contactPhone"] != DBNull.Value) { obj.contactPhone = (string)reader["contactPhone"]; }
                        if (reader["contactPhoneExt"] != DBNull.Value) { obj.contactPhoneExt = (string)reader["contactPhoneExt"]; }
                        if (reader["contactEmail"] != DBNull.Value) { obj.contactEmail = (string)reader["contactEmail"]; }
                    }
                }
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit();
                da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.GetTrackedCarrier");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }
            return obj;
        }

        public OTR_API.TruckerToolsTracking.Models.Broker GetTrackedBroker(int TrackingID)
        {
            OTR_API.TruckerToolsTracking.Models.Broker obj = null;

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString);
            cn.Open();

            SqlCommand cd = new SqlCommand(
                "SELECT TOP 1 ID, TrackingID, companyName, docketNumber, contactName, contactPhone, contactPhoneExt, contactEmail " +
                "FROM TrackingBroker WHERE TrackingID = @TrackingID AND Deleted = 0", cn);
            cd.Parameters.AddWithValue("@TrackingID", TrackingID);

            try
            {
                using (SqlDataReader reader = cd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        obj = new OTR_API.TruckerToolsTracking.Models.Broker();
                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["TrackingID"] != DBNull.Value) { obj.TrackingID = (int)reader["TrackingID"]; }
                        if (reader["companyName"] != DBNull.Value) { obj.companyName = (string)reader["companyName"]; }
                        if (reader["docketNumber"] != DBNull.Value) { obj.docketNumber = (string)reader["docketNumber"]; }
                        if (reader["contactName"] != DBNull.Value) { obj.contactName = (string)reader["contactName"]; }
                        if (reader["contactPhone"] != DBNull.Value) { obj.contactPhone = (string)reader["contactPhone"]; }
                        if (reader["contactPhoneExt"] != DBNull.Value) { obj.contactPhoneExt = (string)reader["contactPhoneExt"]; }
                        if (reader["contactEmail"] != DBNull.Value) { obj.contactEmail = (string)reader["contactEmail"]; }
                    }
                }
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit();
                da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.GetTrackedBroker");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }
            return obj;
        }

        public List<OTR_API.TruckerToolsTracking.Models.Stop> GetTrackedStops(int TrackingID)
        {
            List<OTR_API.TruckerToolsTracking.Models.Stop> stops = new List<OTR_API.TruckerToolsTracking.Models.Stop>();

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString);
            cn.Open();

            SqlCommand cd = new SqlCommand(
                "SELECT ID, TrackingID, loadNumber, orderNumber, address, city, state, zipcode, " +
                "lat, lon, [datetime], datetimeExit, geofenceRadius, notes, stopExternalId " +
                "FROM TrackingStop WHERE TrackingID = @TrackingID AND Deleted = 0 ORDER BY orderNumber", cn);
            cd.Parameters.AddWithValue("@TrackingID", TrackingID);

            try
            {
                using (SqlDataReader reader = cd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        OTR_API.TruckerToolsTracking.Models.Stop stop = new OTR_API.TruckerToolsTracking.Models.Stop();
                        if (reader["ID"] != DBNull.Value) { stop.ID = (int)reader["ID"]; }
                        if (reader["TrackingID"] != DBNull.Value) { stop.TrackingID = (int)reader["TrackingID"]; }
                        if (reader["loadNumber"] != DBNull.Value) { stop.loadNumber = (int)reader["loadNumber"]; }
                        if (reader["orderNumber"] != DBNull.Value) { stop.orderNumber = (int)reader["orderNumber"]; }
                        if (reader["address"] != DBNull.Value) { stop.address = (string)reader["address"]; }
                        if (reader["city"] != DBNull.Value) { stop.city = (string)reader["city"]; }
                        if (reader["state"] != DBNull.Value) { stop.state = (string)reader["state"]; }
                        if (reader["zipcode"] != DBNull.Value) { stop.zipcode = (string)reader["zipcode"]; }
                        if (reader["lat"] != DBNull.Value) { stop.lat = (decimal)reader["lat"]; }
                        if (reader["lon"] != DBNull.Value) { stop.lon = (decimal)reader["lon"]; }
                        if (reader["datetime"] != DBNull.Value) { stop.datetime = (string)reader["datetime"]; }
                        if (reader["datetimeExit"] != DBNull.Value) { stop.datetimeExit = (string)reader["datetimeExit"]; }
                        if (reader["geofenceRadius"] != DBNull.Value) { stop.geofenceRadius = (int)reader["geofenceRadius"]; }
                        if (reader["notes"] != DBNull.Value) { stop.notes = (string)reader["notes"]; }
                        if (reader["stopExternalId"] != DBNull.Value) { stop.stopExternalId = (string)reader["stopExternalId"]; }

                        stops.Add(stop);
                    }
                }
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit();
                da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.GetTrackedStops");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            // Hydrate each stop's actions and metadata
            foreach (var stop in stops)
            {
                stop.actions = GetTrackedStopActions(stop.ID);
                stop.metadata = GetTrackedStopMetadata(stop.ID);
            }

            return stops;
        }

        public List<OTR_API.TruckerToolsTracking.Models.Action> GetTrackedStopActions(int TrackingStopID)
        {
            List<OTR_API.TruckerToolsTracking.Models.Action> actions = new List<OTR_API.TruckerToolsTracking.Models.Action>();

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString);
            cn.Open();

            SqlCommand cd = new SqlCommand(
                "SELECT TrackingStopID, id, name, item, isLastAction, required, driverInput " +
                "FROM TrackingStopAction WHERE TrackingStopID = @TrackingStopID", cn);
            cd.Parameters.AddWithValue("@TrackingStopID", TrackingStopID);

            try
            {
                using (SqlDataReader reader = cd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        OTR_API.TruckerToolsTracking.Models.Action a = new OTR_API.TruckerToolsTracking.Models.Action();
                        if (reader["TrackingStopID"] != DBNull.Value) { a.TrackingStopID = (int)reader["TrackingStopID"]; }
                        if (reader["id"] != DBNull.Value) { a.id = (string)reader["id"]; }
                        if (reader["name"] != DBNull.Value) { a.name = (string)reader["name"]; }
                        if (reader["item"] != DBNull.Value) { a.item = (string)reader["item"]; }
                        if (reader["isLastAction"] != DBNull.Value) { a.isLastAction = (bool)reader["isLastAction"]; }
                        if (reader["required"] != DBNull.Value) { a.required = (bool)reader["required"]; }
                        if (reader["driverInput"] != DBNull.Value) { a.driverInput = (bool)reader["driverInput"]; }

                        actions.Add(a);
                    }
                }
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit();
                da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.GetTrackedStopActions");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }
            return actions;
        }

        public List<OTR_API.TruckerToolsTracking.Models.Metadata> GetTrackedStopMetadata(int TrackingStopID)
        {
            List<OTR_API.TruckerToolsTracking.Models.Metadata> meta = new List<OTR_API.TruckerToolsTracking.Models.Metadata>();

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString);
            cn.Open();

            SqlCommand cd = new SqlCommand(
                "SELECT TrackingStopID, name, value FROM TrackingStopMetadata WHERE TrackingStopID = @TrackingStopID", cn);
            cd.Parameters.AddWithValue("@TrackingStopID", TrackingStopID);

            try
            {
                using (SqlDataReader reader = cd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        OTR_API.TruckerToolsTracking.Models.Metadata m = new OTR_API.TruckerToolsTracking.Models.Metadata();
                        if (reader["TrackingStopID"] != DBNull.Value) { m.TrackingStopID = (int)reader["TrackingStopID"]; }
                        if (reader["name"] != DBNull.Value) { m.name = (string)reader["name"]; }
                        if (reader["value"] != DBNull.Value) { m.value = (string)reader["value"]; }

                        meta.Add(m);
                    }
                }
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit();
                da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.GetTrackedStopMetadata");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }
            return meta;
        }




        public List<OTR_API.TruckerToolsTracking.Models.StatusUpdate> GetLoadTrackingStatusList(OTR_API.TruckerToolsTracking.Models.Load load)
        {
            List<OTR_API.TruckerToolsTracking.Models.StatusUpdate> objList = new List<OTR_API.TruckerToolsTracking.Models.StatusUpdate>();

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            SqlCommand cd = new SqlCommand("spStatusLoadTrackingByLoadID_Get", cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@LoadID", load.loadNumber);

            int reccount = 0;

            try
            {
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {
                        reccount += 1;

                        OTR_API.TruckerToolsTracking.Models.StatusUpdate obj = new OTR_API.TruckerToolsTracking.Models.StatusUpdate();

                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["partnerid"] != DBNull.Value) { obj.partnerid = (int)reader["partnerid"]; }
                        if (reader["accountid"] != DBNull.Value) { obj.accountid = (string)reader["accountid"]; }
                        if (reader["loadTrackExternalId"] != DBNull.Value) { obj.loadTrackExternalId = (int)reader["loadTrackExternalId"]; }
                        if (reader["ltExternalId"] != DBNull.Value) { obj.ltExternalId = (string)reader["ltExternalId"]; }
                        if (reader["driverPhone"] != DBNull.Value) { obj.driverPhone = (string)reader["driverPhone"]; }
                        if (reader["loadNumber"] != DBNull.Value) { obj.loadNumber = (string)reader["loadNumber"]; }
                        if (reader["eventType"] != DBNull.Value) { obj.eventType = (string)reader["eventType"]; }
                        if (reader["StatusDate"] != DBNull.Value) { obj.StatusDate = (DateTime)reader["StatusDate"]; }

                        objList.Add(obj);
                    }
                }
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.GetLoadTrackingStatus");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            if (reccount > 0)
            {
                // Status - Type : 1 = LatestStatus, 2 = Status 
                // Location - Type : 1 = LatestStatus, 2 = Status Location, 3 = LatestLocation, 4 = Location List 

                foreach (OTR_API.TruckerToolsTracking.Models.StatusUpdate su in objList)
                {
                    //Get LatestStatus
                    OTR_API.TruckerToolsTracking.Models.Status latestStatus = GetLoadTrackingStatusStatus(su.ID, 1);
                    if (latestStatus != null)
                    {
                        if (latestStatus.ID > 0)
                        {
                            su.latestStatus = latestStatus;
                        }
                    }


                    if (cn.State != ConnectionState.Closed) { cn.Close(); }

                    //get LatestLocation
                    OTR_API.TruckerToolsTracking.Models.Location latestLocation = GetLoadTrackingStatusLocation(su.ID, 3);
                    if (latestLocation != null)
                    {
                        if (latestLocation.ID > 0)
                        {
                            su.latestLocation = latestLocation;
                        }
                    }

                    if (cn.State != ConnectionState.Closed) { cn.Close(); }

                    //Get Status
                    OTR_API.TruckerToolsTracking.Models.Status laststatus = GetLoadTrackingStatusStatus(su.ID, 2);
                    if (laststatus != null)
                    {
                        if (laststatus.ID > 0)
                        {
                            su.status = laststatus;
                        }
                    }

                    if (cn.State != ConnectionState.Closed) { cn.Close(); }

                    //get Location List
                    List<OTR_API.TruckerToolsTracking.Models.Location> locationlist = GetLoadTrackingStatusLocationList(su.ID, 4);

                    if (locationlist != null)
                    {
                        if (locationlist.Count > 0)
                        {
                            su.locations = locationlist;
                        }
                    }

                    if (cn.State != ConnectionState.Closed) { cn.Close(); }
                }
            }
            

            return objList;
        }

        public OTR_API.TruckerToolsTracking.Models.StatusUpdate GetLoadTrackingStatus(OTR_API.TruckerToolsTracking.Models.StatusUpdate status)
        {
            OTR_API.TruckerToolsTracking.Models.StatusUpdate obj = new TruckerToolsTracking.Models.StatusUpdate();

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            SqlCommand cd = new SqlCommand("spStatusLoadTrackingByID_Get", cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@ID", status.ID);

            try
            {
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {

                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["partnerid"] != DBNull.Value) { obj.partnerid = (int)reader["partnerid"]; }
                        if (reader["accountid"] != DBNull.Value) { obj.accountid = (string)reader["accountid"]; }
                        if (reader["loadTrackExternalId"] != DBNull.Value) { obj.loadTrackExternalId = (int)reader["loadTrackExternalId"]; }
                        if (reader["ltExternalId"] != DBNull.Value) { obj.ltExternalId = (string)reader["ltExternalId"]; }
                        if (reader["driverPhone"] != DBNull.Value) { obj.driverPhone = (string)reader["driverPhone"]; }
                        if (reader["loadNumber"] != DBNull.Value) { obj.loadNumber = (string)reader["loadNumber"]; }
                        if (reader["eventType"] != DBNull.Value) { obj.eventType = (string)reader["eventType"]; }
                        if (reader["StatusDate"] != DBNull.Value) { obj.StatusDate = (DateTime)reader["StatusDate"]; }

                    }
                }
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.GetLoadTrackingStatus");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            // Status - Type : 1 = LatestStatus, 2 = Status 
            // Location - Type : 1 = LatestStatus, 2 = Status Location, 3 = LatestLocation, 4 = Location List 


            //Get LatestStatus
            OTR_API.TruckerToolsTracking.Models.Status latestStatus = GetLoadTrackingStatusStatus(obj.ID, 1);
            if(latestStatus != null)
            {
                if(latestStatus.ID > 0)
                {
                    obj.latestStatus = latestStatus;
                }
            }


            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            //get LatestLocation
            OTR_API.TruckerToolsTracking.Models.Location latestLocation = GetLoadTrackingStatusLocation(obj.ID, 3);
            if (latestLocation != null)
            {
                if (latestLocation.ID > 0)
                {
                    obj.latestLocation = latestLocation;
                }
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            //Get Status
            OTR_API.TruckerToolsTracking.Models.Status laststatus = GetLoadTrackingStatusStatus(obj.ID, 2);
            if (laststatus != null)
            {
                if (laststatus.ID > 0)
                {
                    obj.status = laststatus;
                }
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            //get Location List
            List<OTR_API.TruckerToolsTracking.Models.Location> locationlist = GetLoadTrackingStatusLocationList(obj.ID, 4);

            if (locationlist != null)
            {
                if (locationlist.Count > 0)
                {
                    obj.locations = locationlist;
                }
            }


            if (cn.State != ConnectionState.Closed) { cn.Close(); }


            return obj;
        }

        public OTR_API.TruckerToolsTracking.Models.Status GetLoadTrackingStatusStatus(int ID, int Type)
        {
            OTR_API.TruckerToolsTracking.Models.Status obj = new TruckerToolsTracking.Models.Status();

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "spStatusStatusByAssociatedID_Get";

            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@AssociatedID", ID);
            cd.Parameters.AddWithValue("@type", Type);

            try
            {
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {
                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["name"] != DBNull.Value) { obj.name = (string)reader["name"]; }
                        if (reader["code"] != DBNull.Value) { obj.code = (string)reader["code"]; }
                        if (reader["timeStamp"] != DBNull.Value) { obj.timeStamp = (string)reader["timeStamp"]; }
                        if (reader["timeStampSec"] != DBNull.Value) { obj.timeStampSec = (string)reader["timeStampSec"]; }
                        if (reader["associatedId"] != DBNull.Value) { obj.associatedId = (int)reader["associatedId"]; }
                        if (reader["type"] != DBNull.Value) { obj.type = (int)reader["type"]; }

                    }
                }
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.GetLoadTrackingStatusStatus");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            //get Location
            OTR_API.TruckerToolsTracking.Models.Location location = GetLoadTrackingStatusLocation(obj.ID, Type);
            if (location != null)
            {
                if (location.ID > 0)
                {
                    obj.location = location;
                }
            }

            return obj;
        }


        public OTR_API.TruckerToolsTracking.Models.Location GetLoadTrackingStatusLocation(int ID, int Type)
        {
            OTR_API.TruckerToolsTracking.Models.Location obj = new TruckerToolsTracking.Models.Location();

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "spStatusLocationByAssociatedID_Get";

            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@AssociatedID", ID);
            cd.Parameters.AddWithValue("@type", Type);

            try
            {
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {

                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["lat"] != DBNull.Value) { obj.lat = (string)reader["lat"]; }
                        if (reader["lon"] != DBNull.Value) { obj.lon = (string)reader["lon"]; }
                        if (reader["accuracy"] != DBNull.Value) { obj.accuracy = (string)reader["accuracy"]; }
                        if (reader["timeStamp"] != DBNull.Value) { obj.timeStamp = (string)reader["timeStamp"]; }
                        if (reader["timeStampSec"] != DBNull.Value) { obj.timeStampSec = (string)reader["timeStampSec"]; }
                        if (reader["city"] != DBNull.Value) { obj.city = (string)reader["city"]; }
                        if (reader["state"] != DBNull.Value) { obj.state = (string)reader["state"]; }
                        if (reader["country"] != DBNull.Value) { obj.country = (string)reader["country"]; }
                        if (reader["associatedId"] != DBNull.Value) { obj.associatedId = (int)reader["associatedId"]; }
                        if (reader["type"] != DBNull.Value) { obj.type = (int)reader["type"]; }
                    }
                }
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.GetLoadTrackingStatusLocation");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return obj;
        }

        public List<OTR_API.TruckerToolsTracking.Models.Location> GetLoadTrackingStatusLocationList(int ID, int Type)
        {
            List<OTR_API.TruckerToolsTracking.Models.Location> locationlist = new List<TruckerToolsTracking.Models.Location>();


            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "spStatusLocationByAssociatedID_Get";

            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@AssociatedID", ID);
            cd.Parameters.AddWithValue("@type", Type);

            try
            {
                using (SqlDataReader reader = cd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        OTR_API.TruckerToolsTracking.Models.Location obj = new TruckerToolsTracking.Models.Location();

                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["lat"] != DBNull.Value) { obj.lat = (string)reader["lat"]; }
                        if (reader["lon"] != DBNull.Value) { obj.lon = (string)reader["lon"]; }
                        if (reader["accuracy"] != DBNull.Value) { obj.accuracy = (string)reader["accuracy"]; }
                        if (reader["timeStamp"] != DBNull.Value) { obj.timeStamp = (string)reader["timeStamp"]; }
                        if (reader["timeStampSec"] != DBNull.Value) { obj.timeStampSec = (string)reader["timeStampSec"]; }
                        if (reader["city"] != DBNull.Value) { obj.city = (string)reader["city"]; }
                        if (reader["state"] != DBNull.Value) { obj.state = (string)reader["state"]; }
                        if (reader["country"] != DBNull.Value) { obj.country = (string)reader["country"]; }
                        if (reader["associatedId"] != DBNull.Value) { obj.associatedId = (int)reader["associatedId"]; }
                        if (reader["type"] != DBNull.Value) { obj.type = (int)reader["type"]; }

                        locationlist.Add(obj);
                    }
                }
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.GetLoadTrackingStatusLocationList");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }


            return locationlist;
        }

        public bool InsertTTPayload(string payload, string url, string apitype)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostTT"].ConnectionString); cn.Open();

            string strsql = "Insert Into TruckerToolPayload(Payload, URL, APIType) Values (@Payload, @Url, @APIType)";

            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.Text;

            cd.Parameters.AddWithValue("@Payload", payload);
            cd.Parameters.AddWithValue("@Url", url);
            cd.Parameters.AddWithValue("@APIType", apitype);

            try
            {
                cd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.TT.InsertLoadTracking");
            }


            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return true;

        }
    }

    
}