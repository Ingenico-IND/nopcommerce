using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Newtonsoft.Json.Linq;
using Nop.Core;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Shipping;
using Nop.Plugin.Payments.Worldline.Services;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Directory;
using Nop.Services.Localization;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Plugins;
using Nop.Services.Security;
using Nop.Services.Tax;
using Nop.Web.Framework.Menu;

namespace Nop.Plugin.Payments.Worldline
{
    /// <summary>
    /// Worldline payment processor
    /// </summary>
    public class WorldlinePaymentProcessor : BasePlugin, IPaymentMethod
    {
        #region Fields

        private readonly CurrencySettings _currencySettings;
        private readonly ICheckoutAttributeParser _checkoutAttributeParser;
        private readonly ICurrencyService _currencyService;
        private readonly IGenericAttributeService _genericAttributeService;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILocalizationService _localizationService;
        private readonly IPaymentService _paymentService;
        private readonly ISettingService _settingService;
        private readonly ITaxService _taxService;
        private readonly IWebHelper _webHelper;
        private readonly WorldlineStandardHttpClient _WorldlineHttpClient;
        private readonly WorldlinePaymentSettings _WorldlinePaymentSettings;
        private readonly IPermissionService _permissionService;

        #endregion

        #region Ctor

        public WorldlinePaymentProcessor(CurrencySettings currencySettings,
            ICheckoutAttributeParser checkoutAttributeParser,
            ICurrencyService currencyService,
            IGenericAttributeService genericAttributeService,
            IHttpContextAccessor httpContextAccessor,
            ILocalizationService localizationService,
            IPaymentService paymentService,
            ISettingService settingService,
            ITaxService taxService,
            IWebHelper webHelper,
            IPermissionService permissionService,
            WorldlineStandardHttpClient WorldlineHttpClient,
            WorldlinePaymentSettings WorldlinePaymentSettings)
        {
            _currencySettings = currencySettings;
            _checkoutAttributeParser = checkoutAttributeParser;
            _currencyService = currencyService;
            _genericAttributeService = genericAttributeService;
            _httpContextAccessor = httpContextAccessor;
            _localizationService = localizationService;
            _paymentService = paymentService;
            _settingService = settingService;
            _taxService = taxService;
            _webHelper = webHelper;
            _WorldlineHttpClient = WorldlineHttpClient;
            _WorldlinePaymentSettings = WorldlinePaymentSettings;
            _permissionService = permissionService;
        }

        #endregion

        #region Utilities

        public void ManageSiteMap(SiteMapNode rootNode)
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManagePaymentMethods))
                return;

            var myPluginNode = rootNode.ChildNodes.FirstOrDefault(x => x.SystemName == "MyPlugin");
            if (myPluginNode == null)
            {
                myPluginNode = new SiteMapNode()
                {
                    SystemName = "MyPlugin",
                    Title = "My Plugin",
                    Visible = true,
                    IconClass = "fa-gear"
                };
                rootNode.ChildNodes.Add(myPluginNode);
            }

            myPluginNode.ChildNodes.Add(new SiteMapNode()
            {
                SystemName = "MyPlugin.Configure",
                Title = "Configure",
                ControllerName = "Plugin",
                ActionName = "ConfigureMiscPlugin",
                Visible = true,
                IconClass = "fa-dot-circle-o",
                RouteValues = new RouteValueDictionary() { { "systemName", "Nop.Plugin.Misc.MyPlugin" } },
            });

            myPluginNode.ChildNodes.Add(new SiteMapNode()
            {
                SystemName = "MyPlugin.SomeFeatureList",
                Title = "Some Feature",
                ControllerName = "MyPlugin",
                ActionName = "SomeFeatureList",
                Visible = true,
                IconClass = "fa-dot-circle-o",
                RouteValues = new RouteValueDictionary() { { "area", "Admin" } },  //need to register this route since it has /Admin prefix
            });
        }

            /// <summary>
            /// Gets PDT details
            /// </summary>
            /// <param name="tx">TX</param>
            /// <param name="values">Values</param>
            /// <param name="response">Response</param>
            /// <returns>Result</returns>
            public bool GetPdtDetails(string tx, out Dictionary<string, string> values, out string response)
        {
            response = WebUtility.UrlDecode(_WorldlineHttpClient.GetPdtDetailsAsync(tx).Result);

            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            bool firstLine = true, success = false;
            foreach (var l in response.Split('\n'))
            {
                var line = l.Trim();
                if (firstLine)
                {
                    success = line.Equals("SUCCESS", StringComparison.OrdinalIgnoreCase);
                    firstLine = false;
                }
                else
                {
                    var equalPox = line.IndexOf('=');
                    if (equalPox >= 0)
                        values.Add(line.Substring(0, equalPox), line.Substring(equalPox + 1));
                }
            }

            return success;
        }

        /// <summary>
        /// Verifies IPN
        /// </summary>
        /// <param name="formString">Form string</param>
        /// <param name="values">Values</param>
        /// <returns>Result</returns>
        public bool VerifyIpn(string formString, out Dictionary<string, string> values)
        {
            var response = WebUtility.UrlDecode(_WorldlineHttpClient.VerifyIpnAsync(formString).Result);
            var success = response.Trim().Equals("VERIFIED", StringComparison.OrdinalIgnoreCase);

            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var l in formString.Split('&'))
            {
                var line = l.Trim();
                var equalPox = line.IndexOf('=');
                if (equalPox >= 0)
                    values.Add(line.Substring(0, equalPox), line.Substring(equalPox + 1));
            }

            return success;
        }

        /// <summary>
        /// Create common query parameters for the request
        /// </summary>
        /// <param name="postProcessPaymentRequest">Payment info required for an order processing</param>
        /// <returns>Created query parameters</returns>
        private IDictionary<string, string> CreateQueryParameters(PostProcessPaymentRequest postProcessPaymentRequest)
        {
            //get store location
            var storeLocation = _webHelper.GetStoreLocation();

            //choosing correct order address
            var orderAddress = postProcessPaymentRequest.Order.PickupInStore
                    ? postProcessPaymentRequest.Order.PickupAddress
                    : postProcessPaymentRequest.Order.ShippingAddress;

            //create query parameters
            return new Dictionary<string, string>
            {
                //Worldline ID or an email address associated with your Worldline account
                ["business"] = _WorldlinePaymentSettings.BusinessEmail,

                //the character set and character encoding
                ["charset"] = "utf-8",

                //set return method to "2" (the customer redirected to the return URL by using the POST method, and all payment variables are included)
                ["rm"] = "2",

                ["bn"] = WorldlineHelper.NopCommercePartnerCode,
                ["currency_code"] = _currencyService.GetCurrencyById(_currencySettings.PrimaryStoreCurrencyId)?.CurrencyCode,

                //order identifier
                ["invoice"] = postProcessPaymentRequest.Order.CustomOrderNumber,
                ["custom"] = postProcessPaymentRequest.Order.OrderGuid.ToString(),

                //PDT, IPN and cancel URL
                ["return"] = $"{storeLocation}Plugins/PaymentWorldline/PDTHandler",
                ["notify_url"] = $"{storeLocation}Plugins/PaymentWorldline/IPNHandler",
                ["cancel_return"] = $"{storeLocation}Plugins/PaymentWorldline/CancelOrder",

                //shipping address, if exists
                ["no_shipping"] = postProcessPaymentRequest.Order.ShippingStatus == ShippingStatus.ShippingNotRequired ? "1" : "2",
                ["address_override"] = postProcessPaymentRequest.Order.ShippingStatus == ShippingStatus.ShippingNotRequired ? "0" : "1",
                ["first_name"] = orderAddress?.FirstName,
                ["last_name"] = orderAddress?.LastName,
                ["address1"] = orderAddress?.Address1,
                ["address2"] = orderAddress?.Address2,
                ["city"] = orderAddress?.City,
                ["state"] = orderAddress?.StateProvince?.Abbreviation,
                ["country"] = orderAddress?.Country?.TwoLetterIsoCode,
                ["zip"] = orderAddress?.ZipPostalCode,
                ["email"] = orderAddress?.Email
            };
        }

        /// <summary>
        /// Add order items to the request query parameters
        /// </summary>
        /// <param name="parameters">Query parameters</param>
        /// <param name="postProcessPaymentRequest">Payment info required for an order processing</param>
        private void AddItemsParameters(IDictionary<string, string> parameters, PostProcessPaymentRequest postProcessPaymentRequest)
        {
            //upload order items
            parameters.Add("cmd", "_cart");
            parameters.Add("upload", "1");

            var cartTotal = decimal.Zero;
            var roundedCartTotal = decimal.Zero;
            var itemCount = 1;

            //add shopping cart items
            foreach (var item in postProcessPaymentRequest.Order.OrderItems)
            {
                var roundedItemPrice = Math.Round(item.UnitPriceExclTax, 2);

                //add query parameters
                parameters.Add($"item_name_{itemCount}", item.Product.Name);
                parameters.Add($"amount_{itemCount}", roundedItemPrice.ToString("0.00", CultureInfo.InvariantCulture));
                parameters.Add($"quantity_{itemCount}", item.Quantity.ToString());

                cartTotal += item.PriceExclTax;
                roundedCartTotal += roundedItemPrice * item.Quantity;
                itemCount++;
            }

            //add checkout attributes as order items
            var checkoutAttributeValues = _checkoutAttributeParser.ParseCheckoutAttributeValues(postProcessPaymentRequest.Order.CheckoutAttributesXml);
            foreach (var attributeValue in checkoutAttributeValues)
            {
                var attributePrice = _taxService.GetCheckoutAttributePrice(attributeValue, false, postProcessPaymentRequest.Order.Customer);
                var roundedAttributePrice = Math.Round(attributePrice, 2);

                //add query parameters
                if (attributeValue.CheckoutAttribute == null) 
                    continue;

                parameters.Add($"item_name_{itemCount}", attributeValue.CheckoutAttribute.Name);
                parameters.Add($"amount_{itemCount}", roundedAttributePrice.ToString("0.00", CultureInfo.InvariantCulture));
                parameters.Add($"quantity_{itemCount}", "1");

                cartTotal += attributePrice;
                roundedCartTotal += roundedAttributePrice;
                itemCount++;
            }

            //add shipping fee as a separate order item, if it has price
            var roundedShippingPrice = Math.Round(postProcessPaymentRequest.Order.OrderShippingExclTax, 2);
            if (roundedShippingPrice > decimal.Zero)
            {
                parameters.Add($"item_name_{itemCount}", "Shipping fee");
                parameters.Add($"amount_{itemCount}", roundedShippingPrice.ToString("0.00", CultureInfo.InvariantCulture));
                parameters.Add($"quantity_{itemCount}", "1");

                cartTotal += postProcessPaymentRequest.Order.OrderShippingExclTax;
                roundedCartTotal += roundedShippingPrice;
                itemCount++;
            }

            //add payment method additional fee as a separate order item, if it has price
            var roundedPaymentMethodPrice = Math.Round(postProcessPaymentRequest.Order.PaymentMethodAdditionalFeeExclTax, 2);
            if (roundedPaymentMethodPrice > decimal.Zero)
            {
                parameters.Add($"item_name_{itemCount}", "Payment method fee");
                parameters.Add($"amount_{itemCount}", roundedPaymentMethodPrice.ToString("0.00", CultureInfo.InvariantCulture));
                parameters.Add($"quantity_{itemCount}", "1");

                cartTotal += postProcessPaymentRequest.Order.PaymentMethodAdditionalFeeExclTax;
                roundedCartTotal += roundedPaymentMethodPrice;
                itemCount++;
            }

            //add tax as a separate order item, if it has positive amount
            var roundedTaxAmount = Math.Round(postProcessPaymentRequest.Order.OrderTax, 2);
            if (roundedTaxAmount > decimal.Zero)
            {
                parameters.Add($"item_name_{itemCount}", "Tax amount");
                parameters.Add($"amount_{itemCount}", roundedTaxAmount.ToString("0.00", CultureInfo.InvariantCulture));
                parameters.Add($"quantity_{itemCount}", "1");

                cartTotal += postProcessPaymentRequest.Order.OrderTax;
                roundedCartTotal += roundedTaxAmount;
            }

            if (cartTotal > postProcessPaymentRequest.Order.OrderTotal)
            {
                //get the difference between what the order total is and what it should be and use that as the "discount"
                var discountTotal = Math.Round(cartTotal - postProcessPaymentRequest.Order.OrderTotal, 2);
                roundedCartTotal -= discountTotal;

                //gift card or rewarded point amount applied to cart in nopCommerce - shows in Worldline as "discount"
                parameters.Add("discount_amount_cart", discountTotal.ToString("0.00", CultureInfo.InvariantCulture));
            }

            //save order total that actually sent to Worldline (used for PDT order total validation)
            _genericAttributeService.SaveAttribute(postProcessPaymentRequest.Order, WorldlineHelper.OrderTotalSentToWorldline, roundedCartTotal);
        }

        /// <summary>
        /// Add order total to the request query parameters
        /// </summary>
        /// <param name="parameters">Query parameters</param>
        /// <param name="postProcessPaymentRequest">Payment info required for an order processing</param>
        private void AddOrderTotalParameters(IDictionary<string, string> parameters, PostProcessPaymentRequest postProcessPaymentRequest)
        {
            //round order total
            var roundedOrderTotal = Math.Round(postProcessPaymentRequest.Order.OrderTotal, 2);

            parameters.Add("cmd", "_xclick");
            parameters.Add("item_name", $"Order Number {postProcessPaymentRequest.Order.CustomOrderNumber}");
            parameters.Add("amount", roundedOrderTotal.ToString("0.00", CultureInfo.InvariantCulture));

            //save order total that actually sent to Worldline (used for PDT order total validation)
            _genericAttributeService.SaveAttribute(postProcessPaymentRequest.Order, WorldlineHelper.OrderTotalSentToWorldline, roundedOrderTotal);
        }

        #endregion

        #region Methods

        /// <summary>
        /// Process a payment
        /// </summary>
        /// <param name="processPaymentRequest">Payment info required for an order processing</param>
        /// <returns>Process payment result</returns>
        public ProcessPaymentResult ProcessPayment(ProcessPaymentRequest processPaymentRequest)
        {
            return new ProcessPaymentResult();
        }

        /// <summary>
        /// Post process payment (used by payment gateways that require redirecting to a third-party URL)
        /// </summary>
        /// <param name="postProcessPaymentRequest">Payment info required for an order processing</param>
        public void PostProcessPayment(PostProcessPaymentRequest postProcessPaymentRequest)
        {
            var baseUrl = _WorldlinePaymentSettings.UseSandbox ?
                "https://www.sandbox.Worldline.com/us/cgi-bin/webscr" :
                "https://www.Worldline.com/us/cgi-bin/webscr";

            //create common query parameters for the request
            var queryParameters = CreateQueryParameters(postProcessPaymentRequest);

            //whether to include order items in a transaction
            if (_WorldlinePaymentSettings.PassProductNamesAndTotals)
            {
                //add order items query parameters to the request
                var parameters = new Dictionary<string, string>(queryParameters);
                AddItemsParameters(parameters, postProcessPaymentRequest);

                //remove null values from parameters
                parameters = parameters.Where(parameter => !string.IsNullOrEmpty(parameter.Value))
                    .ToDictionary(parameter => parameter.Key, parameter => parameter.Value);

                //ensure redirect URL doesn't exceed 2K chars to avoid "too long URL" exception
                var redirectUrl = QueryHelpers.AddQueryString(baseUrl, parameters);
                if (redirectUrl.Length <= 2048)
                {
                    _httpContextAccessor.HttpContext.Response.Redirect(redirectUrl);
                    return;
                }
            }

            //or add only an order total query parameters to the request
            AddOrderTotalParameters(queryParameters, postProcessPaymentRequest);

            //remove null values from parameters
            queryParameters = queryParameters.Where(parameter => !string.IsNullOrEmpty(parameter.Value))
                .ToDictionary(parameter => parameter.Key, parameter => parameter.Value);

            var url = QueryHelpers.AddQueryString(baseUrl, queryParameters);
            _httpContextAccessor.HttpContext.Response.Redirect(url);
        }

        /// <summary>
        /// Returns a value indicating whether payment method should be hidden during checkout
        /// </summary>
        /// <param name="cart">Shopping cart</param>
        /// <returns>true - hide; false - display.</returns>
        public bool HidePaymentMethod(IList<ShoppingCartItem> cart)
        {
            //you can put any logic here
            //for example, hide this payment method if all products in the cart are downloadable
            //or hide this payment method if current customer is from certain country
            return false;
        }

        /// <summary>
        /// Gets additional handling fee
        /// </summary>
        /// <param name="cart">Shopping cart</param>
        /// <returns>Additional handling fee</returns>
        public decimal GetAdditionalHandlingFee(IList<ShoppingCartItem> cart)
        {
            return _paymentService.CalculateAdditionalFee(cart,
                _WorldlinePaymentSettings.AdditionalFee, _WorldlinePaymentSettings.AdditionalFeePercentage);
        }

        /// <summary>
        /// Captures payment
        /// </summary>
        /// <param name="capturePaymentRequest">Capture payment request</param>
        /// <returns>Capture payment result</returns>
        public CapturePaymentResult Capture(CapturePaymentRequest capturePaymentRequest)
        {
            return new CapturePaymentResult { Errors = new[] { "Capture method not supported" } };
        }

        /// <summary>
        /// Refunds a payment
        /// </summary>
        /// <param name="refundPaymentRequest">Request</param>
        /// <returns>Result</returns>
        public RefundPaymentResult Refund(RefundPaymentRequest refundPaymentRequest)
        {
            return new RefundPaymentResult { Errors = new[] { "Refund method not supported" } };
        }

        /// <summary>
        /// Voids a payment
        /// </summary>
        /// <param name="voidPaymentRequest">Request</param>
        /// <returns>Result</returns>
        public VoidPaymentResult Void(VoidPaymentRequest voidPaymentRequest)
        {
            return new VoidPaymentResult { Errors = new[] { "Void method not supported" } };
        }

        /// <summary>
        /// Process recurring payment
        /// </summary>
        /// <param name="processPaymentRequest">Payment info required for an order processing</param>
        /// <returns>Process payment result</returns>
        public ProcessPaymentResult ProcessRecurringPayment(ProcessPaymentRequest processPaymentRequest)
        {
            return new ProcessPaymentResult { Errors = new[] { "Recurring payment not supported" } };
        }

        /// <summary>
        /// Cancels a recurring payment
        /// </summary>
        /// <param name="cancelPaymentRequest">Request</param>
        /// <returns>Result</returns>
        public CancelRecurringPaymentResult CancelRecurringPayment(CancelRecurringPaymentRequest cancelPaymentRequest)
        {
            return new CancelRecurringPaymentResult { Errors = new[] { "Recurring payment not supported" } };
        }

        /// <summary>
        /// Gets a value indicating whether customers can complete a payment after order is placed but not completed (for redirection payment methods)
        /// </summary>
        /// <param name="order">Order</param>
        /// <returns>Result</returns>
        public bool CanRePostProcessPayment(Order order)
        {
            if (order == null)
                throw new ArgumentNullException(nameof(order));

            //let's ensure that at least 5 seconds passed after order is placed
            //P.S. there's no any particular reason for that. we just do it
            if ((DateTime.UtcNow - order.CreatedOnUtc).TotalSeconds < 5)
                return false;

            return true;
        }

        /// <summary>
        /// Validate payment form
        /// </summary>
        /// <param name="form">The parsed form values</param>
        /// <returns>List of validating errors</returns>
        public IList<string> ValidatePaymentForm(IFormCollection formCollection)
        {
            //try
            //{
            //    foreach (var key in formCollection.Keys)
            //    {
            //        var value = formCollection[key];
            //    }

            //    string path = _env.WebRootPath;

            //    string json = "";


            //    using (StreamReader r = new StreamReader(path + "\\output.json"))
            //    {
            //        json = r.ReadToEnd();

            //        r.Close();

            //    }
            //    JObject config_data = JObject.Parse(json);
            //    var data = formCollection["msg"].ToString().Split('|');
            //    if (data == null)
            //    {//|| data[1].ToString()== "User Aborted"
            //        ViewBag.abrt = true;
            //        //return Redirect(ControllerContext.HttpContext.Request.UrlReferrer.ToString());
            //        //string referer = Request.Headers["Referer"].ToString();
            //        //RequestHeaders header = Request.GetTypedHeaders();
            //        //Uri uriReferer = header.Referer;
            //        string referer = Request.Headers["Referer"].ToString();
            //        return Redirect(referer);
            //    }
            //    ViewBag.online_transaction_msg = data;
            //    if (data[0] == "0300")
            //    {
            //        //var request_str = "{ :merchant => { :identifier => " + config_data["merchantCode"].ToString() + "} ," +
            //        //                " :transaction => { :deviceIdentifier => 'S', " +
            //        //                     ":currency => " + config_data["currency"] + "," +
            //        //                     ":dateTime => " + string.Format("{0:d/M/yyyy", data[8]) + "," +
            //        //                     ":token => " + data[5].ToString() + "," +
            //        //                     ":requestType => 'S'" +
            //        //                     "}" +
            //        //                    "}";
            //        ViewBag.abrt = false;

            //        var strJ = new
            //        {
            //            merchant = new
            //            {
            //                identifier = config_data["merchantCode"].ToString()
            //            },
            //            transaction = new
            //            {
            //                deviceIdentifier = "S",
            //                currency = config_data["currency"],
            //                dateTime = string.Format("{0:d/M/yyyy}", data[8].ToString()),
            //                token = data[5].ToString(),
            //                requestType = "S"
            //            }
            //        };
            //        //     var request_str = Newtonsoft.Json.JsonConvert.SerializeObject(strJ);

            //        //  JObject request_data = JObject.Parse(request_str);
            //        //using (var client = new HttpClient())
            //        //{
            //        //    client.BaseAddress = new Uri("https://www.paynimo.com/api/paynimoV2.req");

            //        //    //HTTP POST
            //        //    var postTask = client.PostAsync("https://www.paynimo.com/api/paynimoV2.req", request_data);
            //        //    postTask.Wait();

            //        //    var result = postTask.Result;
            //        //    if (result.IsSuccessStatusCode)
            //        //    {
            //        //        return RedirectToAction("Index");
            //        //    }
            //        //}


            //        HttpClient client = new HttpClient();
            //        client.BaseAddress = new Uri("https://www.paynimo.com/api/paynimoV2.req");
            //        client.DefaultRequestHeaders.Accept.Clear();
            //        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            //        HttpResponseMessage response = client.PostAsJsonAsync("https://www.paynimo.com/api/paynimoV2.req", strJ).Result;
            //        var a = response.Content.ReadAsStringAsync();

            //        JObject dual_verification_result = JObject.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(a));
            //        var jsonData = JObject.Parse(dual_verification_result["Result"].ToString()).Children();

            //        List<JToken> tokens = jsonData.Children().ToList();

            //        var jsonData1 = JObject.Parse(tokens[6].ToString()).Children();
            //        List<JToken> tokens1 = jsonData.Children().ToList();
            //        //ViewBag.dual_verification_result = dual_verification_result;
            //        //ViewBag.a = a;
            //        //ViewBag.jsonData = jsonData;
            //        //ViewBag.tokens = tokens;
            //        //ViewBag.paramsData = formCollection["msg"];

            //        // return response;
            //    }

            //}
            //catch (Exception ex)
            //{

            //    //throw;
            //}
            return new List<string>();
        }

        /// <summary>
        /// Get payment information
        /// </summary>
        /// <param name="form">The parsed form values</param>
        /// <returns>Payment info holder</returns>
        public ProcessPaymentRequest GetPaymentInfo(IFormCollection form)
        {
            return new ProcessPaymentRequest();
        }

        /// <summary>
        /// Gets a configuration page URL
        /// </summary>
        public override string GetConfigurationPageUrl()
        {
            return $"{_webHelper.GetStoreLocation()}Admin/Worldline/Configure";
        }

        /// <summary>
        /// Gets a name of a view component for displaying plugin in public store ("payment info" checkout step)
        /// </summary>
        /// <returns>View component name</returns>
        public string GetPublicViewComponentName()
        {
            return "PaymentWorldline";
        }

        /// <summary>
        /// Install the plugin
        /// </summary>
        public override void Install()
        {
            //settings
            _settingService.SaveSetting(new WorldlinePaymentSettings
            {
                UseSandbox = true
            });


            _localizationService.AddOrUpdatePluginLocaleResource("Merchant Code", "Merchant Code");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Worldline.Fields.merchantSchemeCode", "Merchant Scheme Code");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Worldline.Fields.SALT", "SALT");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Worldline.Fields.currency", "Currency");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Worldline.Fields.typeOfPayment", "Type Of Payment");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Worldline.Fields.primaryColor", "Primary Color");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Worldline.Fields.secondaryColor", "Secondary Color");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Worldline.Fields.buttonColor1", "Button Color1");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Worldline.Fields.buttonColor2", "Button Color2");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Worldline.Fields.logoURL", "LogoURL");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Worldline.Fields.enableExpressPay", "Enable ExpressPay");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Worldline.Fields.separateCardMode", "Separate Card Mode");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Worldline.Fields.enableNewWindowFlow", "Enable New Window Flow");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Worldline.Fields.merchantMessage", "Merchant Message");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Worldline.Fields.disclaimerMessage", "Disclaimer Message");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Worldline.Fields.paymentMode", "Payment Mode");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Worldline.Fields.paymentModeOrder", "Payment Mode Order");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Worldline.Fields.enableInstrumentDeRegistration", "Enable Instrument DeRegistration");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Worldline.Fields.transactionType", "Transaction Type");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Worldline.Fields.hideSavedInstruments", "Hide Saved Instruments");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Worldline.Fields.saveInstrument", "Save Instrument");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Worldline.Fields.displayTransactionMessageOnPopup", "Display Transaction Message On Popup");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Worldline.Fields.embedPaymentGatewayOnPage", "Embed Payment Gateway On Page");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Worldline.Fields.enableSI", "Enable SI");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Worldline.Fields.hideSIDetails", "Hide SI Details");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Worldline.Fields.hideSIConfirmation", "Hide SI Confirmation");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Worldline.Fields.expandSIDetails", "Expand SI Details");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Worldline.Fields.enableDebitDay", "Enable Debit Day");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Worldline.Fields.showSIResponseMsg", "Show SI ResponseMsg");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Worldline.Fields.showSIConfirmation", "Show SI Confirmation");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Worldline.Fields.enableTxnForNonSICards", "Enable Txn For Non SI Cards");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Worldline.Fields.showAllModesWithSI", "Show All Modes With SI");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Worldline.Fields.siDetailsAtMerchantEnd", "si Details At MerchantEnd");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Worldline.Fields.amounttype", "Amount Type");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Worldline.Fields.showPGResponseMsg", "Show PG Response Msg");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Worldline.Fields.frequency", "Frequency");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.payments.Worldline.Fields.redirectiontip", "Cards / UPI / Netbanking / Wallets");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.payments.Worldline.Fields.paymentmethoddescription", "_");







            //locales
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Worldline.Fields.AdditionalFee", "Additional fee");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Worldline.Fields.AdditionalFee.Hint", "Enter additional fee to charge your customers.");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Worldline.Fields.AdditionalFeePercentage", "Additional fee. Use percentage");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Worldline.Fields.AdditionalFeePercentage.Hint", "Determines whether to apply a percentage additional fee to the order total. If not enabled, a fixed value is used.");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Worldline.Fields.BusinessEmail", "Business Email");
            //_localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Worldline.Fields.BusinessEmail.Hint", "Specify your Worldline business email.");
            //_localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Worldline.Fields.PassProductNamesAndTotals", "Pass product names and order totals to Worldline");
            //_localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Worldline.Fields.PassProductNamesAndTotals.Hint", "Check if product names and order totals should be passed to Worldline.");
            //_localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Worldline.Fields.PDTToken", "PDT Identity Token");
            //_localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Worldline.Fields.PDTToken.Hint", "Specify PDT identity token");
            //_localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Worldline.Fields.RedirectionTip", "You will be redirected to Worldline site to complete the order.");
            //_localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Worldline.Fields.UseSandbox", "Use Sandbox");
            //_localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Worldline.Fields.UseSandbox.Hint", "Check to enable Sandbox (testing environment).");
            //_localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Worldline.Instructions", @"
            //<p>
            // <b>If you're using this gateway ensure that your primary store currency is supported by Worldline.</b>
            // <br />
            // <br />To use PDT, you must activate PDT and Auto Return in your Worldline account profile. You must also acquire a PDT identity token, which is used in all PDT communication you send to Worldline. Follow these steps to configure your account for PDT:<br />
            // <br />1. Log in to your Worldline account (click <a href=""https://www.Worldline.com/us/webapps/mpp/referral/Worldline-business-account2?partner_id=9JJPJNNPQ7PZ8"" target=""_blank"">here</a> to create your account).
            // <br />2. Click the Profile button.
            // <br />3. Click the Profile and Settings button.
            // <br />4. Select the My selling tools item on left panel.
            // <br />5. Click Website Preferences Update in the Selling online section.
            // <br />6. Under Auto Return for Website Payments, click the On radio button.
            // <br />7. For the Return URL, enter the URL on your site that will receive the transaction ID posted by Worldline after a customer payment ({0}).
            //    <br />8. Under Payment Data Transfer, click the On radio button and get your PDT identity token.
            // <br />9. Click Save.
            // <br />
            //</p>");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Worldline.PaymentMethodDescription", "_");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.Worldline.RoundingWarning", "It looks like you have \"ShoppingCartSettings.RoundPricesDuringCalculation\" setting disabled. Keep in mind that this can lead to a discrepancy of the order total amount, as Worldline only rounds to two decimals.");

            base.Install();
        }

        /// <summary>
        /// Uninstall the plugin
        /// </summary>
        public override void Uninstall()
        {
            //settings
            _settingService.DeleteSetting<WorldlinePaymentSettings>();

            //locales
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Worldline.Fields.AdditionalFee");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Worldline.Fields.AdditionalFee.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Worldline.Fields.AdditionalFeePercentage");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Worldline.Fields.AdditionalFeePercentage.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Worldline.Fields.BusinessEmail");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Worldline.Fields.BusinessEmail.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Worldline.Fields.PassProductNamesAndTotals");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Worldline.Fields.PassProductNamesAndTotals.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Worldline.Fields.PDTToken");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Worldline.Fields.PDTToken.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Worldline.Fields.RedirectionTip");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Worldline.Fields.UseSandbox");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Worldline.Fields.UseSandbox.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Worldline.Instructions");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Worldline.PaymentMethodDescription");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Worldline.RoundingWarning");


            _localizationService.DeletePluginLocaleResource("Merchant Code");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Worldline.Fields.merchantSchemeCode");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Worldline.Fields.SALT");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Worldline.Fields.currency");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Worldline.Fields.typeOfPayment");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Worldline.Fields.primaryColor");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Worldline.Fields.secondaryColor");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Worldline.Fields.buttonColor1");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Worldline.Fields.buttonColor2");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Worldline.Fields.logoURL");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Worldline.Fields.enableExpressPay");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Worldline.Fields.separateCardMode");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Worldline.Fields.enableNewWindowFlow");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Worldline.Fields.merchantMessage");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Worldline.Fields.disclaimerMessage");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Worldline.Fields.paymentMode");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Worldline.Fields.paymentModeOrder");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Worldline.Fields.enableInstrumentDeRegistration");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Worldline.Fields.transactionType");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Worldline.Fields.hideSavedInstruments");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Worldline.Fields.saveInstrument");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Worldline.Fields.displayTransactionMessageOnPopup");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Worldline.Fields.embedPaymentGatewayOnPage");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Worldline.Fields.enableSI");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Worldline.Fields.hideSIDetails");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Worldline.Fields.hideSIConfirmation");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Worldline.Fields.expandSIDetails");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Worldline.Fields.enableDebitDay");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Worldline.Fields.showSIResponseMsg");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Worldline.Fields.showSIConfirmation");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Worldline.Fields.enableTxnForNonSICards");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Worldline.Fields.showAllModesWithSI");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Worldline.Fields.siDetailsAtMerchantEnd");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Worldline.Fields.amounttype");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Worldline.Fields.frequency");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.Worldline.Fields.showPGResponseMsg");
            _localizationService.DeletePluginLocaleResource("Plugins.payments.Worldline.Fields.redirectiontip");
            _localizationService.DeletePluginLocaleResource("Plugins.payments.Worldline.Fields.paymentmethoddescription");

            base.Uninstall();
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets a value indicating whether capture is supported
        /// </summary>
        public bool SupportCapture => true;

        /// <summary>
        /// Gets a value indicating whether partial refund is supported
        /// </summary>
        public bool SupportPartiallyRefund => true;

        /// <summary>
        /// Gets a value indicating whether refund is supported
        /// </summary>
        public bool SupportRefund => true;

        /// <summary>
        /// Gets a value indicating whether void is supported
        /// </summary>
        public bool SupportVoid => false;

        /// <summary>
        /// Gets a recurring payment type of payment method
        /// </summary>
        public RecurringPaymentType RecurringPaymentType => RecurringPaymentType.NotSupported;

        /// <summary>
        /// Gets a payment method type
        /// </summary>
        public PaymentMethodType PaymentMethodType => PaymentMethodType.Redirection;

        /// <summary>
        /// Gets a value indicating whether we should display a payment information page for this plugin
        /// </summary>
        public bool SkipPaymentInfo => false;

        /// <summary>
        /// Gets a payment method description that will be displayed on checkout pages in the public store
        /// </summary>
        public string PaymentMethodDescription => _localizationService.GetResource("Plugins.Payments.Worldline.PaymentMethodDescription");

        #endregion
    }
}