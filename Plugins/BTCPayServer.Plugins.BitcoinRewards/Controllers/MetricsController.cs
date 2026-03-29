#nullable enable

using System;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Plugins.BitcoinRewards.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.BitcoinRewards.Controllers;

/// <summary>
/// Metrics endpoint for monitoring and observability
/// </summary>
[Route("plugins/bitcoin-rewards")]
public class MetricsController : Controller
{
    private readonly RewardMetrics _metrics;
    private readonly ILogger<MetricsController> _logger;

    public MetricsController(
        RewardMetrics metrics,
        ILogger<MetricsController> logger)
    {
        _metrics = metrics;
        _logger = logger;
    }

    /// <summary>
    /// Get metrics in Prometheus text format
    /// GET /plugins/bitcoin-rewards/metrics
    /// </summary>
    [HttpGet("metrics")]
    [AllowAnonymous] // Prometheus scraper needs unauthenticated access
    public IActionResult GetPrometheusMetrics()
    {
        try
        {
            var prometheus = _metrics.ExportPrometheusFormat();
            
            // Add metadata header
            var output = new StringBuilder();
            output.AppendLine("# Bitcoin Rewards Plugin Metrics");
            output.AppendLine($"# Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            output.AppendLine();
            output.AppendLine(prometheus);
            
            return Content(output.ToString(), "text/plain; version=0.0.4");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting Prometheus metrics");
            return StatusCode(500, "Error generating metrics");
        }
    }

    /// <summary>
    /// Get metrics summary in JSON format (requires authentication)
    /// GET /plugins/bitcoin-rewards/{storeId}/metrics/summary
    /// </summary>
    [HttpGet("{storeId}/metrics/summary")]
    [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public IActionResult GetMetricsSummary(string storeId)
    {
        try
        {
            var summary = _metrics.GetSummary();
            
            return Json(new
            {
                storeId,
                timestamp = DateTime.UtcNow,
                metrics = summary
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting metrics summary for store {StoreId}", storeId);
            return StatusCode(500, "Error retrieving metrics");
        }
    }

    /// <summary>
    /// Get specific metric value
    /// GET /plugins/bitcoin-rewards/metrics/counter/{name}
    /// </summary>
    [HttpGet("metrics/counter/{name}")]
    [AllowAnonymous]
    public IActionResult GetCounter(string name)
    {
        try
        {
            var value = _metrics.GetCounter(name);
            return Json(new { metric = name, value, type = "counter" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting counter {Name}", name);
            return StatusCode(500, "Error retrieving counter");
        }
    }

    /// <summary>
    /// Get specific gauge value
    /// GET /plugins/bitcoin-rewards/metrics/gauge/{name}
    /// </summary>
    [HttpGet("metrics/gauge/{name}")]
    [AllowAnonymous]
    public IActionResult GetGauge(string name)
    {
        try
        {
            var value = _metrics.GetGauge(name);
            return Json(new { metric = name, value, type = "gauge" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting gauge {Name}", name);
            return StatusCode(500, "Error retrieving gauge");
        }
    }

    /// <summary>
    /// Get histogram statistics
    /// GET /plugins/bitcoin-rewards/metrics/histogram/{name}
    /// </summary>
    [HttpGet("metrics/histogram/{name}")]
    [AllowAnonymous]
    public IActionResult GetHistogram(string name)
    {
        try
        {
            var stats = _metrics.GetHistogramStats(name);
            
            if (stats == null)
            {
                return NotFound(new { error = "Histogram not found or no data" });
            }
            
            return Json(new 
            { 
                metric = name, 
                stats, 
                type = "histogram" 
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting histogram {Name}", name);
            return StatusCode(500, "Error retrieving histogram");
        }
    }

    /// <summary>
    /// Health check endpoint for the metrics system
    /// GET /plugins/bitcoin-rewards/metrics/health
    /// </summary>
    [HttpGet("metrics/health")]
    [AllowAnonymous]
    public IActionResult Health()
    {
        return Json(new
        {
            status = "healthy",
            service = "bitcoin-rewards-metrics",
            timestamp = DateTime.UtcNow
        });
    }
}
