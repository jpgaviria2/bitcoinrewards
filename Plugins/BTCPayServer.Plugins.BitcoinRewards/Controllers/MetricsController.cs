using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Plugins.BitcoinRewards.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.BitcoinRewards.Controllers
{
    /// <summary>
    /// API controller for exposing Prometheus-compatible metrics
    /// </summary>
    [ApiController]
    [Route("api/v1/bitcoin-rewards")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public class MetricsController : ControllerBase
    {
        private readonly RewardMetrics _metrics;

        public MetricsController(RewardMetrics metrics)
        {
            _metrics = metrics;
        }

        /// <summary>
        /// Get Prometheus-format metrics for monitoring and alerting
        /// </summary>
        /// <returns>Plain text Prometheus metrics</returns>
        [HttpGet("metrics")]
        [AllowAnonymous] // Allow Prometheus scraper access (consider IP whitelist in production)
        public async Task<IActionResult> GetMetrics()
        {
            var metricsText = await Task.Run(() => _metrics.ExportPrometheusFormat());
            return Content(metricsText, "text/plain; version=0.0.4; charset=utf-8");
        }

        /// <summary>
        /// Get JSON-formatted metrics for admin dashboard
        /// </summary>
        /// <returns>JSON metrics summary</returns>
        [HttpGet("metrics/json")]
        [Authorize(Policy = BTCPayServer.Client.Policies.CanModifyStoreSettings)]
        public IActionResult GetMetricsJson()
        {
            var snapshot = _metrics.GetSnapshot();
            return Ok(new
            {
                counters = snapshot.Counters,
                gauges = snapshot.Gauges,
                histograms = snapshot.Histograms.Select(h => new
                {
                    name = h.Key,
                    count = h.Value.Count,
                    sum = h.Value.Sum,
                    mean = h.Value.Mean,
                    min = h.Value.Min,
                    max = h.Value.Max,
                    p50 = h.Value.P50,
                    p95 = h.Value.P95,
                    p99 = h.Value.P99
                }).ToDictionary(x => x.name, x => (object)new
                {
                    x.count,
                    x.sum,
                    x.mean,
                    x.min,
                    x.max,
                    x.p50,
                    x.p95,
                    x.p99
                })
            });
        }

        /// <summary>
        /// Health check endpoint for metrics system
        /// </summary>
        [HttpGet("metrics/health")]
        [AllowAnonymous]
        public IActionResult GetMetricsHealth()
        {
            var snapshot = _metrics.GetSnapshot();
            var totalMetrics = snapshot.Counters.Count + snapshot.Gauges.Count + snapshot.Histograms.Count;
            
            return Ok(new
            {
                status = "healthy",
                totalMetrics,
                counters = snapshot.Counters.Count,
                gauges = snapshot.Gauges.Count,
                histograms = snapshot.Histograms.Count
            });
        }
    }
}
