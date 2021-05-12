using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nop.Web.Framework.Mvc.Routing;

namespace Nop.Plugin.Payments.Ingenico.Infrastructure
{
    public partial class RouteProvider : IRouteProvider
    {
        /// <summary>
        /// Register routes
        /// </summary>
        /// <param name="routeBuilder">Route builder</param>
        public void RegisterRoutes(IRouteBuilder routeBuilder)
        {
            routeBuilder.MapRoute("Plugin.Payments.Ingenico.responseHandler", "Plugins/PaymentIngenico/responseHandler",
                 new { controller = "Ingenico", action = "responseHandler" });
            routeBuilder.MapRoute("Plugin.Payments.Ingenico.refund", "Plugins/PaymentIngenico/Refund",
                 new { controller = "Ingenico", action = "Refund" });
            //PDT
            routeBuilder.MapRoute("Plugin.Payments.Ingenico.PDTHandler", "Plugins/PaymentIngenico/PDTHandler",
                 new { controller = "IngenicoStandard", action = "PDTHandler" });

            //IPN
            routeBuilder.MapRoute("Plugin.Payments.Ingenico.IPNHandler", "Plugins/PaymentIngenico/IPNHandler",
                 new { controller = "IngenicoStandard", action = "IPNHandler" });

            //Cancel
            routeBuilder.MapRoute("Plugin.Payments.Ingenico.CancelOrder", "Plugins/PaymentIngenico/CancelOrder",
                 new { controller = "IngenicoStandard", action = "CancelOrder" });
        }

        /// <summary>
        /// Gets a priority of route provider
        /// </summary>
        public int Priority => -1;
    }
}