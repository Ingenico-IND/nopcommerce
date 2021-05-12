using System;
using System.Collections.Generic;
using System.IO;
using System.Json;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Nop.Core.Domain.Payments;
using Nop.Services.Configuration;
using Nop.Services.Directory;
using Nop.Services.Events;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Payments;
using Nop.Services.Plugins;
using Nop.Services.Security;
using Nop.Web.Areas.Admin.Factories;
using Nop.Web.Areas.Admin.Models.Payments;
using Nop.Web.Framework.Mvc;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Web.Areas.Admin.Controllers
{
    public partial class PaymentController : BaseAdminController
    {
        #region Fields

        private readonly ICountryService _countryService;
        private readonly IEventPublisher _eventPublisher;
        private readonly ILocalizationService _localizationService;
        private readonly INotificationService _notificationService;
        private readonly IPaymentModelFactory _paymentModelFactory;
        private readonly IPaymentPluginManager _paymentPluginManager;
        private readonly IPermissionService _permissionService;
        private readonly ISettingService _settingService;
        private readonly PaymentSettings _paymentSettings;
        private IHostingEnvironment _env;

        #endregion

        #region Ctor

        public PaymentController(ICountryService countryService,
            IEventPublisher eventPublisher,
            ILocalizationService localizationService,
            INotificationService notificationService,
            IPaymentModelFactory paymentModelFactory,
            IPaymentPluginManager paymentPluginManager,
            IPermissionService permissionService,
            ISettingService settingService,
            PaymentSettings paymentSettings,
             IHostingEnvironment hostingEnvironment)
        {
            _countryService = countryService;
            _eventPublisher = eventPublisher;
            _localizationService = localizationService;
            _notificationService = notificationService;
            _paymentModelFactory = paymentModelFactory;
            _paymentPluginManager = paymentPluginManager;
            _permissionService = permissionService;
            _settingService = settingService;
            _paymentSettings = paymentSettings;
            _env = hostingEnvironment;
        }

        #endregion

        #region AddedFromPreviousForm


        //public  PaymentController(IHostingEnvironment env)
        //{
        //    _env = env;
        //}

        // GET: Payment
        public ActionResult Admin()
        {
            try
            {
                //string path = Server.MapPath("~/App_Data/");
                //string path = _env.WebRootPath;
                string path = _env.WebRootPath;

                string json = "";


                using (StreamReader r = new StreamReader(path + "\\output.json"))
                {
                    json = r.ReadToEnd();
                    var jsonData = JObject.Parse(json).Children();
                    List<JToken> tokens = jsonData.Children().ToList();
                    ViewBag.config_data = tokens;
                    r.Close();

                }
                List<SelectListItem> enbDisb = new List<SelectListItem>()
            {
        new SelectListItem() {Text="Enable", Value="true"},
        new SelectListItem() { Text="Disable", Value="false"}
            };

                List<SelectListItem> currencyCodes = new List<SelectListItem>()
            {
        new SelectListItem() {Text="INR", Value="INR"},
        new SelectListItem() { Text="USD", Value="USD"}
            };

                List<SelectListItem> paymentMode = new List<SelectListItem>()
            {
        new SelectListItem() {Text="all", Value="all"},
        new SelectListItem() { Text="cards", Value="cards"},
         new SelectListItem() {Text="netBanking", Value="netBanking"},
        new SelectListItem() { Text="UPI", Value="UPI"},
         new SelectListItem() {Text="imps", Value="imps"},
        new SelectListItem() { Text="wallets", Value="wallets"},
         new SelectListItem() {Text="cashCards", Value="cashCards"},
        new SelectListItem() { Text="NEFTRTGS", Value="NEFTRTGS"},
          new SelectListItem() { Text="emiBanks", Value="emiBanks"}
            };

                List<SelectListItem> typeOfPayment = new List<SelectListItem>()
            {
        new SelectListItem() {Text="TEST", Value="TEST"},
        new SelectListItem() { Text="LIVE", Value="LIVE"}

            };
                List<SelectListItem> amounttype = new List<SelectListItem>()
            {
        new SelectListItem() { Text="Variable", Value="Variable"},
        new SelectListItem() {Text="Fixed", Value="Fixed"}
            };
                List<SelectListItem> frequency = new List<SelectListItem>()
            {
        new SelectListItem() {Text="As and when presented", Value="ADHO"},
        new SelectListItem() {Text="Daily", Value="DAIL"},
        new SelectListItem() {Text="Weekly", Value="WEEK"},
        new SelectListItem() {Text="Monthly", Value="MNTH"},
        new SelectListItem() {Text="Quarterly", Value="QURT"},
        new SelectListItem() {Text="Semi annually", Value="MIAN"},
        new SelectListItem() {Text="Yearly", Value="YEAR"},
        new SelectListItem() {Text="Bi- monthly", Value="BIMN"}
            };

                ViewBag.enbDisb = enbDisb;
                ViewBag.currencyCodes = currencyCodes;
                ViewBag.paymentModes = paymentMode;
                ViewBag.typeOfPayment = typeOfPayment;
                ViewBag.amounttype = amounttype;
                ViewBag.frequency = frequency;
            }
            catch (Exception ex)
            {

                //   throw;
            }
            return View();
        }


        [HttpPost]
        public ActionResult Admin(IFormCollection formCollection)
        {
            try
            {
                string dataString = formCollection["mrctCode"] + "|" + formCollection["txn_id"] + "|" + formCollection["amount"] + "|" + formCollection["accNo"] + "|"
              + formCollection["custID"] + "|" + formCollection["mobNo"] + "|" + formCollection["email"] + "|" + formCollection["debitStartDate"] + "|" + formCollection["debitEndDate"]
              + "|" + formCollection["maxAmount"] + "|" + formCollection["amountType"] + "|" + formCollection["frequency"] + "|" + formCollection["cardNumber"] + "|"
              + formCollection["expMonth"] + "|" + formCollection["expYear"] + "|" + formCollection["cvvCode"] + "|" + formCollection["SALT"];
                string hh = GenerateSHA512String(dataString);
                //formCollection["authenticity_token"] = hh;

                var personlist = ToJSON(formCollection);
                // Pass the "personlist" object for conversion object to JSON string  
                //string jsondata = new JavaScriptSerializer().Serialize(personlist);
                string jsondata = JsonConvert.SerializeObject(personlist);
                string path = _env.WebRootPath;
                //string path = Server.MapPath("~/App_Data/");
                // Write that JSON to txt file,  

                JsonValue json = JsonValue.Parse(jsondata);

                //  result = System.Web.Helpers.Json.parse(jsondata);
                //  FileStream SourceStream= System.IO.File.Open(path + "output.json",System.IO.FileMode.OpenOrCreate);
                //System.IO.File.WriteAllText(path + "\\output.json", json);
                System.IO.File.WriteAllText(path + "\\output.json", json);

                using (StreamReader r = new StreamReader(path + "\\output.json"))
                {
                    //json = r.ReadToEnd();
                    json = r.ReadToEnd();
                    var jsonData = JObject.Parse(json).Children();
                    List<JToken> tokens = jsonData.Children().ToList();
                    ViewBag.config_data = tokens;
                    r.Close();

                }

                List<SelectListItem> enbDisb = new List<SelectListItem>()
            {
        new SelectListItem() {Text="Enable", Value="true"},
        new SelectListItem() { Text="Disable", Value="false"}
            };

                List<SelectListItem> currencyCodes = new List<SelectListItem>()
            {
        new SelectListItem() {Text="INR", Value="INR"},
        new SelectListItem() { Text="USD", Value="USD"}
            };

                List<SelectListItem> paymentMode = new List<SelectListItem>()
            {
        new SelectListItem() {Text="all", Value="all"},
        new SelectListItem() { Text="cards", Value="cards"},
         new SelectListItem() {Text="netBanking", Value="netBanking"},
        new SelectListItem() { Text="UPI", Value="UPI"},
         new SelectListItem() {Text="imps", Value="imps"},
        new SelectListItem() { Text="wallets", Value="wallets"},
         new SelectListItem() {Text="cashCards", Value="cashCards"},
        new SelectListItem() { Text="NEFTRTGS", Value="NEFTRTGS"},
          new SelectListItem() { Text="emiBanks", Value="emiBanks"}
            };

                List<SelectListItem> typeOfPayment = new List<SelectListItem>()
            {
        new SelectListItem() {Text="TEST", Value="TEST"},
        new SelectListItem() { Text="LIVE", Value="LIVE"}

            };
                List<SelectListItem> amounttype = new List<SelectListItem>()
            {
        new SelectListItem() {Text="Fixed", Value="Fixed"},
        new SelectListItem() { Text="Variable", Value="Variable"}
            };
                List<SelectListItem> frequency = new List<SelectListItem>()
            {
        new SelectListItem() {Text="Daily", Value="DAIL"},
        new SelectListItem() {Text="Weekly", Value="WEEK"},
        new SelectListItem() {Text="Monthly", Value="MNTH"},
        new SelectListItem() {Text="Quarterly", Value="QURT"},
        new SelectListItem() {Text="Semi annually", Value="MIAN"},
        new SelectListItem() {Text="Yearly", Value="YEAR"},
        new SelectListItem() {Text="Bi- monthly", Value="BIMN"},
        new SelectListItem() {Text="As and when presented", Value="ADHO"}
            };

                ViewBag.enbDisb = enbDisb;
                ViewBag.currencyCodes = currencyCodes;
                ViewBag.paymentModes = paymentMode;
                ViewBag.typeOfPayment = typeOfPayment;
                ViewBag.amounttype = amounttype;
                ViewBag.frequency = frequency;
            }
            catch (Exception ex)
            {
            }
            //  TempData["msg"] = "Json file Generated! check this in your App_Data folder";
            return View();
        }
        public ActionResult OfflineVerification()
        {
            try
            {
                string path = _env.WebRootPath;
                string tranId = GenerateRandomString(12);
                ViewBag.tranId = tranId;

                using (StreamReader r = new StreamReader(path + "\\output.json"))
                {
                    string json = r.ReadToEnd();


                    ViewBag.config_data = json;
                }
            }
            catch (Exception ex)
            {

                // throw;
            }

            return View();
        }
        public ActionResult Refund()
        {
            try
            {
                string path = _env.WebRootPath;
                string tranId = GenerateRandomString(12);
                ViewBag.tranId = tranId;

                using (StreamReader r = new StreamReader(path + "\\output.json"))
                {
                    string json = r.ReadToEnd();


                    ViewBag.config_data = json;
                }
            }
            catch (Exception ex)
            {

                //       throw;
            }

            return View();
        }

        public ActionResult Reconcile()
        {
            return View();
        }

        [HttpPost]
        public ActionResult Reconcile(IFormCollection fc)
        {
            try
            {
                string path = _env.WebRootPath;

                string json = "";


                using (StreamReader r = new StreamReader(path + "\\output.json"))
                {
                    json = r.ReadToEnd();

                    r.Close();

                }

                JObject config_data = JObject.Parse(json);
                var transaction_ids = fc["merchantRefNo"].ToString().Replace(System.Environment.NewLine, "").Replace(" ", "").Split(',');
                //List<JToken> tokens = new List<JToken>();
                var transDetails = new List<object>();
                //DateTime start_date = DateTime.ParseExact(fc["fromDate"].ToString(), "dd-mm-yyyy", CultureInfo.InvariantCulture);
                //DateTime end_date = DateTime.ParseExact(fc["endDate"], "dd-mm-yyyy", CultureInfo.InvariantCulture);
                DateTime start_date = DateTime.Parse(fc["fromDate"].ToString());
                DateTime end_date = DateTime.Parse(fc["endDate"].ToString());
                var diff = (end_date - start_date).TotalDays;

                foreach (var transaction_id in transaction_ids)
                {
                    int cntK = 0;
                    for (var day = start_date; day <= end_date; day = day.AddDays(1))
                    {
                        var data = new
                        {
                            merchant = new { identifier = config_data["merchantCode"] },
                            transaction = new
                            {
                                deviceIdentifier = "S",
                                currency = config_data["currency"],
                                identifier = transaction_id,
                                dateTime = day.ToString("dd-M-yyyy"),
                                //string.Format("{0:d/M/yyyy}", day.ToString()),
                                requestType = "O"

                            }

                        };

                        HttpClient client = new HttpClient();
                        client.BaseAddress = new Uri("https://www.paynimo.com/api/paynimoV2.req");
                        client.DefaultRequestHeaders.Accept.Clear();
                        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                        HttpResponseMessage response = client.PostAsJsonAsync("https://www.paynimo.com/api/paynimoV2.req", data).Result;
                        var respStr = response.Content.ReadAsStringAsync();

                        data = null;
                        JObject dual_verification_result = JObject.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(respStr));
                        var jsonData = JObject.Parse(dual_verification_result["Result"].ToString()).Children();

                        List<JToken> tokens = jsonData.Children().ToList();
                        if (tokens[6]["paymentTransaction"]["errorMessage"].ToString() == "Transactionn Not Found")
                        {
                            cntK = cntK + 1;
                            if (cntK == diff)
                            {
                                transDetails.Add(tokens);
                                ;
                                tokens = null;
                            }

                            //  break;

                        }
                        else
                        {
                            transDetails.Add(tokens);
                            ;
                            tokens = null;
                            break;
                        }

                    }
                }
                ViewBag.transDetails = transDetails;

            }
            catch (Exception ex)
            {

                //   throw;
            }

            return View();
        }

        public ActionResult s2s(string msg)
        {
            try
            {
                string json = string.Empty;
                string path = _env.WebRootPath;
                string tranId = GenerateRandomString(12);
                ViewBag.tranId = tranId;

                JsonTextReader jsonTextReader = null; //Added_N

                using (StreamReader r = new StreamReader(path + "\\output.json"))
                {
                    json = r.ReadToEnd();
                    ViewBag.config_data = json;
                    jsonTextReader = new JsonTextReader(r);  //Added_N
                }

                var data = msg.Split('|');
                ViewBag.clnt_txn_ref = data[3];
                ViewBag.pg_txn_id = data[5];
                //var dejson=JsonConvert.DeserializeObject(json);
                //JavaScriptSerializer js = new JavaScriptSerializer();
                //JsonSerializer js = new JsonSerializer();  //Added_N 
                //string jtRead=js.Serialize(json,);

                //dynamic dejson = js.Deserialize<dynamic>(json);

                var dejson = JsonConvert.DeserializeObject<dynamic>(json);
                // dynamic dejson = js.Deserialize<dynamic>(jsonTextReader);

                StringBuilder res = new StringBuilder();
                for (int i = 0; i < data.Length - 1; i++)
                {
                    res.Append(data[i] + "|");
                }
                string data_string = res.ToString() + dejson["SALT"];
                var hash = GenerateSHA512StringFors2s(data_string);
                //var hash = GenerateSHA512StringFors2s(data_string);
                //if (data[15].ToString() == hash.Data.ToString().ToLower())
                if (data[15].ToString() == hash.Value.ToString().ToLower())
                {
                    ViewBag.status = "1";
                }
                else
                {
                    ViewBag.status = "0";
                }
            }
            catch (Exception ex)
            {
                //throw ex;
            }
            return View();
        }

        public static string GenerateSHA512String(string inputString)
        {
            SHA512 sha512 = SHA512Managed.Create();
            byte[] bytes = Encoding.UTF8.GetBytes(inputString);
            byte[] hash = sha512.ComputeHash(bytes);
            return GetStringFromHash(hash);
        }
        public JsonResult GenerateSHA512StringFors2s(string inputString) //Addded_NM
        {
            using (SHA512 sha512Hash = SHA512.Create())
            {
                //From String to byte array

                byte[] sourceBytes = Encoding.UTF8.GetBytes(inputString);
                byte[] hashBytes = sha512Hash.ComputeHash(sourceBytes);
                string hash = BitConverter.ToString(hashBytes).Replace("-", String.Empty);
                System.Security.Cryptography.SHA512Managed sha512 = new System.Security.Cryptography.SHA512Managed();
                Byte[] EncryptedSHA512 = sha512.ComputeHash(System.Text.Encoding.UTF8.GetBytes(hash));
                sha512.Clear();
                var bts = Convert.ToBase64String(EncryptedSHA512);
                //return Json(hash, JsonRequestBehavior.AllowGet); //Added_N
                return Json(hash);
            }
        }
        private static string GetStringFromHash(byte[] hash)
        {
            StringBuilder result = new StringBuilder();
            for (int i = 0; i < hash.Length; i++)
            {
                result.Append(hash[i].ToString("X2"));
            }
            return result.ToString();
        }
        public static string GenerateRandomString(int size)
        {
            Guid g = Guid.NewGuid();

            string random1 = g.ToString();
            random1 = random1.Replace("-", "");
            var builder = random1.Substring(0, size);
            return builder.ToString();
        }
        public string ToJSON(IFormCollection collection)
        {
            var list = new Dictionary<string, string>();
            foreach (string key in collection.Keys)
            {
                list.Add(key, collection[key]);
            }

            return JsonConvert.SerializeObject(list); //Added_N
            //return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(list);
        }
        #endregion

        #region Methods  

        public virtual IActionResult PaymentMethods()
        {
            return RedirectToAction("Methods");
        }

        public virtual IActionResult Methods()
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            //prepare model
            var model = _paymentModelFactory.PreparePaymentMethodsModel(new PaymentMethodsModel());

            return View(model);
        }

        [HttpPost]
        public virtual IActionResult Methods(PaymentMethodSearchModel searchModel)
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedDataTablesJson();

            //prepare model
            var model = _paymentModelFactory.PreparePaymentMethodListModel(searchModel);

            return Json(model);
        }

        [HttpPost]
        public virtual IActionResult MethodUpdate(PaymentMethodModel model)
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            var pm = _paymentPluginManager.LoadPluginBySystemName(model.SystemName);
            if (_paymentPluginManager.IsPluginActive(pm))
            {
                if (!model.IsActive)
                {
                    //mark as disabled
                    _paymentSettings.ActivePaymentMethodSystemNames.Remove(pm.PluginDescriptor.SystemName);
                    _settingService.SaveSetting(_paymentSettings);
                }
            }
            else
            {
                if (model.IsActive)
                {
                    //mark as active
                    _paymentSettings.ActivePaymentMethodSystemNames.Add(pm.PluginDescriptor.SystemName);
                    _settingService.SaveSetting(_paymentSettings);
                }
            }

            var pluginDescriptor = pm.PluginDescriptor;
            pluginDescriptor.FriendlyName = model.FriendlyName;
            pluginDescriptor.DisplayOrder = model.DisplayOrder;

            //update the description file
            pluginDescriptor.Save();

            //raise event
            _eventPublisher.Publish(new PluginUpdatedEvent(pluginDescriptor));

            return new NullJsonResult();
        }

        public virtual IActionResult MethodRestrictions()
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            //prepare model
            var model = _paymentModelFactory.PreparePaymentMethodsModel(new PaymentMethodsModel());

            return View(model);
        }

        //we ignore this filter for increase RequestFormLimits
        [AdminAntiForgery(true)]
        //we use 2048 value because in some cases default value (1024) is too small for this action
        [RequestFormLimits(ValueCountLimit = 2048)]
        [HttpPost, ActionName("MethodRestrictions")]
        public virtual IActionResult MethodRestrictionsSave(PaymentMethodsModel model, IFormCollection form)
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            var paymentMethods = _paymentPluginManager.LoadAllPlugins();
            var countries = _countryService.GetAllCountries(showHidden: true);

            foreach (var pm in paymentMethods)
            {
                var formKey = "restrict_" + pm.PluginDescriptor.SystemName;
                var countryIdsToRestrict = (!StringValues.IsNullOrEmpty(form[formKey])
                        ? form[formKey].ToString().Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList()
                        : new List<string>())
                    .Select(x => Convert.ToInt32(x)).ToList();

                var newCountryIds = new List<int>();
                foreach (var c in countries)
                {
                    if (countryIdsToRestrict.Contains(c.Id))
                    {
                        newCountryIds.Add(c.Id);
                    }
                }

                _paymentPluginManager.SaveRestrictedCountries(pm, newCountryIds);
            }

            _notificationService.SuccessNotification(_localizationService.GetResource("Admin.Configuration.Payment.MethodRestrictions.Updated"));
            
            return RedirectToAction("MethodRestrictions");
        }

        #endregion
    }
}