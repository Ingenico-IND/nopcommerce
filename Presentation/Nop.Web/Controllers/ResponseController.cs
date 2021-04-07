using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Headers;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;

namespace Nop.Web.Controllers
{
    public class ResponseController : Controller
    {

        private IHostingEnvironment _env;

        public ResponseController(IHostingEnvironment env)
        {
            _env = env;
        }

        [HttpPost]
        public ActionResult responseHandler(IFormCollection formCollection)
        {
            try
            {
                foreach (var key in formCollection.Keys)
                {
                    var value = formCollection[key];
                }

                string path = _env.WebRootPath;

                string json = "";


                using (StreamReader r = new StreamReader(path + "\\output.json"))
                {
                    json = r.ReadToEnd();

                    r.Close();

                }
                JObject config_data = JObject.Parse(json);
                var data = formCollection["msg"].ToString().Split('|');
                if (data == null)
                {//|| data[1].ToString()== "User Aborted"
                    ViewBag.abrt = true;
                    //return Redirect(ControllerContext.HttpContext.Request.UrlReferrer.ToString());
                    //string referer = Request.Headers["Referer"].ToString();
                    //RequestHeaders header = Request.GetTypedHeaders();
                    //Uri uriReferer = header.Referer;
                    string referer = Request.Headers["Referer"].ToString();
                    return Redirect(referer);
                }
                ViewBag.online_transaction_msg = data;
                if (data[0] == "0300")
                {
                    //var request_str = "{ :merchant => { :identifier => " + config_data["merchantCode"].ToString() + "} ," +
                    //                " :transaction => { :deviceIdentifier => 'S', " +
                    //                     ":currency => " + config_data["currency"] + "," +
                    //                     ":dateTime => " + string.Format("{0:d/M/yyyy", data[8]) + "," +
                    //                     ":token => " + data[5].ToString() + "," +
                    //                     ":requestType => 'S'" +
                    //                     "}" +
                    //                    "}";
                    ViewBag.abrt = false;

                    var strJ = new
                    {
                        merchant = new
                        {
                            identifier = config_data["merchantCode"].ToString()
                        },
                        transaction = new
                        {
                            deviceIdentifier = "S",
                            currency = config_data["currency"],
                            dateTime = string.Format("{0:d/M/yyyy}", data[8].ToString()),
                            token = data[5].ToString(),
                            requestType = "S"
                        }
                    };
                    //     var request_str = Newtonsoft.Json.JsonConvert.SerializeObject(strJ);

                    //  JObject request_data = JObject.Parse(request_str);
                    //using (var client = new HttpClient())
                    //{
                    //    client.BaseAddress = new Uri("https://www.paynimo.com/api/paynimoV2.req");

                    //    //HTTP POST
                    //    var postTask = client.PostAsync("https://www.paynimo.com/api/paynimoV2.req", request_data);
                    //    postTask.Wait();

                    //    var result = postTask.Result;
                    //    if (result.IsSuccessStatusCode)
                    //    {
                    //        return RedirectToAction("Index");
                    //    }
                    //}


                    HttpClient client = new HttpClient();
                    client.BaseAddress = new Uri("https://www.paynimo.com/api/paynimoV2.req");
                    client.DefaultRequestHeaders.Accept.Clear();
                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    HttpResponseMessage response = client.PostAsJsonAsync("https://www.paynimo.com/api/paynimoV2.req", strJ).Result;
                    var a = response.Content.ReadAsStringAsync();

                    JObject dual_verification_result = JObject.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(a));
                    var jsonData = JObject.Parse(dual_verification_result["Result"].ToString()).Children();

                    List<JToken> tokens = jsonData.Children().ToList();

                    var jsonData1 = JObject.Parse(tokens[6].ToString()).Children();
                    List<JToken> tokens1 = jsonData.Children().ToList();
                    ViewBag.dual_verification_result = dual_verification_result;
                    ViewBag.a = a;
                    ViewBag.jsonData = jsonData;
                    ViewBag.tokens = tokens;
                    ViewBag.paramsData = formCollection["msg"];

                    // return response;
                }

            }
            catch (Exception ex)
            {

                //throw;
            }



            return View();
        }
    }
}