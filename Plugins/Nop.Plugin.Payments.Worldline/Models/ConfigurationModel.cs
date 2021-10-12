using Nop.Web.Framework.Mvc.ModelBinding;
using Nop.Web.Framework.Models;

namespace Nop.Plugin.Payments.Worldline.Models
{
    public class ConfigurationModel : BaseNopModel
    {
        public int ActiveStoreScopeConfiguration { get; set; }

        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.utf8")]
        public string utf8 { get; set; }
        public bool utf8_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.authenticity_token")]
        public string authenticity_token { get; set; }
        public bool authenticity_token_OverrideForStore { get; set; }
        [NopResourceDisplayName("Merchant Code")]
        public string merchantCode { get; set; }
        public bool merchantCode_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.merchantSchemeCode")]
        public string merchantSchemeCode { get; set; }
        public bool merchantSchemeCode_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.SALT")]
        public string SALT { get; set; }
        public bool SALT_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.currency")]
        public string currency { get; set; }
        public bool currency_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.typeOfPayment")]
        public string typeOfPayment { get; set; }
        public bool typeOfPayment_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.primaryColor")]
        public string primaryColor { get; set; }
        public bool primaryColor_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.secondaryColor")]
        public string secondaryColor { get; set; }
        public bool secondaryColor_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.buttonColor1")]
        public string buttonColor1 { get; set; }
        public bool buttonColor1_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.buttonColor2")]
        public string buttonColor2 { get; set; }
        public bool buttonColor2_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.logoURL")]
        public string logoURL { get; set; }
        public bool logoURL_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.enableExpressPay")]
        public string enableExpressPay { get; set; }
        public bool enableExpressPay_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.separateCardMode")]
        public string separateCardMode { get; set; }
        public bool separateCardMode_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.enableNewWindowFlow")]
        public string enableNewWindowFlow { get; set; }
        public bool enableNewWindowFlow_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.merchantMessage")]
        public string merchantMessage { get; set; }
        public bool merchantMessage_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.disclaimerMessage")]
        public string disclaimerMessage { get; set; }
        public bool disclaimerMessage_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.paymentMode")]
        public string paymentMode { get; set; }
        public bool paymentMode_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.paymentModeOrder")]
        public string paymentModeOrder { get; set; }
        public bool paymentModeOrder_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.enableInstrumentDeRegistration")]
        public string enableInstrumentDeRegistration { get; set; }
        public bool enableInstrumentDeRegistration_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.transactionType")]
        public string transactionType { get; set; }
        public bool transactionType_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.hideSavedInstruments")]
        public string hideSavedInstruments { get; set; }
        public bool hideSavedInstruments_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.saveInstrument")]
        public string saveInstrument { get; set; }
        public bool saveInstrument_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.displayTransactionMessageOnPopup")]
        public string displayTransactionMessageOnPopup { get; set; }
        public bool displayTransactionMessageOnPopup_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.embedPaymentGatewayOnPage")]
        public string embedPaymentGatewayOnPage { get; set; }
        public bool embedPaymentGatewayOnPage_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.enableSI")]
        public string enableSI { get; set; }
        public bool enableSI_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.hideSIDetails")]
        public string hideSIDetails { get; set; }
        public bool hideSIDetails_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.hideSIConfirmation")]
        public string hideSIConfirmation { get; set; }
        public bool hideSIConfirmation_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.expandSIDetails")]
        public string expandSIDetails { get; set; }
        public bool expandSIDetails_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.enableDebitDay")]
        public string enableDebitDay { get; set; }
        public bool enableDebitDay_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.showSIResponseMsg")]
        public string showSIResponseMsg { get; set; }
        public bool showSIResponseMsg_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.showSIConfirmation")]
        public string showSIConfirmation { get; set; }
        public bool showSIConfirmation_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.enableTxnForNonSICards")]
        public string enableTxnForNonSICards { get; set; }
        public bool enableTxnForNonSICards_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.showAllModesWithSI")]
        public string showAllModesWithSI { get; set; }
        public bool showAllModesWithSI_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.siDetailsAtMerchantEnd")]
        public string siDetailsAtMerchantEnd { get; set; }
        public bool siDetailsAtMerchantEnd_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.amounttype")]
        public string amounttype { get; set; }
        public bool amounttype_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.frequency")]
        public string frequency { get; set; }
        public bool frequency_OverrideForStore { get; set; }



        //[NopResourceDisplayName("Plugins.Payments.Worldline.Fields.enableTxnForNonSICards")]
        //public string merchantLogoUrl { get; set; }
        //public bool merchantLogoUrl_OverrideForStore { get; set; }
        //[NopResourceDisplayName("Plugins.Payments.Worldline.Fields.showAllModesWithSI")]
        //public string merchantMsg { get; set; }
        //public bool merchantMsg_OverrideForStore { get; set; }
        //[NopResourceDisplayName("Plugins.Payments.Worldline.Fields.siDetailsAtMerchantEnd")]
        //public string disclaimerMsg { get; set; }
        //public bool disclaimerMsg_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.showPGResponseMsg")]
        public string showPGResponseMsg { get; set; }
        public bool showPGResponseMsg_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.frequency")]
        public string enableAbortResponse { get; set; }
        public bool enableAbortResponse_OverrideForStore { get; set; }


        [NopResourceDisplayName("Plugins.Payments.PayPalStandard.Fields.UseSandbox")]
        public bool UseSandbox { get; set; }
        public bool UseSandbox_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.PayPalStandard.Fields.BusinessEmail")]
        public string BusinessEmail { get; set; }
        public bool BusinessEmail_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.PayPalStandard.Fields.PDTToken")]
        public string PdtToken { get; set; }
        public bool PdtToken_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.PassProductNamesAndTotals")]
        public bool PassProductNamesAndTotals { get; set; }
        public bool PassProductNamesAndTotals_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.PayPalStandard.Fields.AdditionalFee")]
        public decimal AdditionalFee { get; set; }
        public bool AdditionalFee_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.PayPalStandard.Fields.AdditionalFeePercentage")]
        public bool AdditionalFeePercentage { get; set; }
        public bool AdditionalFeePercentage_OverrideForStore { get; set; }
    }
}