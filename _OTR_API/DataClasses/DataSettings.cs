using System;
using System.Data;
using System.Data.SqlClient;

namespace OTR_API.TruckerTools.DataClasses
{
    public class DataSettings : DataAccess
    {
        /// <summary>
        /// Returns true if the named setting's Detail = '1'.
        /// Returns defaultIfMissing if the row doesn't exist or value is not '1'/'0'.
        /// Returns defaultIfMissing on any read error (audit-logged).
        /// </summary>
        public bool IsEnabled(string description, bool defaultIfMissing = true)
        {
            bool result = defaultIfMissing;

            try
            {
                Connect();

                cmd = new SqlCommand("SELECT Detail FROM Settings WHERE Description = @desc", cnn);
                cmd.Parameters.AddWithValue("@desc", description);

                object o = cmd.ExecuteScalar();
                if (o != null && o != DBNull.Value)
                {
                    string val = Convert.ToString(o);
                    if (val == "1") result = true;
                    else if (val == "0") result = false;
                }
            }
            catch (Exception ex)
            {
                OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit();
                da.InsertErrorAuditLog(ex.Message, "DataSettings.IsEnabled");
            }
            finally
            {
                Disconnect();
            }

            return result;
        }
    }
}
