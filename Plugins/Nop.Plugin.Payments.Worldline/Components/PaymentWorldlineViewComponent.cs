using System;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Nop.Web.Framework.Components;
using Nop.Core.Domain.Orders;
using Nop.Services.Orders;
using Nop.Core;
using Nop.Web;
using Nop.Web.Framework.Components;
using Nop.Web;
using Nop.Web.Factories;
using System.IO;
using Newtonsoft.Json.Linq;

using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Nop.Services.Configuration;

namespace Nop.Plugin.Payments.Worldline.Components
{
    [ViewComponent(Name = "PaymentWorldline")]
    public class PaymentWorldlineViewComponent : NopViewComponent
    {
        private IHostingEnvironment _env;
      //  private readonly IShoppingCartModelFactory = Nop.Web.Factories.IProductModelFactory;
        private readonly IShoppingCartModelFactory _shoppingCartModelFactory;
        private readonly IShoppingCartService _shoppingCartService;
        private readonly IStoreContext _storeContext;
        private readonly IWorkContext _workContext;
        private readonly ISettingService _settingService;

        public PaymentWorldlineViewComponent(IHostingEnvironment env, IShoppingCartModelFactory shoppingCartModelFactory, IShoppingCartService shoppingCartService,
            IStoreContext storeContext, ISettingService settingService,
            IWorkContext workContext)
        {
            _env = env;
            _shoppingCartModelFactory = shoppingCartModelFactory;
            _shoppingCartService = shoppingCartService;
            _settingService = settingService;
            _storeContext = storeContext;
            _workContext = workContext;
        }
        public IViewComponentResult Invoke()
        {
            string path = _env.WebRootPath;
            string tranId = GenerateRandomString(12);
            ViewBag.tranId = tranId;

            ViewBag.debitStartDate = DateTime.Now.ToString("yyyy-MM-dd");
            int year = Convert.ToInt32(DateTime.Now.Year.ToString());
            DateTime date = DateTime.Now;
            var enddate = date.AddYears(30);
            ViewBag.debitEndDate = enddate.ToString("yyyy-MM-dd");

            using (StreamReader r = new StreamReader(path + "//output.json"))
            {
                string json = r.ReadToEnd();

                ViewBag.config_data = json;
                var jsonData = JObject.Parse(json).Children();
                List<JToken> tokens = jsonData.Children().ToList();
                if (Convert.ToBoolean(tokens[25]) == true)
                {
                    if (Convert.ToBoolean(tokens[34]) == true)
                    {
                        ViewBag.enbSi = Convert.ToBoolean(tokens[25]);
                    }
                    else
                    {
                        ViewBag.enbSi = false;
                    }
                }
                else
                {
                    ViewBag.enbSi = false;
                }
            }

            var cart = _shoppingCartService.GetShoppingCart(_workContext.CurrentCustomer, ShoppingCartType.ShoppingCart, _storeContext.CurrentStore.Id);
            var model = _shoppingCartModelFactory.PrepareOrderTotalsModel(cart, false);

            ViewBag.merchantcode = _settingService.GetSetting("worldlinepaymentsettings.merchantcode").Value.ToString();
            ViewBag.currency = _settingService.GetSetting("worldlinepaymentsettings.currency").Value.ToString();
            ViewBag.SALT = _settingService.GetSetting("worldlinepaymentsettings.SALT").Value.ToString();


            ViewBag.paymentMode = _settingService.GetSetting("worldlinepaymentsettings.paymentMode").Value.ToString();
            ViewBag.paymentModeOrder = _settingService.GetSetting("worldlinepaymentsettings.paymentModeOrder").Value.ToString();
            //ViewBag.checkoutElement = _settingService.GetSetting("worldlinepaymentsettings.checkoutElement").Value.ToString();
            ViewBag.merchantLogoUrl = _settingService.GetSetting("worldlinepaymentsettings.logoURL").Value.ToString();
            ViewBag.merchantMsg = _settingService.GetSetting("worldlinepaymentsettings.merchantMessage").Value.ToString();
            ViewBag.disclaimerMsg = _settingService.GetSetting("worldlinepaymentsettings.disclaimerMessage").Value.ToString();

            ViewBag.primaryColor = _settingService.GetSetting("worldlinepaymentsettings.primaryColor").Value.ToString();
            ViewBag.secondaryColor = _settingService.GetSetting("worldlinepaymentsettings.secondaryColor").Value.ToString();
            ViewBag.buttonColor1 = _settingService.GetSetting("worldlinepaymentsettings.buttonColor1").Value.ToString();
            ViewBag.buttonColor2 = _settingService.GetSetting("worldlinepaymentsettings.buttonColor2").Value.ToString();
            ViewBag.merchantSchemeCode = _settingService.GetSetting("worldlinepaymentsettings.merchantSchemeCode").Value.ToString();
            ViewBag.showPGResponseMsg = Convert.ToBoolean(_settingService.GetSetting("worldlinepaymentsettings.showPGResponseMsg").Value.ToString().ToLower());
            ViewBag.enableAbortResponse = Convert.ToBoolean(_settingService.GetSetting("worldlinepaymentsettings.enableAbortResponse").Value.ToString().ToLower());
            ViewBag.enableExpressPay = Convert.ToBoolean(_settingService.GetSetting("worldlinepaymentsettings.enableExpressPay").Value.ToString().ToLower());
            ViewBag.enableNewWindowFlow = Convert.ToBoolean(_settingService.GetSetting("worldlinepaymentsettings.enableNewWindowFlow").Value.ToString().ToLower());
            ViewBag.enableDebitDay = _settingService.GetSetting("worldlinepaymentsettings.merchantSchemeCode").Value.ToString();
            ViewBag.siDetailsAtMerchantEnd = Convert.ToBoolean(_settingService.GetSetting("worldlinepaymentsettings.siDetailsAtMerchantEnd").Value.ToString().ToLower());
            ViewBag.enableSI = Convert.ToBoolean(_settingService.GetSetting("worldlinepaymentsettings.enableSI").Value.ToString().ToLower());
            ViewBag.embedPaymentGatewayOnPage = Convert.ToBoolean(_settingService.GetSetting("worldlinepaymentsettings.embedPaymentGatewayOnPage").Value.ToString().ToLower());


            if (_settingService.GetSetting("worldlinepaymentsettings.merchantSchemeCode").Value.ToString().ToLower()=="test")
            {
                ViewBag.ordTtl = "1.00".ToString();
            }
            else
            {
                ViewBag.ordTtl = model.OrderTotal.Substring(1).Trim();
            }

           



           
            ViewBag.custId=_workContext.CurrentCustomer.Id;

            return View("~/Plugins/Payments.Worldline/Views/PaymentInfo.cshtml");
        }
        public string GenerateRandomString(int size)
        {
            var temp = Guid.NewGuid().ToString().Replace("-", string.Empty);
            var barcode = Regex.Replace(temp, "[a-zA-Z]", string.Empty).Substring(0, 10);

            return barcode.ToString();
        }
        //public JsonResult GenerateSHA512String(string inputString)
        //{
        //    using (SHA512 sha512Hash = SHA512.Create())
        //    {
        //        //From String to byte array
        //        byte[] sourceBytes = Encoding.UTF8.GetBytes(inputString);
        //        byte[] hashBytes = sha512Hash.ComputeHash(sourceBytes);
        //        string hash = BitConverter.ToString(hashBytes).Replace("-", String.Empty);

        //        System.Security.Cryptography.SHA512Managed sha512 = new System.Security.Cryptography.SHA512Managed();

        //        Byte[] EncryptedSHA512 = sha512.ComputeHash(System.Text.Encoding.UTF8.GetBytes(hash));

        //        sha512.Clear();

        //        var bts = Convert.ToBase64String(EncryptedSHA512);

        //        //return Json(hash, JsonRequestBehavior.AllowGet);
        //        return Json(hash, new Newtonsoft.Json.JsonSerializerSettings());
        //    }
        //}
    }
}
