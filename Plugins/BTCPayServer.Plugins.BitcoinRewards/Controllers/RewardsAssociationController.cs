#nullable enable
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Plugins.BitcoinRewards.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.BitcoinRewards.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie + "," + AuthenticationSchemes.Greenfield,
    Policy = Policies.CanModifyStoreSettings)]
[Route("plugins/bitcoin-rewards/{storeId}/part-b/order-associations")]
public sealed class RewardsAssociationController : ControllerBase
{
    private readonly CustomerOrderAssociationService _associationService;

    public RewardsAssociationController(CustomerOrderAssociationService associationService) =>
        _associationService = associationService;

    public sealed class AssociationRequest
    {
        [Required, MaxLength(255)] public string SquareOrderId { get; set; } = string.Empty;
        [Required, MaxLength(128)] public string LightningAddress { get; set; } = string.Empty;
        [MaxLength(100)] public string? RegisterId { get; set; }
        [MaxLength(100)] public string? DeviceId { get; set; }
    }

    [HttpPost]
    public async Task<IActionResult> Associate(
        string storeId,
        AssociationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _associationService.AssociateAsync(storeId, request.SquareOrderId,
                request.LightningAddress, request.RegisterId, request.DeviceId,
                User.Identity?.Name, cancellationToken);
            return CreatedAtAction(nameof(Associate), new { storeId }, new
            {
                id = result.Id,
                orderId = result.SquareOrderId,
                profileId = result.CustomerProfileId,
                state = result.State.ToString()
            });
        }
        catch (CustomerProfileNotFoundException)
        {
            return NotFound(new { error = "Customer profile not found" });
        }
        catch (System.ArgumentException)
        {
            return BadRequest(new { error = "A valid customer Lightning address and Square order ID are required" });
        }
        catch (CustomerOrderAssociationConflictException)
        {
            return Conflict(new { error = "Order association is already bound or belongs to another customer" });
        }
    }

    [HttpDelete("{squareOrderId}")]
    public async Task<IActionResult> Cancel(
        string storeId,
        string squareOrderId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _associationService.CancelAsync(storeId, squareOrderId, cancellationToken)
                ? NoContent()
                : NotFound();
        }
        catch (CustomerOrderAssociationConflictException)
        {
            return Conflict(new { error = "A bound order association cannot be cancelled" });
        }
    }
}
