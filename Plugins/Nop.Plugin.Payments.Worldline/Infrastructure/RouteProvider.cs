using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nop.Web.Framework.Mvc.Routing;

namespace Nop.Plugin.Payments.Worldline.Infrastructure
{
    public partial class RouteProvider : IRouteProvider
    {
        /// <summary>
        /// Register routes
        /// </summary>
        /// <param name="routeBuilder">Route builder</param>
        public void RegisterRoutes(IRouteBuilder routeBuilder)
        {
            routeBuilder.MapRoute("Plugin.Payments.Worldline.responseHandler", "Plugins/PaymentWorldline/responseHandler",
                 new { controller = "Worldline", action = "responseHandler" });
            routeBuilder.MapRoute("Plugin.Payments.Worldline.refund", "Plugins/PaymentWorldline/Refund",
                 new { controller = "Worldline", action = "Refund" });
            //PDT
            routeBuilder.MapRoute("Plugin.Payments.Worldline.PDTHandler", "Plugins/PaymentWorldline/PDTHandler",
                 new { controller = "WorldlineStandard", action = "PDTHandler" });

            //IPN
            routeBuilder.MapRoute("Plugin.Payments.Worldline.IPNHandler", "Plugins/PaymentWorldline/IPNHandler",
                 new { controller = "WorldlineStandard", action = "IPNHandler" });

            //Cancel
            routeBuilder.MapRoute("Plugin.Payments.Worldline.CancelOrder", "Plugins/PaymentWorldline/CancelOrder",
                 new { controller = "WorldlineStandard", action = "CancelOrder" });
        }

        /// <summary>
        /// Gets a priority of route provider
        /// </summary>
        public int Priority => -1;
    }
}