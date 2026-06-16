using System;
using System.Text.RegularExpressions;
using System.Data;
using System.Data.SqlClient;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Configuration;
using System.Security.Cryptography;
using OTR_API.Models;
using System.Net.Mail;
using System.Net.Mime;
using System.Net;
using System.Net.Http;

namespace OTR_API.DataClasses
{
    public class DataAccess
    {
        public enum Mask { None, DateOnly, PhoneWithArea, IpAddress, SSN, Decimal, Digit, Initials };

        protected SqlConnection cnn;

        protected SqlCommand cmd;

        protected void Connect()
        {
            string str = ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString;

            cnn = new SqlConnection(str);

            try
            {
                cnn.Open();
            }
            catch (Exception ec)
            {
                DataAudit da = new DataAudit();
                da.InsertErrorAuditLog(ec.Message, "DataAccess.Connect");
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

    public class DataCheckCalls : DataAccess
    {
        public List<CheckCalls> GetCheckCallsByTripID(int ID)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spCheckCallsByTripID_Get";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@TripID", ID);

            List<CheckCalls> list = new List<CheckCalls>();

            try
            { 
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {

                        CheckCalls obj = new CheckCalls();

                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["DriverTripID"] != DBNull.Value) { obj.DriverTripID = (int)reader["DriverTripID"]; }
                        if (reader["CheckTypeID"] != DBNull.Value) { obj.CheckTypeID = (int)reader["CheckTypeID"]; }
                        if (reader["CheckType"] != DBNull.Value) { obj.CheckType = (string)reader["CheckType"]; }
                        if (reader["CheckDate"] != DBNull.Value) { obj.CheckDate = (DateTime)reader["CheckDate"]; }
                        if (reader["Comments"] != DBNull.Value) { obj.Comments = (string)reader["Comments"]; }
                        if (reader["Offset"] != DBNull.Value) { obj.Offset = (string)reader["Offset"]; }
                        if (reader["Timezone"] != DBNull.Value) { obj.Timezone = (string)reader["Timezone"]; }
                        obj.GPSCoordinates = new GPSLocation();
                        if (reader["Lat"] != DBNull.Value) { obj.GPSCoordinates.Lat = (string)reader["Lat"]; }
                        if (reader["Long"] != DBNull.Value) { obj.GPSCoordinates.Long = (string)reader["Long"]; }

                        DataDocuments dd = new DataDocuments();
                        obj.Documents = dd.GetDocumentsByCheckCall(obj.ID);

                        list.Add(obj);

                    }

                }
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataCheckCalls.GetCheckCallsByTripID");
            }


            if (cn.State != ConnectionState.Closed) { cn.Close(); }


            return list;

        }

        public List<CheckCalls> GetCheckCallsByDriverTripID(int ID)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spCheckCallsByDriverTripID_Get";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@DriverTripID", ID);

            List<CheckCalls> list = new List<CheckCalls>();

            try
            { 
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {

                        CheckCalls obj = new CheckCalls();

                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["DriverTripID"] != DBNull.Value) { obj.DriverTripID = (int)reader["DriverTripID"]; }
                        if (reader["CheckTypeID"] != DBNull.Value) { obj.CheckTypeID = (int)reader["CheckTypeID"]; }
                        if (reader["CheckType"] != DBNull.Value) { obj.CheckType = (string)reader["CheckType"]; }
                        if (reader["CheckDate"] != DBNull.Value) { obj.CheckDate = (DateTime)reader["CheckDate"]; }
                        if (reader["Comments"] != DBNull.Value) { obj.Comments = (string)reader["Comments"]; }
                        if (reader["Offset"] != DBNull.Value) { obj.Offset = (string)reader["Offset"]; }
                        if (reader["Timezone"] != DBNull.Value) { obj.Timezone = (string)reader["Timezone"]; }
                        obj.GPSCoordinates = new GPSLocation();
                        if (reader["Lat"] != DBNull.Value) { obj.GPSCoordinates.Lat = (string)reader["Lat"]; }
                        if (reader["Long"] != DBNull.Value) { obj.GPSCoordinates.Long = (string)reader["Long"]; }

                        DataDocuments dd = new DataDocuments();
                        obj.Documents = dd.GetDocumentsByCheckCall(obj.ID);

                        list.Add(obj);

                    }

                }
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataCheckCalls.GetCheckCallsByDriverTripID");
            }


            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return list;

        }

        public List<CheckCalls> GetCheckCallsByDriverTripDisplay(int ID)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spCheckCallsByDriverTripIDDisplay_Get";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@DriverTripID", ID);

            List<CheckCalls> list = new List<CheckCalls>();

            try
            { 
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {

                        CheckCalls obj = new CheckCalls();

                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["DriverTripID"] != DBNull.Value) { obj.DriverTripID = (int)reader["DriverTripID"]; }
                        if (reader["CheckTypeID"] != DBNull.Value) { obj.CheckTypeID = (int)reader["CheckTypeID"]; }
                        if (reader["CheckType"] != DBNull.Value) { obj.CheckType = (string)reader["CheckType"]; }
                        if (reader["CheckDate"] != DBNull.Value) { obj.CheckDate = (DateTime)reader["CheckDate"]; }
                        if (reader["Comments"] != DBNull.Value) { obj.Comments = (string)reader["Comments"]; }
                        if (reader["Offset"] != DBNull.Value) { obj.Offset = (string)reader["Offset"]; }
                        if (reader["Timezone"] != DBNull.Value) { obj.Timezone = (string)reader["Timezone"]; }
                        obj.GPSCoordinates = new GPSLocation();
                        if (reader["Lat"] != DBNull.Value) { obj.GPSCoordinates.Lat = (string)reader["Lat"]; }
                        if (reader["Long"] != DBNull.Value) { obj.GPSCoordinates.Long = (string)reader["Long"]; }

                        DataDocuments dd = new DataDocuments();
                        obj.Documents = dd.GetDocumentsByCheckCall(obj.ID);

                        list.Add(obj);

                    }

                }
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataCheckCalls.GetCheckCallsByDriverTripDisplay");
            }



            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return list;

        }

        public CheckCalls GetCheckCallsByID(int ID)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spCheckCallsByID_Get";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@ID", ID);

            CheckCalls obj = new CheckCalls();

            try
            { 
                using (SqlDataReader reader = cd.ExecuteReader())
                { 
                    //if (reader.HasRows)
                    //{
                    while (reader.Read())
                    {
                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["DriverTripID"] != DBNull.Value) { obj.DriverTripID = (int)reader["DriverTripID"]; }
                        if (reader["CheckTypeID"] != DBNull.Value) { obj.CheckTypeID = (int)reader["CheckTypeID"]; }
                        if (reader["CheckType"] != DBNull.Value) { obj.CheckType = (string)reader["CheckType"]; }
                        if (reader["CheckDate"] != DBNull.Value) { obj.CheckDate = (DateTime)reader["CheckDate"]; }
                        if (reader["Comments"] != DBNull.Value) { obj.Comments = (string)reader["Comments"]; }
                        if (reader["Offset"] != DBNull.Value) { obj.Offset = (string)reader["Offset"]; }
                        if (reader["Timezone"] != DBNull.Value) { obj.Timezone = (string)reader["Timezone"]; }

                        obj.GPSCoordinates = new GPSLocation();
                        if (reader["Lat"] != DBNull.Value) { obj.GPSCoordinates.Lat = (string)reader["Lat"]; }
                        if (reader["Long"] != DBNull.Value) { obj.GPSCoordinates.Long = (string)reader["Long"]; }

                        DataDocuments dd = new DataDocuments();
                        obj.Documents = dd.GetDocumentsByCheckCall(obj.ID);
                    }

                }
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataCheckCalls.GetCheckCallsByID");
            }


            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return obj;

        }

        public int InsertCheckCalls(CheckCalls checkcall)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spCheckCalls_Insert";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@DriverTripID", checkcall.DriverTripID);
            cd.Parameters.AddWithValue("@CheckTypeID", checkcall.CheckTypeID);
            cd.Parameters.AddWithValue("@CheckDate", checkcall.CheckDate);
            cd.Parameters.AddWithValue("@Comments", checkcall.Comments);
            cd.Parameters.AddWithValue("@Lat", checkcall.GPSCoordinates.Lat);
            cd.Parameters.AddWithValue("@Long", checkcall.GPSCoordinates.Long);
            cd.Parameters.AddWithValue("@Offset", checkcall.Offset);
            cd.Parameters.AddWithValue("@Timezone", checkcall.Timezone);

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
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataCheckCalls.InsertCheckCalls");
            }

            int results = Convert.ToInt32(outputparm.Value);

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return results;
        }

        public bool UpdateCheckCalls(CheckCalls checkcall)
        {
            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spCheckCalls_Update";
            SqlCommand cd = new SqlCommand(strsql, cn);

            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@ID", checkcall.ID);
            cd.Parameters.AddWithValue("@DriverTripID", checkcall.DriverTripID);
            cd.Parameters.AddWithValue("@CheckTypeID", checkcall.CheckTypeID);
            cd.Parameters.AddWithValue("@CheckDate", checkcall.CheckDate);
            cd.Parameters.AddWithValue("@Comments", checkcall.Comments);
            cd.Parameters.AddWithValue("@Lat", checkcall.GPSCoordinates.Lat);
            cd.Parameters.AddWithValue("@Long", checkcall.GPSCoordinates.Long);

            bool i = false;
            try
            {
                i = (cd.ExecuteNonQuery() == 1);
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataCheckCalls.UpdateCheckCalls");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return i;
        }


        public CheckCalls DeleteCheckCalls(CheckCalls checkcall)
        {
            try
            {
                SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

                string strsql = "spCheckCallsByID_Delete";
                SqlCommand cd = new SqlCommand(strsql, cn);
                cd.CommandType = CommandType.StoredProcedure;
                cd.Parameters.AddWithValue("@ID", checkcall.ID);

                bool i = false;
                try
                {
                    i = (cd.ExecuteNonQuery() == 1);
                }
                catch (Exception ex)
                {
                    checkcall.Message = "Error";

                    DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataCheckCalls.DeleteCheckCalls");

                    return checkcall;
                }


                if (cn.State != ConnectionState.Closed) { cn.Close(); }

                checkcall.Message = "Successful";
            }
            catch
            {
                checkcall.Message = "Error";
            }


            return checkcall;
        }

        public List<CheckCallTypes> GetCheckCallsTypes()
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spCheckCallTypes_Get";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;

            List<CheckCallTypes> list = new List<CheckCallTypes>();

            try
            { 
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {

                        CheckCallTypes obj = new CheckCallTypes();

                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["CheckType"] != DBNull.Value) { obj.CheckTypes = (string)reader["CheckType"]; }
                        if (reader["CheckCallDefault"] != DBNull.Value) { obj.Default = (bool)reader["CheckCallDefault"]; }

                        list.Add(obj);

                    }

                }
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataCheckCalls.GetCheckCallsTypes");
            }


            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return list;

        }

        public List<CheckCallTypes> GetCheckCallsNextTypes(int ID)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spCheckCallNextTypes_Get";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@DriverTripID", ID);

            List<CheckCallTypes> list = new List<CheckCallTypes>();

            try
            { 
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {

                        CheckCallTypes obj = new CheckCallTypes();

                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["CheckType"] != DBNull.Value) { obj.CheckTypes = (string)reader["CheckType"]; }
                        if (reader["CheckCallDefault"] != DBNull.Value) { obj.Default = (bool)reader["CheckCallDefault"]; }

                        list.Add(obj);

                    }

                }
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataCheckCalls.GetCheckCallsNextTypes");
            }



            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return list;

        }


        public int InsertDriverTripLocation(DriverTripLocation TripLocation)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spDriverTripLocation_Insert";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@DriverTripID", TripLocation.DriverTripID);
            cd.Parameters.AddWithValue("@LocationType", (int)TripLocation.LocationType);
            cd.Parameters.AddWithValue("@LocationDate", TripLocation.LocationDate);
            cd.Parameters.AddWithValue("@Comments", TripLocation.Comments == null ? "" : TripLocation.Comments);
            cd.Parameters.AddWithValue("@Lat", TripLocation.GPSCoordinates.Lat);
            cd.Parameters.AddWithValue("@Long", TripLocation.GPSCoordinates.Long);
            cd.Parameters.AddWithValue("@Offset", TripLocation.Offset);
            cd.Parameters.AddWithValue("@Timezone", TripLocation.Timezone);

            cd.Parameters.Add("@responseMessage", SqlDbType.Int).Direction = ParameterDirection.Output;

            //SqlParameter outputparm = new SqlParameter("@responseMessage", SqlDbType.Int)
            //{
            //    Direction = ParameterDirection.Output
            //};
            //cd.Parameters.Add(outputparm);


            try
            {
                cd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataCheckCalls.InsertDriverTripLocation");
            }

            //int results = Convert.ToInt32(outputparm.Value);
            int results = Convert.ToInt32(cd.Parameters["@responseMessage"].Value);

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return results;
        }

        public DriverTripLocation GetDriverTripLocationByID(int ID)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spDriverTripLocationByID_Get";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@ID", ID);

            DriverTripLocation obj = new DriverTripLocation();


            try
            { 
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {
                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["DriverTripID"] != DBNull.Value) { obj.DriverTripID = (int)reader["DriverTripID"]; }
                        if (reader["LocationType"] != DBNull.Value)
                        {       obj.LocationType = (LocationTypeOption)reader["LocationType"];
                                obj.LocationTypeName = obj.LocationType.ToString(); 
                        }
                        if (reader["LocationDate"] != DBNull.Value) { obj.LocationDate = (DateTime)reader["LocationDate"]; }
                        if (reader["Comments"] != DBNull.Value) { obj.Comments = (string)reader["Comments"]; }

                        obj.GPSCoordinates = new GPSLocation();
                        if (reader["Lat"] != DBNull.Value) { obj.GPSCoordinates.Lat = (string)reader["Lat"]; }
                        if (reader["Long"] != DBNull.Value) { obj.GPSCoordinates.Long = (string)reader["Long"]; }

                        if (reader["Offset"] != DBNull.Value) { obj.Offset = (string)reader["Offset"]; }
                        if (reader["Timezone"] != DBNull.Value) { obj.Timezone = (string)reader["Timezone"]; }

                        if (reader["NextFence"] != DBNull.Value) { obj.NextFence = (int)reader["NextFence"]; }
                    }

                }
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataCheckCalls.GetDriverTripLocationByID");
            }


            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return obj;

        }

    }

    public class DataDrivers : DataAccess
    {
        public List<Driver> GetAllDrivers()
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spDriverAll_Get";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;

            List<Driver> list = new List<Driver>();

            try
            {
                 using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {

                        Driver obj = new Driver();

                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["FirstName"] != DBNull.Value) { obj.FirstName = (string)reader["FirstName"]; }
                        if (reader["LastName"] != DBNull.Value) { obj.LastName = (string)reader["LastName"]; }
                        if (reader["Cellphone"] != DBNull.Value) { obj.Cellphone = (string)reader["Cellphone"]; }
                        if (reader["EmailAddress"] != DBNull.Value) { obj.EmailAddress = (string)reader["EmailAddress"]; }
                        if (reader["MCNumber"] != DBNull.Value) { obj.MCNumber = (string)reader["MCNumber"]; }
                        if (reader["TruckNumber"] != DBNull.Value) { obj.TruckNumber = (string)reader["TruckNumber"]; }
                        if (reader["TrailerNumber"] != DBNull.Value) { obj.TrailerNumber = (string)reader["TrailerNumber"]; }
                        if (reader["DeviceID"] != DBNull.Value) { obj.DeviceID = (string)reader["DeviceID"]; }
                        if (reader["Created"] != DBNull.Value) { obj.Created = (DateTime)reader["Created"]; }
                        if (reader["DriversLicense"] != DBNull.Value) { obj.DriversLicense = (string)reader["DriversLicense"]; }
                        if (reader["TruckMake"] != DBNull.Value) { obj.TruckMake = (string)reader["TruckMake"]; }
                        if (reader["TruckTag"] != DBNull.Value) { obj.TruckTag = (string)reader["TruckTag"]; }
                        if (reader["TruckColor"] != DBNull.Value) { obj.TruckColor = (string)reader["TruckColor"]; }
                        if (reader["TrailerType"] != DBNull.Value) { obj.TrailerType = (string)reader["TrailerType"]; }
                        if (reader["Capacity"] != DBNull.Value) { obj.Capacity = (string)reader["Capacity"]; }
                        if (reader["Size"] != DBNull.Value) { obj.Size = (string)reader["Size"]; }
                        if (reader["Misc"] != DBNull.Value) { obj.Misc = (string)reader["Misc"]; }
                        if (reader["Notifications"] != DBNull.Value) { obj.Notifications = (bool)reader["Notifications"]; }
                        if (reader["BackgroundSync"] != DBNull.Value) { obj.BackgroundSync = (bool)reader["BackgroundSync"]; }
                        if (reader["Profile"] != DBNull.Value) { obj.Profile = (string)reader["Profile"]; }
                        if (reader["CompanyID"] != DBNull.Value) { obj.CompanyID = (int)reader["CompanyID"]; }

                        obj.GeoFenceRate = 10;
                        obj.GeoFenceUnit = 0;

                        //DataDocuments dd = new DataDocuments();
                        //obj.Documents = dd.GetDocumentsByDriver(obj.ID);

                        list.Add(obj);

                    }
                }
            }
            catch(Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataDrivers.GetAllDrivers");

                Driver obj = new Driver();
                obj.Message = ex.Message.ToString();
                list.Add(obj);
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return list;

        }

        public List<Driver> GetAllCompanyDrivers(int CompanyID)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spDriverAllByCompany_Get";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@CompanyID", CompanyID);

            List<Driver> list = new List<Driver>();

            try
            { 
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {

                        Driver obj = new Driver();

                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["FirstName"] != DBNull.Value) { obj.FirstName = (string)reader["FirstName"]; }
                        if (reader["LastName"] != DBNull.Value) { obj.LastName = (string)reader["LastName"]; }
                        if (reader["Cellphone"] != DBNull.Value) { obj.Cellphone = (string)reader["Cellphone"]; }
                        if (reader["EmailAddress"] != DBNull.Value) { obj.EmailAddress = (string)reader["EmailAddress"]; }
                        if (reader["MCNumber"] != DBNull.Value) { obj.MCNumber = (string)reader["MCNumber"]; }
                        if (reader["TruckNumber"] != DBNull.Value) { obj.TruckNumber = (string)reader["TruckNumber"]; }
                        if (reader["TrailerNumber"] != DBNull.Value) { obj.TrailerNumber = (string)reader["TrailerNumber"]; }
                        if (reader["DeviceID"] != DBNull.Value) { obj.DeviceID = (string)reader["DeviceID"]; }
                        if (reader["Created"] != DBNull.Value) { obj.Created = (DateTime)reader["Created"]; }
                        if (reader["DriversLicense"] != DBNull.Value) { obj.DriversLicense = (string)reader["DriversLicense"]; }
                        if (reader["TruckMake"] != DBNull.Value) { obj.TruckMake = (string)reader["TruckMake"]; }
                        if (reader["TruckTag"] != DBNull.Value) { obj.TruckTag = (string)reader["TruckTag"]; }
                        if (reader["TruckColor"] != DBNull.Value) { obj.TruckColor = (string)reader["TruckColor"]; }
                        if (reader["TrailerType"] != DBNull.Value) { obj.TrailerType = (string)reader["TrailerType"]; }
                        if (reader["Capacity"] != DBNull.Value) { obj.Capacity = (string)reader["Capacity"]; }
                        if (reader["Size"] != DBNull.Value) { obj.Size = (string)reader["Size"]; }
                        if (reader["Misc"] != DBNull.Value) { obj.Misc = (string)reader["Misc"]; }
                        if (reader["Notifications"] != DBNull.Value) { obj.Notifications = (bool)reader["Notifications"]; }
                        if (reader["BackgroundSync"] != DBNull.Value) { obj.BackgroundSync = (bool)reader["BackgroundSync"]; }
                        if (reader["Profile"] != DBNull.Value) { obj.Profile = (string)reader["Profile"]; }
                        if (reader["CompanyID"] != DBNull.Value) { obj.CompanyID = (int)reader["CompanyID"]; }

                        obj.GeoFenceRate = 10;
                        obj.GeoFenceUnit = 0;

                        list.Add(obj);

                    }

                }
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataDrivers.GetAllCompanyDrivers");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return list;

        }

        public Driver GetDriverByLoad(int ID)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spDriverByLoadID_Get";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@LoadID", ID);

            Driver obj = new Driver();
            try
            { 
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {

                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["FirstName"] != DBNull.Value) { obj.FirstName = (string)reader["FirstName"]; }
                        if (reader["LastName"] != DBNull.Value) { obj.LastName = (string)reader["LastName"]; }
                        if (reader["Cellphone"] != DBNull.Value) { obj.Cellphone = (string)reader["Cellphone"]; }
                        if (reader["EmailAddress"] != DBNull.Value) { obj.EmailAddress = (string)reader["EmailAddress"]; }
                        if (reader["MCNumber"] != DBNull.Value) { obj.MCNumber = (string)reader["MCNumber"]; }
                        if (reader["TruckNumber"] != DBNull.Value) { obj.TruckNumber = (string)reader["TruckNumber"]; }
                        if (reader["TrailerNumber"] != DBNull.Value) { obj.TrailerNumber = (string)reader["TrailerNumber"]; }
                        if (reader["TruckNumber"] != DBNull.Value) { obj.TruckNumber = (string)reader["TruckNumber"]; }
                        if (reader["DeviceID"] != DBNull.Value) { obj.DeviceID = (string)reader["DeviceID"]; }
                        if (reader["Created"] != DBNull.Value) { obj.Created = (DateTime)reader["Created"]; }
                        if (reader["DriversLicense"] != DBNull.Value) { obj.DriversLicense = (string)reader["DriversLicense"]; }
                        if (reader["TruckMake"] != DBNull.Value) { obj.TruckMake = (string)reader["TruckMake"]; }
                        if (reader["TruckTag"] != DBNull.Value) { obj.TruckTag = (string)reader["TruckTag"]; }
                        if (reader["TruckColor"] != DBNull.Value) { obj.TruckColor = (string)reader["TruckColor"]; }
                        if (reader["TrailerType"] != DBNull.Value) { obj.TrailerType = (string)reader["TrailerType"]; }
                        if (reader["Capacity"] != DBNull.Value) { obj.Capacity = (string)reader["Capacity"]; }
                        if (reader["Size"] != DBNull.Value) { obj.Size = (string)reader["Size"]; }
                        if (reader["Misc"] != DBNull.Value) { obj.Misc = (string)reader["Misc"]; }
                        if (reader["Notifications"] != DBNull.Value) { obj.Notifications = (bool)reader["Notifications"]; }
                        if (reader["BackgroundSync"] != DBNull.Value) { obj.BackgroundSync = (bool)reader["BackgroundSync"]; }
                        if (reader["CompanyID"] != DBNull.Value) { obj.CompanyID = (int)reader["CompanyID"]; }

                        obj.GeoFenceRate = 10;
                        obj.GeoFenceUnit = 0;

                        DataDocuments dd = new DataDocuments();
                        obj.Documents = dd.GetDocumentsByDriver(obj.ID);

                        DataUrl du = new DataUrl();
                        obj.Url = du.GetDriverUrl(obj.ID);
                    }

                }
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataDrivers.GetDriverByLoad");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return obj;

        }

        public Driver GetDriverByDriverTrip(int ID)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spDriverByDriverTripID_Get";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@ID", ID);

            Driver obj = new Driver();
            try
            { 
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {

                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["FirstName"] != DBNull.Value) { obj.FirstName = (string)reader["FirstName"]; }
                        if (reader["LastName"] != DBNull.Value) { obj.LastName = (string)reader["LastName"]; }
                        if (reader["Cellphone"] != DBNull.Value) { obj.Cellphone = (string)reader["Cellphone"]; }
                        if (reader["EmailAddress"] != DBNull.Value) { obj.EmailAddress = (string)reader["EmailAddress"]; }
                        if (reader["MCNumber"] != DBNull.Value) { obj.MCNumber = (string)reader["MCNumber"]; }
                        if (reader["TruckNumber"] != DBNull.Value) { obj.TruckNumber = (string)reader["TruckNumber"]; }
                        if (reader["TrailerNumber"] != DBNull.Value) { obj.TrailerNumber = (string)reader["TrailerNumber"]; }
                        if (reader["TruckNumber"] != DBNull.Value) { obj.TruckNumber = (string)reader["TruckNumber"]; }
                        if (reader["DeviceID"] != DBNull.Value) { obj.DeviceID = (string)reader["DeviceID"]; }
                        if (reader["Created"] != DBNull.Value) { obj.Created = (DateTime)reader["Created"]; }
                        if (reader["DriversLicense"] != DBNull.Value) { obj.DriversLicense = (string)reader["DriversLicense"]; }
                        if (reader["TruckMake"] != DBNull.Value) { obj.TruckMake = (string)reader["TruckMake"]; }
                        if (reader["TruckTag"] != DBNull.Value) { obj.TruckTag = (string)reader["TruckTag"]; }
                        if (reader["TruckColor"] != DBNull.Value) { obj.TruckColor = (string)reader["TruckColor"]; }
                        if (reader["TrailerType"] != DBNull.Value) { obj.TrailerType = (string)reader["TrailerType"]; }
                        if (reader["Capacity"] != DBNull.Value) { obj.Capacity = (string)reader["Capacity"]; }
                        if (reader["Size"] != DBNull.Value) { obj.Size = (string)reader["Size"]; }
                        if (reader["Misc"] != DBNull.Value) { obj.Misc = (string)reader["Misc"]; }
                        if (reader["Notifications"] != DBNull.Value) { obj.Notifications = (bool)reader["Notifications"]; }
                        if (reader["BackgroundSync"] != DBNull.Value) { obj.BackgroundSync = (bool)reader["BackgroundSync"]; }
                        if (reader["CompanyID"] != DBNull.Value) { obj.CompanyID = (int)reader["CompanyID"]; }

                        obj.GeoFenceRate = 10;
                        obj.GeoFenceUnit = 0;

                        DataDocuments dd = new DataDocuments();
                        obj.Documents = dd.GetDocumentsByDriver(obj.ID);

                        DataUrl du = new DataUrl();
                        obj.Url = du.GetDriverUrl(obj.ID);
                    }

                }


            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataDrivers.GetDriverByDriverTrip");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return obj;

        }

        public Driver GetDriver(int ID, bool IncDetails)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spDriverByID_Get";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@ID", ID);

            Driver obj = new Driver();

            try
            { 
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {

                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["FirstName"] != DBNull.Value) { obj.FirstName = (string)reader["FirstName"]; }
                        if (reader["LastName"] != DBNull.Value) { obj.LastName = (string)reader["LastName"]; }
                        if (reader["Cellphone"] != DBNull.Value) { obj.Cellphone = (string)reader["Cellphone"]; }
                        if (reader["EmailAddress"] != DBNull.Value) { obj.EmailAddress = (string)reader["EmailAddress"]; }
                        if (reader["MCNumber"] != DBNull.Value) { obj.MCNumber = (string)reader["MCNumber"]; }
                        if (reader["TruckNumber"] != DBNull.Value) { obj.TruckNumber = (string)reader["TruckNumber"]; }
                        if (reader["TrailerNumber"] != DBNull.Value) { obj.TrailerNumber = (string)reader["TrailerNumber"]; }
                        if (reader["DeviceID"] != DBNull.Value) { obj.DeviceID = (string)reader["DeviceID"]; }
                        if (reader["Created"] != DBNull.Value) { obj.Created = (DateTime)reader["Created"]; }
                        if (reader["DriversLicense"] != DBNull.Value) { obj.DriversLicense = (string)reader["DriversLicense"]; }
                        if (reader["TruckMake"] != DBNull.Value) { obj.TruckMake = (string)reader["TruckMake"]; }
                        if (reader["TruckTag"] != DBNull.Value) { obj.TruckTag = (string)reader["TruckTag"]; }
                        if (reader["TruckColor"] != DBNull.Value) { obj.TruckColor = (string)reader["TruckColor"]; }
                        if (reader["TrailerType"] != DBNull.Value) { obj.TrailerType = (string)reader["TrailerType"]; }
                        if (reader["Capacity"] != DBNull.Value) { obj.Capacity = (string)reader["Capacity"]; }
                        if (reader["Size"] != DBNull.Value) { obj.Size = (string)reader["Size"]; }
                        if (reader["Misc"] != DBNull.Value) { obj.Misc = (string)reader["Misc"]; }
                        if (reader["Notifications"] != DBNull.Value) { obj.Notifications = (bool)reader["Notifications"]; }
                        if (reader["BackgroundSync"] != DBNull.Value) { obj.BackgroundSync = (bool)reader["BackgroundSync"]; }
                        if (reader["Profile"] != DBNull.Value) { obj.Profile = (string)reader["Profile"]; }
                        if (reader["CompanyID"] != DBNull.Value) { obj.CompanyID = (int)reader["CompanyID"]; }

                        obj.GeoFenceRate = 10;
                        obj.GeoFenceUnit = 0;

                        if (IncDetails)
                        {
                            DataDocuments dd = new DataDocuments();
                            obj.Documents = dd.GetDocumentsByDriver(obj.ID);

                            obj.Devices = new DriverDevice();
                            obj.Devices = GetDriverCurrentDevice(obj);

                            DataUrl du = new DataUrl();
                            obj.Url = du.GetDriverUrl(obj.ID);
                        }
                    }

                }
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataDrivers.GetDriver 1");
            }
            

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return obj;

        }

        public Driver GetDriver(string EmailAddress)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spDriverByEmail_Get";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@Email", EmailAddress);

            Driver obj = new Driver();

            try
            { 
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {
                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["FirstName"] != DBNull.Value) { obj.FirstName = (string)reader["FirstName"]; }
                        if (reader["LastName"] != DBNull.Value) { obj.LastName = (string)reader["LastName"]; }
                        if (reader["Cellphone"] != DBNull.Value) { obj.Cellphone = (string)reader["Cellphone"]; }
                        if (reader["EmailAddress"] != DBNull.Value) { obj.EmailAddress = (string)reader["EmailAddress"]; }
                        if (reader["MCNumber"] != DBNull.Value) { obj.MCNumber = (string)reader["MCNumber"]; }
                        if (reader["TruckNumber"] != DBNull.Value) { obj.TruckNumber = (string)reader["TruckNumber"]; }
                        if (reader["TrailerNumber"] != DBNull.Value) { obj.TrailerNumber = (string)reader["TrailerNumber"]; }
                        if (reader["DeviceID"] != DBNull.Value) { obj.DeviceID = (string)reader["DeviceID"]; }
                        if (reader["Created"] != DBNull.Value) { obj.Created = (DateTime)reader["Created"]; }
                        if (reader["DriversLicense"] != DBNull.Value) { obj.DriversLicense = (string)reader["DriversLicense"]; }
                        if (reader["TruckMake"] != DBNull.Value) { obj.TruckMake = (string)reader["TruckMake"]; }
                        if (reader["TruckTag"] != DBNull.Value) { obj.TruckTag = (string)reader["TruckTag"]; }
                        if (reader["TruckColor"] != DBNull.Value) { obj.TruckColor = (string)reader["TruckColor"]; }
                        if (reader["TrailerType"] != DBNull.Value) { obj.TrailerType = (string)reader["TrailerType"]; }
                        if (reader["Capacity"] != DBNull.Value) { obj.Capacity = (string)reader["Capacity"]; }
                        if (reader["Size"] != DBNull.Value) { obj.Size = (string)reader["Size"]; }
                        if (reader["Misc"] != DBNull.Value) { obj.Misc = (string)reader["Misc"]; }
                        if (reader["Notifications"] != DBNull.Value) { obj.Notifications = (bool)reader["Notifications"]; }
                        if (reader["BackgroundSync"] != DBNull.Value) { obj.BackgroundSync = (bool)reader["BackgroundSync"]; }
                        if (reader["Profile"] != DBNull.Value) { obj.Profile = (string)reader["Profile"]; }
                        if (reader["CompanyID"] != DBNull.Value) { obj.CompanyID = (int)reader["CompanyID"]; }

                        obj.GeoFenceRate = 10;
                        obj.GeoFenceUnit = 0;

                        DataDocuments dd = new DataDocuments();
                        obj.Documents = dd.GetDocumentsByDriver(obj.ID);

                        DataUrl du = new DataUrl();
                        obj.Url = du.GetDriverUrl(obj.ID);
                    }
                }

            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataDrivers.GetDriver 2");
            }



            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return obj;

        }

        public int InsertDriver(Driver driver)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            //Driver ID Enumeration -0 = Error, 1 = Duplicate Email, 2 = Duplicate mc Number, DriverID (over 100)

            string strsql = "spDriver_Insert";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@FirstName", driver.FirstName);
            cd.Parameters.AddWithValue("@LastName", driver.LastName);
            cd.Parameters.AddWithValue("@Cellphone", driver.Cellphone);
            cd.Parameters.AddWithValue("@EmailAddress", driver.EmailAddress);
            cd.Parameters.AddWithValue("@MCNumber", driver.MCNumber);
            cd.Parameters.AddWithValue("@TruckNumber", driver.TruckNumber);
            cd.Parameters.AddWithValue("@TrailerNumber", driver.TrailerNumber);
            cd.Parameters.AddWithValue("@DeviceID", driver.DeviceID);
            cd.Parameters.AddWithValue("@Password", driver.Password);
            cd.Parameters.AddWithValue("@DriversLicense", driver.DriversLicense);
            cd.Parameters.AddWithValue("@TruckMake", driver.TruckMake);
            cd.Parameters.AddWithValue("@TruckTag", driver.TruckTag);
            cd.Parameters.AddWithValue("@TruckColor", driver.TruckColor);
            cd.Parameters.AddWithValue("@TrailerType", driver.TrailerType);
            cd.Parameters.AddWithValue("@Capacity", driver.Capacity);
            cd.Parameters.AddWithValue("@Size", driver.Size);
            cd.Parameters.AddWithValue("@Misc", driver.Misc);
            cd.Parameters.AddWithValue("@Notifications", driver.Notifications);
            cd.Parameters.AddWithValue("@BackgroundSync", driver.BackgroundSync);

            SqlParameter outputparm = new SqlParameter("@responseMessage", SqlDbType.Int)
            {
                Direction = ParameterDirection.Output
            };

            cd.Parameters.Add(outputparm);

            try
            {
                //bool i = (cd.ExecuteNonQuery() == 1);
                cd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataDrivers.InsertDriver");
            }


            int results = Convert.ToInt32(outputparm.Value);

            if (cn.State != ConnectionState.Closed) { cn.Close(); }       

            return results;
        }

        public int UpdateDriver(Driver driver)
        {
            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spDriver_Update";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@ID", driver.ID);
            cd.Parameters.AddWithValue("@FirstName", driver.FirstName);
            cd.Parameters.AddWithValue("@LastName", driver.LastName);
            cd.Parameters.AddWithValue("@Cellphone", driver.Cellphone);
            cd.Parameters.AddWithValue("@EmailAddress", driver.EmailAddress);
            cd.Parameters.AddWithValue("@MCNumber", driver.MCNumber);
            cd.Parameters.AddWithValue("@TruckNumber", driver.TruckNumber);
            cd.Parameters.AddWithValue("@TrailerNumber", driver.TrailerNumber);
            cd.Parameters.AddWithValue("@DeviceID", driver.DeviceID);
            cd.Parameters.AddWithValue("@DriversLicense", driver.DriversLicense);
            cd.Parameters.AddWithValue("@TruckMake", driver.TruckMake);
            cd.Parameters.AddWithValue("@TruckTag", driver.TruckTag);
            cd.Parameters.AddWithValue("@TruckColor", driver.TruckColor);
            cd.Parameters.AddWithValue("@TrailerType", driver.TrailerType);
            cd.Parameters.AddWithValue("@Capacity", driver.Capacity);
            cd.Parameters.AddWithValue("@Size", driver.Size);
            cd.Parameters.AddWithValue("@Misc", driver.Misc);
            cd.Parameters.AddWithValue("@Notifications", driver.Notifications);
            cd.Parameters.AddWithValue("@BackgroundSync", driver.BackgroundSync);

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
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataDrivers.UpdateDriver");
            }

            int results = Convert.ToInt32(outputparm.Value);

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return results;
        }

        public int UpdateDriverProfile(Driver driver)
        {
            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spDriverProfile_Update";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@ID", driver.ID);
            cd.Parameters.AddWithValue("@Profile", driver.Profile);

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
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataDrivers.UpdateDriverProfile");
            }

            int results = Convert.ToInt32(outputparm.Value);

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return results;
        }

        public int UpdatePassword(Driver driver, string ipaddress)
        {
            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spDriverPassword_Update";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@ID", driver.ID);
            cd.Parameters.AddWithValue("@EmailAddress", driver.EmailAddress);
            cd.Parameters.AddWithValue("@Password", driver.Password);
            cd.Parameters.AddWithValue("@IP", ipaddress);

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
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataDrivers.UpdatePassword");
            }

            int results = Convert.ToInt32(outputparm.Value);

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return results;
        }

        public bool DeleteDriver(int ID)
        {
            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spDriver_Delete";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@ID", ID);

            bool i = false; ;

            try
            {
                i = (cd.ExecuteNonQuery() == 1);
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataDrivers.DeleteDriver");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return i;
        }

        public int LoginDriver(string email, string password, string ipaddress)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            //Response ID Enumeration 0 = No Matching Email, 1 = Wrong Password , DriverID (over 100)

            string strsql = "spDriverLogin";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@email", email);
            cd.Parameters.AddWithValue("@password", password);
            cd.Parameters.AddWithValue("@ipaddress", ipaddress);


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
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataDrivers.LoginDriver");
            }

            int results = Convert.ToInt32(outputparm.Value);

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return results;

        }

        public string ResetDriverPassword(Driver driver, byte[] Token, string ipaddress)
        {
            string msg = "";

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spDriverResetTicket_Verify";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@email", driver.EmailAddress);
            cd.Parameters.AddWithValue("@TokenHash", Token);
            cd.Parameters.AddWithValue("@ipaddress", ipaddress);

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
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataDrivers.ResetDriverPassword");
            }

            int results = Convert.ToInt32(outputparm.Value);

            switch (results)
            {
                case 0:
                    msg = "Error Searching Reset Token";

                    break;

                case 1:
                    msg = "No Matching Reset Token";

                    break;

                default:
                    bool includeLowercase = true;
                    bool includeUppercase = true;
                    bool includeNumeric = true;
                    bool includeSpecial = false;
                    bool includeSpaces = false;
                    int lengthOfPassword = 10;

                    string password = PasswordGenerator.GeneratePassword(includeLowercase, includeUppercase, includeNumeric, includeSpecial, includeSpaces, lengthOfPassword);

                    while (!PasswordGenerator.PasswordIsValid(includeLowercase, includeUppercase, includeNumeric, includeSpecial, includeSpaces, password))
                    {
                        password = PasswordGenerator.GeneratePassword(includeLowercase, includeUppercase, includeNumeric, includeSpecial, includeSpaces, lengthOfPassword);
                    }

                    driver = GetDriver(driver.EmailAddress);

                    driver.Password = password;

                    int i = UpdatePassword(driver, ipaddress);

                    switch(i)
                    {
                        case 0:
                            msg = "Error Saving New Password";
                            break;
                        case 1:
                            msg = "Email and ID Does Not Match";
                            break;

                        default:
                            msg = password;
                            break;
                    }

                    break;
            }


            return msg;
        }

        public int ResetPasswordRequest(Driver driver, byte[] Token, string ipaddress)
        {
            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spDriverResetTicket_Insert";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@email", driver.EmailAddress);
            cd.Parameters.AddWithValue("@TokenHash", Token);
            cd.Parameters.AddWithValue("@expirationdate", DateTime.Now.AddDays(1));
            cd.Parameters.AddWithValue("@ipaddress", ipaddress);


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
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataDrivers.ResetPasswordRequest");
            }

            int results = Convert.ToInt32(outputparm.Value);

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return results;

        }

        public List<LoadMessages> GetDriverMessagesByDriver(int ID)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spDriverMessageByDriverID_Get";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@DriverID", ID);

            List<LoadMessages> list = new List<LoadMessages>();

            try
            { 
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {

                        LoadMessages obj = new LoadMessages();

                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["DriverTripID"] != DBNull.Value) { obj.DriverTripID = (int)reader["DriverTripID"]; }
                        if (reader["DriverID"] != DBNull.Value) { obj.DriverID = (int)reader["DriverID"]; }
                        if (reader["DriverName"] != DBNull.Value) { obj.DriverName = (string)reader["DriverName"]; }
                        if (reader["RepID"] != DBNull.Value) { obj.RepID = (int)reader["RepID"]; }
                        if (reader["CompanyRepName"] != DBNull.Value) { obj.CompanyRepName = (string)reader["CompanyRepName"]; }

                        obj.MessageType = new MessageType();
                        if (reader["MessageTypeID"] != DBNull.Value) { obj.MessageType.TypeOption = (MessageTypeOption)reader["MessageTypeID"]; }
                        if (reader["MessageDate"] != DBNull.Value) { obj.MessageDate = (DateTime)reader["MessageDate"]; }
                        if (reader["Message"] != DBNull.Value) { obj.Message = (string)reader["Message"]; }

                        obj.MessageFrom = new Messenger();
                        if (reader["MessageFromID"] != DBNull.Value) { obj.MessageFrom.MessengerID = (int)reader["MessageFromID"]; }
                        if (reader["MessageFrom"] != DBNull.Value) { obj.MessageFrom.MessengerType = (MessengerTypeOption)reader["MessageFrom"]; }

                        switch (obj.MessageFrom.MessengerType)
                        {
                            case MessengerTypeOption.Driver:
                                obj.MessageFrom.MessengerName = obj.DriverName;
                                if (reader["RepViewedDate"] != DBNull.Value) { obj.ViewedDate = (DateTime)reader["RepViewedDate"]; }
                                break;
                            case MessengerTypeOption.CompanyRep:
                                obj.MessageFrom.MessengerName = obj.CompanyRepName;
                                if (reader["DriverViewedDate"] != DBNull.Value) { obj.ViewedDate = (DateTime)reader["DriverViewedDate"]; }
                                break;
                            default:
                                break;
                        }

                        list.Add(obj);

                    }

                }
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataDrivers.GetDriverMessagesByDriver");
            }




            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return list;

        }

        public int InsertDriverMessage(LoadMessages message)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spDriverMessage_Insert";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;

            cd.Parameters.AddWithValue("@MessageTypeID", (int)message.MessageType.TypeOption);
            cd.Parameters.AddWithValue("@Message", message.Message);
            cd.Parameters.AddWithValue("@MessageFromID", message.MessageFrom.MessengerID);
            cd.Parameters.AddWithValue("@MessageFrom", (int)message.MessageFrom.MessengerType);
            cd.Parameters.AddWithValue("@DriverID", message.DriverID);
            cd.Parameters.AddWithValue("@RepID", message.RepID);

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
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataDrivers.InsertDriverMessage");
            }

            int results = Convert.ToInt32(outputparm.Value);

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return results;
        }

        public LoadMessages GetDriverMessage(int ID)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spLoadMessageByID_Get";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@ID", ID);

            LoadMessages obj = new LoadMessages();

            try
            { 
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {

                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["DriverTripID"] != DBNull.Value) { obj.DriverTripID = (int)reader["DriverTripID"]; }
                        if (reader["DriverID"] != DBNull.Value) { obj.DriverID = (int)reader["DriverID"]; }
                        if (reader["DriverName"] != DBNull.Value) { obj.DriverName = (string)reader["DriverName"]; }
                        if (reader["RepID"] != DBNull.Value) { obj.RepID = (int)reader["RepID"]; }
                        if (reader["CompanyRepName"] != DBNull.Value) { obj.CompanyRepName = (string)reader["CompanyRepName"]; }

                        obj.MessageType = new MessageType();
                        if (reader["MessageTypeID"] != DBNull.Value) { obj.MessageType.TypeOption = (MessageTypeOption)reader["MessageTypeID"]; }
                        if (reader["MessageDate"] != DBNull.Value) { obj.MessageDate = (DateTime)reader["MessageDate"]; }
                        if (reader["Message"] != DBNull.Value) { obj.Message = (string)reader["Message"]; }

                        obj.MessageFrom = new Messenger();
                        if (reader["MessageFromID"] != DBNull.Value) { obj.MessageFrom.MessengerID = (int)reader["MessageFromID"]; }
                        if (reader["MessageFrom"] != DBNull.Value) { obj.MessageFrom.MessengerType = (MessengerTypeOption)reader["MessageFrom"]; }

                        switch (obj.MessageFrom.MessengerType)
                        {
                            case MessengerTypeOption.Driver:
                                obj.MessageFrom.MessengerName = obj.DriverName;
                                if (reader["RepViewedDate"] != DBNull.Value) { obj.ViewedDate = (DateTime)reader["RepViewedDate"]; }
                                break;
                            case MessengerTypeOption.CompanyRep:
                                obj.MessageFrom.MessengerName = obj.CompanyRepName;
                                if (reader["DriverViewedDate"] != DBNull.Value) { obj.ViewedDate = (DateTime)reader["DriverViewedDate"]; }
                                break;
                            default:
                                break;
                        }

                        obj.ResultMessage = "Successful";

                    }

                }
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataDrivers.GetDriverMessage");
            }



            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return obj;

        }


        public List<DriverDevice> GetDriverDevices(int ID)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spDriverDevices_Get";

            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@DriverID", ID);

            List<DriverDevice> list = new List<DriverDevice>();

            try
            { 
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {

                        DriverDevice obj = new DriverDevice();

                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["DriverID"] != DBNull.Value) { obj.DriverID = (int)reader["DriverID"]; }
                        if (reader["DeviceID"] != DBNull.Value) { obj.DeviceID = (string)reader["DeviceID"]; }
                        if (reader["DeviceType"] != DBNull.Value) { obj.DeviceType = (string)reader["DeviceType"]; }
                        if (reader["Token"] != DBNull.Value) { obj.Token = (string)reader["Token"]; }

                        if (reader["Created"] != DBNull.Value) { obj.Created = (DateTime)reader["Created"]; }
                        if (reader["Updated"] != DBNull.Value) { obj.Updated = (DateTime)reader["Updated"]; }

                        list.Add(obj);

                    }

                }
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataDrivers.GetDriverDevices");
            }


            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return list;

        }

        public DriverDevice GetDriverCurrentDevice(Driver driver)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spDriverDevicesCurrent_Get";

            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@DriverID", driver.ID);
            cd.Parameters.AddWithValue("@DeviceID", driver.DeviceID);

            DriverDevice obj = new DriverDevice();

            try
            { 
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {
                    
                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["DriverID"] != DBNull.Value) { obj.DriverID = (int)reader["DriverID"]; }
                        if (reader["DeviceID"] != DBNull.Value) { obj.DeviceID = (string)reader["DeviceID"]; }
                        if (reader["DeviceType"] != DBNull.Value) { obj.DeviceType = (string)reader["DeviceType"]; }
                        if (reader["Token"] != DBNull.Value) { obj.Token = (string)reader["Token"]; }

                        if (reader["Created"] != DBNull.Value) { obj.Created = (DateTime)reader["Created"]; }
                        if (reader["Updated"] != DBNull.Value) { obj.Updated = (DateTime)reader["Updated"]; }

                    }
                }
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataDrivers.GetDriverCurrentDevice");
            }


            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return obj;

        }

        public int InsertDriverToken(DriverDevice driverdevice)
        {
            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            //DriverDevice ID Enumeration - 0 = Error, 1 = Duplicate Email, 2 = Duplicate mc Number, DriverDeviceID (over 100)

            string strsql = "spDriverDevice_Insert";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;

            cd.Parameters.AddWithValue("@DriverID", driverdevice.DriverID);
            cd.Parameters.AddWithValue("@DeviceID", driverdevice.DeviceID);
            cd.Parameters.AddWithValue("@DeviceType", driverdevice.DeviceType);
            cd.Parameters.AddWithValue("@Token", driverdevice.Token);
            cd.Parameters.AddWithValue("@Updated", DateTime.Now);

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
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataDrivers.InsertDriverToken");
            }

            int results = Convert.ToInt32(outputparm.Value);

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return results;
        }

        public int DeleteDriverToken(DriverDevice driverdevice)
        {
            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            //DriverDevice ID Enumeration - 0 = Error, 1 = Duplicate Email, 2 = Duplicate mc Number, DriverDeviceID (over 100)

            string strsql = "spDriverDevice_Delete";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;

            cd.Parameters.AddWithValue("@DriverID", driverdevice.ID);
            cd.Parameters.AddWithValue("@DeviceID", driverdevice.DeviceID);

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
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataDrivers.DeleteDriverToken");
            }

            int results = Convert.ToInt32(outputparm.Value);

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return results;
        }


        public int InsertCompany(Company company)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            //Company ID Enumeration -0 = Error, CompanyID (over 100)

            string strsql = "spCompany_Insert";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@CompanyName", company.CompanyName);

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
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataDrivers.InsertCompany");
            }

            int results = Convert.ToInt32(outputparm.Value);

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return results;
        }

        public int UpdateCompany(Company company)
        {
            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spCompany_Update";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@ID", company.ID);
            cd.Parameters.AddWithValue("@CompanyName", company.CompanyName);

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
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataDrivers.UpdateCompany");
            }

            int results = Convert.ToInt32(outputparm.Value);

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return results;
        }

        public List<Company> GetAllCompany()
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spCompanyAll_Get";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;

            List<Company> list = new List<Company>();

            try
            {

                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {

                        Company obj = new Company();
                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["CompanyName"] != DBNull.Value) { obj.CompanyName = (string)reader["CompanyName"]; }

                        list.Add(obj);

                    }

                }

            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataDrivers.GetAllCompany");
            }



            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return list;

        }


    }

    public class DataCompanyReps : DataAccess
    {
        public List<CompanyReps> GetAllCompanyReps()
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spCompanyRepAll_Get";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;

            List<CompanyReps> list = new List<CompanyReps>();

            try
            { 
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {

                        CompanyReps obj = new CompanyReps();

                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["RepID"] != DBNull.Value) { obj.RepID = (int)reader["RepID"]; }
                        if (reader["FullName"] != DBNull.Value) { obj.FullName = (string)reader["FullName"]; }
                        if (reader["EmailAddress"] != DBNull.Value) { obj.EmailAddress = (string)reader["EmailAddress"]; }
                        if (reader["Phone"] != DBNull.Value) { obj.Phone = (string)reader["Phone"]; }

                        list.Add(obj);

                    }

                }
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataCompanyReps.GetAllCompanyReps");
            }


            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return list;

        }

        public CompanyReps GetCompanyRep(int ID)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spCompanyRepByID_Get";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@ID", ID);

            CompanyReps obj = new CompanyReps();

            try
            { 
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {

                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["RepID"] != DBNull.Value) { obj.RepID = (int)reader["RepID"]; }
                        if (reader["FullName"] != DBNull.Value) { obj.FullName = (string)reader["FullName"]; }
                        if (reader["EmailAddress"] != DBNull.Value) { obj.EmailAddress = (string)reader["EmailAddress"]; }
                        if (reader["Phone"] != DBNull.Value) { obj.Phone = (string)reader["Phone"]; }

                    }

                }
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataCompanyReps.GetCompanyRep");
            }



            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return obj;

        }

        public CompanyReps GetCompanyRepByTrip(int ID)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spCompanyRepByTripID_Get";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@TripID", ID);

            CompanyReps obj = new CompanyReps();

            try
            { 
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {

                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["RepID"] != DBNull.Value) { obj.RepID = (int)reader["RepID"]; }
                        if (reader["FullName"] != DBNull.Value) { obj.FullName = (string)reader["FullName"]; }
                        if (reader["EmailAddress"] != DBNull.Value) { obj.EmailAddress = (string)reader["EmailAddress"]; }
                        if (reader["Phone"] != DBNull.Value) { obj.Phone = (string)reader["Phone"]; }

                    }

                }
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataCompanyReps.GetCompanyRepByTrip");
            }



            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return obj;

        }

        public int InsertCompanyRep(CompanyReps Rep)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spCompanyRep_Insert";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@RepID", Rep.RepID);
            cd.Parameters.AddWithValue("@FullName", Rep.FullName);
            cd.Parameters.AddWithValue("@Email", Rep.EmailAddress);
            cd.Parameters.AddWithValue("@Phone", Rep.Phone);

            SqlParameter outputparm = new SqlParameter("@responseMessage", SqlDbType.Int)
            {
                Direction = ParameterDirection.Output
            };

            try
            {
                cd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataDrivers.InsertCompanyRep");
            }

            int results = Convert.ToInt32(outputparm.Value);

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return results;
        }

        public bool UpdateCompanyRep(CompanyReps Rep)
        {
            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spCompanyRep_Update";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@ID", Rep.ID);
            cd.Parameters.AddWithValue("@RepID", Rep.RepID);
            cd.Parameters.AddWithValue("@FullName", Rep.FullName);
            cd.Parameters.AddWithValue("@EmailAddress", Rep.EmailAddress);
            cd.Parameters.AddWithValue("@Phone", Rep.Phone);

            bool i = false;
            try
            {
                i = (cmd.ExecuteNonQuery() == 1);
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataCompanyReps.UpdateCompanyRep");
            }


            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return i;
        }

        public bool DeleteCompanyRep(int ID)
        {
            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spCompanyRep_Delete";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@ID", ID);


            bool i = false;
            try
            {
                i = (cmd.ExecuteNonQuery() == 1);
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataCompanyReps.DeleteCompanyRep");
            }


            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return i;
        }



    }

    public class DataLoads : DataAccess
    {
        public Loads GetLoad(int ID)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spLoadsByLoadID_Get";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@LoadID", ID);

            Loads obj = new Loads();


            try
            { 
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {

                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["LoadID"] != DBNull.Value) { obj.LoadID = (int)reader["LoadID"]; }
                        if (reader["TripID"] != DBNull.Value) { obj.TripID = (int)reader["TripID"]; }

                        if (reader["LoadStatus"] != DBNull.Value) { obj.LoadStatus = (string)reader["LoadStatus"]; }
                        if (reader["Temp"] != DBNull.Value) { obj.Temp = (string)reader["Temp"]; }
                        if (reader["TotalPallets"] != DBNull.Value) { obj.TotalPallets = (int)reader["TotalPallets"]; }
                        if (reader["TotalWeight"] != DBNull.Value) { obj.TotalWeight = (int)reader["TotalWeight"]; }
                        if (reader["TotalPieces"] != DBNull.Value) { obj.TotalPieces = (int)reader["TotalPieces"]; }
                        if (reader["TotalMiles"] != DBNull.Value) { obj.TotalMiles = (int)reader["TotalMiles"]; }
                        if (reader["HazMat"] != DBNull.Value) { obj.HazMat = (bool)reader["HazMat"]; }

                        if (reader["LoadStatus"] != DBNull.Value)
                        {
                            obj.CompanyRep = new CompanyReps();
                            obj.CompanyRep.RepID = (int)reader["RepID"];
                            obj.CompanyRep.FullName = (string)reader["CompanyRepName"];
                            obj.CompanyRep.EmailAddress = (string)reader["CompanyRepEmail"];
                            obj.CompanyRep.Phone = (string)reader["CompanyRepPhone"];
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.GetLoad");
            }


            
            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            obj.LoadStops = GetLoadStops(obj);

            return obj;

        }


        public DriverTrip GetDriverActiveLoad(int ID)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spDriverLoadActive_Get";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@DriverID", ID);

            DriverTrip obj = new DriverTrip();

            try
            { 
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {
                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["LoadID"] != DBNull.Value) { obj.LoadID = (int)reader["LoadID"]; }
                        if (reader["TripID"] != DBNull.Value) { obj.TripID = (int)reader["TripID"]; }
                        if (reader["DriverID"] != DBNull.Value) { obj.DriverID = (int)reader["DriverID"]; }

                        if (reader["Active"] != DBNull.Value) { obj.Active = (bool)reader["Active"]; }
                        if (reader["ActiveDate"] != DBNull.Value) { obj.ActiveDate = (DateTime)reader["ActiveDate"]; }
                    }
                }
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.GetDriverActiveLoad");
            }


            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            obj.Load = GetLoad(obj.LoadID);

            DataDrivers dd = new DataDrivers();
            obj.Driver = dd.GetDriver(obj.DriverID, false);

            return obj;

        }

        public List<DriverTrip> GetDriverLoadHistory(int ID)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spDriverLoadsByDriverID_Get";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@DriverID", ID);

            List<DriverTrip> HistoryList = new List<DriverTrip>();

            try
            { 
                using (SqlDataReader reader = cd.ExecuteReader())
                {
                

                    while (reader.Read())
                    {

                        DriverTrip obj = new DriverTrip();

                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["LoadID"] != DBNull.Value) { obj.LoadID = (int)reader["LoadID"]; }
                        if (reader["TripID"] != DBNull.Value) { obj.TripID = (int)reader["TripID"]; }
                        if (reader["DriverID"] != DBNull.Value) { obj.DriverID = (int)reader["DriverID"]; }

                        if (reader["Active"] != DBNull.Value) { obj.Active = (bool)reader["Active"]; }
                        if (reader["ActiveDate"] != DBNull.Value) { obj.ActiveDate = (DateTime)reader["ActiveDate"]; }

                        HistoryList.Add(obj);
                    }

                
                }
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.GetDriverLoadHistory");
            }


            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            DataDrivers dd = new DataDrivers();
            foreach (DriverTrip ls in HistoryList)
            {
                ls.Load = GetLoad(ls.LoadID);
                //ls.Driver = dd.GetDriver(ls.DriverID, false);
            }

            return HistoryList;

        }

        public List<LoadStops> GetLoadStops(Loads Load)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spLoadStopsByLoadID_Get";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@LoadID", Load.LoadID);

            List<LoadStops> list = new List<LoadStops>();
            try
            { 
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {
                        LoadStops obj = new LoadStops();

                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["LoadDetailID"] != DBNull.Value) { obj.LoadDetailID = (int)reader["LoadDetailID"]; }
                        if (reader["LoadID"] != DBNull.Value) { obj.LoadID = (int)reader["LoadID"]; }
                        if (reader["LoadStopNumber"] != DBNull.Value) { obj.LoadStopNumber = (int)reader["LoadStopNumber"]; }
                        if (reader["StopTypeID"] != DBNull.Value) { obj.StopTypeID = (int)reader["StopTypeID"]; }
                        if (reader["StopType"] != DBNull.Value) { obj.StopType = (string)reader["StopType"]; }
                        if (reader["ScheduleDate"] != DBNull.Value) { obj.ScheduleDate = (DateTime)reader["ScheduleDate"]; }
                        if (reader["ScheduleTimeFrom"] != DBNull.Value) { obj.ScheduleTimeFrom = ((string)reader["ScheduleTimeFrom"]).Trim(); }
                        if (reader["ScheduleTimeTo"] != DBNull.Value) { obj.ScheduleTimeTo = ((string)reader["ScheduleTimeTo"]).Trim(); }
                        if (reader["Name"] != DBNull.Value) { obj.Name = (string)reader["Name"]; }
                        if (reader["Address1"] != DBNull.Value) { obj.Address1 = (string)reader["Address1"]; }
                        if (reader["Address2"] != DBNull.Value) { obj.Address2 = (string)reader["Address2"]; }
                        if (reader["City"] != DBNull.Value) { obj.City = (string)reader["City"]; }
                        if (reader["State"] != DBNull.Value) { obj.State = (string)reader["State"]; }
                        if (reader["Zip"] != DBNull.Value) { obj.Zip = (string)reader["Zip"]; }
                        if (reader["Contact"] != DBNull.Value) { obj.Contact = (string)reader["Contact"]; }
                        if (reader["Phone"] != DBNull.Value) { obj.Phone = (string)reader["Phone"]; }
                        if (reader["AltPhone"] != DBNull.Value) { obj.AltPhone = (string)reader["AltPhone"]; }
                        obj.GPSCoordinates = new GPSLocation();
                        if (reader["Lat"] != DBNull.Value) { obj.GPSCoordinates.Lat = (string)reader["Lat"]; }
                        if (reader["Long"] != DBNull.Value) { obj.GPSCoordinates.Long = (string)reader["Long"]; }
                        if (reader["CommodityDesc"] != DBNull.Value) { obj.CommodityDesc = ((string)reader["CommodityDesc"]).Trim(); }
                        if (reader["Weight"] != DBNull.Value) { obj.Weight = (int)reader["Weight"]; }
                        if (reader["Pieces"] != DBNull.Value) { obj.Pieces = (int)reader["Pieces"]; }
                        if (reader["Pallets"] != DBNull.Value) { obj.Pallets = (int)reader["Pallets"]; }

                        list.Add(obj);
                    }
                }

            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.GetLoadStops");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            foreach(LoadStops ls in list)
            {
                ls.ReferenceNumbers = GetReferenceNumbers(ls);
            }

            return list;

        }

        public List<ReferenceNumbers> GetReferenceNumbers(LoadStops LoadStop)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spReferenceNumbersByLoadDetailID_Get";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@LoadDetailID", LoadStop.LoadDetailID);

            List<ReferenceNumbers> list = new List<ReferenceNumbers>();

            try
            { 
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {
                        ReferenceNumbers obj = new ReferenceNumbers();

                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["LoadDetailID"] != DBNull.Value) { obj.LoadDetailID = (int)reader["LoadDetailID"]; }
                        if (reader["ReferenceNumberID"] != DBNull.Value) { obj.ReferenceNumberID = (int)reader["ReferenceNumberID"]; }
                        if (reader["ReferenceNumberType"] != DBNull.Value) { obj.ReferenceNumberType = (string)reader["ReferenceNumberType"]; }
                        if (reader["ReferenceNumber"] != DBNull.Value) { obj.ReferenceNumber = (string)reader["ReferenceNumber"]; }

                        list.Add(obj);
                    }
                }
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.GetReferenceNumbers");
            }


            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return list;

        }


        public int ConfirmLoad(LoadConfirms Confirm, string ipaddress)
        {
            string msg = "";
            int LoadID = 0;

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spLoadConfirm_Verify";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@DriverID", Confirm.Driver.ID);
            cd.Parameters.AddWithValue("@Code", Confirm.ConfirmCode.ToUpper());
            cd.Parameters.AddWithValue("@ipaddress", ipaddress);

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
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.ConfirmLoad");
            }


            int results = Convert.ToInt32(outputparm.Value);

            switch (results)
            {
                case 0:
                    msg = "No Matching Confirm Code";

                    break;

                case 1:
                    msg = "Load Missing";

                    break;

                default:
                    strsql = "spLoadConfirm_Confirm";

                    cd = new SqlCommand(strsql, cn);
                    cd.CommandType = CommandType.StoredProcedure;
                    cd.Parameters.AddWithValue("@LoadID", results);
                    cd.Parameters.AddWithValue("@Code", Confirm.ConfirmCode);
                    cd.Parameters.AddWithValue("@DriverID", Confirm.Driver.ID);
                    cd.Parameters.AddWithValue("@ipaddress", ipaddress);
                    cd.Parameters.AddWithValue("@Lat", Confirm.GPS.Lat);
                    cd.Parameters.AddWithValue("@Long", Confirm.GPS.Long);
                    cd.Parameters.AddWithValue("@Offset", Confirm.Offset);
                    cd.Parameters.AddWithValue("@Timezone", Confirm.Timezone);

                    outputparm = new SqlParameter("@responseMessage", SqlDbType.Int)
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
                        DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.ConfirmLoad b");
                    }

                    int i = Convert.ToInt32(outputparm.Value);

                    switch (i)
                    {
                        case 0:
                            msg = "No Matching Confirm Code";
                            break;
                        case 1:
                            msg = "Load Missing";
                            break;

                        default:

                            LoadID = i;
                            break;
                    }

                    break;
            }

            return LoadID;
        }

        public LoadConfirms InsertLoadConfirm(LoadConfirms Confirm)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spLoadConfirm_Insert";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@LoadID", Confirm.LoadID);
            cd.Parameters.AddWithValue("@ConfirmCode", Confirm.ConfirmCode);
            cd.Parameters.AddWithValue("@ExpirationDate", Confirm.ExpirationDate);

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
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.InsertLoadConfirm");
            }

            int i = Convert.ToInt32(outputparm.Value);

            switch (i)
            {
                case 0:
                    Confirm.Message = "Load Does Not Exist";
                    break;
                case 1:
                    Confirm.Message = "DriverTrip / Load Already Active";
                    break;

                case 2:
                    Confirm.Message = "Confirm Code Already Exists";
                    break;

                case 3:
                    Confirm.Message = "Load Already Completed";
                    break;

                default:
                    Confirm.ConfirmID = i;
                    Confirm.Message = "Successful";
                    break;
            }


            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return Confirm;
        }


        public int InsertLoad(Loads load)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spLoads_Insert";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@LoadID", load.LoadID);
            cd.Parameters.AddWithValue("@LoadStatus", load.LoadStatus);
            cd.Parameters.AddWithValue("@RepID", load.CompanyRep.RepID);
            cd.Parameters.AddWithValue("@Temp", load.Temp);
            cd.Parameters.AddWithValue("@TotalPallets", load.TotalPallets);
            cd.Parameters.AddWithValue("@TotalWeight", load.TotalWeight);
            cd.Parameters.AddWithValue("@TotalPieces", load.TotalPieces);
            cd.Parameters.AddWithValue("@TotalMiles", load.TotalMiles);
            cd.Parameters.AddWithValue("@Hazmat", load.HazMat);
            cd.Parameters.AddWithValue("@TripID", load.TripID);

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
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.InsertLoad");
            }

            int results = Convert.ToInt32(outputparm.Value);

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            if(results > 0)
            {

                foreach(LoadStops ls in load.LoadStops)
                {
                    int ils = InsertLoadStop(ls);

                    if(ils > 0)
                    {
                        foreach(ReferenceNumbers Refs in ls.ReferenceNumbers)
                        {
                            int iRef = InsertReferenceNumber(Refs);
                        }
                    }
                }
            }

            return results;

        }

        public bool UpdateLoad(Loads load)
        {
            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spLoads_Update";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@LoadID", load.LoadID);
            cd.Parameters.AddWithValue("@LoadStatus", load.LoadStatus);
            cd.Parameters.AddWithValue("@RepID", load.CompanyRep.RepID);
            cd.Parameters.AddWithValue("@Temp", load.Temp);
            cd.Parameters.AddWithValue("@TotalPallets", load.TotalPallets);
            cd.Parameters.AddWithValue("@TotalWeight", load.TotalWeight);
            cd.Parameters.AddWithValue("@TotalPieces", load.TotalPieces);
            cd.Parameters.AddWithValue("@Hazmat", load.HazMat);
            cd.Parameters.AddWithValue("@TripID", load.TripID);

            //cd.Parameters.Add(new SqlParameter("@ID", load.ID));

            bool i = false;
            try
            {
                i = (cd.ExecuteNonQuery() == 1);
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.UpdateLoad");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return i;
        }

        public bool DeleteLoad(Loads load)
        {
            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spLoads_Delete";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@LoadID", load.LoadID);


            bool i = false;
            try
            {
                i = (cd.ExecuteNonQuery() == 1);
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.DeleteLoad");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return i;
        }


        public int InsertLoadStop(LoadStops loadstop)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spLoadStops_Insert";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;

            cd.Parameters.AddWithValue("@LoadID", loadstop.LoadID);
            cd.Parameters.AddWithValue("@LoadDetailID", loadstop.LoadDetailID);
            cd.Parameters.AddWithValue("@LoadStopNumber", loadstop.LoadStopNumber);
            cd.Parameters.AddWithValue("@StopTypeID", loadstop.StopTypeID);
            cd.Parameters.AddWithValue("@StopType", loadstop.StopType);
            cd.Parameters.AddWithValue("@ScheduleDate", loadstop.ScheduleDate);
            cd.Parameters.AddWithValue("@ScheduleTimeFrom", loadstop.ScheduleTimeFrom);
            cd.Parameters.AddWithValue("@ScheduleTimeTo", loadstop.ScheduleTimeTo);
            cd.Parameters.AddWithValue("@Name", loadstop.Name);
            cd.Parameters.AddWithValue("@Address1", loadstop.Address1);
            cd.Parameters.AddWithValue("@Address2", loadstop.Address2);
            cd.Parameters.AddWithValue("@City", loadstop.City);
            cd.Parameters.AddWithValue("@State", loadstop.State);
            cd.Parameters.AddWithValue("@Zip", loadstop.Zip);
            cd.Parameters.AddWithValue("@Contact", loadstop.Contact);
            cd.Parameters.AddWithValue("@Phone", loadstop.Phone);
            cd.Parameters.AddWithValue("@AltPhone", loadstop.AltPhone);
            cd.Parameters.AddWithValue("@Lat", loadstop.GPSCoordinates.Lat);
            cd.Parameters.AddWithValue("@Long", loadstop.GPSCoordinates.Long);
            cd.Parameters.AddWithValue("@CommodityDesc", loadstop.CommodityDesc);
            cd.Parameters.AddWithValue("@Weight", loadstop.Weight);
            cd.Parameters.AddWithValue("@Pieces", loadstop.Pieces);
            cd.Parameters.AddWithValue("@Pallets", loadstop.Pallets);

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
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.InsertLoadStop");
            }

            int results = Convert.ToInt32(outputparm.Value);

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return results;
        }

        public bool UpdateLoadStop(LoadStops loadstop)
        {
            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spLoadStops_Update";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;

            //cd.Parameters.Add(new SqlParameter("@LoadID", loadstop.LoadID));
            cd.Parameters.AddWithValue("@LoadDetailID", loadstop.LoadDetailID);
            cd.Parameters.AddWithValue("@LoadStopNumber", loadstop.LoadStopNumber);
            cd.Parameters.AddWithValue("@StopTypeID", loadstop.StopTypeID);
            cd.Parameters.AddWithValue("@StopType", loadstop.StopType);
            cd.Parameters.AddWithValue("@ScheduleDate", loadstop.ScheduleDate);
            cd.Parameters.AddWithValue("@ScheduleTimeFrom", loadstop.ScheduleTimeFrom);
            cd.Parameters.AddWithValue("@ScheduleTimeTo", loadstop.ScheduleTimeTo);
            cd.Parameters.AddWithValue("@Name", loadstop.Name);
            cd.Parameters.AddWithValue("@Address1", loadstop.Address1);
            cd.Parameters.AddWithValue("@Address2", loadstop.Address2);
            cd.Parameters.AddWithValue("@City", loadstop.City);
            cd.Parameters.AddWithValue("@State", loadstop.State);
            cd.Parameters.AddWithValue("@Zip", loadstop.Zip);
            cd.Parameters.AddWithValue("@Contact", loadstop.Contact);
            cd.Parameters.AddWithValue("@Phone", loadstop.Phone);
            cd.Parameters.AddWithValue("@AltPhone", loadstop.AltPhone);
            cd.Parameters.AddWithValue("@Lat", loadstop.GPSCoordinates.Lat);
            cd.Parameters.AddWithValue("@Long", loadstop.GPSCoordinates.Long);
            cd.Parameters.AddWithValue("@CommodityDesc", loadstop.CommodityDesc);
            cd.Parameters.AddWithValue("@Weight", loadstop.Weight);
            cd.Parameters.AddWithValue("@Pieces", loadstop.Pieces);
            cd.Parameters.AddWithValue("@Pallets", loadstop.Pallets);


            bool i = false;
            try
            {
                i = (cd.ExecuteNonQuery() == 1);
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.UpdateLoadStop");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return i;
        }

        public bool DeleteLoadStop(LoadStops loadstop)
        {
            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spLoadStops_Delete";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@LoadDetailID", loadstop.LoadDetailID);


            bool i = false;
            try
            {
                i = (cd.ExecuteNonQuery() == 1);
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.DeleteLoadStop");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return i;
        }


        public int InsertReferenceNumber(ReferenceNumbers refnumber)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spReferenceNumbers_Insert";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@ReferenceNumberID", refnumber.ReferenceNumberID);
            cd.Parameters.AddWithValue("@LoadDetailID", refnumber.LoadDetailID);
            cd.Parameters.AddWithValue("@ReferenceNumberType", refnumber.ReferenceNumberType);
            cd.Parameters.AddWithValue("@ReferenceNumber", refnumber.ReferenceNumber);

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
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.InsertReferenceNumber");
            }

            int results = Convert.ToInt32(outputparm.Value);

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return results;
        }

        public bool UpdateReferenceNumber(ReferenceNumbers refnumber)
        {
            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spReferenceNumber_Update";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@ReferenceNumberID", refnumber.ReferenceNumberID);
            cd.Parameters.AddWithValue("@LoadDetailID", refnumber.LoadDetailID);
            cd.Parameters.AddWithValue("@ReferenceNumberType", refnumber.ReferenceNumberType);
            cd.Parameters.AddWithValue("@ReferenceNumber", refnumber.ReferenceNumber);

            bool i = false;
            try
            {
                i = (cd.ExecuteNonQuery() == 1);
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.UpdateReferenceNumber");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return i;
        }

        public bool DeleteReferenceNumber(ReferenceNumbers refnumber)
        {
            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spReferenceNumber_Delete";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@ReferenceNumberID", refnumber.ReferenceNumberID);

            bool i = false;
            try
            {
                i = (cd.ExecuteNonQuery() == 1);
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.DeleteReferenceNumber");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return i;
        }

        public List<TripPath> DriverTripPath(int ID)
        {
            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spDriverTripLocationHistory";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@DriverTripID", ID);

            List<TripPath> list = new List<TripPath>();

            try
            { 
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {

                        TripPath obj = new TripPath();

                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["EventType"] != DBNull.Value) { obj.EventType = (string)reader["EventType"]; }
                        if (reader["EventDate"] != DBNull.Value) { obj.EventDate = (DateTime)reader["EventDate"]; }
                        if (reader["Message"] != DBNull.Value) { obj.Message = (string)reader["Message"]; }

                        obj.GPSLocation = new GPSLocation();
                        if (reader["Lat"] != DBNull.Value) { obj.GPSLocation.Lat = (string)reader["Lat"]; }
                        if (reader["Long"] != DBNull.Value) { obj.GPSLocation.Long = (string)reader["Long"]; }

                        if (reader["Offset"] != DBNull.Value) { obj.Offset = (string)reader["Offset"]; }
                        if (reader["Timezone"] != DBNull.Value) { obj.Timezone = (string)reader["Timezone"]; }

                        list.Add(obj);

                    }

                }
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.DriverTripPath");
            }


            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return list;
        }



        //Remove before Production
        public List<LoadConfirms> GetLoadConfirms()
        {
            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = @"Select LoadConfirms.*, Driver.FirstName, Driver.LastName, Loads.LoadStatus, DriverTrip.Active as DriverTripActive, ForcedStop, Completed, EndDate, LoadConfirms.Active  
            From LoadConfirms Left Outer join Driver on LoadConfirms.DriverID = Driver.ID
            Left Outer join DriverTrip on LoadConfirms.ConfirmID = DriverTrip.ConfirmID inner join Loads on LoadConfirms.LoadID = Loads.LoadID 
            Order by LoadConfirms.ConfirmID Desc";

            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.Text;

            List<LoadConfirms> LoadConfirmList = new List<LoadConfirms>();

            try
            { 
                using (SqlDataReader reader = cd.ExecuteReader())
                {
                    while (reader.Read())
                    {

                        LoadConfirms obj = new LoadConfirms();

                        if (reader["ConfirmID"] != DBNull.Value) { obj.ConfirmID = (int)reader["ConfirmID"]; }
                        if (reader["LoadID"] != DBNull.Value) { obj.LoadID = (int)reader["LoadID"]; }

                        if (reader["ConfirmCode"] != DBNull.Value) { obj.ConfirmCode = (string)reader["ConfirmCode"]; }
                        if (reader["ExpirationDate"] != DBNull.Value) { obj.ExpirationDate = (DateTime)reader["ExpirationDate"]; }
                        if (reader["LoadStatus"] != DBNull.Value) { obj.LoadStatus = (string)reader["LoadStatus"]; }
                        if (reader["Active"] != DBNull.Value) { obj.Active = (bool)reader["Active"]; }

                        obj.Driver = new Driver();
                        if (reader["DriverID"] != DBNull.Value) { obj.Driver.ID = (int)reader["DriverID"]; }
                        if (reader["FirstName"] != DBNull.Value) { obj.Driver.FirstName = (string)reader["FirstName"]; }
                        if (reader["LastName"] != DBNull.Value) { obj.Driver.LastName = (string)reader["LastName"]; }

                        if (reader["ConfirmDate"] != DBNull.Value) { obj.ConfirmDate = (DateTime)reader["ConfirmDate"]; }

                        obj.DriverTrip = new DriverTrip();
                        if (reader["DriverTripID"] != DBNull.Value) { obj.DriverTrip.ID = (int)reader["DriverTripID"]; }
                        if (reader["DriverTripActive"] != DBNull.Value) { obj.DriverTrip.Active = (bool)reader["DriverTripActive"]; }
                        if (reader["ForcedStop"] != DBNull.Value) { obj.DriverTrip.ForcedStop = (bool)reader["ForcedStop"]; }
                        if (reader["Completed"] != DBNull.Value) { obj.DriverTrip.Completed = (bool)reader["Completed"]; }
                        if (reader["EndDate"] != DBNull.Value) { obj.DriverTrip.EndDate = (DateTime)reader["EndDate"]; }

                        obj.GPS = new GPSLocation();
                        if (reader["Lat"] != DBNull.Value) { obj.GPS.Lat = (string)reader["Lat"]; }
                        if (reader["Long"] != DBNull.Value) { obj.GPS.Long = (string)reader["Long"]; }


                        LoadConfirmList.Add(obj);
                    }
                }

            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.GetLoadConfirms");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return LoadConfirmList;
        }

        public List<LoadConfirms> GetActiveLoadConfirms()
        {
            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = @"Select LoadConfirms.*, Driver.FirstName, Driver.LastName, Loads.LoadStatus, DriverTrip.Active as DriverTripActive, ForcedStop, Completed, EndDate, LoadConfirms.Active  
            From LoadConfirms Left Outer join Driver on LoadConfirms.DriverID = Driver.ID
            Left Outer join DriverTrip on LoadConfirms.ConfirmID = DriverTrip.ConfirmID inner join Loads on LoadConfirms.LoadID = Loads.LoadID 
            Where LoadConfirms.Active = 1 
            Order by LoadConfirms.ConfirmID Desc";

            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.Text;

            List<LoadConfirms> LoadConfirmList = new List<LoadConfirms>();

            try
            { 
                using (SqlDataReader reader = cd.ExecuteReader())
                {
                    while (reader.Read())
                    {

                        LoadConfirms obj = new LoadConfirms();

                        if (reader["ConfirmID"] != DBNull.Value) { obj.ConfirmID = (int)reader["ConfirmID"]; }
                        if (reader["LoadID"] != DBNull.Value) { obj.LoadID = (int)reader["LoadID"]; }

                        if (reader["ConfirmCode"] != DBNull.Value) { obj.ConfirmCode = (string)reader["ConfirmCode"]; }
                        if (reader["ExpirationDate"] != DBNull.Value) { obj.ExpirationDate = (DateTime)reader["ExpirationDate"]; }
                        if (reader["LoadStatus"] != DBNull.Value) { obj.LoadStatus = (string)reader["LoadStatus"]; }
                        if (reader["Active"] != DBNull.Value) { obj.Active = (bool)reader["Active"]; }

                        obj.Driver = new Driver();
                        if (reader["DriverID"] != DBNull.Value) { obj.Driver.ID = (int)reader["DriverID"]; }
                        if (reader["FirstName"] != DBNull.Value) { obj.Driver.FirstName = (string)reader["FirstName"]; }
                        if (reader["LastName"] != DBNull.Value) { obj.Driver.LastName = (string)reader["LastName"]; }

                        if (reader["ConfirmDate"] != DBNull.Value) { obj.ConfirmDate = (DateTime)reader["ConfirmDate"]; }

                        obj.DriverTrip = new DriverTrip();
                        if (reader["DriverTripID"] != DBNull.Value) { obj.DriverTrip.ID = (int)reader["DriverTripID"]; }
                        if (reader["DriverTripActive"] != DBNull.Value) { obj.DriverTrip.Active = (bool)reader["DriverTripActive"]; }
                        if (reader["ForcedStop"] != DBNull.Value) { obj.DriverTrip.ForcedStop = (bool)reader["ForcedStop"]; }
                        if (reader["Completed"] != DBNull.Value) { obj.DriverTrip.Completed = (bool)reader["Completed"]; }
                        if (reader["EndDate"] != DBNull.Value) { obj.DriverTrip.EndDate = (DateTime)reader["EndDate"]; }

                        obj.GPS = new GPSLocation();
                        if (reader["Lat"] != DBNull.Value) { obj.GPS.Lat = (string)reader["Lat"]; }
                        if (reader["Long"] != DBNull.Value) { obj.GPS.Long = (string)reader["Long"]; }


                        LoadConfirmList.Add(obj);
                    }
                }
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.GetActiveLoadConfirms");
            }


            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return LoadConfirmList;
        }

        public List<LoadConfirms> GetLoadConfirmHistory(int ID)
        {
            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = @"Select LoadConfirms.*, Driver.FirstName, Driver.LastName, Loads.LoadStatus, DriverTrip.Active as DriverTripActive, ForcedStop, Completed, EndDate, LoadConfirms.Active  
            From LoadConfirms Left Outer join Driver on LoadConfirms.DriverID = Driver.ID
            Left Outer join DriverTrip on LoadConfirms.ConfirmID = DriverTrip.ConfirmID inner join Loads on LoadConfirms.LoadID = Loads.LoadID 
            Where LoadConfirms.LoadID = @LoadID 
            Order by LoadConfirms.ConfirmID Desc";

            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.Text;
            cd.Parameters.AddWithValue("@LoadID", ID);

            List<LoadConfirms> LoadConfirmList = new List<LoadConfirms>();

            try
            { 
                using (SqlDataReader reader = cd.ExecuteReader())
                {
                    while (reader.Read())
                    {

                        LoadConfirms obj = new LoadConfirms();

                        if (reader["ConfirmID"] != DBNull.Value) { obj.ConfirmID = (int)reader["ConfirmID"]; }
                        if (reader["LoadID"] != DBNull.Value) { obj.LoadID = (int)reader["LoadID"]; }

                        if (reader["ConfirmCode"] != DBNull.Value) { obj.ConfirmCode = (string)reader["ConfirmCode"]; }
                        if (reader["ExpirationDate"] != DBNull.Value) { obj.ExpirationDate = (DateTime)reader["ExpirationDate"]; }
                        if (reader["LoadStatus"] != DBNull.Value) { obj.LoadStatus = (string)reader["LoadStatus"]; }
                        if (reader["Active"] != DBNull.Value) { obj.Active = (bool)reader["Active"]; }

                        obj.Driver = new Driver();
                        if (reader["DriverID"] != DBNull.Value) { obj.Driver.ID = (int)reader["DriverID"]; }
                        if (reader["FirstName"] != DBNull.Value) { obj.Driver.FirstName = (string)reader["FirstName"]; }
                        if (reader["LastName"] != DBNull.Value) { obj.Driver.LastName = (string)reader["LastName"]; }

                        if (reader["ConfirmDate"] != DBNull.Value) { obj.ConfirmDate = (DateTime)reader["ConfirmDate"]; }

                        obj.DriverTrip = new DriverTrip();
                        if (reader["DriverTripID"] != DBNull.Value) { obj.DriverTrip.ID = (int)reader["DriverTripID"]; }
                        if (reader["DriverTripActive"] != DBNull.Value) { obj.DriverTrip.Active = (bool)reader["DriverTripActive"]; }
                        if (reader["ForcedStop"] != DBNull.Value) { obj.DriverTrip.ForcedStop = (bool)reader["ForcedStop"]; }
                        if (reader["Completed"] != DBNull.Value) { obj.DriverTrip.Completed = (bool)reader["Completed"]; }
                        if (reader["EndDate"] != DBNull.Value) { obj.DriverTrip.EndDate = (DateTime)reader["EndDate"]; }

                        obj.GPS = new GPSLocation();
                        if (reader["Lat"] != DBNull.Value) { obj.GPS.Lat = (string)reader["Lat"]; }
                        if (reader["Long"] != DBNull.Value) { obj.GPS.Long = (string)reader["Long"]; }


                        LoadConfirmList.Add(obj);
                    }
                }
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.GetLoadConfirmHistory");
            }


            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return LoadConfirmList;
        }

        //remove before production
        public List<Loads> GetLoads()
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = @"SELECT Loads.ID, TripID, LoadID, LoadStatus, 
            Temp, TotalPallets, TotalWeight, TotalPieces, TotalMiles, HazMat, Loads.RepID, Reps.FullName as CompanyRepName, Reps.EmailAddress as CompanyRepEmail
            FROM Loads inner join Reps on Loads.RepID = Reps.RepID
            Order by LoadID Desc";

//            Select DriverTrip.ID as DriverTripID, DriverTrip.TripID, DriverTrip.LoadID, DriverID, DriverTrip.Active, ActiveDate, LoadStatus, Loads.RepID, Reps.FullName, Reps.EmailAddress, Reps.Phone Temp, TotalPallets, TotalWeight, HazMat, TotalMiles
//From DriverTrip

//    inner join Loads on DriverTrip.LoadID = Loads.LoadID

//    inner join Reps on Loads.RepiD = Reps.RepID


            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.Text;

            List<Loads> HistoryList = new List<Loads>();

            try
            { 
                using (SqlDataReader reader = cd.ExecuteReader())
                {


                    while (reader.Read())
                    {

                        Loads obj = new Loads();
                        obj.CompanyRep = new CompanyReps();

                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["LoadID"] != DBNull.Value) { obj.LoadID = (int)reader["LoadID"]; }
                        if (reader["TripID"] != DBNull.Value) { obj.TripID = (int)reader["TripID"]; }

                        if (reader["LoadStatus"] != DBNull.Value) { obj.LoadStatus = (string)reader["LoadStatus"]; }
                        if (reader["Temp"] != DBNull.Value) { obj.Temp = (string)reader["Temp"]; }
                        if (reader["TotalPallets"] != DBNull.Value) { obj.TotalPallets = (int)reader["TotalPallets"]; }
                        if (reader["TotalWeight"] != DBNull.Value) { obj.TotalWeight = (int)reader["TotalWeight"]; }
                        if (reader["TotalPieces"] != DBNull.Value) { obj.TotalPieces = (int)reader["TotalPieces"]; }
                        if (reader["TotalMiles"] != DBNull.Value) { obj.TotalMiles = (int)reader["TotalMiles"]; }
                        if (reader["HazMat"] != DBNull.Value) { obj.HazMat = (bool)reader["HazMat"]; }

                        if (reader["LoadStatus"] != DBNull.Value)
                        {
                            obj.CompanyRep.RepID = (int)reader["RepID"];
                            obj.CompanyRep.FullName = (string)reader["CompanyRepName"];
                            obj.CompanyRep.EmailAddress = (string)reader["CompanyRepEmail"];
                        }

                        HistoryList.Add(obj);
                    }


                }
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.GetLoads");
            }


            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            foreach(Loads ld in HistoryList)
            {
                ld.LoadStops = GetLoadStops(ld);
            }


            return HistoryList;

        }

        public List<Loads> GetLoadswDriver()
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = @"Select Loads.ID, Loads.LoadID, LoadStatus, Loads.RepID, Temp, TotalPallets, TotalWeight, TotalPieces, TotalMiles, HazMat, Loads.TripID, 
                DriverTrip.ID as DriverTripID, DriverTrip.DriverID, DriverTrip.Active, DriverTrip.ActiveDate, Reps.FullName as CompanyRepName, Reps.EmailAddress as CompanyRepEmail, Driver.FirstName + ' ' + Driver.LastName as DriverName
                From Loads left outer join DriverTrip on Loads.LoadID = DriverTrip.LoadID and Loads.TripID = DriverTrip.TripID
                    left outer join Reps on Loads.RepID = Reps.RepID
                    left outer join Driver on DriverTrip.DriverID = Driver.ID
                Order by LoadID desc, DriverTripID desc";


            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.Text;

            List<Loads> HistoryList = new List<Loads>();

            try
            { 
                using (SqlDataReader reader = cd.ExecuteReader())
                {


                    while (reader.Read())
                    {

                        Loads obj = new Loads();
                        obj.CompanyRep = new CompanyReps();

                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["LoadID"] != DBNull.Value) { obj.LoadID = (int)reader["LoadID"]; }
                        if (reader["TripID"] != DBNull.Value) { obj.TripID = (int)reader["TripID"]; }

                        if (reader["LoadStatus"] != DBNull.Value) { obj.LoadStatus = (string)reader["LoadStatus"]; }
                        if (reader["Temp"] != DBNull.Value) { obj.Temp = (string)reader["Temp"]; }
                        if (reader["TotalPallets"] != DBNull.Value) { obj.TotalPallets = (int)reader["TotalPallets"]; }
                        if (reader["TotalWeight"] != DBNull.Value) { obj.TotalWeight = (int)reader["TotalWeight"]; }
                        if (reader["TotalPieces"] != DBNull.Value) { obj.TotalPieces = (int)reader["TotalPieces"]; }
                        if (reader["TotalMiles"] != DBNull.Value) { obj.TotalMiles = (int)reader["TotalMiles"]; }
                        if (reader["HazMat"] != DBNull.Value) { obj.HazMat = (bool)reader["HazMat"]; }

                        if (reader["LoadStatus"] != DBNull.Value)
                        {
                            obj.CompanyRep.RepID = (int)reader["RepID"];
                            if (reader["CompanyRepName"] != DBNull.Value) { obj.CompanyRep.FullName = (string)reader["CompanyRepName"]; }
                            if (reader["CompanyRepEmail"] != DBNull.Value) { obj.CompanyRep.EmailAddress = (string)reader["CompanyRepEmail"]; }

                        }

                        if (reader["DriverTripID"] != DBNull.Value) { obj.DriverTripID = (int)reader["DriverTripID"]; }
                        if (reader["DriverID"] != DBNull.Value) { obj.DriverID = (int)reader["DriverID"]; }
                        if (reader["DriverName"] != DBNull.Value) { obj.Driver = (string)reader["DriverName"]; } else { obj.Driver = "";}
                        if (reader["Active"] != DBNull.Value) { obj.Active = (bool)reader["Active"]; }
                        if (reader["ActiveDate"] != DBNull.Value) { obj.ActiveDate = (DateTime)reader["ActiveDate"]; }

                        HistoryList.Add(obj);
                    }
                }
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.GetLoadswDriver");
            }



            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return HistoryList;

        }

        public List<Loads> GetLoadswDriverByDriverID(int DriverID)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = @"Select Loads.ID, Loads.LoadID, LoadStatus, Loads.RepID, Temp, TotalPallets, TotalWeight, TotalPieces, TotalMiles, HazMat, Loads.TripID, 
                DriverTrip.ID as DriverTripID, DriverTrip.DriverID, DriverTrip.Active, DriverTrip.ActiveDate, Reps.FullName as CompanyRepName, Reps.EmailAddress as CompanyRepEmail, Driver.FirstName + ' ' + Driver.LastName as DriverName
                From Loads left outer join DriverTrip on Loads.LoadID = DriverTrip.LoadID and Loads.TripID = DriverTrip.TripID
                    left outer join Reps on Loads.RepID = Reps.RepID
                    left outer join Driver on DriverTrip.DriverID = Driver.ID
                Where DriverTrip.DriverID = " + DriverID.ToString(); 
                strsql += " Order by LoadID desc, DriverTripID desc";


            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.Text;

            List<Loads> HistoryList = new List<Loads>();


            try
            { 
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {

                        Loads obj = new Loads();
                        obj.CompanyRep = new CompanyReps();

                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["LoadID"] != DBNull.Value) { obj.LoadID = (int)reader["LoadID"]; }
                        if (reader["TripID"] != DBNull.Value) { obj.TripID = (int)reader["TripID"]; }

                        if (reader["LoadStatus"] != DBNull.Value) { obj.LoadStatus = (string)reader["LoadStatus"]; }
                        if (reader["Temp"] != DBNull.Value) { obj.Temp = (string)reader["Temp"]; }
                        if (reader["TotalPallets"] != DBNull.Value) { obj.TotalPallets = (int)reader["TotalPallets"]; }
                        if (reader["TotalWeight"] != DBNull.Value) { obj.TotalWeight = (int)reader["TotalWeight"]; }
                        if (reader["TotalPieces"] != DBNull.Value) { obj.TotalPieces = (int)reader["TotalPieces"]; }
                        if (reader["TotalMiles"] != DBNull.Value) { obj.TotalMiles = (int)reader["TotalMiles"]; }
                        if (reader["HazMat"] != DBNull.Value) { obj.HazMat = (bool)reader["HazMat"]; }


                        if (reader["LoadStatus"] != DBNull.Value)
                        {
                            obj.CompanyRep.RepID = (int)reader["RepID"];
                            if (reader["CompanyRepName"] != DBNull.Value) { obj.CompanyRep.FullName = (string)reader["CompanyRepName"]; }
                            if (reader["CompanyRepEmail"] != DBNull.Value) { obj.CompanyRep.EmailAddress = (string)reader["CompanyRepEmail"]; }

                        }

                        if (reader["DriverTripID"] != DBNull.Value) { obj.DriverTripID = (int)reader["DriverTripID"]; }
                        if (reader["DriverID"] != DBNull.Value) { obj.DriverID = (int)reader["DriverID"]; }
                        if (reader["DriverName"] != DBNull.Value) { obj.Driver = (string)reader["DriverName"]; } else { obj.Driver = ""; }
                        if (reader["Active"] != DBNull.Value) { obj.Active = (bool)reader["Active"]; }
                        if (reader["ActiveDate"] != DBNull.Value) { obj.ActiveDate = (DateTime)reader["ActiveDate"]; }

                        HistoryList.Add(obj);
                    }
                }
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.GetLoadswDriverByDriverID");
            }


            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return HistoryList;

        }

        public Loads GetLoadByLoadID(int LoadID)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spLoadsByLoadID_Get";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@LoadID", LoadID);

            Loads obj = new Loads();
            obj.CompanyRep = new CompanyReps();

            try
            { 
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {

                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["LoadID"] != DBNull.Value) { obj.LoadID = (int)reader["LoadID"]; }
                        if (reader["TripID"] != DBNull.Value) { obj.TripID = (int)reader["TripID"]; }

                        if (reader["LoadStatus"] != DBNull.Value) { obj.LoadStatus = (string)reader["LoadStatus"]; }
                        if (reader["Temp"] != DBNull.Value) { obj.Temp = (string)reader["Temp"]; }
                        if (reader["TotalPallets"] != DBNull.Value) { obj.TotalPallets = (int)reader["TotalPallets"]; }
                        if (reader["TotalWeight"] != DBNull.Value) { obj.TotalWeight = (int)reader["TotalWeight"]; }
                        if (reader["TotalPieces"] != DBNull.Value) { obj.TotalPieces = (int)reader["TotalPieces"]; }
                        if (reader["TotalMiles"] != DBNull.Value) { obj.TotalMiles = (int)reader["TotalMiles"]; }
                        if (reader["HazMat"] != DBNull.Value) { obj.HazMat = (bool)reader["HazMat"]; }

                        if (reader["LoadStatus"] != DBNull.Value)
                        {
                            obj.CompanyRep.RepID = (int)reader["RepID"];
                            obj.CompanyRep.FullName = (string)reader["CompanyRepName"];
                            obj.CompanyRep.EmailAddress = (string)reader["CompanyRepEmail"];
                        }

                        if (reader["DriverTripID"] != DBNull.Value) { obj.DriverTripID = (int)reader["DriverTripID"]; }
                        if (reader["DriverID"] != DBNull.Value) { obj.DriverID = (int)reader["DriverID"]; }
                        if (reader["DriverName"] != DBNull.Value) { obj.Driver = (string)reader["DriverName"]; } else { obj.Driver = ""; }
                        if (reader["Active"] != DBNull.Value) { obj.Active = (bool)reader["Active"]; }
                        if (reader["ActiveDate"] != DBNull.Value) { obj.ActiveDate = (DateTime)reader["ActiveDate"]; }
                    }
                }
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.GetLoadByLoadID");
            }


            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            obj.LoadStops = GetLoadStops(obj);

            return obj;

        }

        public Loads GetLoadByTripID(int TripID)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spLoadsByTripID_Get";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@TripID", TripID);

            Loads obj = new Loads();
            obj.CompanyRep = new CompanyReps();

            try
            { 
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {

                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["LoadID"] != DBNull.Value) { obj.LoadID = (int)reader["LoadID"]; }
                        if (reader["TripID"] != DBNull.Value) { obj.TripID = (int)reader["TripID"]; }

                        if (reader["LoadStatus"] != DBNull.Value) { obj.LoadStatus = (string)reader["LoadStatus"]; }
                        if (reader["Temp"] != DBNull.Value) { obj.Temp = (string)reader["Temp"]; }
                        if (reader["TotalPallets"] != DBNull.Value) { obj.TotalPallets = (int)reader["TotalPallets"]; }
                        if (reader["TotalWeight"] != DBNull.Value) { obj.TotalWeight = (int)reader["TotalWeight"]; }
                        if (reader["TotalPieces"] != DBNull.Value) { obj.TotalPieces = (int)reader["TotalPieces"]; }
                        if (reader["TotalMiles"] != DBNull.Value) { obj.TotalMiles = (int)reader["TotalMiles"]; }
                        if (reader["HazMat"] != DBNull.Value) { obj.HazMat = (bool)reader["HazMat"]; }

                        if (reader["LoadStatus"] != DBNull.Value)
                        {
                            obj.CompanyRep.RepID = (int)reader["RepID"];
                            obj.CompanyRep.FullName = (string)reader["CompanyRepName"];
                            obj.CompanyRep.EmailAddress = (string)reader["CompanyRepEmail"];
                        }

                        if (reader["DriverTripID"] != DBNull.Value) { obj.DriverTripID = (int)reader["DriverTripID"]; }
                        if (reader["DriverID"] != DBNull.Value) { obj.DriverID = (int)reader["DriverID"]; }
                        if (reader["DriverName"] != DBNull.Value) { obj.Driver = (string)reader["DriverName"]; } else { obj.Driver = ""; }
                        if (reader["Active"] != DBNull.Value) { obj.Active = (bool)reader["Active"]; }
                        if (reader["ActiveDate"] != DBNull.Value) { obj.ActiveDate = (DateTime)reader["ActiveDate"]; }
                    }
                }
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.GetLoadByTripID");
            }


            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            obj.LoadStops = GetLoadStops(obj);

            return obj;

        }

        public Loads GetLoadByDriverTripID(int DriverTripID)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spLoadsByDriverTripID_Get";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@DriverTripID", DriverTripID);

            Loads obj = new Loads();
            obj.CompanyRep = new CompanyReps();

            try
            { 
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {

                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["LoadID"] != DBNull.Value) { obj.LoadID = (int)reader["LoadID"]; }
                        if (reader["TripID"] != DBNull.Value) { obj.TripID = (int)reader["TripID"]; }

                        if (reader["LoadStatus"] != DBNull.Value) { obj.LoadStatus = (string)reader["LoadStatus"]; }
                        if (reader["Temp"] != DBNull.Value) { obj.Temp = (string)reader["Temp"]; }
                        if (reader["TotalPallets"] != DBNull.Value) { obj.TotalPallets = (int)reader["TotalPallets"]; }
                        if (reader["TotalWeight"] != DBNull.Value) { obj.TotalWeight = (int)reader["TotalWeight"]; }
                        if (reader["TotalPieces"] != DBNull.Value) { obj.TotalPieces = (int)reader["TotalPieces"]; }
                        if (reader["TotalMiles"] != DBNull.Value) { obj.TotalMiles = (int)reader["TotalMiles"]; }
                        if (reader["HazMat"] != DBNull.Value) { obj.HazMat = (bool)reader["HazMat"]; }
    
                        if (reader["LoadStatus"] != DBNull.Value)
                        {
                            obj.CompanyRep.RepID = (int)reader["RepID"];
                            obj.CompanyRep.FullName = (string)reader["CompanyRepName"];
                            obj.CompanyRep.EmailAddress = (string)reader["CompanyRepEmail"];
                        }

                        if (reader["DriverTripID"] != DBNull.Value) { obj.DriverTripID = (int)reader["DriverTripID"]; }
                        if (reader["DriverID"] != DBNull.Value) { obj.DriverID = (int)reader["DriverID"]; }
                        if (reader["DriverName"] != DBNull.Value) { obj.Driver = (string)reader["DriverName"]; } else { obj.Driver = ""; }
                        if (reader["Active"] != DBNull.Value) { obj.Active = (bool)reader["Active"]; }
                        if (reader["ActiveDate"] != DBNull.Value) { obj.ActiveDate = (DateTime)reader["ActiveDate"]; }
                    }
                }
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.GetLoadByDriverTripID");
            }


            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            obj.LoadStops = GetLoadStops(obj);

            return obj;

        }

        public bool UpdateLoadRep(Loads load)
        {
            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spLoadsRep_Update";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@LoadID", load.LoadID);
            cd.Parameters.AddWithValue("@RepID", load.CompanyRep.RepID);
            cd.Parameters.AddWithValue("@TripID", load.TripID);

            //cd.Parameters.Add(new SqlParameter("@ID", load.ID));

            bool i = false;
            try
            {
                i = (cd.ExecuteNonQuery() == 1);
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataLoads.UpdateLoadRep");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return i;
        }
    }

    public class DataDocuments : DataAccess
    {
        public Documents GetDocumentsByID(int ID)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spDocument_Get";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@ID", ID);

            Documents obj = new Documents();

            try
            { 
                using (SqlDataReader reader = cd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["CheckCallID"] != DBNull.Value) { obj.CheckCallID = (int)reader["CheckCallID"]; }
                        if (reader["DriverID"] != DBNull.Value) { obj.DriverID = (int)reader["DriverID"]; }
                        if (reader["DocumentType"] != DBNull.Value) { obj.DocumentType = (DocumentTypeOption)reader["DocumentType"]; }
                        if (reader["Comments"] != DBNull.Value) { obj.Comments = (string)reader["Comments"]; }
                        if (reader["FileType"] != DBNull.Value) { obj.FileType = (FileTypeOption)reader["FileType"]; }
                        if (reader["FileName"] != DBNull.Value) { obj.FileName = (string)reader["FileName"]; }
                        if (reader["FileLocation"] != DBNull.Value)
                        {
                            obj.FileLocation = (string)reader["FileLocation"];
                            obj.Link = "uploads/" + obj.FileLocation + "/" + obj.FileName;
                        }
                        if (reader["Created"] != DBNull.Value) { obj.Created = (DateTime)reader["Created"]; }
                        if (reader["Deleted"] != DBNull.Value) { obj.Deleted = (bool)reader["Deleted"]; }
                    }
                }
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataDocuments.GetDocumentsByID");
            }


            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return obj;
        }

        public List<Documents> GetDocumentsByDriver(int ID)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spDocumentByDriver_Get";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@DriverID", ID);

            List<Documents> list = new List<Documents>();

            try
            { 
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {

                        Documents obj = new Documents();

                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["CheckCallID"] != DBNull.Value) { obj.CheckCallID = (int)reader["CheckCallID"]; }
                        if (reader["DriverID"] != DBNull.Value) { obj.DriverID = (int)reader["DriverID"]; }
                        if (reader["DocumentType"] != DBNull.Value) { obj.DocumentType = (DocumentTypeOption)reader["DocumentType"]; }                    
                        if (reader["Comments"] != DBNull.Value) { obj.Comments = (string)reader["Comments"]; }
                        if (reader["FileType"] != DBNull.Value) { obj.FileType = (FileTypeOption)reader["FileType"]; }
                        if (reader["FileName"] != DBNull.Value) { obj.FileName = (string)reader["FileName"]; }
                        if (reader["FileLocation"] != DBNull.Value)
                        {
                            obj.FileLocation = (string)reader["FileLocation"];
                            obj.Link = "uploads/" + obj.FileLocation + "/" + obj.FileName;
                        }
                        if (reader["Created"] != DBNull.Value) { obj.Created = (DateTime)reader["Created"]; }
                        if (reader["Deleted"] != DBNull.Value) { obj.Deleted = (bool)reader["Deleted"]; }

                        list.Add(obj);

                    }

                }
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataDocuments.GetDocumentsByDriver");
            }



            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return list;

        }

        public List<Documents> GetDocumentsByCheckCall(int ID)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spDocumentByCheckCall_Get";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@CheckCallID", ID);

            List<Documents> list = new List<Documents>();

            try
            { 
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {

                        Documents obj = new Documents();

                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["CheckCallID"] != DBNull.Value) { obj.CheckCallID = (int)reader["CheckCallID"]; }
                        if (reader["DriverID"] != DBNull.Value) { obj.DriverID = (int)reader["DriverID"]; }
                        if (reader["DocumentType"] != DBNull.Value) { obj.DocumentType = (DocumentTypeOption)reader["DocumentType"]; }
                        if (reader["Comments"] != DBNull.Value) { obj.Comments = (string)reader["Comments"]; }
                        if (reader["FileType"] != DBNull.Value) { obj.FileType = (FileTypeOption)reader["FileType"]; }
                        if (reader["FileName"] != DBNull.Value) { obj.FileName = (string)reader["FileName"]; }
                        if (reader["FileLocation"] != DBNull.Value)
                        {
                            obj.FileLocation = (string)reader["FileLocation"];
                            obj.Link = "uploads/" + obj.FileLocation + "/" + obj.FileName;
                        }
                        if (reader["Created"] != DBNull.Value) { obj.Created = (DateTime)reader["Created"]; }
                        if (reader["Deleted"] != DBNull.Value) { obj.Deleted = (bool)reader["Deleted"]; }

                        list.Add(obj);

                    }

                }
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataDocuments.GetDocumentsByCheckCall");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return list;

        }

        public List<Documents> GetDocumentsByDriverTrip(int ID)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spDocumentByDriverTrip_Get";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@DriverTripID", ID);

            List<Documents> list = new List<Documents>();

            try
            { 
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {

                        Documents obj = new Documents();

                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["CheckCallID"] != DBNull.Value) { obj.CheckCallID = (int)reader["CheckCallID"]; }
                        if (reader["DriverID"] != DBNull.Value) { obj.DriverID = (int)reader["DriverID"]; }
                        if (reader["DocumentType"] != DBNull.Value) { obj.DocumentType = (DocumentTypeOption)reader["DocumentType"]; }
                        if (reader["Comments"] != DBNull.Value) { obj.Comments = (string)reader["Comments"]; }
                        if (reader["FileType"] != DBNull.Value) { obj.FileType = (FileTypeOption)reader["FileType"]; }
                        if (reader["FileName"] != DBNull.Value) { obj.FileName = (string)reader["FileName"]; }
                        if (reader["FileLocation"] != DBNull.Value)
                        {
                            obj.FileLocation = (string)reader["FileLocation"];
                            obj.Link = "uploads/" + obj.FileLocation + "/" + obj.FileName;
                        }
                        if (reader["Created"] != DBNull.Value) { obj.Created = (DateTime)reader["Created"]; }
                        if (reader["Deleted"] != DBNull.Value) { obj.Deleted = (bool)reader["Deleted"]; }

                        list.Add(obj);

                    }

                }
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataDocuments.GetDocumentsByDriverTrip");
            }



            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return list;

        }

        public List<Documents> GetDocumentsByLoad(int ID)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spDocumentByLoad_Get";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@LoadID", ID);

            List<Documents> list = new List<Documents>();

            try
            { 
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {

                        Documents obj = new Documents();

                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["CheckCallID"] != DBNull.Value) { obj.CheckCallID = (int)reader["CheckCallID"]; }
                        if (reader["DriverID"] != DBNull.Value) { obj.DriverID = (int)reader["DriverID"]; }
                        if (reader["DocumentType"] != DBNull.Value) { obj.DocumentType = (DocumentTypeOption)reader["DocumentType"]; }
                        if (reader["Comments"] != DBNull.Value) { obj.Comments = (string)reader["Comments"]; }
                        if (reader["FileType"] != DBNull.Value) { obj.FileType = (FileTypeOption)reader["FileType"]; }
                        if (reader["FileName"] != DBNull.Value) { obj.FileName = (string)reader["FileName"]; }
                        if (reader["FileLocation"] != DBNull.Value)
                        {
                            obj.FileLocation = (string)reader["FileLocation"];
                            obj.Link = "uploads/" + obj.FileLocation + "/" + obj.FileName;
                        }
                        if (reader["Created"] != DBNull.Value) { obj.Created = (DateTime)reader["Created"]; }
                        if (reader["Deleted"] != DBNull.Value) { obj.Deleted = (bool)reader["Deleted"]; }

                        list.Add(obj);

                    }

                }
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataDocuments.GetDocumentsByLoad");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return list;

        }

        public int InsertDocument(Documents document)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();
            string strsql = "spDocument_Insert";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;

            cd.Parameters.AddWithValue("@DocumentType", (int)document.DocumentType);
            cd.Parameters.AddWithValue("@DriverID", document.DriverID);
            cd.Parameters.AddWithValue("@CheckCallID", document.CheckCallID);
            cd.Parameters.AddWithValue("@Comments", document.Comments);
            cd.Parameters.AddWithValue("@FileType", (int)document.FileType);
            cd.Parameters.AddWithValue("@FileName", document.FileName);
            cd.Parameters.AddWithValue("@FileLocation", document.FileLocation);


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
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataDocuments.InsertDocument");
            }

            int i = Convert.ToInt32(outputparm.Value);

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return i;

        }

        public bool DeleteDocument(Documents document)
        {
            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spDocument_Delete";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@ID", document.ID);

            bool i = false;
            try
            {
                i = (cmd.ExecuteNonQuery() == 1);
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataDocuments.DeleteDocument");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return i;
        }

        public int InsertUpload(string FileName, string ipaddress)
        {
            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spDocumentUpload_Insert";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@FileName", FileName);
            cd.Parameters.AddWithValue("@ipaddress", ipaddress);

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
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataDocuments.InsertUpload");
            }

            int i = Convert.ToInt32(outputparm.Value);

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return i;
        }

        public Documents GetUpload(int ID)
        {
            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spDocumentUpload_Get";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@ID", ID);

            List<Documents> list = new List<Documents>();

            Documents obj = new Documents();

            try
            { 
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {
                        if (reader["FileName"] != DBNull.Value) { obj.FileName = (string)reader["FileName"]; }
                    }

                }
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataDocuments.GetUpload");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return obj;
        }

        public bool ClaimUpload(int ID)
        {
            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spDocumentUpload_Claim";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@ID", ID);

            bool i = false;
            try
            {
                i = (cmd.ExecuteNonQuery() == 1);
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataDocuments.ClaimUpload");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return i;
        }

        public FileTypeOption FileType(string Fileext)
        {
            FileTypeOption op = FileTypeOption.NA;

            switch (Fileext.ToLower())
            {
                case ".jpg":
                case ".jpeg":
                    op = FileTypeOption.JPG;
                    break;
                case ".png":
                    op = FileTypeOption.PNG;
                    break;
                case ".doc":
                    op = FileTypeOption.DOC;
                    break;
                case ".docx":
                    op = FileTypeOption.DOCX;
                    break;
                case ".pdf":
                    op = FileTypeOption.PDF;
                    break;
                case ".xls":
                    op = FileTypeOption.XLS;
                    break;
                case ".xlsx":
                    op = FileTypeOption.XLSX;
                    break;
                case ".rtf":
                    op = FileTypeOption.RTF;
                    break;
                case ".csv":
                    op = FileTypeOption.CSV;
                    break;
                case ".txt":
                    op = FileTypeOption.TXT;
                    break;
                case ".odt":
                    op = FileTypeOption.ODT;
                    break;
                case ".ods":
                    op = FileTypeOption.ODS;
                    break;
                case ".tif":
                case ".tiff":
                    op = FileTypeOption.TIF;
                    break;
                default:
                    op = FileTypeOption.NA;
                    break;
            }
            return op;
        }

        public string FileExtention(FileTypeOption Filetype)
        {
            string ext = "";

            switch (Filetype)
            {
                case FileTypeOption.JPG:
                    ext = ".jpg";
                    break;
                case FileTypeOption.PNG:
                    ext = ".png";
                    break;
                case FileTypeOption.DOC:
                case FileTypeOption.DOCX:
                    ext = ".docx";
                    break;
                case FileTypeOption.PDF:
                    ext = ".pdf";
                    break;
                case FileTypeOption.XLS:
                case FileTypeOption.XLSX:
                    ext = ".xlsx";
                    break;
                case FileTypeOption.RTF:
                    ext = ".rtf";
                    break;
                case FileTypeOption.CSV:
                    ext = ".csv";
                    break;
                case FileTypeOption.TXT:
                    ext = ".txt";
                    break;
                case FileTypeOption.ODT:
                    ext = ".odt";
                    break;
                case FileTypeOption.ODS:
                    ext = ".ods";
                    break;
                case FileTypeOption.TIF:
                    ext = ".tif";
                    break;
                default:
                    ext = ".";
                    break;
            }
            return ext;
        }
    }

    public class DataMessages : DataAccess
    {
        public List<LoadMessages> GetLoadMessagesByDriver(int ID)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spLoadMessageByDriverID_Get";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@DriverID", ID);

            List<LoadMessages> list = new List<LoadMessages>();

            try
            { 
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {

                        LoadMessages obj = new LoadMessages();

                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["DriverTripID"] != DBNull.Value) { obj.DriverTripID = (int)reader["DriverTripID"]; }
                        if (reader["DriverID"] != DBNull.Value) { obj.DriverID = (int)reader["DriverID"]; }
                        if (reader["DriverName"] != DBNull.Value) { obj.DriverName = (string)reader["DriverName"]; }
                        if (reader["RepID"] != DBNull.Value) { obj.RepID = (int)reader["RepID"]; }
                        if (reader["CompanyRepName"] != DBNull.Value) { obj.CompanyRepName = (string)reader["CompanyRepName"]; }

                        obj.MessageType = new MessageType();
                        if (reader["MessageTypeID"] != DBNull.Value) { obj.MessageType.TypeOption = (MessageTypeOption)reader["MessageTypeID"]; }
                        if (reader["MessageDate"] != DBNull.Value) { obj.MessageDate = (DateTime)reader["MessageDate"]; }
                        if (reader["Message"] != DBNull.Value) { obj.Message = (string)reader["Message"]; }

                        obj.MessageFrom = new Messenger();
                        if (reader["MessageFromID"] != DBNull.Value) { obj.MessageFrom.MessengerID = (int)reader["MessageFromID"]; }
                        if (reader["MessageFrom"] != DBNull.Value) { obj.MessageFrom.MessengerType = (MessengerTypeOption)reader["MessageFrom"]; }

                        switch (obj.MessageFrom.MessengerType)
                        {
                            case MessengerTypeOption.Driver:
                                obj.MessageFrom.MessengerName = obj.DriverName;
                                if (reader["RepViewedDate"] != DBNull.Value) { obj.ViewedDate = (DateTime)reader["RepViewedDate"]; }
                                break;
                            case MessengerTypeOption.CompanyRep:
                                obj.MessageFrom.MessengerName = obj.CompanyRepName;
                                if (reader["DriverViewedDate"] != DBNull.Value) { obj.ViewedDate = (DateTime)reader["DriverViewedDate"]; }
                                break;
                            default:
                                break;
                        }

                        obj.GPSCoordinates = new GPSLocation();
                        if (reader["Lat"] != DBNull.Value) { obj.GPSCoordinates.Lat = (string)reader["Lat"]; }
                        if (reader["Long"] != DBNull.Value) { obj.GPSCoordinates.Long = (string)reader["Long"]; }

                        if (reader["Offset"] != DBNull.Value) { obj.Offset = (string)reader["Offset"]; }
                        if (reader["Timezone"] != DBNull.Value) { obj.Timezone = (string)reader["Timezone"]; }

                        if (reader["LoadID"] != DBNull.Value) { obj.LoadID = (int)reader["LoadID"]; }
                        if (reader["TripID"] != DBNull.Value) { obj.TripID = (int)reader["TripID"]; }

                        list.Add(obj);

                    }

                }

            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataMessages.GetLoadMessagesByDriver");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return list;

        }

        public List<LoadMessages> GetLoadMessagesByDriverTrip(int ID)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spLoadMessageByDriverTripID_Get";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@DriverTripID", ID);

            List<LoadMessages> list = new List<LoadMessages>();

            try
            { 
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {

                        LoadMessages obj = new LoadMessages();

                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["DriverTripID"] != DBNull.Value) { obj.DriverTripID = (int)reader["DriverTripID"]; }
                        if (reader["DriverID"] != DBNull.Value) { obj.DriverID = (int)reader["DriverID"]; }
                        if (reader["DriverName"] != DBNull.Value) { obj.DriverName = (string)reader["DriverName"]; }
                        if (reader["RepID"] != DBNull.Value) { obj.RepID = (int)reader["RepID"]; }
                        if (reader["CompanyRepName"] != DBNull.Value) { obj.CompanyRepName = (string)reader["CompanyRepName"]; }

                        obj.MessageType = new MessageType();
                        if (reader["MessageTypeID"] != DBNull.Value) { obj.MessageType.TypeOption = (MessageTypeOption)reader["MessageTypeID"]; }
                        if (reader["MessageDate"] != DBNull.Value) { obj.MessageDate = (DateTime)reader["MessageDate"]; }
                        if (reader["Message"] != DBNull.Value) { obj.Message = (string)reader["Message"]; }

                        obj.MessageFrom = new Messenger();
                        if (reader["MessageFromID"] != DBNull.Value) { obj.MessageFrom.MessengerID = (int)reader["MessageFromID"]; }
                        if (reader["MessageFrom"] != DBNull.Value) { obj.MessageFrom.MessengerType = (MessengerTypeOption)reader["MessageFrom"]; }

                        switch (obj.MessageFrom.MessengerType)
                        {
                            case MessengerTypeOption.Driver:
                                obj.MessageFrom.MessengerName = obj.DriverName;
                                if (reader["RepViewedDate"] != DBNull.Value) { obj.ViewedDate = (DateTime)reader["RepViewedDate"]; }
                                break;
                            case MessengerTypeOption.CompanyRep:
                                obj.MessageFrom.MessengerName = obj.CompanyRepName;
                                if (reader["DriverViewedDate"] != DBNull.Value) { obj.ViewedDate = (DateTime)reader["DriverViewedDate"]; }
                                break;
                            default:
                                break;
                        }

                        obj.GPSCoordinates = new GPSLocation();
                        if(reader["Lat"] != DBNull.Value) { obj.GPSCoordinates.Lat = (string)reader["Lat"]; }
                        if (reader["Long"] != DBNull.Value) { obj.GPSCoordinates.Long = (string)reader["Long"]; }

                        if (reader["Offset"] != DBNull.Value) { obj.Offset = (string)reader["Offset"]; }
                        if (reader["Timezone"] != DBNull.Value) { obj.Timezone = (string)reader["Timezone"]; }

                        if (reader["LoadID"] != DBNull.Value) { obj.LoadID = (int)reader["LoadID"]; }
                        if (reader["TripID"] != DBNull.Value) { obj.TripID = (int)reader["TripID"]; }

                        list.Add(obj);

                    }

                }

            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataMessages.GetLoadMessagesByDriverTrip");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return list;

        }

        public List<LoadMessages> GetLoadMessagesByLoad(int ID)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spLoadMessageByLoadID_Get";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@LoadID", ID);

            List<LoadMessages> list = new List<LoadMessages>();

            try
            { 
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {

                        LoadMessages obj = new LoadMessages();

                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["DriverTripID"] != DBNull.Value) { obj.DriverTripID = (int)reader["DriverTripID"]; }
                        if (reader["DriverID"] != DBNull.Value) { obj.DriverID = (int)reader["DriverID"]; }
                        if (reader["DriverName"] != DBNull.Value) { obj.DriverName = (string)reader["DriverName"]; }
                        if (reader["RepID"] != DBNull.Value) { obj.RepID = (int)reader["RepID"]; }
                        if (reader["CompanyRepName"] != DBNull.Value) { obj.CompanyRepName = (string)reader["CompanyRepName"]; }

                        obj.MessageType = new MessageType();
                        if (reader["MessageTypeID"] != DBNull.Value) { obj.MessageType.TypeOption = (MessageTypeOption)reader["MessageTypeID"]; }
                        if (reader["MessageDate"] != DBNull.Value) { obj.MessageDate = (DateTime)reader["MessageDate"]; }
                        if (reader["Message"] != DBNull.Value) { obj.Message = (string)reader["Message"]; }

                        obj.MessageFrom = new Messenger();
                        if (reader["MessageFromID"] != DBNull.Value) { obj.MessageFrom.MessengerID = (int)reader["MessageFromID"]; }
                        if (reader["MessageFrom"] != DBNull.Value) { obj.MessageFrom.MessengerType = (MessengerTypeOption)reader["MessageFrom"]; }

                        switch (obj.MessageFrom.MessengerType)
                        {
                            case MessengerTypeOption.Driver:
                                obj.MessageFrom.MessengerName = obj.DriverName;
                                if (reader["RepViewedDate"] != DBNull.Value) { obj.ViewedDate = (DateTime)reader["RepViewedDate"]; }
                                break;
                            case MessengerTypeOption.CompanyRep:
                                obj.MessageFrom.MessengerName = obj.CompanyRepName;
                                if (reader["DriverViewedDate"] != DBNull.Value) { obj.ViewedDate = (DateTime)reader["DriverViewedDate"]; }
                                break;
                            default:
                                break;
                        }

                        obj.GPSCoordinates = new GPSLocation();
                        if (reader["Lat"] != DBNull.Value) { obj.GPSCoordinates.Lat = (string)reader["Lat"]; }
                        if (reader["Long"] != DBNull.Value) { obj.GPSCoordinates.Long = (string)reader["Long"]; }

                        if (reader["Offset"] != DBNull.Value) { obj.Offset = (string)reader["Offset"]; }
                        if (reader["Timezone"] != DBNull.Value) { obj.Timezone = (string)reader["Timezone"]; }

                        if (reader["LoadID"] != DBNull.Value) { obj.LoadID = (int)reader["LoadID"]; }
                        if (reader["TripID"] != DBNull.Value) { obj.TripID = (int)reader["TripID"]; }

                        list.Add(obj);

                    }

                }

            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataMessages.GetLoadMessagesByLoad");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return list;

        }

        public List<LoadMessages> GetLoadMessagesUnReadByDriver(int ID)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spLoadMessageUnReadByDriverID_Get";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@DriverID", ID);

            List<LoadMessages> list = new List<LoadMessages>();

            try
            { 
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {

                        LoadMessages obj = new LoadMessages();

                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["DriverTripID"] != DBNull.Value) { obj.DriverTripID = (int)reader["DriverTripID"]; }
                        if (reader["DriverID"] != DBNull.Value) { obj.DriverID = (int)reader["DriverID"]; }
                        if (reader["DriverName"] != DBNull.Value) { obj.DriverName = (string)reader["DriverName"]; }
                        if (reader["RepID"] != DBNull.Value) { obj.RepID = (int)reader["RepID"]; }
                        if (reader["CompanyRepName"] != DBNull.Value) { obj.CompanyRepName = (string)reader["CompanyRepName"]; }

                        obj.MessageType = new MessageType();
                        if (reader["MessageTypeID"] != DBNull.Value) { obj.MessageType.TypeOption = (MessageTypeOption)reader["MessageTypeID"]; }
                        if (reader["MessageDate"] != DBNull.Value) { obj.MessageDate = (DateTime)reader["MessageDate"]; }
                        if (reader["Message"] != DBNull.Value) { obj.Message = (string)reader["Message"]; }

                        obj.MessageFrom = new Messenger();
                        if (reader["MessageFromID"] != DBNull.Value) { obj.MessageFrom.MessengerID = (int)reader["MessageFromID"]; }
                        if (reader["MessageFrom"] != DBNull.Value) { obj.MessageFrom.MessengerType = (MessengerTypeOption)reader["MessageFrom"]; }

                        switch (obj.MessageFrom.MessengerType)
                        {
                            case MessengerTypeOption.Driver:
                                obj.MessageFrom.MessengerName = obj.DriverName;
                                if (reader["RepViewedDate"] != DBNull.Value) { obj.ViewedDate = (DateTime)reader["RepViewedDate"]; }
                                break;
                            case MessengerTypeOption.CompanyRep:
                                obj.MessageFrom.MessengerName = obj.CompanyRepName;
                                if (reader["DriverViewedDate"] != DBNull.Value) { obj.ViewedDate = (DateTime)reader["DriverViewedDate"]; }
                                break;
                            default:
                                break;
                        }

                        obj.GPSCoordinates = new GPSLocation();
                        if (reader["Lat"] != DBNull.Value) { obj.GPSCoordinates.Lat = (string)reader["Lat"]; }
                        if (reader["Long"] != DBNull.Value) { obj.GPSCoordinates.Long = (string)reader["Long"]; }

                        if (reader["Offset"] != DBNull.Value) { obj.Offset = (string)reader["Offset"]; }
                        if (reader["Timezone"] != DBNull.Value) { obj.Timezone = (string)reader["Timezone"]; }

                        if (reader["LoadID"] != DBNull.Value) { obj.LoadID = (int)reader["LoadID"]; }
                        if (reader["TripID"] != DBNull.Value) { obj.TripID = (int)reader["TripID"]; }

                        list.Add(obj);

                    }

                }


            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataMessages.GetLoadMessagesUnReadByDriver");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return list;

        }

        public List<LoadMessages> GetLoadMessagesUnReadByRep(int ID)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spLoadMessageUnReadByRepID_Get";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@RepID", ID);

            List<LoadMessages> list = new List<LoadMessages>();

            try
            { 
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {

                        LoadMessages obj = new LoadMessages();

                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["DriverTripID"] != DBNull.Value) { obj.DriverTripID = (int)reader["DriverTripID"]; }
                        if (reader["DriverID"] != DBNull.Value) { obj.DriverID = (int)reader["DriverID"]; }
                        if (reader["DriverName"] != DBNull.Value) { obj.DriverName = (string)reader["DriverName"]; }
                        if (reader["RepID"] != DBNull.Value) { obj.RepID = (int)reader["RepID"]; }
                        if (reader["CompanyRepName"] != DBNull.Value) { obj.CompanyRepName = (string)reader["CompanyRepName"]; }

                        obj.MessageType = new MessageType();
                        if (reader["MessageTypeID"] != DBNull.Value) { obj.MessageType.TypeOption = (MessageTypeOption)reader["MessageTypeID"]; }
                        if (reader["MessageDate"] != DBNull.Value) { obj.MessageDate = (DateTime)reader["MessageDate"]; }
                        if (reader["Message"] != DBNull.Value) { obj.Message = (string)reader["Message"]; }

                        obj.MessageFrom = new Messenger();
                        if (reader["MessageFromID"] != DBNull.Value) { obj.MessageFrom.MessengerID = (int)reader["MessageFromID"]; }
                        if (reader["MessageFrom"] != DBNull.Value) { obj.MessageFrom.MessengerType = (MessengerTypeOption)reader["MessageFrom"]; }

                        switch (obj.MessageFrom.MessengerType)
                        {
                            case MessengerTypeOption.Driver:
                                obj.MessageFrom.MessengerName = obj.DriverName;
                                if (reader["RepViewedDate"] != DBNull.Value) { obj.ViewedDate = (DateTime)reader["RepViewedDate"]; }
                                break;
                            case MessengerTypeOption.CompanyRep:
                                obj.MessageFrom.MessengerName = obj.CompanyRepName;
                                if (reader["DriverViewedDate"] != DBNull.Value) { obj.ViewedDate = (DateTime)reader["DriverViewedDate"]; }
                                break;
                            default:
                                break;
                        }

                        obj.GPSCoordinates = new GPSLocation();
                        if (reader["Lat"] != DBNull.Value) { obj.GPSCoordinates.Lat = (string)reader["Lat"]; }
                        if (reader["Long"] != DBNull.Value) { obj.GPSCoordinates.Long = (string)reader["Long"]; }

                        if (reader["Offset"] != DBNull.Value) { obj.Offset = (string)reader["Offset"]; }
                        if (reader["Timezone"] != DBNull.Value) { obj.Timezone = (string)reader["Timezone"]; }

                        if (reader["LoadID"] != DBNull.Value) { obj.LoadID = (int)reader["LoadID"]; }
                        if (reader["TripID"] != DBNull.Value) { obj.TripID = (int)reader["TripID"]; }


                        list.Add(obj);

                    }

                }
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataMessages.GetLoadMessagesUnReadByRep");
            }


            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return list;

        }

        public LoadMessages GetLoadMessage(int ID)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spLoadMessageByID_Get";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@ID", ID);

            LoadMessages obj = new LoadMessages();

            try
            { 
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {

                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["DriverTripID"] != DBNull.Value) { obj.DriverTripID = (int)reader["DriverTripID"]; }
                        if (reader["DriverID"] != DBNull.Value) { obj.DriverID = (int)reader["DriverID"]; }
                        if (reader["DriverName"] != DBNull.Value) { obj.DriverName = (string)reader["DriverName"]; }
                        if (reader["RepID"] != DBNull.Value) { obj.RepID = (int)reader["RepID"]; }
                        if (reader["CompanyRepName"] != DBNull.Value) { obj.CompanyRepName = (string)reader["CompanyRepName"]; }

                        obj.MessageType = new MessageType();
                        if (reader["MessageTypeID"] != DBNull.Value) { obj.MessageType.TypeOption = (MessageTypeOption)reader["MessageTypeID"]; }
                        if (reader["MessageDate"] != DBNull.Value) { obj.MessageDate = (DateTime)reader["MessageDate"]; }
                        if (reader["Message"] != DBNull.Value) { obj.Message = (string)reader["Message"]; }

                        obj.MessageFrom = new Messenger();
                        if (reader["MessageFromID"] != DBNull.Value) { obj.MessageFrom.MessengerID = (int)reader["MessageFromID"]; }
                        if (reader["MessageFrom"] != DBNull.Value) { obj.MessageFrom.MessengerType = (MessengerTypeOption)reader["MessageFrom"]; }

                        switch (obj.MessageFrom.MessengerType)
                        {
                            case MessengerTypeOption.Driver:
                                obj.MessageFrom.MessengerName = obj.DriverName;
                                if (reader["RepViewedDate"] != DBNull.Value) { obj.ViewedDate = (DateTime)reader["RepViewedDate"]; }
                                break;
                            case MessengerTypeOption.CompanyRep:
                                obj.MessageFrom.MessengerName = obj.CompanyRepName;
                                if (reader["DriverViewedDate"] != DBNull.Value) { obj.ViewedDate = (DateTime)reader["DriverViewedDate"]; }
                                break;
                            default:
                                break;
                        }

                        obj.GPSCoordinates = new GPSLocation();
                        if (reader["Lat"] != DBNull.Value) { obj.GPSCoordinates.Lat = (string)reader["Lat"]; }
                        if (reader["Long"] != DBNull.Value) { obj.GPSCoordinates.Long = (string)reader["Long"]; }

                        if (reader["Offset"] != DBNull.Value) { obj.Offset = (string)reader["Offset"]; }
                        if (reader["Timezone"] != DBNull.Value) { obj.Timezone = (string)reader["Timezone"]; }

                        if (reader["LoadID"] != DBNull.Value) { obj.LoadID = (int)reader["LoadID"]; }
                        if (reader["TripID"] != DBNull.Value) { obj.TripID = (int)reader["TripID"]; }

                        obj.ResultMessage = "Successful";

                    }

                }
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataMessages.GetLoadMessage");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return obj;

        }

        public int InsertLoadMessage(LoadMessages message)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spLoadMessage_Insert";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;

            if(message.GPSCoordinates == null)
                message.GPSCoordinates = new GPSLocation() { Lat = "", Long = "" };

            cd.Parameters.AddWithValue("@DriverTripID", message.DriverTripID);
            cd.Parameters.AddWithValue("@MessageTypeID", (int)message.MessageType.TypeOption);
            cd.Parameters.AddWithValue("@Message", message.Message);
            cd.Parameters.AddWithValue("@MessageFromID", message.MessageFrom.MessengerID);
            cd.Parameters.AddWithValue("@MessageFrom", (int)message.MessageFrom.MessengerType);
            cd.Parameters.AddWithValue("@DriverID", message.DriverID);
            cd.Parameters.AddWithValue("@RepID", message.RepID);
            cd.Parameters.AddWithValue("@Lat", message.GPSCoordinates.Lat);
            cd.Parameters.AddWithValue("@Long", message.GPSCoordinates.Long);
            cd.Parameters.AddWithValue("@Offset", message.Offset);
            cd.Parameters.AddWithValue("@Timezone", message.Timezone);

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
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataMessages.InsertLoadMessage");
            }

            int results = Convert.ToInt32(outputparm.Value);

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return results;
        }

        public LoadMessages DeleteLoadMessage(LoadMessages message)
        {

            try
            {
                SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

                string strsql = "spLoadMessage_Delete";
                SqlCommand cd = new SqlCommand(strsql, cn);
                cd.CommandType = CommandType.StoredProcedure;
                cd.Parameters.AddWithValue("@ID", message.ID);

                bool i = false;
                try
                {
                    i = (cmd.ExecuteNonQuery() == 1);
                }
                catch (Exception ex)
                {
                    DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataMessages.DeleteLoadMessage");
                    message.ResultMessage = "Error";
                    return message;
                }

                if (cn.State != ConnectionState.Closed) { cn.Close(); }

                message.ResultMessage = "Successful";
            }
            catch
            {
                message.ResultMessage = "Error";
            }


            return message;
        }

        public int DriverViewedLoadMessage(LoadMessages message)
        {
            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spLoadMessageDriver_Viewed";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@ID", message.ID);
            cd.Parameters.AddWithValue("@DriverID", message.DriverID);
            cd.Parameters.AddWithValue("@Lat", message.GPSCoordinates.Lat);
            cd.Parameters.AddWithValue("@Long", message.GPSCoordinates.Long);


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
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataMessages.DriverViewedLoadMessage");
            }

            int results = Convert.ToInt32(outputparm.Value);

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return results;

        }

        public int RepViewedLoadMessage(LoadMessages message)
        {
            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spLoadMessageRep_Viewed";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@ID", message.ID);
            cd.Parameters.AddWithValue("@RepID", message.RepID);

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
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataMessages.RepViewedLoadMessage");
            }

            int results = Convert.ToInt32(outputparm.Value);

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return results;
        }

        public int GetCountAwaitingDriverMessage(int DriverID)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spLoadMessageAwaitingDriver_Get";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@DriverID", DriverID);

            int obj = new int();

            try
            {
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {

                        if (reader["MessageCount"] != DBNull.Value) { obj = (int)reader["MessageCount"]; }
                    }
                }
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataMessages.GetCountAwaitingDriverMessage");
            }


            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return obj;
        }

        public int GetCountAwaitingRepMessage(int RepID)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spLoadMessageAwaitingCompanyRep_Get";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@RepID", RepID);

            int obj = new int();

            try
            { 
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {

                        if (reader["MessageCount"] != DBNull.Value) { obj = (int)reader["MessageCount"]; }
                    }
                }
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataMessages.GetCountAwaitingRepMessage");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return obj;
        }

        public int GetCountAwaitingLoadMessage(int LoadID)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spLoadMessageAwaitingLoad_Get";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@LoadID", LoadID);

            int obj = new int();

            try
            { 
                using (SqlDataReader reader = cd.ExecuteReader())
                {
                    while (reader.Read())
                    {

                        if (reader["MessageCount"] != DBNull.Value) { obj = (int)reader["MessageCount"]; }
                    }
                }
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataMessages.GetCountAwaitingLoadMessage");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return obj;
        }

        public int GetCountAwaitingCompanyMessage()
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spLoadMessageAwaitingCompany_Get";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;

            int obj = new int();

            try
            { 
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {

                        if (reader["MessageCount"] != DBNull.Value) { obj = (int)reader["MessageCount"]; }
                    }
                }
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataMessages.GetCountAwaitingCompanyMessage");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return obj;
        }


        public int InsertPushNotification(DriverNotification notification)
        {
            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spDriverPushNotification_Insert";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;

            cd.Parameters.AddWithValue("@DriverID", notification.DriverID);
            cd.Parameters.AddWithValue("@DriverTripID", notification.DriverTripID);
            cd.Parameters.AddWithValue("@DeviceID", notification.DeviceID);
            cd.Parameters.AddWithValue("@DeviceType", notification.DeviceType);
            cd.Parameters.AddWithValue("@Token", notification.Token);
            cd.Parameters.AddWithValue("@Title", notification.Title);
            cd.Parameters.AddWithValue("@Message", notification.Message);
            cd.Parameters.AddWithValue("@MessageType", notification.MessageType);
            cd.Parameters.AddWithValue("@MessageData", notification.MessageData);
            cd.Parameters.AddWithValue("@SentMessageJson", notification.SentMessageJson);
            cd.Parameters.AddWithValue("@ResultMessageJson", notification.ResultMessageJson);
            cd.Parameters.AddWithValue("@Message_ID", notification.message_id);
            cd.Parameters.AddWithValue("@Multicast_ID", notification.multicast_id);

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
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataMessages.InsertPushNotification");
            }

            int results = Convert.ToInt32(outputparm.Value);

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return results;
        }

        public int PushNotificationViewed(DriverNotification notification)
        {
            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spDriverPushNotification_Viewed";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;

            if (notification.message_id != "0" && notification.message_id.Length > 1)
            {
                cd.Parameters.AddWithValue("@ID", notification.message_id.ToString());
                cd.Parameters.AddWithValue("@Field", 3);
                cd.Parameters.AddWithValue("@Lat", notification.GPSCoordinates.Lat);
                cd.Parameters.AddWithValue("@Long", notification.GPSCoordinates.Long);
                cd.Parameters.AddWithValue("@Timezone", notification.Timezone);
                cd.Parameters.AddWithValue("@Offset", notification.Offset);
            }
            else if (notification.multicast_id != "0" && notification.multicast_id.Length > 1)
            {
                cd.Parameters.AddWithValue("@ID", notification.multicast_id.ToString());
                cd.Parameters.AddWithValue("@Field", 2);
                cd.Parameters.AddWithValue("@Lat", notification.GPSCoordinates.Lat);
                cd.Parameters.AddWithValue("@Long", notification.GPSCoordinates.Long);
                cd.Parameters.AddWithValue("@Timezone", notification.Timezone);
                cd.Parameters.AddWithValue("@Offset", notification.Offset);
            }
            else if(notification.ID != 0)
            {
                cd.Parameters.AddWithValue("@ID", notification.ID.ToString());
                cd.Parameters.AddWithValue("@Field", 1);
                cd.Parameters.AddWithValue("@Lat", notification.GPSCoordinates.Lat);
                cd.Parameters.AddWithValue("@Long", notification.GPSCoordinates.Long);
                cd.Parameters.AddWithValue("@Timezone", notification.Timezone);
                cd.Parameters.AddWithValue("@Offset", notification.Offset);
            }
            else
            {
                return 0;
            }

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
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataMessages.PushNotificationViewed");
            }

            int results = Convert.ToInt32(outputparm.Value);

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return results;
        }


    }

    public class DataAudit : DataAccess
    {
        public void InsertRawHTTP(string Raw)
        {

            try
            {
                SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

                string strsql = "Insert into RawHTTP (RawData) Values (@Raw);";
                SqlCommand cd = new SqlCommand(strsql, cn);
                cd.CommandType = CommandType.Text;

                cd.Parameters.AddWithValue("@Raw", Raw);

                try
                {
                    cd.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataAudit.InsertRawAudit");
                }

                if (cn.State != ConnectionState.Closed) { cn.Close(); }
            }
            catch
            {

            }


        }

        public void InsertRawAudit(string Raw)
        {

            try
            {
                SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

                string strsql = "Insert into RawAudit (RawJson) Values (@RawJson);";
                SqlCommand cd = new SqlCommand(strsql, cn);
                cd.CommandType = CommandType.Text;

                cd.Parameters.AddWithValue("@RawJson", Raw);

                try
                {
                    cd.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataAudit.InsertRawAudit");
                }

                if (cn.State != ConnectionState.Closed) { cn.Close(); }
            }
            catch
            {

            }


        }

        public List<LogMetaEntry> ViewRawAudit()
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "Select ID, RawJson, EntryDate From RawAudit Order by ID Desc;";

            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.Text;


            List<LogMetaEntry> list = new List<LogMetaEntry>();

            try
            { 
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {

                        LogMetaEntry obj = new LogMetaEntry();

                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["RawJson"] != DBNull.Value) { obj.RawJson = (string)reader["RawJson"]; }
                        if (reader["entrydate"] != DBNull.Value) { obj.EntryDate = (DateTime)reader["entrydate"]; }

                        list.Add(obj);

                    }

                    if (cn.State != ConnectionState.Closed) { cn.Close(); }

                }
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataAudit.ViewRawAudit");
            }

            return list;
        }

        public void ClearRawAudit()
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "Truncate Table RawAudit;";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.Text;

            try
            {
                cd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataAudit.ClearRawAudit");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

        }

        public List<OTR_API.TruckerTools.Models.RAW> GetRawAudit(OTR_API.TruckerTools.Models.RAW rw)
        {
            List<OTR_API.TruckerTools.Models.RAW> objList = new List<TruckerTools.Models.RAW>();

            if (rw.key == "6KLY2Ftn83E9X3eSzDMMILsPkjg86de3")
            {
                SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

                string strsql = "sp_FilterRaw";
                SqlCommand cd = new SqlCommand(strsql, cn);
                cd.CommandTimeout = 180;
                cd.CommandType = CommandType.StoredProcedure;
                cd.Parameters.AddWithValue("@Filter", rw.Filter);
                cd.Parameters.AddWithValue("@Value", rw.Value);

                try
                {
                    using (SqlDataReader reader = cd.ExecuteReader())
                    {

                        while (reader.Read())
                        {
                            OTR_API.TruckerTools.Models.RAW obj = new OTR_API.TruckerTools.Models.RAW();

                            if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                            if (reader["EntryDate"] != DBNull.Value) { obj.EntryDate = (DateTime)reader["EntryDate"]; }
                            if (reader["rawjson"] != DBNull.Value) { obj.Json = (string)reader["rawjson"]; }

                            objList.Add(obj);
                        }
                    }
                }
                catch (Exception ex)
                {
                    OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataAudit.GetRawAudit");
                    OTR_API.TruckerTools.Models.RAW obj = new OTR_API.TruckerTools.Models.RAW();
                    obj.ID = 0;
                    obj.EntryDate = DateTime.Now;
                    obj.Json = "{\"Mesage\":\"Error - " + ex.Message + "\"}";

                    objList.Add(obj);
                }

                if (cn.State != ConnectionState.Closed) { cn.Close(); }
            }
            else
            {
                OTR_API.TruckerTools.Models.RAW obj = new OTR_API.TruckerTools.Models.RAW();
                obj.ID = 0;
                obj.EntryDate = DateTime.Now;
                obj.Json = "{\"Mesage\":\"No Records returned\"}";

                objList.Add(obj);
            }

            return objList;
        }

        public void InsertErrorAuditLog(string Error, string Procedure)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "Insert Into AuditLogs(LogType, LogTypeName, LogMessage) Values (20, 'Error', @Error)";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.Text;
            cd.Parameters.AddWithValue("@Error", Procedure + ": " + Error);
            
            try
            {
                cd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataAudit.DataAudit da = new DataAudit(); da.InsertErrorAuditLog");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }
        }

        public void InsertAuditLog(int TypeID, string Type, string Error, string Procedure)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "Insert Into AuditLogs(LogType, LogTypeName, LogMessage) Values (@ID, @Type, @Msg)";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.Text;
            cd.Parameters.AddWithValue("@ID", TypeID);
            cd.Parameters.AddWithValue("@Type", Type);
            cd.Parameters.AddWithValue("@Msg", Procedure + ": " + Error);

            try
            {
                cd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataAudit.DataAudit da = new DataAudit(); da.InsertErrorAuditLog");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }
        }
    }

    public class DataUrl
    {
        public Urls CreateUrl(string FullUrl)
        {

            int tokenlength = 8;

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            //string strsql = "Insert into Urls (FullUrl, Token) Values (@FullUrl, @Token);";
            string strsql = "spUrls_Insert";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;

            cd.Parameters.AddWithValue("@FullUrl", FullUrl);
            cd.Parameters.AddWithValue("@TokenLength", tokenlength);


            SqlParameter outputparm = new SqlParameter("@responseID", SqlDbType.Int)
            {
                Direction = ParameterDirection.Output
            };
            cd.Parameters.Add(outputparm);

            SqlParameter outputparm2 = new SqlParameter("@responseMessage", SqlDbType.VarChar, tokenlength)
            {
                Direction = ParameterDirection.Output
            };
            cd.Parameters.Add(outputparm2);


            try
            {
                cd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataUrl.CreateUrl");
            }

            Urls results = new Urls();
            results.FullUrl = FullUrl;
            results.ID = Convert.ToInt32(outputparm.Value);
            results.TinyUrl = Convert.ToString(outputparm2.Value);

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return results;
        }


        public Urls GetUrl(int ID)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spUrls_GetUrlByID_Get";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@UrlID", ID);

            Urls obj = new Urls();
            try
            {
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {
                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["FullUrl"] != DBNull.Value) { obj.FullUrl = (string)reader["FullUrl"]; }
                        if (reader["Keyword"] != DBNull.Value) { obj.TinyUrl = (string)reader["Keyword"]; }
                        if (reader["UrlDate"] != DBNull.Value) { obj.UrlDate = (DateTime)reader["UrlDate"]; }
                    }

                }
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataUrl.GetShortUrl");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return obj;

        }

        public Urls GetUrl(string Url, int Url_SearchBy_Type)
        {

            SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            string strsql = "spUrls_GetUrlByUrl_Get";
            SqlCommand cd = new SqlCommand(strsql, cn);
            cd.CommandType = CommandType.StoredProcedure;
            cd.Parameters.AddWithValue("@Url", Url);
            cd.Parameters.AddWithValue("@UrlSearchByType", Url_SearchBy_Type);


            Urls obj = new Urls();
            try
            {
                using (SqlDataReader reader = cd.ExecuteReader())
                {

                    while (reader.Read())
                    {
                        if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
                        if (reader["FullUrl"] != DBNull.Value) { obj.FullUrl = (string)reader["FullUrl"]; }
                        if (reader["Keyword"] != DBNull.Value) { obj.TinyUrl = (string)reader["Keyword"]; }
                        if (reader["UrlDate"] != DBNull.Value) { obj.UrlDate = (DateTime)reader["UrlDate"]; }
                    }

                }
            }
            catch (Exception ex)
            {
                DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataUrl.GetShortUrl");
            }

            if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return obj;

        }



        public static String TinyUrl(int Number)
        {
            String chars = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ-_~";
            int baseNumber = chars.Length;

            int r;
            String newNumber = "";

            // in r we have the offset of the char that was converted to the new base
            while (Number >= baseNumber)
            {
                r = Number % baseNumber;
                newNumber = chars[r] + newNumber;
                Number = Number / baseNumber;
            }
            // the last number to convert
            newNumber = chars[Number] + newNumber;

            return newNumber;
        }


        public InMotionUrl GetDriverUrl(int ID)
        {

            //SqlConnection cn = new SqlConnection(ConfigurationManager.ConnectionStrings["hostjson"].ConnectionString); cn.Open();

            //string strsql = "spUrls_GetUrlByID_Get";
            //SqlCommand cd = new SqlCommand(strsql, cn);
            //cd.CommandType = CommandType.StoredProcedure;
            //cd.Parameters.AddWithValue("@UrlID", ID);

            InMotionUrl obj = new InMotionUrl();
            obj.DriverID = ID;
            obj.Url = @"http://access.vectortransport.com";
            obj.Menu = "Loadboard";

            //try
            //{
            //    using (SqlDataReader reader = cd.ExecuteReader())
            //    {

            //        while (reader.Read())
            //        {
            //            if (reader["ID"] != DBNull.Value) { obj.ID = (int)reader["ID"]; }
            //            if (reader["FullUrl"] != DBNull.Value) { obj.FullUrl = (string)reader["FullUrl"]; }
            //            if (reader["Keyword"] != DBNull.Value) { obj.TinyUrl = (string)reader["Keyword"]; }
            //            if (reader["UrlDate"] != DBNull.Value) { obj.UrlDate = (DateTime)reader["UrlDate"]; }
            //        }

            //    }
            //}
            //catch (Exception ex)
            //{
            //    DataAudit da = new DataAudit(); da.InsertErrorAuditLog(ex.Message, "DataUrl.GetShortUrl");
            //}

            //if (cn.State != ConnectionState.Closed) { cn.Close(); }

            return obj;

        }
    }

    public class Communicate
    {
        public ResponseMessage SendEmail(string strTo, string strBody, string strSubject)
        {
            ResponseMessage response = new ResponseMessage();

            System.Net.Configuration.SmtpSection section = (System.Net.Configuration.SmtpSection)ConfigurationManager.GetSection("system.net/mailSettings/smtp");

            // Create a message and set up the recipients.
            MailMessage email = new MailMessage();
            email.From = new MailAddress(section.From);
            email.To.Add(new MailAddress(strTo));
            email.Subject = strSubject;
            email.Body = strBody;

            SmtpClient client = new SmtpClient()
            {
                Host = section.Network.Host,
                Port = section.Network.Port,
                EnableSsl = section.Network.EnableSsl,
                DeliveryMethod = section.DeliveryMethod,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(section.Network.UserName, section.Network.Password)
            };

            try
            {
                client.Send(email);
                response.Message = "Successful";
            }
            catch (Exception ex)
            {
                response.Message = "Exception caught in CreateMessageWithMultipleViews(): " + ex.ToString();
            }

            return response;
        }

        public static void SendEmail(string strTo, string strBody, string strSubject, string strAttachment)
        {
            System.Net.Configuration.SmtpSection section = (System.Net.Configuration.SmtpSection)ConfigurationManager.GetSection("system.net/mailSettings/smtp");

            // Create a message and set up the recipients.
            MailMessage email = new MailMessage();
            email.From = new MailAddress(section.From);
            email.To.Add(new MailAddress(strTo));
            email.Subject = strSubject;
            email.Body = strBody;

            char delim = ',';
            foreach (string sSubstr in strAttachment.Split(delim))
            {
                // Create  the file attachment for this e-mail message.
                Attachment data = new Attachment(sSubstr, MediaTypeNames.Application.Octet);
                // Add time stamp information for the file.
                ContentDisposition disposition = data.ContentDisposition;
                disposition.CreationDate = System.IO.File.GetCreationTime(sSubstr);
                disposition.ModificationDate = System.IO.File.GetLastWriteTime(sSubstr);
                disposition.ReadDate = System.IO.File.GetLastAccessTime(sSubstr);
                // Add the file attachment to this e-mail message.
                email.Attachments.Add(data);

            }

            // Send the message.
            SmtpClient client = new SmtpClient()
            {
                Host = section.Network.Host,
                Port = section.Network.Port,
                EnableSsl = section.Network.EnableSsl,
                DeliveryMethod = section.DeliveryMethod,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(section.Network.UserName, section.Network.Password)
            };
            //SmtpClient client = new SmtpClient()
            //{
            //    Host = "smtp.gmail.com",
            //    Port = 587,
            //    EnableSsl = true,
            //    DeliveryMethod = SmtpDeliveryMethod.Network,
            //    UseDefaultCredentials = false,
            //    Credentials = new NetworkCredential("ge.thompson.dev@gmail.com", "mollyb74")
            //};

            try
            {
                client.Send(email);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception caught in CreateMessageWithMultipleViews(): {0}", ex.ToString());
            }
        }

        public static void SendEmail(string strTo, string strBCC, string strBody, string strSubject, string strAttachment)
        {
            System.Net.Configuration.SmtpSection section = (System.Net.Configuration.SmtpSection)ConfigurationManager.GetSection("system.net/mailSettings/smtp");

            // Create a message and set up the recipients.
            MailMessage email = new MailMessage();
            email.From = new MailAddress(section.From);
            email.To.Add(new MailAddress(strTo));
            email.Bcc.Add(new MailAddress(strBCC));
            email.Subject = strSubject;
            email.Body = strBody;

            char delim = ',';
            foreach (string sSubstr in strAttachment.Split(delim))
            {
                // Create  the file attachment for this e-mail message.
                Attachment data = new Attachment(sSubstr, MediaTypeNames.Application.Octet);
                // Add time stamp information for the file.
                ContentDisposition disposition = data.ContentDisposition;
                disposition.CreationDate = System.IO.File.GetCreationTime(sSubstr);
                disposition.ModificationDate = System.IO.File.GetLastWriteTime(sSubstr);
                disposition.ReadDate = System.IO.File.GetLastAccessTime(sSubstr);
                // Add the file attachment to this e-mail message.
                email.Attachments.Add(data);

            }

            // Send the message.
            SmtpClient client = new SmtpClient()
            {
                Host = section.Network.Host,
                Port = section.Network.Port,
                EnableSsl = section.Network.EnableSsl,
                DeliveryMethod = section.DeliveryMethod,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(section.Network.UserName, section.Network.Password)
            };

            try
            {
                client.Send(email);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception caught in CreateMessageWithMultipleViews(): {0}", ex.ToString());
            }
        }

        public static void SendEmailMultipleViews(string strTo, string txtBody, string htmlBody, string strSubject)
        {
            System.Net.Configuration.SmtpSection section = (System.Net.Configuration.SmtpSection)ConfigurationManager.GetSection("system.net/mailSettings/smtp");

            // Create a message and set up the recipients.
            MailMessage email = new MailMessage();
            email.From = new MailAddress(section.From);
            email.To.Add(new MailAddress(strTo));
            email.Subject = strSubject;
            email.Body = txtBody;


            // Construct the alternate body as HTML.
            string body = "<!DOCTYPE HTML PUBLIC \"-//W3C//DTD HTML 4.0 Transitional//EN\">";
            body += "<HTML><HEAD><META http-equiv=Content-Type content=\"text/html; charset=iso-8859-1\">";
            body += "</HEAD><BODY>" + htmlBody.Replace("\r\n", "") + "</BODY></HTML>";

            ContentType mimeType = new System.Net.Mime.ContentType("text/html");
            // Add the alternate body to the message.

            AlternateView alternate = AlternateView.CreateAlternateViewFromString(body, mimeType);
            email.AlternateViews.Add(alternate);

            // Send the message.
            SmtpClient client = new SmtpClient()
            {
                Host = section.Network.Host,
                Port = section.Network.Port,
                EnableSsl = section.Network.EnableSsl,
                DeliveryMethod = section.DeliveryMethod,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(section.Network.UserName, section.Network.Password)
            };

            try
            {
                client.Send(email);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception caught in SendEmailMultipleViews(): " + ex.ToString(), "Error");
            }

            alternate.Dispose();
        }

        public static void SendEmailMultipleViews(string strTo, string txtBody, string htmlBody, string strSubject, string strAttachment)
        {
            System.Net.Configuration.SmtpSection section = (System.Net.Configuration.SmtpSection)ConfigurationManager.GetSection("system.net/mailSettings/smtp");

            // Create a message and set up the recipients.
            MailMessage email = new MailMessage();
            email.From = new MailAddress(section.From);
            email.To.Add(new MailAddress(strTo));
            email.Subject = strSubject;
            email.Body = txtBody;


            // Construct the alternate body as HTML.
            string body = "<!DOCTYPE HTML PUBLIC \"-//W3C//DTD HTML 4.0 Transitional//EN\">";
            body += "<HTML><HEAD><META http-equiv=Content-Type content=\"text/html; charset=iso-8859-1\">";
            body += "</HEAD><BODY>" + htmlBody.Replace("\r\n", "") + "</BODY></HTML>";

            ContentType mimeType = new System.Net.Mime.ContentType("text/html");
            // Add the alternate body to the message.

            AlternateView alternate = AlternateView.CreateAlternateViewFromString(body, mimeType);
            email.AlternateViews.Add(alternate);

            char delim = ',';
            foreach (string sSubstr in strAttachment.Split(delim))
            {
                // Create  the file attachment for this e-mail message.
                Attachment data = new Attachment(sSubstr, MediaTypeNames.Application.Octet);
                // Add time stamp information for the file.
                ContentDisposition disposition = data.ContentDisposition;
                disposition.CreationDate = System.IO.File.GetCreationTime(sSubstr);
                disposition.ModificationDate = System.IO.File.GetLastWriteTime(sSubstr);
                disposition.ReadDate = System.IO.File.GetLastAccessTime(sSubstr);
                // Add the file attachment to this e-mail message.
                email.Attachments.Add(data);
            }

            // Send the message.
            SmtpClient client = new SmtpClient()
            {
                Host = section.Network.Host,
                Port = section.Network.Port,
                EnableSsl = section.Network.EnableSsl,
                DeliveryMethod = section.DeliveryMethod,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(section.Network.UserName, section.Network.Password)
            };

            try
            {
                client.Send(email);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception caught in SendEmailMultipleViews(): " + ex.ToString(), "Error");
            }

            alternate.Dispose();
        }

    }

    public class AppFunctions
    {

    }

}