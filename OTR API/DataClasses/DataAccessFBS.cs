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
using System.Net.Mail;
using System.Net.Mime;
using System.Net;
using System.Net.Http;

namespace OTR_API.FBS.DataClasses
{
    public class DataAccess
    {
        public enum Mask { None, DateOnly, PhoneWithArea, IpAddress, SSN, Decimal, Digit, Initials };

        protected SqlConnection cnn;

        protected SqlCommand cmd;

        protected void Connect()
        {
            string str = ConfigurationManager.ConnectionStrings["hostFBS"].ConnectionString;

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
}