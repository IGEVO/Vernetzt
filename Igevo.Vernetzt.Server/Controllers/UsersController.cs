using System.Security.Claims;
using Igevo.Vernetzt.Core.DTOs;
using Igevo.Vernetzt.Core.DTOs.Auth;
using Igevo.Vernetzt.Core.DTOs.Common;
using Igevo.Vernetzt.Core.DTOs.Users;
using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Igevo.Vernetzt.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly IAuthService _authService;
    private readonly IContactAssignmentConsentService _consentService;
    private readonly IAccountEmailChangeService _emailChangeService;
    private readonly ILogger<UsersController> _logger;

    public UsersController(
        IUserService userService,
        IAuthService authService,
        IContactAssignmentConsentService consentService,
        IAccountEmailChangeService emailChangeService,
        ILogger<UsersController> logger)
    {
        _userService = userService;
        _authService = authService;
        _consentService = consentService;
        _emailChangeService = emailChangeService;
        _logger = logger;
    }

    /// <summary>
    /// Gets the current user's profile.
    /// </summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<UserDto>>> GetCurrentUser(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = GetUserId();
            var user = await _userService.GetCurrentUserAsync(userId, cancellationToken);

            if (user == null)
            {
                return NotFound(ApiResponse<object>.CreateError("Benutzer nicht gefunden.", "NOT_FOUND"));
            }

            return Ok(ApiResponse<UserDto>.CreateSuccess(MapToDto(user)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting current user");
            return StatusCode(500, ApiResponse<object>.CreateError("Ein Fehler ist beim Abrufen des Benutzers aufgetreten."));
        }
    }

    /// <summary>
    /// Gets all users (SystemAdmin only).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<UserDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<IEnumerable<UserDto>>>> GetAllUsers(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = GetUserId();
            var users = await _userService.GetAllAsync(userId, cancellationToken);
            var passkeyCounts = await _authService.GetPasskeyCountsAsync(cancellationToken);
            var dtos = users.Select(user =>
            {
                var dto = MapToDto(user);
                dto.PasskeyCount = passkeyCounts.TryGetValue(user.Id, out var count) ? count : 0;
                return dto;
            });
            return Ok(ApiResponse<IEnumerable<UserDto>>.CreateSuccess(dtos));
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized attempt to get all users");
            return StatusCode(403, ApiResponse<object>.CreateError("Nur Systemadministratoren können alle Benutzer abrufen.", "FORBIDDEN"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all users");
            return StatusCode(500, ApiResponse<object>.CreateError("Ein Fehler ist beim Abrufen der Benutzer aufgetreten."));
        }
    }

    /// <summary>
    /// Gets all contacts with their organization assignments (visible to authenticated users).
    /// </summary>
    [HttpGet("contacts")]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<ContactDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<IEnumerable<ContactDto>>>> GetContacts(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = GetUserId();
            var contacts = await _userService.GetAllContactsAsync(userId, cancellationToken);
            return Ok(ApiResponse<IEnumerable<ContactDto>>.CreateSuccess(contacts));
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized attempt to get contacts");
            return StatusCode(401, ApiResponse<object>.CreateError("Nicht autorisiert.", "UNAUTHORIZED"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting contacts");
            return StatusCode(500, ApiResponse<object>.CreateError("Ein Fehler ist beim Abrufen der Kontakte aufgetreten."));
        }
    }

    /// <summary>
    /// Gets assignment-based recipient candidates for email flows.
    /// </summary>
    [HttpGet("recipient-candidates")]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<RecipientCandidateDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<IEnumerable<RecipientCandidateDto>>>> GetRecipientCandidates(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = GetUserId();
            var candidates = await _userService.GetRecipientCandidatesAsync(userId, cancellationToken);
            return Ok(ApiResponse<IEnumerable<RecipientCandidateDto>>.CreateSuccess(candidates));
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized attempt to get recipient candidates");
            return StatusCode(401, ApiResponse<object>.CreateError("Nicht autorisiert.", "UNAUTHORIZED"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting recipient candidates");
            return StatusCode(500, ApiResponse<object>.CreateError("Ein Fehler ist beim Abrufen der Empfängerliste aufgetreten."));
        }
    }

    /// <summary>
    /// Searches contacts with server-side filtering and pagination.
    /// Searches by name, club, department, and position.
    /// </summary>
    [HttpGet("contacts/search")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<ContactDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<PagedResult<ContactDto>>>> SearchContacts(
        [FromQuery] ContactSearchParameters parameters,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = GetUserId();
            var result = await _userService.SearchContactsAsync(userId, parameters, cancellationToken);
            return Ok(ApiResponse<PagedResult<ContactDto>>.CreateSuccess(result));
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized attempt to search contacts");
            return StatusCode(401, ApiResponse<object>.CreateError("Nicht autorisiert.", "UNAUTHORIZED"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching contacts");
            return StatusCode(500, ApiResponse<object>.CreateError("Ein Fehler ist beim Suchen der Kontakte aufgetreten."));
        }
    }

    /// <summary>
    /// Gets own contact assignments including context-email management metadata.
    /// </summary>
    [HttpGet("me/contact-assignments")]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<MyContactAssignmentDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<IEnumerable<MyContactAssignmentDto>>>> GetMyContactAssignments(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = GetUserId();
            var assignments = await _userService.GetMyContactAssignmentsAsync(userId, cancellationToken);
            return Ok(ApiResponse<IEnumerable<MyContactAssignmentDto>>.CreateSuccess(assignments));
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized attempt to get own contact assignments");
            return StatusCode(401, ApiResponse<object>.CreateError("Nicht autorisiert.", "UNAUTHORIZED"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting own contact assignments");
            return StatusCode(500, ApiResponse<object>.CreateError("Ein Fehler ist beim Abrufen der Zuordnungen aufgetreten."));
        }
    }

    /// <summary>
    /// Gets contact assignments for a specific user (user administration).
    /// Only SystemAdmin/Manager may access.
    /// </summary>
    [HttpGet("{id}/contact-assignments")]
    [Authorize(Roles = "SystemAdmin,Manager")]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<MyContactAssignmentDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<IEnumerable<MyContactAssignmentDto>>>> GetUserContactAssignments(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var requestingUserId = GetUserId();
            var assignments = await _userService.GetUserContactAssignmentsAsync(requestingUserId, id, cancellationToken);
            return Ok(ApiResponse<IEnumerable<MyContactAssignmentDto>>.CreateSuccess(assignments));
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized attempt to get assignments for user {TargetUserId}", id);
            return StatusCode(403, ApiResponse<object>.CreateError("Nicht autorisiert.", "FORBIDDEN"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting assignments for user {TargetUserId}", id);
            return StatusCode(500, ApiResponse<object>.CreateError("Ein Fehler ist beim Abrufen der Zuordnungen aufgetreten."));
        }
    }

    /// <summary>
    /// Consents to an own pending assignment and sets who may see it (and its role/office address).
    /// </summary>
    [HttpPost("me/contact-assignments/{assignmentId}/confirm")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ConfirmMyContactAssignment(
        Guid assignmentId,
        [FromBody] ConfirmContactAssignmentDto dto,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await _consentService.ConfirmAsync(GetUserId(), assignmentId, dto.Visibility, cancellationToken));

    /// <summary>
    /// Declines an own pending assignment; it is removed and the assigning person is informed.
    /// </summary>
    [HttpPost("me/contact-assignments/{assignmentId}/decline")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeclineMyContactAssignment(
        Guid assignmentId,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await _consentService.DeclineAsync(GetUserId(), assignmentId, cancellationToken));

    /// <summary>
    /// Sends the consent request for a pending assignment again (editors of the entity; at most once per 24 hours).
    /// </summary>
    [HttpPost("contact-assignments/{assignmentId}/consent-reminder")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> SendContactAssignmentConsentReminder(
        Guid assignmentId,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await _consentService.SendReminderAsync(GetUserId(), assignmentId, cancellationToken));

    private IActionResult ToActionResult(ContactAssignmentConsentResult result)
    {
        var body = result.Success
            ? ApiResponse<object>.CreateSuccess(new { message = result.Message })
            : ApiResponse<object>.CreateError(result.Message);
        return result.Status switch
        {
            ContactAssignmentConsentStatus.Success => Ok(body),
            ContactAssignmentConsentStatus.NotFound => NotFound(body),
            ContactAssignmentConsentStatus.Forbidden => StatusCode(StatusCodes.Status403Forbidden, body),
            ContactAssignmentConsentStatus.TooManyRequests => StatusCode(StatusCodes.Status429TooManyRequests, body),
            _ => Conflict(body)
        };
    }

    /// <summary>
    /// Updates own context email and visibility for one assignment.
    /// </summary>
    [HttpPut("me/contact-assignments/{assignmentId}/email")]
    [ProducesResponseType(typeof(ApiResponse<UpdateMyContactAssignmentEmailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<UpdateMyContactAssignmentEmailResponse>>> UpdateMyContactAssignmentEmail(
        Guid assignmentId,
        [FromBody] UpdateMyContactAssignmentEmailDto dto,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = GetUserId();
            var response = await _userService.UpdateMyContactAssignmentEmailAsync(userId, assignmentId, dto, cancellationToken);

            if (!response.Success)
            {
                return BadRequest(ApiResponse<UpdateMyContactAssignmentEmailResponse>.CreateError(response.Message, "UPDATE_FAILED"));
            }

            return Ok(ApiResponse<UpdateMyContactAssignmentEmailResponse>.CreateSuccess(response));
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized attempt to update own context email");
            return StatusCode(401, ApiResponse<object>.CreateError("Nicht autorisiert.", "UNAUTHORIZED"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating own context email for assignment {AssignmentId}", assignmentId);
            return StatusCode(500, ApiResponse<object>.CreateError("Ein Fehler ist beim Speichern aufgetreten."));
        }
    }

    /// <summary>
    /// Updates context email and visibility for one assignment of another user (user administration).
    /// Only SystemAdmin/Manager may access.
    /// </summary>
    [HttpPut("{id}/contact-assignments/{assignmentId}/email")]
    [Authorize(Roles = "SystemAdmin,Manager")]
    [ProducesResponseType(typeof(ApiResponse<UpdateMyContactAssignmentEmailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<UpdateMyContactAssignmentEmailResponse>>> UpdateUserContactAssignmentEmail(
        Guid id,
        Guid assignmentId,
        [FromBody] UpdateMyContactAssignmentEmailDto dto,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var requestingUserId = GetUserId();
            var response = await _userService.UpdateUserContactAssignmentEmailAsync(requestingUserId, id, assignmentId, dto, cancellationToken);

            if (!response.Success)
            {
                return BadRequest(ApiResponse<UpdateMyContactAssignmentEmailResponse>.CreateError(response.Message, "UPDATE_FAILED"));
            }

            return Ok(ApiResponse<UpdateMyContactAssignmentEmailResponse>.CreateSuccess(response));
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized attempt to update context email for user {TargetUserId}, assignment {AssignmentId}", id, assignmentId);
            return StatusCode(403, ApiResponse<object>.CreateError("Nicht autorisiert.", "FORBIDDEN"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating context email for user {TargetUserId}, assignment {AssignmentId}", id, assignmentId);
            return StatusCode(500, ApiResponse<object>.CreateError("Ein Fehler ist beim Speichern aufgetreten."));
        }
    }

    /// <summary>
    /// Resends context-email verification for one own assignment.
    /// </summary>
    [HttpPost("me/contact-assignments/{assignmentId}/email/resend-verification")]
    [ProducesResponseType(typeof(ApiResponse<UpdateMyContactAssignmentEmailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<UpdateMyContactAssignmentEmailResponse>>> ResendMyContactAssignmentEmailVerification(
        Guid assignmentId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = GetUserId();
            var response = await _userService.ResendMyContactAssignmentEmailVerificationAsync(userId, assignmentId, cancellationToken);

            if (!response.Success)
            {
                return BadRequest(ApiResponse<UpdateMyContactAssignmentEmailResponse>.CreateError(response.Message, "RESEND_FAILED"));
            }

            return Ok(ApiResponse<UpdateMyContactAssignmentEmailResponse>.CreateSuccess(response));
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized attempt to resend own context email verification");
            return StatusCode(401, ApiResponse<object>.CreateError("Nicht autorisiert.", "UNAUTHORIZED"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resending context email verification for assignment {AssignmentId}", assignmentId);
            return StatusCode(500, ApiResponse<object>.CreateError("Ein Fehler ist beim erneuten Versand aufgetreten."));
        }
    }

    /// <summary>
    /// Resends context-email verification for one assignment of another user (user administration).
    /// Only SystemAdmin/Manager may access.
    /// </summary>
    [HttpPost("{id}/contact-assignments/{assignmentId}/email/resend-verification")]
    [Authorize(Roles = "SystemAdmin,Manager")]
    [ProducesResponseType(typeof(ApiResponse<UpdateMyContactAssignmentEmailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<UpdateMyContactAssignmentEmailResponse>>> ResendUserContactAssignmentEmailVerification(
        Guid id,
        Guid assignmentId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var requestingUserId = GetUserId();
            var response = await _userService.ResendUserContactAssignmentEmailVerificationAsync(requestingUserId, id, assignmentId, cancellationToken);

            if (!response.Success)
            {
                return BadRequest(ApiResponse<UpdateMyContactAssignmentEmailResponse>.CreateError(response.Message, "RESEND_FAILED"));
            }

            return Ok(ApiResponse<UpdateMyContactAssignmentEmailResponse>.CreateSuccess(response));
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized attempt to resend context email verification for user {TargetUserId}, assignment {AssignmentId}", id, assignmentId);
            return StatusCode(403, ApiResponse<object>.CreateError("Nicht autorisiert.", "FORBIDDEN"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resending context email verification for user {TargetUserId}, assignment {AssignmentId}", id, assignmentId);
            return StatusCode(500, ApiResponse<object>.CreateError("Ein Fehler ist beim erneuten Versand aufgetreten."));
        }
    }

    /// <summary>
    /// Gets a user by ID.
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<UserDto>>> GetUser(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var requestingUserId = GetUserId();
            var user = await _userService.GetByIdAsync(requestingUserId, id, cancellationToken);

            if (user == null)
            {
                return NotFound(ApiResponse<object>.CreateError("Benutzer nicht gefunden oder kein Zugriff.", "NOT_FOUND"));
            }

            return Ok(ApiResponse<UserDto>.CreateSuccess(MapToDto(user)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user {UserId}", id);
            return StatusCode(500, ApiResponse<object>.CreateError("Ein Fehler ist beim Abrufen des Benutzers aufgetreten."));
        }
    }

    /// <summary>
    /// Creates a new user (SystemAdmin and Manager only).
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<UserDto>>> CreateUser(
        [FromBody] CreateUserDto dto,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
                return BadRequest(ApiResponse<object>.CreateError("Ungültige Eingabedaten.", "VALIDATION_ERROR", errors));
            }

            var requestingUserId = GetUserId();
            var user = MapFromCreateDto(dto);
            var created = await _userService.CreateAsync(requestingUserId, user, cancellationToken);

            return CreatedAtAction(
                nameof(GetUser),
                new { id = created.Id },
                ApiResponse<UserDto>.CreateSuccess(MapToDto(created)));
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized attempt to create user");
            return StatusCode(403, ApiResponse<object>.CreateError("Nur Systemadministratoren und Manager können Benutzer erstellen.", "FORBIDDEN"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating user");
            return StatusCode(500, ApiResponse<object>.CreateError("Ein Fehler ist beim Erstellen des Benutzers aufgetreten."));
        }
    }

    /// <summary>
    /// Updates a user (users can update themselves, SystemAdmin can update anyone).
    /// </summary>
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<UserDto>>> UpdateUser(
        Guid id,
        [FromBody] UpdateUserDto dto,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
                return BadRequest(ApiResponse<object>.CreateError("Ungültige Eingabedaten.", "VALIDATION_ERROR", errors));
            }

            var requestingUserId = GetUserId();
            var user = MapFromUpdateDto(dto);
            var updated = await _userService.UpdateAsync(requestingUserId, id, user, cancellationToken);

            if (updated == null)
            {
                return NotFound(ApiResponse<object>.CreateError("Benutzer nicht gefunden oder kein Zugriff.", "NOT_FOUND"));
            }

            return Ok(ApiResponse<UserDto>.CreateSuccess(MapToDto(updated)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating user {UserId}", id);
            return StatusCode(500, ApiResponse<object>.CreateError("Ein Fehler ist beim Aktualisieren des Benutzers aufgetreten."));
        }
    }

    /// <summary>
    /// Deletes a user (SystemAdmin only).
    /// </summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeleteUser(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var requestingUserId = GetUserId();
            var result = await _userService.DeleteAsync(requestingUserId, id, cancellationToken);

            if (!result)
            {
                return NotFound();
            }

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting user {UserId}", id);
            return StatusCode(500);
        }
    }

    /// <summary>
    /// Assigns a claim/role to a user (SystemAdmin only).
    /// </summary>
    [HttpPost("{id}/claims")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> AssignClaim(
        Guid id,
        [FromBody] AssignClaimDto dto,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var requestingUserId = GetUserId();
            var result = await _userService.AssignClaimAsync(requestingUserId, id, dto.Claim, cancellationToken);

            if (!result)
            {
                return BadRequest("Claim ist bereits zugewiesen oder Benutzer wurde nicht gefunden.");
            }

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error assigning claim to user {UserId}", id);
            return StatusCode(500);
        }
    }

    /// <summary>
    /// Removes a claim/role from a user (SystemAdmin only).
    /// </summary>
    [HttpDelete("{id}/claims/{claim}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RemoveClaim(
        Guid id,
        UserClaim claim,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var requestingUserId = GetUserId();
            var result = await _userService.RemoveClaimAsync(requestingUserId, id, claim, cancellationToken);

            if (!result)
            {
                return BadRequest("Claim ist nicht zugewiesen oder Benutzer wurde nicht gefunden.");
            }

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing claim from user {UserId}", id);
            return StatusCode(500);
        }
    }

    /// <summary>
    /// Requests a change of the own login address. A confirmation link is sent to the new address;
    /// the address only changes once that link is opened.
    /// </summary>
    [HttpPost("me/email-change")]
    [EnableRateLimiting("PublicTokenFlow")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public Task<IActionResult> RequestMyEmailChange(
        [FromBody] RequestAccountEmailChangeDto dto,
        CancellationToken cancellationToken = default)
        => RequestEmailChange(GetUserId(), dto, cancellationToken);

    /// <summary>
    /// Requests a change of another user's login address (SystemAdmin; Manager except for SystemAdmin accounts).
    /// The confirmation link is sent to the new address.
    /// </summary>
    [HttpPost("{id}/email-change")]
    [Authorize(Roles = "SystemAdmin,Manager")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public Task<IActionResult> RequestUserEmailChange(
        Guid id,
        [FromBody] RequestAccountEmailChangeDto dto,
        CancellationToken cancellationToken = default)
        => RequestEmailChange(id, dto, cancellationToken);

    private async Task<IActionResult> RequestEmailChange(
        Guid userId,
        RequestAccountEmailChangeDto dto,
        CancellationToken cancellationToken)
    {
        var result = await _emailChangeService.RequestChangeAsync(GetUserId(), userId, dto.NewEmail, cancellationToken);
        return result.Status switch
        {
            AccountEmailChangeStatus.ConfirmationSent => Ok(ApiResponse<object>.CreateSuccess(new { }, result.Message)),
            AccountEmailChangeStatus.NotFound => NotFound(ApiResponse<object>.CreateError(result.Message, "NOT_FOUND")),
            AccountEmailChangeStatus.Forbidden => StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.CreateError(result.Message, "FORBIDDEN")),
            AccountEmailChangeStatus.Conflict => Conflict(ApiResponse<object>.CreateError(result.Message, "CONFLICT")),
            AccountEmailChangeStatus.SendFailed => StatusCode(StatusCodes.Status502BadGateway, ApiResponse<object>.CreateError(result.Message, "SEND_FAILED")),
            _ => BadRequest(ApiResponse<object>.CreateError(result.Message, "INVALID"))
        };
    }

    /// <summary>
    /// Removes all passkeys of a user and ends their sessions (SystemAdmin only, not for the own account).
    /// Used when a user lost their device; they can then sign in with a login link and set up a new passkey.
    /// </summary>
    [HttpDelete("{id}/passkeys")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetPasskeys(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await _authService.ResetPasskeysAsync(GetUserId(), id, cancellationToken);
        return result.Status switch
        {
            PasskeyResetStatus.Done => Ok(ApiResponse<object>.CreateSuccess(
                new { removedPasskeys = result.RemovedPasskeys },
                result.RemovedPasskeys == 0
                    ? "Der Benutzer hatte keine Passkeys. Alle Sitzungen wurden beendet."
                    : $"{result.RemovedPasskeys} Passkey(s) entfernt. Alle Sitzungen wurden beendet.")),
            PasskeyResetStatus.OwnAccount => BadRequest(ApiResponse<object>.CreateError(
                "Deine eigenen Passkeys verwaltest du unter „Mein Konto“.", "OWN_ACCOUNT")),
            PasskeyResetStatus.NotFound => NotFound(ApiResponse<object>.CreateError("Benutzer nicht gefunden.", "NOT_FOUND")),
            _ => StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.CreateError("Keine Berechtigung.", "FORBIDDEN"))
        };
    }

    private Guid GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            throw new UnauthorizedAccessException("User ID not found in token");
        }
        return userId;
    }

    private static UserDto MapToDto(User user) => new()
    {
        Id = user.Id,
        Email = user.Email,
        FirstName = user.FirstName,
        LastName = user.LastName,
        PhoneNumber = user.PhoneNumber,
        EmailVisibility = user.EmailVisibility,
        PhoneVisibility = user.PhoneVisibility,
        HasAvatar = user.HasAvatar,
        Claims = user.Claims,
        CreatedAt = user.CreatedAt,
        UpdatedAt = user.UpdatedAt,
        LastLoginAt = user.LastLoginAt
    };

    private static User MapFromCreateDto(CreateUserDto dto) => new()
    {
        Email = dto.Email,
        FirstName = dto.FirstName,
        LastName = dto.LastName,
        PhoneNumber = dto.PhoneNumber,
        EmailVisibility = dto.EmailVisibility,
        PhoneVisibility = dto.PhoneVisibility,
    };

    private static User MapFromUpdateDto(UpdateUserDto dto) => new()
    {
        FirstName = dto.FirstName,
        LastName = dto.LastName,
        PhoneNumber = dto.PhoneNumber,
        EmailVisibility = dto.EmailVisibility,
        PhoneVisibility = dto.PhoneVisibility,
    };
}
