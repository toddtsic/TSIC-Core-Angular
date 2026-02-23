using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.Contracts.Dtos.Customer;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

/// <summary>
/// Customer Configure — SuperUser-only CRUD for Customer records.
/// </summary>
[ApiController]
[Route("api/customer-configure")]
[Authorize(Policy = "SuperUserOnly")]
public class CustomerConfigureController : ControllerBase
{
    private readonly ICustomerConfigureService _service;

    public CustomerConfigureController(ICustomerConfigureService service)
    {
        _service = service;
    }

    private string GetUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new UnauthorizedAccessException("User ID not found in claims.");

    // ── Read ────────────────────────────────────────────────

    /// <summary>List all customers with timezone name and job count.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<CustomerListDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<CustomerListDto>>> GetAll(CancellationToken ct)
    {
        var result = await _service.GetAllCustomersAsync(ct);
        return Ok(result);
    }

    /// <summary>Get a single customer's editable detail (includes ADN credentials).</summary>
    [HttpGet("{customerId:guid}")]
    [ProducesResponseType(typeof(CustomerDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerDetailDto>> GetById(Guid customerId, CancellationToken ct)
    {
        var result = await _service.GetCustomerByIdAsync(customerId, ct);
        if (result is null) return NotFound();
        return Ok(result);
    }

    /// <summary>List all timezones for dropdown selection.</summary>
    [HttpGet("timezones")]
    [ProducesResponseType(typeof(List<TimezoneDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<TimezoneDto>>> GetTimezones(CancellationToken ct)
    {
        var result = await _service.GetTimezonesAsync(ct);
        return Ok(result);
    }

    // ── Write ───────────────────────────────────────────────

    /// <summary>Create a new customer.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(CustomerDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CustomerDetailDto>> Create(
        [FromBody] CreateCustomerRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _service.CreateCustomerAsync(request, GetUserId(), ct);
            return CreatedAtAction(nameof(GetById), new { customerId = result.CustomerId }, result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Update an existing customer.</summary>
    [HttpPut("{customerId:guid}")]
    [ProducesResponseType(typeof(CustomerDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerDetailDto>> Update(
        Guid customerId, [FromBody] UpdateCustomerRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _service.UpdateCustomerAsync(customerId, request, GetUserId(), ct);
            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Delete a customer (rejected if customer has associated jobs).</summary>
    [HttpDelete("{customerId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Delete(Guid customerId, CancellationToken ct)
    {
        try
        {
            await _service.DeleteCustomerAsync(customerId, ct);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }
}
