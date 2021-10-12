using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Plugin.Payments.Worldline.Models;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Messages;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Security;
using Nop.Web.Factories;
using Nop.Web.Models.ShoppingCart;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;
using Nop.Core.Data;
using Nop.Services.Catalog;
using Microsoft.AspNetCore.Mvc.Rendering;
using Newtonsoft.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Routing;
using Nop.Web.Framework.Menu;
using Nop.Web.Areas.Admin.Models.Orders;

namespace Nop.Plugin.Payments.Worldline.Controllers
{
    public class WorldlineController : BasePaymentController
    {
        #region Fields

        private readonly IGenericAttributeService _genericAttributeService;
        private readonly IOrderProcessingService _orderProcessingService;
        private readonly IOrderService _orderService;
        private readonly IPaymentPluginManager _paymentPluginManager;
        private readonly IPermissionService _permissionService;
        private readonly ILocalizationService _localizationService;
        private readonly ILogger _logger;
        private readonly INotificationService _notificationService;
        private readonly ISettingService _settingService;
        private readonly IStoreContext _storeContext;
        private readonly IWebHelper _webHelper;
        private readonly IWorkContext _workContext;
        private readonly ShoppingCartSettings _shoppingCartSettings;
        private readonly IHostingEnvironment _env;
        private readonly IShoppingCartService _shoppingCartService;
        private readonly IShoppingCartModelFactory _shoppingCartModelFactory;
        private readonly IRepository<Order> _orderRepository;
        private readonly IProductService _productService;
        private readonly ICustomerActivityService _customerActivityService;
        #endregion

        #region Ctor

        public WorldlineController(IGenericAttributeService genericAttributeService, IShoppingCartModelFactory shoppingCartModelFactory,
            IOrderProcessingService orderProcessingService,
            IOrderService orderService,
            IPaymentPluginManager paymentPluginManager,
            IPermissionService permissionService,
            ILocalizationService localizationService,
            ILogger logger,
            INotificationService notificationService,
            ISettingService settingService,
            IStoreContext storeContext,
            IWebHelper webHelper,
            IWorkContext workContext,
            IRepository<Order> orderRepository,
            IProductService productService,
            IHostingEnvironment env, IShoppingCartService shoppingCartService,
            ICustomerActivityService customerActivityService,
            ShoppingCartSettings shoppingCartSettings)
        {
            _genericAttributeService = genericAttributeService;
            _orderProcessingService = orderProcessingService;
            _orderService = orderService;
            _paymentPluginManager = paymentPluginManager;
            _permissionService = permissionService;
            _localizationService = localizationService;
            _logger = logger;
            _notificationService = notificationService;
            _settingService = settingService;
            _storeContext = storeContext;
            _webHelper = webHelper;
            _workContext = workContext;
            _shoppingCartSettings = shoppingCartSettings;
            _shoppingCartService = shoppingCartService;
            _shoppingCartModelFactory = shoppingCartModelFactory;
            _env = env;
            _orderRepository = orderRepository;
            _productService = productService;
            _customerActivityService = customerActivityService;
        }

        #endregion

        #region Utilities

        protected virtual void ProcessRecurringPayment(string invoiceId, PaymentStatus newPaymentStatus, string transactionId, string ipnInfo)
        {
            Guid orderNumberGuid;

            try
            {
                orderNumberGuid = new Guid(invoiceId);
            }
            catch
            {
                orderNumberGuid = Guid.Empty;
            }

            var order = _orderService.GetOrderByGuid(orderNumberGuid);
            if (order == null)
            {
                _logger.Error("PayPal IPN. Order is not found", new NopException(ipnInfo));
                return;
            }

            var recurringPayments = _orderService.SearchRecurringPayments(initialOrderId: order.Id);

            foreach (var rp in recurringPayments)
            {
                switch (newPaymentStatus)
                {
                    case PaymentStatus.Authorized:
                    case PaymentStatus.Paid:
                        {
                            var recurringPaymentHistory = rp.RecurringPaymentHistory;
                            if (!recurringPaymentHistory.Any())
                            {
                                //first payment
                                var rph = new RecurringPaymentHistory
                                {
                                    RecurringPaymentId = rp.Id,
                                    OrderId = order.Id,
                                    CreatedOnUtc = DateTime.UtcNow
                                };
                                rp.RecurringPaymentHistory.Add(rph);
                                _orderService.UpdateRecurringPayment(rp);
                            }
                            else
                            {
                                //next payments
                                var processPaymentResult = new ProcessPaymentResult
                                {
                                    NewPaymentStatus = newPaymentStatus
                                };
                                if (newPaymentStatus == PaymentStatus.Authorized)
                                    processPaymentResult.AuthorizationTransactionId = transactionId;
                                else
                                    processPaymentResult.CaptureTransactionId = transactionId;

                                _orderProcessingService.ProcessNextRecurringPayment(rp,
                                    processPaymentResult);
                            }
                        }

                        break;
                    case PaymentStatus.Voided:
                        //failed payment
                        var failedPaymentResult = new ProcessPaymentResult
                        {
                            Errors = new[] { $"PayPal IPN. Recurring payment is {nameof(PaymentStatus.Voided).ToLower()} ." },
                            RecurringPaymentFailed = true
                        };
                        _orderProcessingService.ProcessNextRecurringPayment(rp, failedPaymentResult);
                        break;
                }
            }

            //OrderService.InsertOrderNote(newOrder.OrderId, sb.ToString(), DateTime.UtcNow);
            _logger.Information("PayPal IPN. Recurring info", new NopException(ipnInfo));
        }

        protected virtual void ProcessPayment(string orderNumber, string ipnInfo, PaymentStatus newPaymentStatus, decimal mcGross, string transactionId)
        {
            Guid orderNumberGuid;

            try
            {
                orderNumberGuid = new Guid(orderNumber);
            }
            catch
            {
                orderNumberGuid = Guid.Empty;
            }

            var order = _orderService.GetOrderByGuid(orderNumberGuid);

            if (order == null)
            {
                _logger.Error("PayPal IPN. Order is not found", new NopException(ipnInfo));
                return;
            }

            //order note
            order.OrderNotes.Add(new OrderNote
            {
                Note = ipnInfo,
                DisplayToCustomer = false,
                CreatedOnUtc = DateTime.UtcNow
            });

            _orderService.UpdateOrder(order);

            //validate order total
            if ((newPaymentStatus == PaymentStatus.Authorized || newPaymentStatus == PaymentStatus.Paid) && !Math.Round(mcGross, 2).Equals(Math.Round(order.OrderTotal, 2)))
            {
                var errorStr = $"PayPal IPN. Returned order total {mcGross} doesn't equal order total {order.OrderTotal}. Order# {order.Id}.";
                //log
                _logger.Error(errorStr);
                //order note
                order.OrderNotes.Add(new OrderNote
                {
                    Note = errorStr,
                    DisplayToCustomer = false,
                    CreatedOnUtc = DateTime.UtcNow
                });
                _orderService.UpdateOrder(order);

                return;
            }

            switch (newPaymentStatus)
            {
                case PaymentStatus.Authorized:
                    if (_orderProcessingService.CanMarkOrderAsAuthorized(order))
                        _orderProcessingService.MarkAsAuthorized(order);
                    break;
                case PaymentStatus.Paid:
                    if (_orderProcessingService.CanMarkOrderAsPaid(order))
                    {
                        order.AuthorizationTransactionId = transactionId;
                        _orderService.UpdateOrder(order);

                        _orderProcessingService.MarkOrderAsPaid(order);
                    }

                    break;
                case PaymentStatus.Refunded:
                    var totalToRefund = Math.Abs(mcGross);
                    if (totalToRefund > 0 && Math.Round(totalToRefund, 2).Equals(Math.Round(order.OrderTotal, 2)))
                    {
                        //refund
                        if (_orderProcessingService.CanRefundOffline(order))
                            _orderProcessingService.RefundOffline(order);
                    }
                    else
                    {
                        //partial refund
                        if (_orderProcessingService.CanPartiallyRefundOffline(order, totalToRefund))
                            _orderProcessingService.PartiallyRefundOffline(order, totalToRefund);
                    }

                    break;
                case PaymentStatus.Voided:
                    if (_orderProcessingService.CanVoidOffline(order))
                        _orderProcessingService.VoidOffline(order);

                    break;
            }
        }

        #endregion

        #region Methods
       
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
                var merchantcode = _settingService.GetSetting("worldlinepaymentsettings.merchantcode");
                var currency = _settingService.GetSetting("worldlinepaymentsettings.currency");
             //   JObject config_data = JObject.Parse(json);
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
                            identifier =merchantcode.Value //config_data["merchantCode"].ToString()
                        },
                        transaction = new
                        {
                            deviceIdentifier = "S",
                            currency = currency.Value, //config_data["currency"],
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
                    var cart = _shoppingCartService.GetShoppingCart(_workContext.CurrentCustomer, ShoppingCartType.ShoppingCart, _storeContext.CurrentStore.Id);
                    var modelTtl = _shoppingCartModelFactory.PrepareOrderTotalsModel(cart, false);
                    var model = new ShoppingCartModel();
                    model = _shoppingCartModelFactory.PrepareShoppingCartModel(new ShoppingCartModel(), cart,
                        isEditable: false,
                      prepareAndDisplayOrderReviewData:true);
                    //  var model1 = _shoppingCartModelFactory.pre(cart, false);
                  
                      Order order = new Order();
                    order.CustomerId = _workContext.CurrentCustomer.Id;

                    order.AuthorizationTransactionId = data[5];
                    order.AuthorizationTransactionCode = data[3];
                    order.AuthorizationTransactionResult = tokens[6]["paymentTransaction"]["statusMessage"].ToString();
                    order.CaptureTransactionResult = formCollection["msg"].ToString();
                    if (tokens[6]["paymentTransaction"]["statusMessage"].ToString() == "SUCCESS")
                    {
                        order.PaymentStatusId = 30;

                    }
                    else
                    {
                        order.PaymentStatusId = 10;
                    }
                    order.StoreId = _storeContext.CurrentStore.Id;
                    order.BillingAddressId = (int)(_workContext.CurrentCustomer.BillingAddressId);
                    order.PickupInStore = model.OrderReviewData.SelectedPickupInStore;
                    order.OrderStatusId = 10;
                    order.ShippingStatusId = 20;
                    
                    order.PaymentMethodSystemName="Payments.Worldline";
                    order.CustomerCurrencyCode = "INR";
                    order.CurrencyRate = _workContext.WorkingCurrency.Rate;
                    order.CustomerTaxDisplayTypeId = 10;
                    order.OrderSubtotalInclTax = Convert.ToDecimal(modelTtl.SubTotal.Substring(1)) + Convert.ToDecimal(modelTtl.Tax.Substring(1));
                    order.OrderSubtotalExclTax = Convert.ToDecimal(modelTtl.SubTotal.Substring(1));
                    order.OrderSubTotalDiscountInclTax = Convert.ToDecimal(0.00);
                    order.OrderSubTotalDiscountExclTax = Convert.ToDecimal(0.00);
                    order.OrderShippingInclTax = Convert.ToDecimal(modelTtl.Shipping.Substring(1));
                    order.OrderShippingExclTax = Convert.ToDecimal(0.00);
                    order.PaymentMethodAdditionalFeeInclTax = Convert.ToDecimal(0.00);
                    order.PaymentMethodAdditionalFeeExclTax = Convert.ToDecimal(0.00);
                    order.TaxRates = modelTtl.TaxRates.ToString();
                    order.OrderTax= Convert.ToDecimal(0.00);
                    order.OrderDiscount = Convert.ToDecimal(0.00);
                    order.OrderTotal = Convert.ToDecimal(modelTtl.OrderTotal.Substring(1));
                        order.RefundedAmount = Convert.ToDecimal(0.00);
                    order.CustomerLanguageId = _storeContext.CurrentStore.DefaultLanguageId;
                        order.AffiliateId = _workContext.CurrentCustomer.AffiliateId;
                    order.AllowStoringCreditCardNumber = false;

                    order.Deleted = false;
                    order.ShippingAddressId = model.OrderReviewData.ShippingAddress.Id;
                    order.CreatedOnUtc = DateTime.UtcNow;
                    //order.CustomOrderNumber
                    order.OrderGuid = Guid.NewGuid();
                  
                        var last=_orderRepository.Table.OrderByDescending(p => p.Id).First();
                    int custOrdnum = last.Id + 1;
                    order.CustomOrderNumber = custOrdnum.ToString();
                  
                    foreach (var item in _workContext.CurrentCustomer.ShoppingCartItems)
                    {
                        //_shoppingCartService.DeleteShoppingCartItem(item.Id);
                        var product = _productService.GetProductById(item.ProductId);
                        var newOrderItem = new OrderItem
                        {
                            OrderItemGuid = Guid.NewGuid(),
                            Order = order,
                            ProductId = item.ProductId,
                            UnitPriceInclTax = product.Price,
                            UnitPriceExclTax = product.Price,
                            PriceInclTax = product.Price*item.Quantity,
                            PriceExclTax = product.Price * item.Quantity,
                            OriginalProductCost = product.ProductCost,
                            AttributeDescription = "",
                            AttributesXml = item.AttributesXml,
                            Quantity = item.Quantity,
                            DiscountAmountInclTax = Convert.ToDecimal(0.00),
                            DiscountAmountExclTax = Convert.ToDecimal(0.00),
                            DownloadCount = 0,
                            IsDownloadActivated = product.IsDownload,
                            LicenseDownloadId = product.DownloadId,
                            ItemWeight =product.Weight,
                            RentalStartDateUtc = item.RentalStartDateUtc,
                            RentalEndDateUtc = item.RentalEndDateUtc
                        };
                        order.OrderItems.Add(newOrderItem);
                    }
                    _orderService.InsertOrder(order);
                    List<int> ids = new List<int>();


                    foreach (var item in _workContext.CurrentCustomer.ShoppingCartItems)
                    {
                        ids.Add(item.Id);
                      //  _shoppingCartService.DeleteShoppingCartItem(item.Id);
                    }
                    //int itemCnt = _workContext.CurrentCustomer.ShoppingCartItems.Count;
                    //for (int i = 0; i < itemCnt; i++)
                    //{

                    //}
                    foreach (var item in ids)
                    {
                       _shoppingCartService.DeleteShoppingCartItem(item);

                    }
                        _notificationService.SuccessNotification("Order Placed successfully!");

                    //ViewBag.dual_verification_result = dual_verification_result;
                    //ViewBag.a = a;
                    //ViewBag.jsonData = jsonData;
                    //ViewBag.tokens = tokens;
                    //ViewBag.paramsData = formCollection["msg"];

                    // return response;
                }

            }
            catch (Exception ex)
            {

                //throw;
            }
            return RedirectToAction("Index","Home");
          //  return ViewComponent("ResponseHandler", new { formCollection = formCollection });
            //  return View("~/Plugins/Payments.Worldline/Views/PaymentInfo.cshtml");
            //   return Content("Success");
            //return View("~/Plugins/Payments.Worldline/Views/PaymentInfo.cshtml");
            //  return Redirect(_storeContext.CurrentStore.Url+ "checkout/OpcSavePaymentInfo");
        }
        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        public IActionResult Configure()
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            //load settings for a chosen store scope
            var storeScope = _storeContext.ActiveStoreScopeConfiguration;
            var worldlinePaymentSettings = _settingService.LoadSetting<WorldlinePaymentSettings>(storeScope);
            //using (StreamReader r = new StreamReader("~/Plugins/Payments.Worldline/output.json"))
            //{
            //    var b = r.ReadToEnd();
            //}

         
       


        var model = new ConfigurationModel
            {
                utf8 = worldlinePaymentSettings.utf8,
                authenticity_token = worldlinePaymentSettings.authenticity_token,
                merchantCode = worldlinePaymentSettings.merchantCode,
                merchantSchemeCode = worldlinePaymentSettings.merchantSchemeCode,
                SALT = worldlinePaymentSettings.SALT,
                currency = worldlinePaymentSettings.currency,
                typeOfPayment = worldlinePaymentSettings.typeOfPayment,
                primaryColor = worldlinePaymentSettings.primaryColor,
                secondaryColor = worldlinePaymentSettings.secondaryColor,
                buttonColor1 = worldlinePaymentSettings.buttonColor1,
                buttonColor2 = worldlinePaymentSettings.buttonColor2,
                logoURL = worldlinePaymentSettings.logoURL,
                enableExpressPay = worldlinePaymentSettings.enableExpressPay,
                separateCardMode = worldlinePaymentSettings.separateCardMode,
                enableNewWindowFlow = worldlinePaymentSettings.enableNewWindowFlow,
                merchantMessage = worldlinePaymentSettings.merchantMessage,
                disclaimerMessage = worldlinePaymentSettings.disclaimerMessage,
                paymentMode = worldlinePaymentSettings.paymentMode,
                paymentModeOrder = worldlinePaymentSettings.paymentModeOrder,
                enableInstrumentDeRegistration = worldlinePaymentSettings.enableInstrumentDeRegistration,
                transactionType = worldlinePaymentSettings.transactionType,
                hideSavedInstruments = worldlinePaymentSettings.hideSavedInstruments,
                saveInstrument = worldlinePaymentSettings.saveInstrument,
                displayTransactionMessageOnPopup = worldlinePaymentSettings.displayTransactionMessageOnPopup,
                embedPaymentGatewayOnPage = worldlinePaymentSettings.embedPaymentGatewayOnPage,
                enableSI = worldlinePaymentSettings.enableSI,
                hideSIDetails = worldlinePaymentSettings.hideSIDetails,
                hideSIConfirmation = worldlinePaymentSettings.hideSIConfirmation,
                expandSIDetails = worldlinePaymentSettings.expandSIDetails,
                enableDebitDay = worldlinePaymentSettings.enableDebitDay,
                showSIResponseMsg = worldlinePaymentSettings.showSIResponseMsg,
                showSIConfirmation = worldlinePaymentSettings.showSIConfirmation,
                enableTxnForNonSICards = worldlinePaymentSettings.enableTxnForNonSICards,
                showAllModesWithSI = worldlinePaymentSettings.showAllModesWithSI,
                siDetailsAtMerchantEnd = worldlinePaymentSettings.siDetailsAtMerchantEnd,
                amounttype = worldlinePaymentSettings.amounttype,
                frequency = worldlinePaymentSettings.frequency,
            //merchantLogoUrl = worldlinePaymentSettings.merchantLogoUrl,
            //merchantMsg = worldlinePaymentSettings.merchantMsg,
            //disclaimerMsg = worldlinePaymentSettings.disclaimerMsg,
            showPGResponseMsg = worldlinePaymentSettings.showPGResponseMsg,
            enableAbortResponse = worldlinePaymentSettings.enableAbortResponse,
           



        

        //UseSandbox = worldlinePaymentSettings.UseSandbox,
        //BusinessEmail = worldlinePaymentSettings.BusinessEmail,
        //PdtToken = worldlinePaymentSettings.PdtToken,
        //PassProductNamesAndTotals = worldlinePaymentSettings.PassProductNamesAndTotals,
        //AdditionalFee = worldlinePaymentSettings.AdditionalFee,
        //AdditionalFeePercentage = worldlinePaymentSettings.AdditionalFeePercentage,
        ActiveStoreScopeConfiguration = storeScope
            };

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
            List<SelectListItem> transactionTypes = new List<SelectListItem>()
            {
        new SelectListItem() { Text="SALE", Value="SALE"}
      
            };

            ViewBag.enbDisb = enbDisb;
            ViewBag.currencyCodes = currencyCodes;
            ViewBag.paymentModes = paymentMode;
            ViewBag.typeOfPayment = typeOfPayment;
            ViewBag.amounttype = amounttype;
            ViewBag.frequency = frequency;
            ViewBag.transactionTypes = transactionTypes;



            if (storeScope <= 0)

                return View("~/Plugins/Payments.Worldline/Views/Configure.cshtml", model);

            model.utf8_OverrideForStore = _settingService.SettingExists(worldlinePaymentSettings, x => x.utf8, storeScope);
            model.authenticity_token_OverrideForStore = _settingService.SettingExists(worldlinePaymentSettings, x => x.authenticity_token, storeScope);

            model.merchantCode_OverrideForStore = _settingService.SettingExists(worldlinePaymentSettings, x => x.merchantCode, storeScope);
            model.merchantSchemeCode_OverrideForStore = _settingService.SettingExists(worldlinePaymentSettings, x => x.merchantSchemeCode, storeScope);
            model.SALT_OverrideForStore = _settingService.SettingExists(worldlinePaymentSettings, x => x.SALT, storeScope);
            model.currency_OverrideForStore = _settingService.SettingExists(worldlinePaymentSettings, x => x.currency, storeScope);
            model.typeOfPayment_OverrideForStore = _settingService.SettingExists(worldlinePaymentSettings, x => x.typeOfPayment, storeScope);
            model.primaryColor_OverrideForStore = _settingService.SettingExists(worldlinePaymentSettings, x => x.primaryColor, storeScope);
            model.secondaryColor_OverrideForStore = _settingService.SettingExists(worldlinePaymentSettings, x => x.secondaryColor, storeScope);
            model.buttonColor1_OverrideForStore = _settingService.SettingExists(worldlinePaymentSettings, x => x.buttonColor1, storeScope);
            model.buttonColor2_OverrideForStore = _settingService.SettingExists(worldlinePaymentSettings, x => x.buttonColor2, storeScope);
            model.logoURL_OverrideForStore = _settingService.SettingExists(worldlinePaymentSettings, x => x.logoURL, storeScope);
            model.enableExpressPay_OverrideForStore = _settingService.SettingExists(worldlinePaymentSettings, x => x.enableExpressPay, storeScope);
            model.separateCardMode_OverrideForStore = _settingService.SettingExists(worldlinePaymentSettings, x => x.separateCardMode, storeScope);
            model.enableNewWindowFlow_OverrideForStore = _settingService.SettingExists(worldlinePaymentSettings, x => x.enableNewWindowFlow, storeScope);
            model.merchantMessage_OverrideForStore = _settingService.SettingExists(worldlinePaymentSettings, x => x.merchantMessage, storeScope);
            model.disclaimerMessage_OverrideForStore = _settingService.SettingExists(worldlinePaymentSettings, x => x.disclaimerMessage, storeScope);
            model.paymentMode_OverrideForStore = _settingService.SettingExists(worldlinePaymentSettings, x => x.paymentMode, storeScope);
            model.paymentModeOrder_OverrideForStore = _settingService.SettingExists(worldlinePaymentSettings, x => x.paymentModeOrder, storeScope);
            model.enableInstrumentDeRegistration_OverrideForStore = _settingService.SettingExists(worldlinePaymentSettings, x => x.enableInstrumentDeRegistration, storeScope);
            model.transactionType_OverrideForStore = _settingService.SettingExists(worldlinePaymentSettings, x => x.transactionType, storeScope);
            model.hideSavedInstruments_OverrideForStore = _settingService.SettingExists(worldlinePaymentSettings, x => x.hideSavedInstruments, storeScope);
            model.saveInstrument_OverrideForStore = _settingService.SettingExists(worldlinePaymentSettings, x => x.saveInstrument, storeScope);
            model.displayTransactionMessageOnPopup_OverrideForStore = _settingService.SettingExists(worldlinePaymentSettings, x => x.displayTransactionMessageOnPopup, storeScope);
            model.embedPaymentGatewayOnPage_OverrideForStore = _settingService.SettingExists(worldlinePaymentSettings, x => x.embedPaymentGatewayOnPage, storeScope);
            model.enableSI_OverrideForStore = _settingService.SettingExists(worldlinePaymentSettings, x => x.enableSI, storeScope);
            model.hideSIDetails_OverrideForStore = _settingService.SettingExists(worldlinePaymentSettings, x => x.hideSIDetails, storeScope);
            model.hideSIConfirmation_OverrideForStore = _settingService.SettingExists(worldlinePaymentSettings, x => x.hideSIConfirmation, storeScope);
            model.expandSIDetails_OverrideForStore = _settingService.SettingExists(worldlinePaymentSettings, x => x.expandSIDetails, storeScope);
            model.enableDebitDay_OverrideForStore = _settingService.SettingExists(worldlinePaymentSettings, x => x.enableDebitDay, storeScope);
            model.showSIResponseMsg_OverrideForStore = _settingService.SettingExists(worldlinePaymentSettings, x => x.showSIResponseMsg, storeScope);
            model.showSIConfirmation_OverrideForStore = _settingService.SettingExists(worldlinePaymentSettings, x => x.showSIConfirmation, storeScope);
            model.enableTxnForNonSICards_OverrideForStore = _settingService.SettingExists(worldlinePaymentSettings, x => x.enableTxnForNonSICards, storeScope);
            model.showAllModesWithSI_OverrideForStore = _settingService.SettingExists(worldlinePaymentSettings, x => x.showAllModesWithSI, storeScope);
            model.siDetailsAtMerchantEnd_OverrideForStore = _settingService.SettingExists(worldlinePaymentSettings, x => x.siDetailsAtMerchantEnd, storeScope);
            model.amounttype_OverrideForStore = _settingService.SettingExists(worldlinePaymentSettings, x => x.amounttype, storeScope);
            model.frequency_OverrideForStore = _settingService.SettingExists(worldlinePaymentSettings, x => x.frequency, storeScope);

          //  model.merchantLogoUrl_OverrideForStore = _settingService.SettingExists(worldlinePaymentSettings, x => x.merchantLogoUrl, storeScope);
           // model.merchantMsg_OverrideForStore = _settingService.SettingExists(worldlinePaymentSettings, x => x.merchantMsg, storeScope);
            //model.disclaimerMsg_OverrideForStore = _settingService.SettingExists(worldlinePaymentSettings, x => x.disclaimerMsg, storeScope);
            model.showPGResponseMsg_OverrideForStore = _settingService.SettingExists(worldlinePaymentSettings, x => x.showPGResponseMsg, storeScope);
            model.enableAbortResponse_OverrideForStore = _settingService.SettingExists(worldlinePaymentSettings, x => x.enableAbortResponse, storeScope);
          

            //model.UseSandbox_OverrideForStore = _settingService.SettingExists(worldlinePaymentSettings, x => x.UseSandbox, storeScope);
            //model.BusinessEmail_OverrideForStore = _settingService.SettingExists(worldlinePaymentSettings, x => x.BusinessEmail, storeScope);
            //model.PdtToken_OverrideForStore = _settingService.SettingExists(worldlinePaymentSettings, x => x.PdtToken, storeScope);
            //model.PassProductNamesAndTotals_OverrideForStore = _settingService.SettingExists(worldlinePaymentSettings, x => x.PassProductNamesAndTotals, storeScope);
            //model.AdditionalFee_OverrideForStore = _settingService.SettingExists(worldlinePaymentSettings, x => x.AdditionalFee, storeScope);
            //model.AdditionalFeePercentage_OverrideForStore = _settingService.SettingExists(worldlinePaymentSettings, x => x.AdditionalFeePercentage, storeScope);

            return View("~/Plugins/Payments.Worldline/Views/Configure.cshtml", model);
        }

        [HttpPost]
        [AuthorizeAdmin]
        [AdminAntiForgery]
        [Area(AreaNames.Admin)]
        public IActionResult Configure(ConfigurationModel model)
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            if (!ModelState.IsValid)
                return Configure();

            //load settings for a chosen store scope
            var storeScope = _storeContext.ActiveStoreScopeConfiguration;
            var worldlinePaymentSettings = _settingService.LoadSetting<WorldlinePaymentSettings>(storeScope);




            //save settings
            worldlinePaymentSettings.merchantCode = model.merchantCode;
            worldlinePaymentSettings.merchantSchemeCode = model.merchantSchemeCode;
            worldlinePaymentSettings.SALT = model.SALT;
            worldlinePaymentSettings.currency = model.currency;
            worldlinePaymentSettings.typeOfPayment = model.typeOfPayment;
            worldlinePaymentSettings.primaryColor = model.primaryColor;
            worldlinePaymentSettings.secondaryColor = model.secondaryColor;
            worldlinePaymentSettings.buttonColor1 = model.buttonColor1;
            worldlinePaymentSettings.buttonColor2 = model.buttonColor2;
            worldlinePaymentSettings.logoURL = model.logoURL;
            worldlinePaymentSettings.enableExpressPay = model.enableExpressPay;
            worldlinePaymentSettings.separateCardMode = model.separateCardMode;
            worldlinePaymentSettings.enableNewWindowFlow = model.enableNewWindowFlow;
            worldlinePaymentSettings.merchantMessage = model.merchantMessage;
            worldlinePaymentSettings.disclaimerMessage = model.disclaimerMessage;
            worldlinePaymentSettings.paymentMode = model.paymentMode;
            worldlinePaymentSettings.paymentModeOrder = model.paymentModeOrder;
            worldlinePaymentSettings.enableInstrumentDeRegistration = model.enableInstrumentDeRegistration;
            worldlinePaymentSettings.transactionType = model.transactionType;
            worldlinePaymentSettings.hideSavedInstruments = model.hideSavedInstruments;
            worldlinePaymentSettings.saveInstrument = model.saveInstrument;
            worldlinePaymentSettings.displayTransactionMessageOnPopup = model.displayTransactionMessageOnPopup;
            worldlinePaymentSettings.embedPaymentGatewayOnPage = model.embedPaymentGatewayOnPage;
            worldlinePaymentSettings.enableSI = model.enableSI;
            worldlinePaymentSettings.hideSIDetails = model.hideSIDetails;
            worldlinePaymentSettings.hideSIConfirmation = model.hideSIConfirmation;
            worldlinePaymentSettings.expandSIDetails = model.expandSIDetails;
            worldlinePaymentSettings.enableDebitDay = model.enableDebitDay;
            worldlinePaymentSettings.showSIResponseMsg = model.showSIResponseMsg;
            worldlinePaymentSettings.showSIConfirmation = model.showSIConfirmation;
            worldlinePaymentSettings.enableTxnForNonSICards = model.enableTxnForNonSICards;
            worldlinePaymentSettings.showAllModesWithSI = model.showAllModesWithSI;
            worldlinePaymentSettings.siDetailsAtMerchantEnd = model.siDetailsAtMerchantEnd;
            worldlinePaymentSettings.amounttype = model.amounttype;
            worldlinePaymentSettings.utf8 = model.utf8;
            worldlinePaymentSettings.authenticity_token = model.authenticity_token;
            worldlinePaymentSettings.frequency = model.frequency;


          //  worldlinePaymentSettings.merchantLogoUrl = model.merchantLogoUrl;
           //worldlinePaymentSettings.merchantMsg = model.merchantMsg;
           //   worldlinePaymentSettings.disclaimerMsg = model.disclaimerMsg;
              worldlinePaymentSettings.showPGResponseMsg = model.showPGResponseMsg;
             worldlinePaymentSettings.enableAbortResponse = model.enableAbortResponse;



            //worldlinePaymentSettings.UseSandbox = model.UseSandbox;
            //worldlinePaymentSettings.BusinessEmail = model.BusinessEmail;
            //worldlinePaymentSettings.PdtToken = model.PdtToken;
            //worldlinePaymentSettings.PassProductNamesAndTotals = model.PassProductNamesAndTotals;
            //worldlinePaymentSettings.AdditionalFee = model.AdditionalFee;
            //worldlinePaymentSettings.AdditionalFeePercentage = model.AdditionalFeePercentage;

            /* We do not clear cache after each setting update.
             * This behavior can increase performance because cached settings will not be cleared 
             * and loaded from database after each update */



            _settingService.SaveSettingOverridablePerStore(worldlinePaymentSettings, x => x.merchantCode, model.merchantCode_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(worldlinePaymentSettings, x => x.merchantSchemeCode, model.merchantSchemeCode_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(worldlinePaymentSettings, x => x.SALT, model.SALT_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(worldlinePaymentSettings, x => x.currency, model.currency_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(worldlinePaymentSettings, x => x.typeOfPayment, model.typeOfPayment_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(worldlinePaymentSettings, x => x.primaryColor, model.primaryColor_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(worldlinePaymentSettings, x => x.secondaryColor, model.secondaryColor_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(worldlinePaymentSettings, x => x.buttonColor1, model.buttonColor1_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(worldlinePaymentSettings, x => x.buttonColor2, model.buttonColor2_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(worldlinePaymentSettings, x => x.logoURL, model.logoURL_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(worldlinePaymentSettings, x => x.enableExpressPay, model.enableExpressPay_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(worldlinePaymentSettings, x => x.separateCardMode, model.separateCardMode_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(worldlinePaymentSettings, x => x.enableNewWindowFlow, model.enableNewWindowFlow_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(worldlinePaymentSettings, x => x.merchantMessage, model.merchantMessage_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(worldlinePaymentSettings, x => x.disclaimerMessage, model.disclaimerMessage_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(worldlinePaymentSettings, x => x.paymentMode, model.paymentMode_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(worldlinePaymentSettings, x => x.paymentModeOrder, model.paymentModeOrder_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(worldlinePaymentSettings, x => x.enableInstrumentDeRegistration, model.enableInstrumentDeRegistration_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(worldlinePaymentSettings, x => x.transactionType, model.transactionType_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(worldlinePaymentSettings, x => x.hideSavedInstruments, model.hideSavedInstruments_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(worldlinePaymentSettings, x => x.saveInstrument, model.saveInstrument_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(worldlinePaymentSettings, x => x.displayTransactionMessageOnPopup, model.displayTransactionMessageOnPopup_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(worldlinePaymentSettings, x => x.embedPaymentGatewayOnPage, model.embedPaymentGatewayOnPage_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(worldlinePaymentSettings, x => x.enableSI, model.enableSI_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(worldlinePaymentSettings, x => x.hideSIDetails, model.hideSIDetails_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(worldlinePaymentSettings, x => x.hideSIConfirmation, model.hideSIConfirmation_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(worldlinePaymentSettings, x => x.expandSIDetails, model.expandSIDetails_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(worldlinePaymentSettings, x => x.enableDebitDay, model.enableDebitDay_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(worldlinePaymentSettings, x => x.showSIResponseMsg, model.showSIResponseMsg_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(worldlinePaymentSettings, x => x.showSIConfirmation, model.showSIConfirmation_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(worldlinePaymentSettings, x => x.enableTxnForNonSICards, model.enableTxnForNonSICards_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(worldlinePaymentSettings, x => x.showAllModesWithSI, model.showAllModesWithSI_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(worldlinePaymentSettings, x => x.siDetailsAtMerchantEnd, model.siDetailsAtMerchantEnd_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(worldlinePaymentSettings, x => x.amounttype, model.amounttype_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(worldlinePaymentSettings, x => x.utf8, model.utf8_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(worldlinePaymentSettings, x => x.authenticity_token, model.authenticity_token_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(worldlinePaymentSettings, x => x.frequency, model.frequency_OverrideForStore, storeScope, false);

            //_settingService.SaveSettingOverridablePerStore(worldlinePaymentSettings, x => x.merchantLogoUrl, model.merchantLogoUrl_OverrideForStore, storeScope, false);
          //  _settingService.SaveSettingOverridablePerStore(worldlinePaymentSettings, x => x.merchantMsg, model.merchantMsg_OverrideForStore, storeScope, false);
            //_settingService.SaveSettingOverridablePerStore(worldlinePaymentSettings, x => x.disclaimerMsg, model.disclaimerMsg_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(worldlinePaymentSettings, x => x.showPGResponseMsg, model.showPGResponseMsg_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(worldlinePaymentSettings, x => x.enableAbortResponse, model.enableAbortResponse_OverrideForStore, storeScope, false);



         


            _settingService.SaveSettingOverridablePerStore(worldlinePaymentSettings, x => x.UseSandbox, model.UseSandbox_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(worldlinePaymentSettings, x => x.BusinessEmail, model.BusinessEmail_OverrideForStore, storeScope, false);
            //_settingService.SaveSettingOverridablePerStore(worldlinePaymentSettings, x => x.PdtToken, model.PdtToken_OverrideForStore, storeScope, false);
            //_settingService.SaveSettingOverridablePerStore(worldlinePaymentSettings, x => x.PassProductNamesAndTotals, model.PassProductNamesAndTotals_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(worldlinePaymentSettings, x => x.AdditionalFee, model.AdditionalFee_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(worldlinePaymentSettings, x => x.AdditionalFeePercentage, model.AdditionalFeePercentage_OverrideForStore, storeScope, false);

            //now clear settings cache
            _settingService.ClearCache();
            SiteMapNode rootNode = new SiteMapNode();
            var menuItem = new SiteMapNode
            {
                SystemName = "Home",
                Title = "Home",
                ControllerName = "Home",
                ActionName = "Overview",
                Visible = true,
                RouteValues = new RouteValueDictionary() { { "area", "admin" } }
            };

            //var menuItem = new SiteMapNode()
            //{
            //    SystemName = "YourCustomSystemName",
            //    Title = "Plugin Title",
            //    ControllerName = "ControllerName",
            //    ActionName = "List",
            //    Visible = true,
            //    RouteValues = new RouteValueDictionary() { { "area", null } },
            //};
            var pluginNode = menuItem.ChildNodes.FirstOrDefault(x => x.SystemName == "Low stock");
            if (pluginNode != null)
                pluginNode.ChildNodes.Add(menuItem);

            _notificationService.SuccessNotification(_localizationService.GetResource("Admin.Plugins.Saved"));

            return Configure();
        }
        [Area(AreaNames.Admin)]
        public JsonResult GenerateSHA512String(string inputString)
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

                //return Json(hash, JsonRequestBehavior.AllowGet);
                return Json(hash, new Newtonsoft.Json.JsonSerializerSettings());
            }
        }
        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        public ActionResult Refund()

        {
            try
            {
                string path = _env.WebRootPath;
                string tranId = GenerateRandomString(12);
                ViewBag.tranId = tranId;
                var merchantcode = _settingService.GetSetting("worldlinepaymentsettings.merchantcode");
                var currency = _settingService.GetSetting("worldlinepaymentsettings.merchantcode");
                ViewBag.merchantcode = merchantcode.Value.ToString();
                ViewBag.currency = currency.Value.ToString();
                
                //using (StreamReader r = new StreamReader(path + "\\output.json"))
                //{
                //    string json = r.ReadToEnd();


                //    ViewBag.config_data = json;
                //}
            }
            catch (Exception ex)
            {

                //       throw;
            }
            //"~/Plugins/Payments.Worldline/Views/Refund.cshtml"

            return View("~/Plugins/Payments.Worldline/Views/Refund.cshtml");
           // return View();
        }


        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        [HttpPost]
        public ActionResult Refund(IFormCollection fc)
        {
            try
            {
                string path = _env.WebRootPath;
                string json = "";
                string merchantcode = _settingService.GetSetting("worldlinepaymentsettings.merchantcode").Value.ToString();
                string currency = _settingService.GetSetting("worldlinepaymentsettings.currency").Value.ToString();
                ViewBag.merchantcode = merchantcode;
                ViewBag.currency = currency;
                //using (StreamReader r = new StreamReader(path + "\\output.json"))
                //{
                //    json = r.ReadToEnd();
                //    r.Close();
                //}
                //JObject config_data = JObject.Parse(json);
                DateTime start_date = DateTime.Parse(fc["inputDate"].ToString());
                var data = new
                {
                    merchant = new { identifier = merchantcode },
                    cart = new
                    {
                    },
                    transaction = new
                    {
                        deviceIdentifier = "S",
                        amount = fc["amount"].ToString(),
                        currency = currency,
                        dateTime = start_date.ToString("dd-MM-yyyy"),
                        token = fc["token"].ToString(),
                        //string.Format("{0:d/M/yyyy}", day.ToString()),
                        requestType = "R"
                    }
                };
                HttpClient client = new HttpClient();
                client.BaseAddress = new Uri("https://www.paynimo.com/api/paynimoV2.req");
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                HttpResponseMessage response = client.PostAsJsonAsync("https://www.paynimo.com/api/paynimoV2.req", data).Result;
                var respStr = response.Content.ReadAsStringAsync();
                //data = null;
                JObject dual_verification_result = JObject.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(respStr));
                var jsonData = JObject.Parse(dual_verification_result["Result"].ToString()).Children();
                List<JToken> tokens = jsonData.Children().ToList();
                string statusCode = tokens[6]["paymentTransaction"]["statusCode"].ToString();
                string amount = tokens[6]["paymentTransaction"]["amount"].ToString();
             
                if (statusCode == "0499") //Change the code as per requirement
                {
                    Order order = _orderRepository.Table.Where(a => a.AuthorizationTransactionId == fc["token"].ToString()).ToList().FirstOrDefault();
                    order.PaymentStatusId = 40;
                    _orderService.UpdateOrder(order);
                    order.OrderNotes.Add(new OrderNote
                    {
                        Note = "Order has been marked as refunded. Amount = " + amount + "",
                        DisplayToCustomer = false,
                        CreatedOnUtc = DateTime.UtcNow
                    });
                    _orderService.UpdateOrder(order);
                    _customerActivityService.InsertActivity("EditOrder",
                    string.Format(_localizationService.GetResource("ActivityLog.EditOrder"), order.CustomOrderNumber), order);
                }
                ////
                ViewBag.Tokens = tokens;
               
            }
            catch (Exception ex)
            {
                //   throw;
            }
            return View("~/Plugins/Payments.Worldline/Views/Refund.cshtml");
        }
       

        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        public ActionResult Reconcile()
        {
            return View("~/Plugins/Payments.Worldline/Views/Reconcile.cshtml");
        }



        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        [HttpPost]
        public ActionResult Reconcile(IFormCollection fc)
        {
            try
            {
                string path = _env.WebRootPath;
                string json = "";
                var merchantcode = _settingService.GetSetting("worldlinepaymentsettings.merchantcode").Value.ToString();
                var currency = _settingService.GetSetting("worldlinepaymentsettings.currency").Value.ToString();
                ViewBag.merchantcode = merchantcode;
                ViewBag.currency = currency;
                //using (StreamReader r = new StreamReader(path + "\\output.json"))
                //{
                //    json = r.ReadToEnd();
                //    r.Close();
                //}
                // JObject config_data = JObject.Parse(json);
                OrderSearchModel searchModel = new OrderSearchModel();
                searchModel.StoreId = 0;
                //var transaction_ids = getAuthorizationTransactionId(searchModel);
                //var transaction_ids = fc["merchantRefNo"].ToString().Replace(System.Environment.NewLine, "").Replace(" ", "").Split(',');
                //List<JToken> tokens = new List<JToken>();
                var transDetails = new List<object>();
                //DateTime start_date = DateTime.ParseExact(fc["fromDate"].ToString(), "dd-mm-yyyy", CultureInfo.InvariantCulture);
                //DateTime end_date = DateTime.ParseExact(fc["endDate"], "dd-mm-yyyy", CultureInfo.InvariantCulture);
                DateTime start_date = DateTime.Parse(fc["fromDate"].ToString());
                DateTime end_date = DateTime.Parse(fc["endDate"].ToString());
                var diff = (end_date - start_date).TotalDays;
                end_date = end_date.AddDays(1);
                List<Order> lstOrder = _orderRepository.Table.Where(a => a.CreatedOnUtc <= end_date && a.CreatedOnUtc >= start_date && a.AuthorizationTransactionCode != null).ToList();
                foreach (Order order in lstOrder)
                {
                    int cntK = 0;
                    var authorizationTransactionCode = order.AuthorizationTransactionCode;
                    var day = order.CreatedOnUtc;
                    //for (var day = start_date; day <= end_date; day = day.AddDays(1))
                    //{
                    var data = new
                    {
                        merchant = new { identifier = merchantcode },
                        transaction = new
                        {
                            deviceIdentifier = "S",
                            currency = currency,
                            identifier = authorizationTransactionCode,
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
                        ////
                        if (order.PaymentStatusId != 30)
                        {
                            string amount = tokens[6]["paymentTransaction"]["amount"].ToString();
                            order.PaymentStatusId = 30;
                            _orderService.UpdateOrder(order);
                            order.OrderNotes.Add(new OrderNote
                            {
                                Note = "Order has been marked as Paid. Amount = " + amount + "",
                                DisplayToCustomer = false,
                                CreatedOnUtc = DateTime.UtcNow
                            });
                            _orderService.UpdateOrder(order);
                            _customerActivityService.InsertActivity("EditOrder",
                 string.Format(_localizationService.GetResource("ActivityLog.EditOrder"), order.CustomOrderNumber), order);
                        }
                        ///
                        transDetails.Add(tokens);
                        ;
                        tokens = null;
                        //break;
                    }
                    //}
                }
                ViewBag.transDetails = transDetails;
            }
            catch (Exception ex)
            {
                //   throw;
            }
            return View("~/Plugins/Payments.Worldline/Views/Reconcile.cshtml");
        }


        //[AuthorizeAdmin]
        //[Area(AreaNames.Admin)]
        //[HttpPost]
        //public ActionResult Reconcile(IFormCollection fc)
        //{
        //    try
        //    {
        //        string path = _env.WebRootPath;

        //        string json = "";

        //        var merchantcode = _settingService.GetSetting("worldlinepaymentsettings.merchantcode").Value.ToString();

        //        var currency = _settingService.GetSetting("worldlinepaymentsettings.merchantcode").Value.ToString();

        //        ViewBag.merchantcode = merchantcode;
        //        ViewBag.currency = currency;


        //        //using (StreamReader r = new StreamReader(path + "\\output.json"))
        //        //{


        //        //    json = r.ReadToEnd();

        //        //    r.Close();

        //        //}

        //       // JObject config_data = JObject.Parse(json);
        //        var transaction_ids = fc["merchantRefNo"].ToString().Replace(System.Environment.NewLine, "").Replace(" ", "").Split(',');
        //        //List<JToken> tokens = new List<JToken>();
        //        var transDetails = new List<object>();
        //        //DateTime start_date = DateTime.ParseExact(fc["fromDate"].ToString(), "dd-mm-yyyy", CultureInfo.InvariantCulture);
        //        //DateTime end_date = DateTime.ParseExact(fc["endDate"], "dd-mm-yyyy", CultureInfo.InvariantCulture);
        //        DateTime start_date = DateTime.Parse(fc["fromDate"].ToString());
        //        DateTime end_date = DateTime.Parse(fc["endDate"].ToString());
        //        var diff = (end_date - start_date).TotalDays;

        //        foreach (var transaction_id in transaction_ids)
        //        {
        //            int cntK = 0;
        //            for (var day = start_date; day <= end_date; day = day.AddDays(1))
        //            {
        //                var data = new
        //                {
        //                    merchant = new { identifier = merchantcode },
        //                    transaction = new
        //                    {
        //                        deviceIdentifier = "S",
        //                        currency = currency,
        //                        identifier = transaction_id,
        //                        dateTime = day.ToString("dd-M-yyyy"),
        //                        //string.Format("{0:d/M/yyyy}", day.ToString()),
        //                        requestType = "O"

        //                    }

        //                };

        //                HttpClient client = new HttpClient();
        //                client.BaseAddress = new Uri("https://www.paynimo.com/api/paynimoV2.req");
        //                client.DefaultRequestHeaders.Accept.Clear();
        //                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        //                HttpResponseMessage response = client.PostAsJsonAsync("https://www.paynimo.com/api/paynimoV2.req", data).Result;
        //                var respStr = response.Content.ReadAsStringAsync();

        //                data = null;
        //                JObject dual_verification_result = JObject.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(respStr));
        //                var jsonData = JObject.Parse(dual_verification_result["Result"].ToString()).Children();

        //                List<JToken> tokens = jsonData.Children().ToList();
        //                if (tokens[6]["paymentTransaction"]["errorMessage"].ToString() == "Transactionn Not Found")
        //                {
        //                    cntK = cntK + 1;
        //                    if (cntK == diff)
        //                    {
        //                        transDetails.Add(tokens);
        //                        ;
        //                        tokens = null;
        //                    }

        //                    //  break;

        //                }
        //                else
        //                {
        //                    transDetails.Add(tokens);
        //                    ;
        //                    tokens = null;
        //                    break;
        //                }

        //            }
        //        }
        //        ViewBag.transDetails = transDetails;

        //    }
        //    catch (Exception ex)
        //    {

        //        //   throw;
        //    }

        //    return View("~/Plugins/Payments.Worldline/Views/Reconcile.cshtml");
        //}
        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        public ActionResult OfflineVerification()
        {
            try
            {
                string path = _env.WebRootPath;
                string tranId = GenerateRandomString(12);
                ViewBag.tranId = tranId;
                var merchantcode = _settingService.GetSetting("worldlinepaymentsettings.merchantcode").Value.ToString();

                var currency = _settingService.GetSetting("worldlinepaymentsettings.currency").Value.ToString();

                ViewBag.merchantcode = merchantcode.ToString();
                ViewBag.currency = currency.ToString();
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

            return View("~/Plugins/Payments.Worldline/Views/OfflineVerification.cshtml");
        }
        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        [HttpPost]
        public ActionResult OfflineVerification(IFormCollection fc)
        {
            try
            {
                string path = _env.WebRootPath;

                string json = "";

                var merchantcode = _settingService.GetSetting("worldlinepaymentsettings.merchantcode").Value.ToString();

                var currency = _settingService.GetSetting("worldlinepaymentsettings.merchantcode").Value.ToString();

                ViewBag.merchantcode = merchantcode;
                ViewBag.currency = currency;


                //using (StreamReader r = new StreamReader(path + "\\output.json"))
                //{


                //    json = r.ReadToEnd();

                //    r.Close();

                //}

              //  JObject config_data = JObject.Parse(json);
                DateTime start_date = DateTime.Parse(fc["date"].ToString());
                var data = new
                {
                    merchant = new { identifier = merchantcode },
                    transaction = new
                    {
                        deviceIdentifier = "S",
                        currency = currency,
                        identifier = fc["merchantRefNo"].ToString(),
                        dateTime = start_date.ToString("dd-M-yyyy"),
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

                ViewBag.Tokens = tokens;

                //var transaction_ids = fc["merchantRefNo"].ToString().Replace(System.Environment.NewLine, "").Replace(" ", "").Split(',');
                //List<JToken> tokens = new List<JToken>();
                var transDetails = new List<object>();
                //DateTime start_date = DateTime.ParseExact(fc["fromDate"].ToString(), "dd-mm-yyyy", CultureInfo.InvariantCulture);
                //DateTime end_date = DateTime.ParseExact(fc["endDate"], "dd-mm-yyyy", CultureInfo.InvariantCulture);
                //DateTime start_date = DateTime.Parse(fc["fromDate"].ToString());
                //DateTime end_date = DateTime.Parse(fc["endDate"].ToString());
                //var diff = (end_date - start_date).TotalDays;

                //foreach (var transaction_id in transaction_ids)
                //{
                //    int cntK = 0;
                //    for (var day = start_date; day <= end_date; day = day.AddDays(1))
                //    {
                       
                //        if (tokens[6]["paymentTransaction"]["errorMessage"].ToString() == "Transactionn Not Found")
                //        {
                //            cntK = cntK + 1;
                //            if (cntK == diff)
                //            {
                //                transDetails.Add(tokens);
                //                ;
                //                tokens = null;
                //            }

                //            //  break;

                //        }
                //        else
                //        {
                //            transDetails.Add(tokens);
                //            ;
                //            tokens = null;
                //            break;
                //        }

                //    }
                //}
                //ViewBag.transDetails = transDetails;

            }
            catch (Exception ex)
            {

                //   throw;
            }

            return View("~/Plugins/Payments.Worldline/Views/OfflineVerification.cshtml");
        }
        //[AuthorizeAdmin]
        //[Area(AreaNames.Admin)]
        public ActionResult s2s(string msg)
        {
            try
            {
                string json = string.Empty;
                string path = _env.WebRootPath;
                string tranId = GenerateRandomString(12);
                ViewBag.tranId = tranId;

                JsonTextReader jsonTextReader = null; //Added_N

                //using (StreamReader r = new StreamReader(path + "\\output.json"))
                //{
                //    json = r.ReadToEnd();
                //    ViewBag.config_data = json;
                //    jsonTextReader = new JsonTextReader(r);  //Added_N
                //}

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
                var salt = _settingService.GetSetting("worldlinepaymentsettings.SALT").Value.ToString();

                string data_string = res.ToString() + salt;// dejson["SALT"];
                var hash = GenerateSHA512StringFors2s(data_string);
                //var hash = GenerateSHA512StringFors2s(data_string);
                //if (data[15].ToString() == hash.Data.ToString().ToLower())
                //if (data[15].ToString() == hash.Value.ToString().ToLower())
                //{
                //    ViewBag.status = "1";
                //}
                //else
                //{
                //    ViewBag.status = "0";
                //}
                if (data[15].ToString() == hash.Value.ToString().ToLower())
                {
                    ViewBag.status = "1";
                    Order order = _orderRepository.Table.Where(a => a.AuthorizationTransactionId == data[5].ToString()).ToList().FirstOrDefault();
                    //
                    if (order.PaymentStatusId != 30)
                    {
                        string amount = data[6].ToString();
                        order.PaymentStatusId = 30;
                        _orderService.UpdateOrder(order);
                        order.OrderNotes.Add(new OrderNote
                        {
                            Note = "Order has been marked as Paid. Amount = " + amount + "",
                            DisplayToCustomer = false,
                            CreatedOnUtc = DateTime.UtcNow
                        });
                        _orderService.UpdateOrder(order);
                        _customerActivityService.InsertActivity("EditOrder",
             string.Format(_localizationService.GetResource("ActivityLog.EditOrder"), order.CustomOrderNumber), order);
                    }
                    //
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
            return View("~/Plugins/Payments.Worldline/Views/s2s.cshtml");
        }

        public static string GenerateRandomString(int size)
        {
            Guid g = Guid.NewGuid();

            string random1 = g.ToString();
            random1 = random1.Replace("-", "");
            var builder = random1.Substring(0, size);
            return builder.ToString();
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
        //action displaying notification (warning) to a store owner about inaccurate PayPal rounding
        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        public IActionResult RoundingWarning(bool passProductNamesAndTotals)
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            //prices and total aren't rounded, so display warning
            if (passProductNamesAndTotals && !_shoppingCartSettings.RoundPricesDuringCalculation)
                return Json(new { Result = _localizationService.GetResource("Plugins.Payments.Worldline.RoundingWarning") });

            return Json(new { Result = string.Empty });
        }

        public IActionResult PDTHandler()
        {
            var tx = _webHelper.QueryString<string>("tx");

            if (!(_paymentPluginManager.LoadPluginBySystemName("Payments.Worldline") is WorldlinePaymentProcessor processor) || !_paymentPluginManager.IsPluginActive(processor))
                throw new NopException("Worldline module cannot be loaded");

            if (processor.GetPdtDetails(tx, out var values, out var response))
            {
                values.TryGetValue("custom", out var orderNumber);
                var orderNumberGuid = Guid.Empty;
                try
                {
                    orderNumberGuid = new Guid(orderNumber);
                }
                catch
                {
                    // ignored
                }

                var order = _orderService.GetOrderByGuid(orderNumberGuid);

                if (order == null)
                    return RedirectToAction("Index", "Home", new { area = string.Empty });

                var mcGross = decimal.Zero;

                try
                {
                    mcGross = decimal.Parse(values["mc_gross"], new CultureInfo("en-US"));
                }
                catch (Exception exc)
                {
                    _logger.Error("PayPal PDT. Error getting mc_gross", exc);
                }

                values.TryGetValue("payer_status", out var payerStatus);
                values.TryGetValue("payment_status", out var paymentStatus);
                values.TryGetValue("pending_reason", out var pendingReason);
                values.TryGetValue("mc_currency", out var mcCurrency);
                values.TryGetValue("txn_id", out var txnId);
                values.TryGetValue("payment_type", out var paymentType);
                values.TryGetValue("payer_id", out var payerId);
                values.TryGetValue("receiver_id", out var receiverId);
                values.TryGetValue("invoice", out var invoice);
                values.TryGetValue("payment_fee", out var paymentFee);

                var sb = new StringBuilder();
                sb.AppendLine("PayPal PDT:");
                sb.AppendLine("mc_gross: " + mcGross);
                sb.AppendLine("Payer status: " + payerStatus);
                sb.AppendLine("Payment status: " + paymentStatus);
                sb.AppendLine("Pending reason: " + pendingReason);
                sb.AppendLine("mc_currency: " + mcCurrency);
                sb.AppendLine("txn_id: " + txnId);
                sb.AppendLine("payment_type: " + paymentType);
                sb.AppendLine("payer_id: " + payerId);
                sb.AppendLine("receiver_id: " + receiverId);
                sb.AppendLine("invoice: " + invoice);
                sb.AppendLine("payment_fee: " + paymentFee);

                var newPaymentStatus = WorldlineHelper.GetPaymentStatus(paymentStatus, string.Empty);
                sb.AppendLine("New payment status: " + newPaymentStatus);

                //order note
                order.OrderNotes.Add(new OrderNote
                {
                    Note = sb.ToString(),
                    DisplayToCustomer = false,
                    CreatedOnUtc = DateTime.UtcNow
                });
                _orderService.UpdateOrder(order);

                //validate order total
                var orderTotalSentToPayPal = _genericAttributeService.GetAttribute<decimal?>(order, WorldlineHelper.OrderTotalSentToWorldline);
                if (orderTotalSentToPayPal.HasValue && mcGross != orderTotalSentToPayPal.Value)
                {
                    var errorStr = $"PayPal PDT. Returned order total {mcGross} doesn't equal order total {order.OrderTotal}. Order# {order.Id}.";
                    //log
                    _logger.Error(errorStr);
                    //order note
                    order.OrderNotes.Add(new OrderNote
                    {
                        Note = errorStr,
                        DisplayToCustomer = false,
                        CreatedOnUtc = DateTime.UtcNow
                    });
                    _orderService.UpdateOrder(order);

                    return RedirectToAction("Index", "Home", new { area = string.Empty });
                }

                //clear attribute
                if (orderTotalSentToPayPal.HasValue)
                    _genericAttributeService.SaveAttribute<decimal?>(order, WorldlineHelper.OrderTotalSentToWorldline, null);

                if (newPaymentStatus != PaymentStatus.Paid)
                    return RedirectToRoute("CheckoutCompleted", new { orderId = order.Id });

                if (!_orderProcessingService.CanMarkOrderAsPaid(order))
                    return RedirectToRoute("CheckoutCompleted", new { orderId = order.Id });

                //mark order as paid
                order.AuthorizationTransactionId = txnId;
                _orderService.UpdateOrder(order);
                _orderProcessingService.MarkOrderAsPaid(order);

                return RedirectToRoute("CheckoutCompleted", new { orderId = order.Id });
            }
            else
            {
                if (!values.TryGetValue("custom", out var orderNumber))
                    orderNumber = _webHelper.QueryString<string>("cm");

                var orderNumberGuid = Guid.Empty;

                try
                {
                    orderNumberGuid = new Guid(orderNumber);
                }
                catch
                {
                    // ignored
                }

                var order = _orderService.GetOrderByGuid(orderNumberGuid);
                if (order == null)
                    return RedirectToAction("Index", "Home", new { area = string.Empty });

                //order note
                order.OrderNotes.Add(new OrderNote
                {
                    Note = "PayPal PDT failed. " + response,
                    DisplayToCustomer = false,
                    CreatedOnUtc = DateTime.UtcNow
                });
                _orderService.UpdateOrder(order);

                return RedirectToRoute("CheckoutCompleted", new { orderId = order.Id });
            }

         
        }

        public IActionResult IPNHandler()
        {
            byte[] parameters;

            using (var stream = new MemoryStream())
            {
                Request.Body.CopyTo(stream);
                parameters = stream.ToArray();
            }

            var strRequest = Encoding.ASCII.GetString(parameters);

            if (!(_paymentPluginManager.LoadPluginBySystemName("Payments.Worldline") is WorldlinePaymentProcessor processor) || !_paymentPluginManager.IsPluginActive(processor))
                throw new NopException("PayPal Standard module cannot be loaded");

            if (!processor.VerifyIpn(strRequest, out var values))
            {
                _logger.Error("PayPal IPN failed.", new NopException(strRequest));

                //nothing should be rendered to visitor
                return Content(string.Empty);
            }

            var mcGross = decimal.Zero;

            try
            {
                mcGross = decimal.Parse(values["mc_gross"], new CultureInfo("en-US"));
            }
            catch
            {
                // ignored
            }

            values.TryGetValue("payment_status", out var paymentStatus);
            values.TryGetValue("pending_reason", out var pendingReason);
            values.TryGetValue("txn_id", out var txnId);
            values.TryGetValue("txn_type", out var txnType);
            values.TryGetValue("rp_invoice_id", out var rpInvoiceId);

            var sb = new StringBuilder();
            sb.AppendLine("PayPal IPN:");
            foreach (var kvp in values)
            {
                sb.AppendLine(kvp.Key + ": " + kvp.Value);
            }

            var newPaymentStatus = WorldlineHelper.GetPaymentStatus(paymentStatus, pendingReason);
            sb.AppendLine("New payment status: " + newPaymentStatus);

            var ipnInfo = sb.ToString();

            switch (txnType)
            {
                case "recurring_payment":
                    ProcessRecurringPayment(rpInvoiceId, newPaymentStatus, txnId, ipnInfo);
                    break;
                case "recurring_payment_failed":
                    if (Guid.TryParse(rpInvoiceId, out var orderGuid))
                    {
                        var order = _orderService.GetOrderByGuid(orderGuid);
                        if (order != null)
                        {
                            var recurringPayment = _orderService.SearchRecurringPayments(initialOrderId: order.Id)
                                .FirstOrDefault();
                            //failed payment
                            if (recurringPayment != null)
                                _orderProcessingService.ProcessNextRecurringPayment(recurringPayment,
                                    new ProcessPaymentResult
                                    {
                                        Errors = new[] { txnType },
                                        RecurringPaymentFailed = true
                                    });
                        }
                    }

                    break;
                default:
                    values.TryGetValue("custom", out var orderNumber);
                    ProcessPayment(orderNumber, ipnInfo, newPaymentStatus, mcGross, txnId);

                    break;
            }

            //nothing should be rendered to visitor
            return Content(string.Empty);
        }

        public IActionResult CancelOrder()
        {
            var order = _orderService.SearchOrders(_storeContext.CurrentStore.Id,
                customerId: _workContext.CurrentCustomer.Id, pageSize: 1).FirstOrDefault();

            if (order != null)
                return RedirectToRoute("OrderDetails", new { orderId = order.Id });

            return RedirectToRoute("Homepage");
        }

        #endregion
    }
}