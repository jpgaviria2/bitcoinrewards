using System;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Plugins.BitcoinRewards.Models;
using BTCPayServer.Plugins.BitcoinRewards.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.BitcoinRewards.Controllers
{
    /// <summary>
    /// Controller for analytics dashboard and data export
    /// </summary>
    [ApiController]
    [Route("api/v1/bitcoin-rewards")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public class AnalyticsController : ControllerBase
    {
        private readonly AnalyticsService _analyticsService;
        
        public AnalyticsController(AnalyticsService analyticsService)
        {
            _analyticsService = analyticsService;
        }
        
        /// <summary>
        /// Get analytics dashboard data
        /// </summary>
        [HttpGet("{storeId}/analytics")]
        [Authorize(Policy = Policies.CanViewStoreSettings)]
        public async Task<IActionResult> GetAnalytics(
            string storeId,
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null)
        {
            // Default to last 30 days
            var start = startDate ?? DateTime.UtcNow.AddDays(-30);
            var end = endDate ?? DateTime.UtcNow;
            
            var dashboard = await _analyticsService.GenerateDashboardAsync(storeId, start, end);
            return Ok(dashboard);
        }
        
        /// <summary>
        /// Export analytics data
        /// </summary>
        [HttpGet("{storeId}/analytics/export")]
        [Authorize(Policy = Policies.CanModifyStoreSettings)]
        public async Task<IActionResult> ExportAnalytics(
            string storeId,
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null,
            [FromQuery] string format = "csv")
        {
            var request = new ExportRequest
            {
                StoreId = storeId,
                StartDate = startDate ?? DateTime.UtcNow.AddDays(-30),
                EndDate = endDate ?? DateTime.UtcNow,
                Format = format.ToLowerInvariant() switch
                {
                    "json" => ExportFormat.JSON,
                    "excel" => ExportFormat.Excel,
                    _ => ExportFormat.CSV
                }
            };
            
            var data = await _analyticsService.ExportDataAsync(request);
            
            var contentType = request.Format switch
            {
                ExportFormat.JSON => "application/json",
                ExportFormat.Excel => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                _ => "text/csv"
            };
            
            var filename = $"bitcoin-rewards-{storeId}-{DateTime.UtcNow:yyyyMMdd}.{format}";
            
            return File(data, contentType, filename);
        }
    }
}
