using System;
using System.Text;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using System.Collections.Generic;

namespace OTR_API.DataClasses
{
    public class AES256CBCEncrypter : IDisposable
    {
        void IDisposable.Dispose()
        {

        }

        private static readonly Encoding encoding = Encoding.UTF8;

        public static string Encrypt(string plainText, string key, string iv)
        {
            try
            {
                RijndaelManaged aes = new RijndaelManaged();
                aes.KeySize = 256;
                aes.BlockSize = 128;
                aes.Padding = PaddingMode.PKCS7;
                aes.Mode = CipherMode.CBC;

                aes.Key = encoding.GetBytes(key);
                aes.IV = encoding.GetBytes(iv);

                ICryptoTransform AESEncrypt = aes.CreateEncryptor(aes.Key, aes.IV);
                byte[] buffer = encoding.GetBytes(plainText);

                string encryptedText = Convert.ToBase64String(AESEncrypt.TransformFinalBlock(buffer, 0, buffer.Length));

                return encryptedText;
            }
            catch (Exception e)
            {
                throw new Exception("Error encrypting: " + e.Message);
            }
        }

        public static string Decrypt(string plainText, string key, string iv)
        {
            try
            {
                RijndaelManaged aes = new RijndaelManaged();
                aes.KeySize = 256;
                aes.BlockSize = 128;
                aes.Padding = PaddingMode.PKCS7;
                aes.Mode = CipherMode.CBC;
                aes.Key = encoding.GetBytes(key);
                aes.IV = encoding.GetBytes(iv);

                byte[] base64Decoded = Convert.FromBase64String(plainText);
                string base64DecodedStr = encoding.GetString(base64Decoded);

                ICryptoTransform AESDecrypt = aes.CreateDecryptor(aes.Key, aes.IV);

                byte[] buffer = Convert.FromBase64String(plainText);

                return encoding.GetString(AESDecrypt.TransformFinalBlock(buffer, 0, buffer.Length));
            }
            catch (Exception e)
            {
                throw new Exception("Error decrypting: " + e.Message);
            }
        }

        static byte[] HmacSHA256(String data, String key)
        {
            using (HMACSHA256 hmac = new HMACSHA256(encoding.GetBytes(key)))
            {
                return hmac.ComputeHash(encoding.GetBytes(data));
            }
        }

        public static byte[] MD5Byte(string input)
        {
            System.Text.StringBuilder hash = new System.Text.StringBuilder();
            MD5CryptoServiceProvider md5provider = new MD5CryptoServiceProvider();
            byte[] bytes = md5provider.ComputeHash(new System.Text.UTF8Encoding().GetBytes(input));

            return bytes;
        }

        public static string MD5Hash(string input)
        {
            System.Text.StringBuilder hash = new System.Text.StringBuilder();
            MD5CryptoServiceProvider md5provider = new MD5CryptoServiceProvider();
            byte[] bytes = md5provider.ComputeHash(new System.Text.UTF8Encoding().GetBytes(input));

            for (int i = 0; i < bytes.Length; i++)
            {
                hash.Append(bytes[i].ToString("x2"));
            }
            return hash.ToString();
        }

        public static string Friendly(string input)
        {
            //replace all ‘+’ (plus)characters with ‘-’ (dash)
            //replace all ‘/’ (slash)characters with ‘_’ (underscore)
            //remove all ‘=’ (equal)characters.

            return input.Replace("+","-").Replace("/","_").Replace("=","");
        }

        public static string UnFriendly(string input)
        {

            return input.Replace("-", "+").Replace("_", "/");
        }

        public string TTAuthentication(string input, string secretKey)
        {

            // Convert Secret Key to 32 characters.
            byte[] secretbyte = AES256CBCEncrypter.MD5Byte(secretKey);

            //Convert Secret Byte to String
            StringBuilder secretasStr = new StringBuilder();
            for (int i = 0; i < secretbyte.Length; i++)
            {
                secretasStr.Append(secretbyte[i].ToString("x2"));
            }

            //IV is first 16 characters of the Secret Key
            string iv = secretKey.Substring(0, 16);


            // Encrypt and decrypt the sample text via the Aes256CbcEncrypter class.
            string Encrypted = AES256CBCEncrypter.Encrypt(input, secretasStr.ToString(), iv);
            
            //Convert encrypted input to url friendly text
            string friendly = AES256CBCEncrypter.Friendly(Encrypted);

            //Base64 Encode Friendly URL Text
            byte[] urlencode = Encoding.UTF8.GetBytes(AES256CBCEncrypter.Friendly(Encrypted));

            //Convert Base64 Encode Friendly URL Text to string
            string urlencode64 = Convert.ToBase64String(urlencode);

            //Could be an error here - the suggested encoding would include LF as the new line character but by default dotnet is using CRLF - not sure how to change this.


            return urlencode64;
        }
    }
}