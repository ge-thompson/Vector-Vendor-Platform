using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.UI;
using System.Web.UI.WebControls;
using System.Net.Http;
using System.Net.Http.Headers;

namespace OTR_API
{
    public partial class inmotionreset : System.Web.UI.Page
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            string strhere = "";
            try
            {


                string email = "";
                string reset = "";

                List<string> keys = new List<string>(Request.QueryString.AllKeys);

                if (keys.Contains("email"))
                    try { email = Request.QueryString["email"]; } catch { }

                if (keys.Contains("reset"))
                    try { reset = Request.QueryString["reset"]; } catch { }

                strhere = "parameters";

                if (email.Length > 0 && reset.Length > 0)
                {
                    using (var client = new HttpClient())
                    {
                        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", "5308b4420df2474da6bf30cb42f9be86:Df668F91cD305F3Aa0050284bE56f890F8127ca0abc=");

                        client.BaseAddress = new Uri("http://inmotion.vectortransport.com");

                        Models.Driver driver = new Models.Driver();
                        driver.EmailAddress = email;
                        driver.ResetToken = reset;

                        var responseTask = client.PutAsJsonAsync("/api/driver/ResetPassword", driver);

                        strhere = "Create Response";

                        responseTask.Wait();

                        strhere = "Wait";

                        var result = responseTask.Result;

                        strhere = "Result";

                        if (result.IsSuccessStatusCode)
                        {
                            strhere = "Success";

                            lblSuccess.Text = "Successful - you should receive an email with a password to " + email;

                            //var readTask = result.Content.ReadAsAsync<Object[]>();
                            //readTask.Wait();

                            // var success = readTask.Result;
                        }
                        else
                        {
                            strhere += ", Failure";

                            lblSuccess.Text = result.Content.ToString() + " - " + strhere;
                        }
                    }

                }
            }
            catch(Exception ex)
            {

                lblSuccess.Text = strhere + " Error: " + ex.Message.ToString() + " - " + strhere;
            }

        }
    }
}